"""由真实 Guest 的随包 Office 渲染下载成品，在宿主复用现有语义/逐页金样门禁。"""
from __future__ import annotations

import argparse
import importlib.util
import json
import sys
import uuid
from pathlib import Path

import paramiko

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, sha256, write_json
from identity import probe
from lab import REPO, load_configuration
from providers import QemuLab


def verify(target: str, evidence: Path, remote: str) -> dict:
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    identity = probe(lab, target)
    record = json.loads(evidence.read_text(encoding="utf-8"))
    if (record.get("status") != "passed"
            or record.get("scope") != "installed-program-http-batch-formatter-only"
            or record.get("guest_uuid") != lab.state(target)["uuid"]
            or len(record.get("outputs", [])) != 2):
        raise RuntimeError("MATCHING_INSTALLED_HTTP_BATCH_EVIDENCE_REQUIRED")
    if not remote.startswith("/home/partyopsqa/formatter-") or ".." in Path(remote).parts:
        raise RuntimeError("GUEST_FORMATTER_WORKSPACE_REQUIRED")
    oracle_path = REPO / "scripts/verify-document-formatter-parity.py"
    spec = importlib.util.spec_from_file_location("golden_oracle", oracle_path)
    oracle = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(oracle)
    golden = REPO / "backend/tests/fixtures/document-formatter-source/expected-source-formatted.docx"
    local = evidence.resolve().parent / ("golden-" + uuid.uuid4().hex[:12])
    local.mkdir()
    result = {"scope": "installed-http-download-golden-only", "status": "failed",
              "runtime_environment_passed": False, "guest_identity": identity,
              "api_evidence_sha256": sha256(evidence), "golden_sha256": sha256(golden),
              "oracle_sha256": sha256(oracle_path), "renderer": "/opt/partyops/office-runtime/program/soffice",
              "render_location": "original-linux-guest", "outputs": []}
    client = paramiko.SSHClient()
    client.load_host_keys(str(lab.root / "keys/known_hosts"))
    client.set_missing_host_key_policy(paramiko.RejectPolicy())
    client.connect("127.0.0.1", port=matrix["targets"][target]["ssh_port"], username="partyopsqa",
                   key_filename=str(lab.root / "keys/guest_ed25519"), look_for_keys=False, allow_agent=False, timeout=15)
    try:
        golden_signature = oracle._semantic_equivalence_signature(oracle._document_signature(golden))
        sources = [golden]
        for item in record["outputs"]:
            output = (evidence.resolve().parent / item["filename"]).resolve(strict=True)
            if output.parent != evidence.resolve().parent or sha256(output) != item["sha256"]:
                raise RuntimeError("DOWNLOADED_OUTPUT_HASH_OR_PATH_MISMATCH")
            signature = oracle._document_signature(output)
            oracle._require_contract(signature)
            if oracle._semantic_equivalence_signature(signature) != golden_signature:
                raise RuntimeError("GOLDEN_SEMANTIC_MISMATCH")
            sources.append(output)
        guest_work = remote.rstrip("/") + "/" + local.name
        with client.open_sftp() as sftp:
            sftp.mkdir(guest_work)
            for source in sources:
                sftp.put(str(source), guest_work + "/" + source.name)
        # 标准用户运行安装版转换器；路径和哈希同时在 Guest 内复核。
        script = """import hashlib,json,os,subprocess
from pathlib import Path
work=Path(WORK)
require=lambda condition: condition or (_ for _ in ()).throw(RuntimeError('RENDER_IDENTITY_OR_INPUT_MISMATCH'))
require(os.getuid()!=0)
require(json.loads(Path('/etc/partyops-vm-lab.json').read_text())=={'uuid':UUID,'purpose':'disposable-qa'})
require(Path('/proc/sys/kernel/random/boot_id').read_text().strip()==BOOT)
require(hashlib.sha256(Path('/opt/partyops/partyops').read_bytes()).hexdigest()==EXEC_SHA)
for filename,checksum in SOURCES:
 source=work/filename
 require(hashlib.sha256(source.read_bytes()).hexdigest()==checksum)
 command=[OFFICE,'--headless','-env:UserInstallation='+ (work/('profile-'+source.stem)).as_uri(),'--convert-to','pdf','--outdir',str(work),str(source)]
 completed=subprocess.run(command,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=180)
 print(completed.stdout.decode(errors='replace'))
 require(completed.returncode==0 and source.with_suffix('.pdf').is_file())
print(json.dumps({'renderer_sha256':hashlib.sha256(Path(OFFICE).read_bytes()).hexdigest()}))
"""
        replacements = {"WORK": guest_work, "UUID": record["guest_uuid"], "BOOT": record["boot_id"],
                        "EXEC_SHA": record["executable_sha256"], "SOURCES": [(p.name, sha256(p)) for p in sources],
                        "OFFICE": result["renderer"]}
        for key, value in replacements.items():
            script = script.replace(key, repr(value))
        stdin, stdout, stderr = client.exec_command("python3 -", timeout=900)
        stdin.write(script)
        stdin.channel.shutdown_write()
        output, errors = stdout.read().decode(errors="replace"), stderr.read().decode(errors="replace")
        (local / "guest-render.log").write_text(output + errors, encoding="utf-8")
        if stdout.channel.recv_exit_status():
            raise RuntimeError("ACTUAL_GUEST_RENDER_FAILED")
        result.update(json.loads(output.strip().splitlines()[-1]))
        with client.open_sftp() as sftp:
            for source in sources:
                pdf = source.with_suffix(".pdf").name
                sftp.get(guest_work + "/" + pdf, str(local / pdf))
        for source in sources[1:]:
            pdf = local / source.with_suffix(".pdf").name
            visual = oracle._visual_page_comparison(local / golden.with_suffix(".pdf").name, pdf,
                                                    allow_rasterization_noise=True)
            result["outputs"].append({"filename": source.name, "sha256": sha256(source),
                                      "pdf_sha256": sha256(pdf), "semantic_contract": "passed",
                                      "visual_comparison": visual})
            if not visual["passed"]:
                raise RuntimeError("GOLDEN_VISUAL_MISMATCH")
        result["status"] = "passed"
    except Exception as exc:
        result["error"] = type(exc).__name__ + ": " + str(exc)
        raise
    finally:
        client.close()
        result.update(verified_at=now(), report_path=str(local))
        write_json(local / "download-golden-evidence.json", result)
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("target")
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--remote", required=True)
    args = parser.parse_args()
    print(json.dumps(verify(args.target, args.evidence, args.remote), ensure_ascii=False, indent=2))
