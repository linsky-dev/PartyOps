"""在原版 Guest 执行已有安装版 WPS HTTP 探针，绑定当前制品并收回下载成品。"""
from __future__ import annotations

import argparse
import importlib.util
import json
import re
import shlex
import stat
import sys
import uuid
from pathlib import Path

import paramiko

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, sha256, write_json
from identity import probe, runtime_binding
from lab import HERE, REPO, fingerprint, inventory, load_configuration
from provenance import bind_package
from providers import QemuLab


def feature_inputs(local: Path) -> Path:
    """复用原公文质量门禁的输入生成器与验收规则，不在 Guest 部署开发依赖。"""
    path = REPO / "scripts/verify-document-formatter-features-e2e.py"
    spec = importlib.util.spec_from_file_location("feature_fixtures", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.create_word_fixture(local / "feature-input.docx")
    module.create_pdf_fixture(local / "feature-input.pdf")
    cases = []

    def case(name, feature, options, extension=".docx", contains=(), excludes=(), source="feature-input.docx"):
        cases.append({"id": name, "feature_id": feature, "source": source, "sha256": sha256(local / source),
                      "options": {"compatibility_mode": "wps", **options}, "extension": extension,
                      "contains": list(contains), "excludes": list(excludes)})

    case("replace", "replace", {"plan_name": "E2E 四模式", "rules": [
        {"mode": "text", "find": "旧称", "replace": "新称"},
        {"mode": "regex", "find": "2026年", "replace": "二〇二六年"},
        {"mode": "wildcard", "find": "第[0-9]{1,}条", "replace": "条款"},
        {"mode": "format", "find": "宋体", "font_name": "黑体", "font_size": 18, "alignment": "center"}]},
         contains=("新称", "二〇二六年", "条款", "格式文字"), excludes=("旧称", "2026年", "第12条"))
    case("redheader", "redheader", {"document_type": "down", "copy_number": "1", "security": "秘密★10年",
         "urgency": "加急", "agency": "中共测试市委文件", "document_number": "测试党发〔2026〕12号",
         "signatory": "", "imprint": "中共测试市委办公室"},
         contains=("中共测试市委文件", "测试党发〔2026〕12号", "中共测试市委办公室"))
    case("rename", "rename", {"parts": ["title", "custom"], "custom_text": "定稿", "separator": "-", "rotation_words": ""})
    for target, mode in (("docx", "pages"), ("pdf", "pages"), ("txt", "pages"), ("png", "pages"), ("jpg", "long")):
        case("convert-" + target, "convert", {"target_format": target, "image_mode": mode, "page_selection": "all",
             "dpi": 144, "same_name_policy": "auto-rename"}, "." + target,
             contains=("正文内容",) if target == "txt" else ())
    case("pdf-to-word", "pdf-to-word", {"normalize_punctuation": True}, contains=("PartyOps",), source="feature-input.pdf")
    manifest = local / "features-manifest.json"
    write_json(manifest, {"fixture_generator_sha256": sha256(path), "cases": cases})
    return manifest


def exercise(target: str, host_sha256: str, all_features: bool = False, qa_font: Path | None = None) -> dict:
    if not re.fullmatch(r"[a-f0-9]{64}", host_sha256):
        raise ValueError("FORMATTER_HOST_SHA256_REQUIRED")
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    system = probe(lab, target)
    if system.get("installed_package") is not True or system.get("os") != "linux":
        raise RuntimeError("ORIGINAL_LINUX_INSTALLED_PACKAGE_REQUIRED")
    packages, _ = inventory(matrix, REPO / "artifacts")
    package_id = next(key for key, value in matrix["packages"].items() if target in value["required_targets"])
    package = bind_package(lab, packages[package_id], fingerprint())
    name = "formatter-" + uuid.uuid4().hex[:12]
    local = lab.root / "reports" / target / name
    local.mkdir(parents=True)
    remote = "/home/partyopsqa/" + name
    source = REPO / "backend/tests/fixtures/document-formatter-source/input-manual-break.docx"
    script = HERE / "guest/installed-formatter-api.py"
    manifest = feature_inputs(local) if all_features else None
    context = {"target": target, "package": package, "guest_identity": system,
               "environment": runtime_binding(lab, target), "remote": remote,
               "restore_generation": lab.state(target).get("restore_generation"),
               "script_sha256": sha256(script), "host_sha256": host_sha256}
    write_json(local / "context.json", context)
    if qa_font:
        context["qa_font_sha256"] = sha256(qa_font)
        write_json(local / "context.json", context)
    client = paramiko.SSHClient()
    client.load_host_keys(str(lab.root / "keys/known_hosts"))
    client.set_missing_host_key_policy(paramiko.RejectPolicy())
    client.connect("127.0.0.1", port=matrix["targets"][target]["ssh_port"], username="partyopsqa",
                   key_filename=str(lab.root / "keys/guest_ed25519"), look_for_keys=False, allow_agent=False, timeout=15)
    try:
        with client.open_sftp() as sftp:
            sftp.mkdir(remote)
            sftp.put(str(script), remote + "/probe.py")
            sftp.put(str(source), remote + "/input.docx")
            if qa_font:
                sftp.put(str(qa_font), remote + "/qa-font.ttf")
            if manifest:
                sftp.put(str(HERE / "guest/installed-formatter-features.py"), remote + "/installed-formatter-features.py")
                for name in ("features-manifest.json", "feature-input.docx", "feature-input.pdf"):
                    sftp.put(str(local / name), remote + "/" + name)
        command = ["python3", remote + "/probe.py", "--uuid", context["environment"]["vm_uuid"],
                   "--host-sha256", host_sha256, "--source", remote + "/input.docx",
                   "--source-sha256", sha256(source), "--output", remote + "/output"]
        if manifest:
            command += ["--features-manifest", remote + "/features-manifest.json"]
        if qa_font:
            command.extend(["--qa-font", remote + "/qa-font.ttf", "--qa-font-sha256", context["qa_font_sha256"]])
        _, stdout, stderr = client.exec_command(shlex.join(command), timeout=1800)
        output, errors = stdout.read().decode(errors="replace"), stderr.read().decode(errors="replace")
        code = stdout.channel.recv_exit_status()
        (local / "controller.log").write_text(output + errors, encoding="utf-8")
        with client.open_sftp() as sftp:
            # 不递归回收测试数据库或凭据，只收回探针输出目录的普通证据文件。
            try:
                entries = sftp.listdir_attr(remote + "/output")
            except FileNotFoundError:
                entries = []
            for item in entries:
                if stat.S_ISREG(item.st_mode) and Path(item.filename).name == item.filename:
                    sftp.get(remote + "/output/" + item.filename, str(local / item.filename))
        if code == 0 and manifest:
            # 原门禁以实际解码验证图片尺寸；继续复用同一 PyMuPDF 库，不能只看文件头。
            import fitz
            for image in [*local.glob("convert-*.png"), *local.glob("convert-*.jpg")]:
                pixmap = fitz.Pixmap(str(image))
                if pixmap.width <= 0 or pixmap.height <= 0:
                    raise RuntimeError("FEATURE_CONVERT_IMAGE_INVALID")
        result = {"generated_at": now(), "target": target, "exit_code": code, "report_path": str(local),
                  "remote": remote, "runtime_environment_passed": False,
                  "files": {path.name: sha256(path) for path in local.iterdir() if path.is_file()}}
        write_json(local / "controller.json", result)
        return result
    finally:
        client.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target")
    parser.add_argument("--host-sha256", required=True)
    parser.add_argument("--all-features", action="store_true")
    parser.add_argument("--qa-font", type=Path, help="仅安装到已登记Guest用户字体目录，用于同进程重试")
    args = parser.parse_args()
    result = exercise(args.target, args.host_sha256, args.all_features, args.qa_font)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    raise SystemExit(result["exit_code"])
