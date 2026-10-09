#!/usr/bin/env bash
set -euo pipefail
umask 077
# 只取证官方 ARM bottle；不安装、不运行 SDK，不变更产品 SDK 锁。
[[ "${PARTYOPS_PROBE_CONFIRM:-}" == ARM_SDK_RC6_PREFLIGHT ]] || exit 2
[[ -n "${RUNNER_TEMP:-}" ]] || exit 2
export HOME="$RUNNER_TEMP/partyops-arm-sdk-isolated/home"
export TMPDIR="$RUNNER_TEMP/partyops-arm-sdk-isolated/tmp"
PYTHON_BIN="$(command -v python3)"
exec "$PYTHON_BIN" - <<'ARM_SDK_PY'
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import platform
import re
import shutil
import subprocess
import sys
import tarfile
import time
import urllib.error
import urllib.request

EXPECTED_VERSION = "6.14.1"
EXPECTED_SHA = "ada6e9683f3e1c9c4f0dc372680250252cfca61ac77c1f8f6299ad3b8452cfd8"
PRODUCT_SHA = "87ac2d657bb9278bf9330c871dfd4fb8472b109334913a9b78a04f02663fa372"
METADATA_URL = "https://formulae.brew.sh/api/formula/mono.json"
base = Path(os.environ["RUNNER_TEMP"]) / "partyops-arm-sdk-isolated"
reports = Path(os.environ["RUNNER_TEMP"]) / "partyops-arm-sdk-reports"
receipt = {"schema": 1, "purpose": "ARM SDK 只读取证，不构建产品", "sdk_executed": False,
           "product_gate_satisfied": False, "product_changes": False,
           "metadata_source": METADATA_URL, "expected_version": EXPECTED_VERSION,
           "expected_bottle_sha256": EXPECTED_SHA, "blockers": [], "commands": []}
# 子进程不继承凭据；HOME、缓存和临时目录均为本次隔离目录。
env = {"PATH": os.environ.get("PATH", "/usr/bin:/bin:/usr/sbin:/sbin"),
       "HOME": str(base / "home"), "TMPDIR": str(base / "tmp"), "LANG": "C",
       "HOMEBREW_CACHE": str(base / "cache"), "HOMEBREW_LOGS": str(base / "logs"),
       "HOMEBREW_NO_AUTO_UPDATE": "1", "HOMEBREW_NO_ANALYTICS": "1",
       "HOMEBREW_NO_ENV_HINTS": "1", "HOMEBREW_NO_INSTALL_CLEANUP": "1"}

def redact(value):
    value = re.sub(r"https?://[^\s)]+", "[URL 已隐藏]", str(value))
    for path, label in ((str(base), "$ISOLATED"), (str(reports), "$REPORTS")):
        value = value.replace(path, label)
    return value

def run(args, timeout=60):
    result = subprocess.run(args, env=env, cwd=base, stdin=subprocess.DEVNULL,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            text=True, errors="replace", timeout=timeout)
    receipt["commands"].append({"tool": Path(args[0]).name, "args": [redact(a) for a in args[1:]],
                                "returncode": result.returncode,
                                "stdout": redact(result.stdout[:16000]),
                                "stderr": redact(result.stderr[:4000])})
    if result.returncode:
        raise RuntimeError("工具失败：" + Path(args[0]).name)
    return result.stdout

def metadata():
    # 公开官方元数据；不携带认证、cookie 或环境代理凭据。
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    for attempt in range(2):
        try:
            with opener.open(METADATA_URL, timeout=20) as response:
                return json.loads(response.read(2 * 1024 * 1024))
        except urllib.error.HTTPError as error:
            if attempt or (error.code != 429 and error.code < 500):
                raise RuntimeError("官方 metadata HTTP " + str(error.code)) from None
            time.sleep(20 if error.code == 429 else 2)
        except (urllib.error.URLError, TimeoutError):
            if attempt:
                raise RuntimeError("官方 metadata 连接失败") from None
            time.sleep(2)

