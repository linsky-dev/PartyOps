"""在专用 Linux Guest 内验证安装版首次配置及真实批量排版 HTTP 链路。

仅使用 Python 标准库控制安装后的程序；不导入 PartyOps 源码，不伪造处理
结果。输出是局部验收证据，不能替代桌面交互或九包完整生命周期门禁。
"""
from __future__ import annotations

import argparse
import hashlib
import http.cookiejar
import json
import os
import platform
import secrets
import socket
import subprocess
import time
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone
from pathlib import Path


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def is_child(path, parent):
    """兼容 UOS 原版 Python 3.7 的路径边界检查。"""
    try:
        return bool(path.relative_to(parent).parts)
    except ValueError:
        return False


def install_qa_font(source, checksum, job):
    """只在明确缺字体时安装外部 QA 字体，不掩盖其他排版故障。"""
    assert job["state"] == "failed" and not job["outputs"]
    assert len(job["items"]) == 2 and all(
        item.get("error_code") == "REQUIRED_FONT_MISSING" for item in job["items"])
    assert digest(source) == checksum, "QA字体哈希不一致"
    directory = Path.home() / ".local/share/fonts/partyops-vm-lab"
    assert is_child(directory.resolve(), Path.home().resolve())
    directory.mkdir(parents=True, exist_ok=True)
    target = directory / (checksum + ".ttf")
    assert not target.exists() and not target.is_symlink(), "不得覆盖已有字体"
    with target.open("xb") as stream:
        stream.write(source.read_bytes())
    assert digest(target) == checksum
    subprocess.run(["fc-cache", "-f", str(directory)], check=True, timeout=60,
                   stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    return {"path": str(target), "sha256": checksum}


def main():
    if not __debug__:
        raise RuntimeError("验收不得使用 Python -O 跳过断言")
    parser = argparse.ArgumentParser()
    parser.add_argument("--uuid", required=True)
    parser.add_argument("--host-sha256", required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--source-sha256", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--features-manifest", type=Path)
    parser.add_argument("--qa-font", type=Path)
    parser.add_argument("--qa-font-sha256")
    args = parser.parse_args()
    assert bool(args.qa_font) == bool(args.qa_font_sha256)
    marker = json.loads(Path("/etc/partyops-vm-lab.json").read_text())
    assert marker == {"uuid": args.uuid, "purpose": "disposable-qa"}
    assert os.getuid() != 0, "必须使用标准用户"
    executable = Path("/opt/partyops/partyops").resolve(strict=True)
    host = executable.parent / "formatter-host/partyops-document-formatter-host"
    assert digest(host) == args.host_sha256, "安装版排版宿主与实测哈希不一致"
    assert digest(args.source) == args.source_sha256, "输入金样被更改"
    output = args.output.resolve()
    assert is_child(output, Path.home().resolve())
    output.mkdir(parents=True, exist_ok=False)
    report = {
        "scope": "installed-program-http-batch-formatter-only",
        "status": "failed", "runtime_environment_passed": False,
        "guest_uuid": args.uuid, "arch": platform.machine(), "uid": os.getuid(),
        "boot_id": Path("/proc/sys/kernel/random/boot_id").read_text().strip(),
        "app_version": (executable.parent / "VERSION").read_text().strip(),
        "executable_sha256": digest(executable), "host_sha256": digest(host),
        "source_sha256": args.source_sha256, "outputs": [],
    }
    # 凭据仅驻留当前测试进程内存，不写入日志和报告。
    password = secrets.token_urlsafe(32)
    cookies = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(
        urllib.request.ProxyHandler({}), urllib.request.HTTPCookieProcessor(cookies)
    )
    origin = "http://127.0.0.1:18865"
    local_base = "http://127.0.0.1:18868"
    local_token = ""
    # 端口有已有实例时直接退出，绝不向未知主实例创建测试管理员。
    for port in (18865, 18868):
        with socket.socket() as probe:
            probe.bind(("127.0.0.1", port))

    def request(base, path, method="GET", payload=None, content_type=None, authorization=None):
        headers = {"Origin": origin}
        if base == local_base and local_token:
            headers["X-PartyOps-Local-Token"] = local_token
        if base == origin:
            for cookie in cookies:
                if cookie.name == "partyops_csrf":
                    headers["X-PartyOps-CSRF"] = cookie.value
        if authorization:
            headers["Authorization"] = authorization
        if isinstance(payload, dict):
            payload = json.dumps(payload).encode("utf-8")
            content_type = "application/json"
        if content_type:
            headers["Content-Type"] = content_type
        query = urllib.request.Request(base + path, data=payload, method=method, headers=headers)
        with opener.open(query, timeout=60) as response:
            body = response.read()
            if "application/json" in response.headers.get("Content-Type", ""):
                return json.loads(body)
            return body

    # 不继承任何开发期排版宿主、测试替身或外部数据目录。
    environment = {key: value for key, value in os.environ.items()
                   if not key.startswith(("PARTYOPS_", "MONO_", "PYTHON", "LD_LIBRARY_PATH"))}
    environment.update(
        DISPLAY=":0", XAUTHORITY=str(Path.home() / ".Xauthority"),
        DBUS_SESSION_BUS_ADDRESS=f"unix:path=/run/user/{os.getuid()}/bus",
        PARTYOPS_MODE="personal", PARTYOPS_ENVIRONMENT="production",
        PARTYOPS_DATA_DIR=str(output / "中文 空格数据"), PARTYOPS_HOST="127.0.0.1",
        PARTYOPS_BIND_HOST="127.0.0.1", PARTYOPS_PORT="18865",
        PARTYOPS_OFFICIAL_FORMAT_PORT="18868", PARTYOPS_TLS_ENABLED="false",
        PARTYOPS_SEED_DEMO="false",
    )
    process = None
    try:
        with (output / "installed-server.log").open("w", encoding="utf-8") as log:
            process = subprocess.Popen([str(executable)], cwd=output, env=environment,
                                       stdout=log, stderr=subprocess.STDOUT)
            report["pid"] = process.pid
            assert Path(f"/proc/{process.pid}/exe").resolve() == executable
            deadline = time.monotonic() + 300
            while True:
                assert process.poll() is None, "安装版启动失败，检查服务日志"
                try:
                    health = request(origin, "/api/v1/health")
                    break
                except urllib.error.URLError:
                    assert time.monotonic() < deadline, "安装版健康检查超时"
                    time.sleep(2)
            assert health["status"] == "ok" and health["app_version"] == report["app_version"]
            report["health"] = health
            assert request(origin, "/api/v1/bootstrap/status")["configured"] is False
            user = {"username": "formatterqa", "display_name": "排版验收", "password": password}
            request(origin, "/api/v1/bootstrap/host", "POST", user)
            request(origin, "/api/v1/auth/login", "POST", user)
            assert request(origin, "/api/v1/bootstrap/status")["configured"] is True
            ticket = request(origin, "/api/v1/official-format/local-ticket", "POST", {"origin": origin})
            assert ticket["local_base_url"] == local_base
            session = request(local_base, "/v1/sessions", "POST", {}, authorization="Bearer " + ticket["ticket"])
            local_token = session["session_token"]
            session_path = "/v1/sessions/" + session["session_id"]
            capabilities = request(local_base, "/v1/capabilities")
            assert capabilities["source_host_ready"] is True and capabilities["capability_count"] == 25
            assert len(capabilities["features"]) == 6
            report["capability_count"] = capabilities["capability_count"]
            document_ids = []
            for filename in ("中文 空格一.docx", "中文 空格二.docx"):
                boundary = "PartyOpsQA" + secrets.token_hex(12)
                header = f'--{boundary}\r\nContent-Disposition: form-data; name="document"; filename="{filename}"\r\nContent-Type: application/vnd.openxmlformats-officedocument.wordprocessingml.document\r\n\r\n'
                body = header.encode("utf-8") + args.source.read_bytes() + f"\r\n--{boundary}--\r\n".encode()
                uploaded = request(local_base, session_path + "/documents", "POST", body,
                                   "multipart/form-data; boundary=" + boundary)
                document_ids.append(uploaded["document_id"])
            def submit_format():
                job = request(local_base, session_path + "/jobs", "POST", {
                    "feature_id": "format", "document_ids": document_ids,
                    "options": {"scope": "full", "compatibility_mode": "wps"},
                })
                job_path = session_path + "/jobs/" + job["id"]
                deadline = time.monotonic() + 900
                while job["state"] in {"queued", "running"}:
                    assert time.monotonic() < deadline, "批量排版超时"
                    time.sleep(2)
                    job = request(local_base, job_path)
                return job, job_path

            job, job_path = submit_format()
            report["job"] = job
            if args.qa_font:
                report["before_font_install"] = job
                report["qa_font"] = install_qa_font(args.qa_font, args.qa_font_sha256, job)
                assert process.poll() is None, "安装字体期间程序退出"
                job, job_path = submit_format()
                report["job"] = job
                assert process.poll() is None, "字体重试期间程序退出"
                report["font_refresh_same_pid"] = process.pid
            assert job["state"] == "completed" and len(job["outputs"]) == 2
            for index, item in enumerate(job["outputs"], 1):
                result_path = output / f"installed-result-{index}.docx"
                result_path.write_bytes(request(local_base, job_path + "/outputs/" + item["id"]))
                report["outputs"].append({"filename": result_path.name, "sha256": digest(result_path)})
            if args.features_manifest:
                # 仅加载同目录的验收控制器；它通过已安装服务处理文档，不导入产品源码。
                import importlib.util
                module_spec = importlib.util.spec_from_file_location(
                    "installed_features", Path(__file__).with_name("installed-formatter-features.py"))
                module = importlib.util.module_from_spec(module_spec)
                module_spec.loader.exec_module(module)
                report["features"] = module.exercise(request, local_base, session_path, args.features_manifest, output)
            request(local_base, session_path, "DELETE")
            assert digest(args.source) == args.source_sha256, "原始输入被改写"
            report["status"] = "passed"
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=30)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
                report["status"] = "failed"
                report["shutdown_error"] = "进程未正常退出"
        report["verified_at"] = datetime.now(timezone(timedelta(hours=8))).isoformat(timespec="seconds")
        report["timezone"] = "Asia/Shanghai"
        (output / "installed-api-evidence.json").write_text(
            json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )
    assert report["status"] == "passed"
    print("安装版 HTTP 批量排版通过；仍需对下载结果执行金样比较及其他生命周期测试。")


if __name__ == "__main__":
    main()
