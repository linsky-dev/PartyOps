"""向当前业务验收 Guest 传入固定 OCR/模型样本；完整校验后才供正式产品接口使用。"""
import argparse
import json
import sys
from pathlib import Path

import paramiko

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, sha256, write_json
from identity import probe, runtime_binding
from lab import REPO, fingerprint, inventory, load_configuration
from provenance import bind_package
from providers import QemuLab


def verification_script(remote, manifest, required_free_bytes=2 * 1024**3,
                       space_error_code="MODEL_IMPORT_GUEST_SPACE_REQUIRED"):
    # 只拼接 repr 值；不能全局替换 WORK，否则会污染错误码内的同名子串。
    return "import hashlib,json,os\nfrom pathlib import Path\nwork=Path(" + repr(remote) + ")\nmanifest=" + repr(manifest) + "\n" + """if not work.resolve().is_dir() or os.getuid()==0:raise RuntimeError('STANDARD_USER_WORK_REQUIRED')
for item in manifest.values():
 value=hashlib.sha256()
 with (work/item['filename']).open('rb') as stream:
  for chunk in iter(lambda:stream.read(1024*1024),b''):value.update(chunk)
 if value.hexdigest()!=item['sha256']:raise RuntimeError('INPUT_HASH_MISMATCH')
free=os.statvfs(str(work))
if free.f_bavail*free.f_frsize<REQUIRED_FREE_BYTES:raise RuntimeError('SPACE_ERROR_CODE')
(work/'inputs.json').write_text(json.dumps(manifest))
print(json.dumps({'inputs':manifest,'guest_free_bytes':free.f_bavail*free.f_frsize}))
""".replace("REQUIRED_FREE_BYTES", str(required_free_bytes)).replace("SPACE_ERROR_CODE", space_error_code)


def runtime_input_files(target, matrix, lab_root, ocr_only=False):
    """按显式模式选择固定哈希的运行时输入，不探测模型能力。"""
    files = {
        "ocr": (lab_root / "reports/uos-deb-x64/ocr-input-20260906.png", "070bd312f8654446bcca77c4efaea6df05e6a6ffba631da71e542bacb112d8d6"),
    }
    if ocr_only:
        return files
    files.update({
        "embedding": (REPO / "artifacts/model-packs/PartyOps_BGE_Small_ZH_1.5.0.partyops-modelpack", "57df3669e368497ad6d428dab309fb7665229a57edfe3a4fe1f2afd6a0f1a155"),
        "llm": (REPO / "artifacts/model-packs/PartyOps_Qwen3_0.6B_Q8_0.partyops-modelpack", "deb85abe5c9d9c79fddb943424144770ed35499a4f5b9e15047812297f448d63"),
    })
    if matrix["targets"][target]["arch"] == "x86_64":
        files["intent_router"] = (
            REPO / "artifacts/model-packs/needle2-intent-2.0.4-linux-amd64.partyops-modelpack",
            "d7afd07eb71536568224d58c73d81abfc881b3fc885ee985d0ee5af2c54ca8e3")
    return files


def prepare(target, ocr_only=False):
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    identity = probe(lab, target)
    pointer = json.loads((lab.root / "state" / ("business-" + target + ".json")).read_text(encoding="utf-8"))
    packages, _ = inventory(matrix, REPO / "artifacts")
    package_id = next(key for key, item in matrix["packages"].items() if target in item["required_targets"])
    package = bind_package(lab, packages[package_id], fingerprint())
    expected = {"target": target, "package": package, "environment": runtime_binding(lab, target),
                "restore_generation": lab.state(target).get("restore_generation")}
    if pointer["context"] != expected or identity.get("installed_package") is not True:
        raise RuntimeError("CURRENT_INSTALLED_BUSINESS_CONTEXT_REQUIRED")
    files = runtime_input_files(target, matrix, lab.root, ocr_only)
    manifest = {}
    for capability, (path, checksum) in files.items():
        if not path.is_file() or sha256(path) != checksum:
            raise RuntimeError("PINNED_RUNTIME_INPUT_MISSING_OR_CHANGED:" + capability)
        manifest[capability] = {"filename": path.name, "sha256": checksum}
    remote = pointer["remote"]
    client = paramiko.SSHClient()
    client.load_host_keys(str(lab.root / "keys/known_hosts"))
    client.set_missing_host_key_policy(paramiko.RejectPolicy())
    client.connect("127.0.0.1", port=matrix["targets"][target]["ssh_port"], username="partyopsqa",
                   key_filename=str(lab.root / "keys/guest_ed25519"), look_for_keys=False, allow_agent=False, timeout=15)
    try:
        with client.open_sftp() as sftp:
            for path, checksum in files.values():
                existing = None
                try:
                    existing = sftp.stat(remote + "/" + path.name)
                except FileNotFoundError:
                    pass
                if existing is None or existing.st_size != path.stat().st_size:
                    sftp.put(str(path), remote + "/" + path.name)
            # 等量文件仍由 Guest 逐字节验证；损坏文件失败退出，不写有效输入清单。
        required_free_bytes = max(16 * 1024**2, files["ocr"][0].stat().st_size * 2) if ocr_only else 2 * 1024**3
        space_error_code = "RUNTIME_INPUT_GUEST_SPACE_REQUIRED" if ocr_only else "MODEL_IMPORT_GUEST_SPACE_REQUIRED"
        script = verification_script(remote, manifest, required_free_bytes, space_error_code)
        stdin, stdout, stderr = client.exec_command("python3 -", timeout=300)
        stdin.write(script)
        stdin.channel.shutdown_write()
        output, errors = stdout.read().decode(), stderr.read().decode()
        if stdout.channel.recv_exit_status():
            raise RuntimeError("GUEST_INPUT_VERIFICATION_FAILED:" + errors[-1000:])
        record = {"target": target, "at": now(), "context": expected, "remote": remote,
                  "runtime_environment_passed": False, **json.loads(output)}
        write_json(lab.root / "reports" / target / ("runtime-inputs-" + package["sha256"][:12] + ".json"), record)
        return record
    finally:
        client.close()


def argument_parser():
    parser = argparse.ArgumentParser()
    parser.add_argument("target")
    parser.add_argument("--ocr-only", action="store_true", help="仅准备固定哈希的中文 OCR 图片输入")
    return parser


if __name__ == "__main__":
    parser = argument_parser()
    args = parser.parse_args()
    print(json.dumps(prepare(args.target, ocr_only=args.ocr_only), ensure_ascii=False, indent=2))