def normalized(parts):
    result = []
    for part in parts:
        if part in ("", "."):
            continue
        if part == "..":
            if not result:
                raise ValueError("归档路径逃逸")
            result.pop()
        else:
            result.append(part)
    return tuple(result)

def audit_members(members):
    if len(members) > 100000 or sum(m.size for m in members) > 2 * 1024**3:
        raise ValueError("归档数量或解压体积超限")
    names, links = set(), {}
    for member in members:
        path = PurePosixPath(member.name)
        if path.is_absolute() or ".." in path.parts:
            raise ValueError("归档路径逃逸")
        name = normalized(path.parts)
        if not name or name in names:
            raise ValueError("归档路径重复或为空")
        names.add(name)
        if member.issym() or member.islnk():
            target = PurePosixPath(member.linkname)
            if target.is_absolute():
                raise ValueError("归档链接逃逸")
            # 保守拒绝 x/../.. 形态，避免 x 自身为链接时词法折叠漏掉逃逸。
            non_parent_seen = False
            for part in target.parts:
                if part == ".." and non_parent_seen:
                    raise ValueError("不支持混合父目录的链接目标")
                if part not in ("..", "."):
                    non_parent_seen = True
            links[name] = normalized((name[:-1] if member.issym() else ()) + target.parts)
        elif not (member.isfile() or member.isdir()):
            raise ValueError("归档包含特殊文件")
    def resolve(parts, seen=()):
        for index in range(1, len(parts) + 1):
            prefix = parts[:index]
            if prefix in links:
                if prefix in seen or len(seen) >= 40:
                    raise ValueError("归档链接循环")
                return resolve(links[prefix] + parts[index:], seen + (prefix,))
        return parts
    for name in names:
        resolve(name)
    return {"members": len(members), "links": len(links), "symlink_escape_count": 0,
            "expanded_bytes": sum(m.size for m in members)}

def extract(archive, root):
    with tarfile.open(archive, "r:gz") as tar:
        members = tar.getmembers()
        info = audit_members(members)
        root.mkdir()
        def destination_for(member):
            destination = root.joinpath(*PurePosixPath(member.name).parts)
            if not destination.parent.resolve().is_relative_to(root.resolve()):
                raise ValueError("写入前父目录逃逸")
            if not destination.resolve().is_relative_to(root.resolve()):
                raise ValueError("写入前目标逃逸")
            return destination
        # 先写目录及普通文件，最后建链接；杜绝通过链接写出隔离目录。
        for member in members:
            destination = destination_for(member)
            if member.isdir():
                destination.mkdir(parents=True, exist_ok=True)
            elif member.isfile():
                destination.parent.mkdir(parents=True, exist_ok=True)
                with tar.extractfile(member) as source, destination.open("xb") as output:
                    shutil.copyfileobj(source, output)
                destination.chmod(0o600)  # SDK 原生文件不可执行。
        for member in members:
            if member.issym() or member.islnk():
                destination = destination_for(member)
                destination.parent.mkdir(parents=True, exist_ok=True)
                if member.issym():
                    destination.symlink_to(member.linkname)
                else:
                    target = root / member.linkname
                    if not target.resolve().is_relative_to(root.resolve()) or not target.is_file():
                        raise ValueError("硬链接目标异常")
                    os.link(target, destination)
        for path in root.rglob("*"):
            if not path.resolve().is_relative_to(root.resolve()):
                raise ValueError("解压后链接逃逸")
        return info

def minimum_versions(text):
    versions = []
    # LC_SOURCE_VERSION 的 version 不是最低 OS，不能混入判断。
    for block in re.finditer(r"^\s*cmd\s+(LC_BUILD_VERSION|LC_VERSION_MIN_MACOSX)\s*\n"
                             r"(.*?)(?=^\s*(?:cmd\s|Load command\s)|\Z)", text, re.M | re.S):
        field = "minos" if block.group(1) == "LC_BUILD_VERSION" else "version"
        versions.extend(re.findall(r"^\s*" + field + r"\s+(\d+(?:\.\d+){0,2})\s*$", block.group(2), re.M))
    return versions

def version_tuple(version):
    parts = tuple(int(part) for part in version.split("."))
    return (parts + (0, 0, 0))[:3]

def archive_member_versions(text):
    headers = list(re.finditer(r"^.*\(([^)]+)\):\s*$", text, re.M))
    result = []
    for index, header in enumerate(headers):
        body = text[header.end():headers[index + 1].start() if index + 1 < len(headers) else len(text)]
        versions = minimum_versions(body)
        result.append({"member": header.group(1), "minimum_macos": versions,
                       "minimum_os_unknown": not bool(versions)})
    return result

def external_dependencies(text, install_ids):
    loads = re.findall(r"^\s+(/[^\s]+)\s+\(", text, re.M)
    return [load for load in loads if load not in install_ids and
            re.match(r"^/(?:opt/homebrew|usr/local)/(?:opt|Cellar|lib)/", load)]

def main():
    if platform.system() != "Darwin" or platform.machine() != "arm64":
        raise RuntimeError("仅允许原生 Darwin arm64")
    for directory in (base / "home", base / "tmp", base / "cache", base / "logs", reports):
        directory.mkdir(parents=True, exist_ok=False)
    receipt["host"] = {"system": platform.system(), "machine": platform.machine(),
                       "macos": platform.mac_ver()[0]}
    data = metadata()
    bottle = data["bottle"]["stable"]["files"]["arm64_sequoia"]
    receipt["metadata"] = {"version": data["versions"]["stable"], "bottle_sha256": bottle["sha256"],
                           "bottle_tag": "arm64_sequoia"}
    if data["versions"]["stable"] != EXPECTED_VERSION or bottle["sha256"] != EXPECTED_SHA:
        raise RuntimeError("官方 metadata 与已核版本或 SHA 不符，停止")
    receipt["blockers"].append("当前产品 profile 仅接受 Mono 6.12.0；6.14.1 需另行审定，不代表 ARM 能力否定")
    brew = shutil.which("brew", path=env["PATH"])
    if not brew:
        raise RuntimeError("runner 未提供 Homebrew，停止")
    run([brew, "fetch", "--formula", "--force", "--bottle-tag=arm64_sequoia", "mono"], 600)
    cached = run([brew, "--cache", "--bottle-tag=arm64_sequoia", "mono"], 60).strip()
    archive = Path(cached).resolve(strict=True)
    if not archive.is_relative_to((base / "cache").resolve()) or not archive.is_file():
        raise RuntimeError("bottle 缓存路径不在隔离目录")
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    receipt["download_sha256"] = digest
    if digest != EXPECTED_SHA:
        raise RuntimeError("下载 bottle SHA 不符，停止")
    root = base / "bottle"
    receipt["archive"] = extract(archive, root)
    # 只用 runner 的系统工具解析结构，不执行任何 bottle 中的程序或库。
    files = [p for p in root.rglob("*") if p.is_file() and not p.is_symlink()]
    # 下一步 ARM profile 只依据已锁 bottle 的真实配置；不猜测 dllmap。
    catalog = {"bottle_sha256": digest, "sdk_version": EXPECTED_VERSION, "text_inputs": [], "bcl": []}
    for path in files:
        relative = path.relative_to(root).as_posix()
        if relative in ("mono/6.14.1/etc/mono/config", "mono/6.14.1/etc/mono/4.5/machine.config",
                        "mono/6.14.1/lib/pkgconfig/mono-2.pc"):
            content = path.read_bytes()
            catalog["text_inputs"].append({"path": relative, "sha256": hashlib.sha256(content).hexdigest(),
                                           "content": content.decode("utf-8")})
        if relative.startswith("mono/6.14.1/lib/mono/4.5/") and path.suffix == ".dll":
            catalog["bcl"].append({"path": relative, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    (reports / "sdk-input-catalog.json").write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    receipt["structure"] = {"file_count": len(files),
        "static_libraries": [str(p.relative_to(root)) for p in files if p.suffix == ".a"],
        "sdk_entries": [str(p.relative_to(root)) for p in files if p.name in
                        ("mono", "mkbundle", "jit.h", "assembly.h", "mono-2.pc", "config", "machine.config")]}
    static = [p for p in root.rglob("libmono-2.0.a") if p.is_file()]
    if not static:
        receipt["blockers"].append("缺少产品要求的 libmono-2.0.a")
    elif any(hashlib.sha256(p.read_bytes()).hexdigest() != PRODUCT_SHA for p in static):
        receipt["blockers"].append("当前产品 profile 的 Intel 静态库 SHA 不匹配；ARM SDK 接受规则需另行审定")
    runtime_static = {path.resolve() for path in static}
    binaries = []
    for path in files:
        with path.open("rb") as source:
            magic = source.read(8)
        if magic[:4] not in (b"\xcf\xfa\xed\xfe", b"\xce\xfa\xed\xfe", b"\xfe\xed\xfa\xcf",
                             b"\xfe\xed\xfa\xce", b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca") and magic != b"!<arch>\n":
            continue
        item = {"path": str(path.relative_to(root)), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
        item["category"] = ("runtime_candidate" if path.resolve() in runtime_static or
                            path.name.startswith(("libmono-native", "libMonoPosixHelper"))
                            else "sdk_tool_or_other")
        item["file"] = redact(run(["/usr/bin/file", "-b", str(path)]))
        item["architectures"] = run(["/usr/bin/lipo", "-archs", str(path)]).strip().split()
        item["dependencies"] = redact(run(["/usr/bin/otool", "-L", str(path)]))
        item["install_ids"] = []
        if ".dylib" in path.name:
            ids = run(["/usr/bin/otool", "-D", str(path)])
            item["install_ids"] = [line.strip() for line in ids.splitlines()
                                   if line.strip().startswith("/") and not line.rstrip().endswith(":")]
        item["external_homebrew_dependencies"] = external_dependencies(item["dependencies"], item["install_ids"])
        if item["category"] == "runtime_candidate" and item["external_homebrew_dependencies"]:
            receipt["blockers"].append("依赖外部 Homebrew 动态库：" + item["path"])
        loads = run(["/usr/bin/otool", "-l", str(path)])
        item["load_commands"] = redact(loads[:120000])
        item["minimum_macos"] = minimum_versions(loads)
        item["minimum_os_unknown"] = not bool(item["minimum_macos"])
        if magic == b"!<arch>\n":
            item["archive_members"] = archive_member_versions(loads)
            item["minimum_os_unknown"] = not item["archive_members"] or any(
                member["minimum_os_unknown"] for member in item["archive_members"])
        binaries.append(item)
        if item["category"] == "runtime_candidate" and "arm64" not in item["architectures"]:
            receipt["blockers"].append("缺 arm64：" + item["path"])
        if item["category"] == "runtime_candidate" and any(version_tuple(version) > (11, 0, 0)
                                                             for version in item["minimum_macos"]):
            receipt["blockers"].append("最低 macOS 高于 11：" + item["path"])
        if item["category"] == "runtime_candidate" and item["minimum_os_unknown"]:
            receipt["blockers"].append("最低 macOS 未知：" + item["path"])
    (reports / "binary-manifest.json").write_text(json.dumps(binaries, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    receipt["binary_count"] = len(binaries)
    receipt["status"] = "evidence_complete_product_gate_blocked"

if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        receipt["status"] = "stopped"
        receipt["error"] = redact(type(error).__name__ + ": " + str(error))
    finally:
        reports.mkdir(parents=True, exist_ok=True)
        (reports / "sdk-receipt.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"status": receipt["status"], "receipt": "$RUNNER_TEMP/partyops-arm-sdk-reports/sdk-receipt.json",
                          "product_gate_satisfied": False}, ensure_ascii=False))
    sys.exit(0 if receipt["status"] == "evidence_complete_product_gate_blocked" else 1)
ARM_SDK_PY
