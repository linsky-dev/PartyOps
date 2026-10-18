"""Windows限域发布驱动；默认只准备blocked内容，不部署、不调用旧intent。"""
from __future__ import annotations
import argparse
import copy
import datetime
import hashlib
import json
import importlib.util
import re
import sys
import os
import secrets
import shutil
import urllib.error
import urllib.request
from pathlib import Path

import prepare as preparation
import windows_receiver_factory as receiver

HERE = Path(__file__).resolve().parent
MANIFEST_NAME = "release-manifest-windows-startup-20261017.json"
BASE_URL = "https://partyops-rc6-win7-x86.app.workbuddy.host"
SCOPE = "windows-startup-20261017-only"
GATE = preparation.ROOT / ".release-gates/full-function-tests.json"
p = preparation.prior


def now(): return datetime.datetime.now(datetime.timezone.utc).isoformat()


def inputs():
    p.require(p.digest(preparation.BASELINE) == preparation.BASELINE_SHA, "SIGNED_BASELINE_CHANGED")
    baseline = p.read(preparation.BASELINE)
    metadata = p.read(preparation.PRIOR / "batch/post-switch-receiver/upload-metadata.json")
    preparation.windows_only_plan(baseline, metadata, {"filename": preparation.NAME, "bytes": receiver.SIZE, "sha256": receiver.SHA})
    p.require(metadata["manifest_sha256"] == preparation.BASELINE_SHA, "RECEIVER_BASELINE_CHANGED")
    return baseline, metadata["assets"]


def local_evidence(row, expected_name=None):
    path = Path(row["path"]).resolve(strict=True)
    p.require(path.is_relative_to(HERE.parent) and path.suffix == ".json", "ACCEPTANCE_EVIDENCE_OUTSIDE_QA")
    p.require(p.digest(path) == row["sha256"], "ACCEPTANCE_EVIDENCE_CHANGED")
    expected = expected_name if isinstance(expected_name, tuple) else (expected_name,)
    p.require(expected_name is None or path in {HERE.parent / name for name in expected}, "WRONG_ACCEPTANCE_CONTROLLER_SOURCE")
    value = p.read(path)
    p.require(value.get("candidate", {}).get("package", {}).get("sha256") == receiver.SHA, "ACCEPTANCE_CANDIDATE_REQUIRED")
    return path, value


def qa_driver(filename):
    # 只导入既有离线验证函数；不调用控制器Guest/HTTP入口。
    sys.path.insert(0, str(HERE.parent))
    spec = importlib.util.spec_from_file_location("publication_" + filename.replace("-", "_"), HERE.parent / filename)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module


def formatter_evidence(path, value):
    p.require(path.name == "summary.json" and re.fullmatch(r"win10-formatter-[a-f0-9]{12}", path.parent.name)
              and value.get("scope") == "original-win10-x64-installed-http-formatter"
              and value.get("status") == "passed" and value.get("formatter_acceptance_passed") is True
              and value.get("executable_checks_status") == "passed" and value.get("font_render_use") == "passed"
              and value.get("six_feature_native_host_proof") == "passed" and value.get("completed_at")
              and not value.get("error") and not value.get("cleanup_error"), "FINAL_WPS_CONTROLLER_ACCEPTANCE_REQUIRED")
    formatter = qa_driver("win10-installed-formatter.py")
    checks = value.get("checks", [])
    p.require(len({c.get("id") for c in checks}) == len(checks), "DUPLICATE_FORMATTER_CHECKS")
    checks = {c["id"]: c for c in checks}
    p.require(set(checks) == {"installed-wps-x64-font-files", "feature-native-source-host", "six-feature-http-output-contracts",
                            "format-and-pdf-golden", "five-font-visible-pdf-glyphs"}, "FORMATTER_CHECKS_INCOMPLETE")
    formatter.validate_wps(checks["installed-wps-x64-font-files"]["result"])
    native, http = checks["feature-native-source-host"], checks["six-feature-http-output-contracts"]
    feature_folder = path.parent
    resume = value.get("resume_sources")
    if resume is not None:
        p.require(isinstance(resume, dict) and re.fullmatch(r"win10-formatter-[a-f0-9]{12}", str(resume.get("run", "")))
                  and resume["run"] != path.parent.name, "FORMATTER_RESUME_SOURCE_INVALID")
        feature_folder = path.parent.parent / resume["run"]
        shared = formatter.load(formatter.ROOT / "qa/vm-lab/scripts/verify-win10-x86-installed-wps.py", "publication_resume_oracle")
        inherited, recomputed_sources = formatter.resume_font_evidence(feature_folder, value["candidate"], value.get("native_host_sha256"), shared.oracle)
        p.require(resume == recomputed_sources and all(checks.get(row["id"]) == row for row in inherited),
                  "FORMATTER_RESUME_EVIDENCE_CHANGED")
    feature_file = feature_folder / "installed-features-evidence.json"
    features = p.read(feature_file)
    p.require(http.get("status") == "passed" and http.get("cases") == 9 and http.get("image_decode") == "passed"
              and native.get("result") == formatter.verify_native_cases(features, native.get("proofs", {}), value.get("native_host_sha256")),
              "WPS_NATIVE_FEATURE_OUTPUTS_REQUIRED")
    build = p.read(preparation.BUILD); payload = Path(build["windows_payload"]["manifest"]["path"])
    p.require(p.digest(payload) == value["candidate"]["manifest_sha256"], "WPS_PAYLOAD_MANIFEST_CHANGED")
    hosts = [r for r in p.read(payload)["files"] if r.get("path") == "formatter-host/PartyOps.DocumentFormatter.Host.exe"]
    p.require(len(hosts) == 1 and hosts[0]["sha256"] == value.get("native_host_sha256"), "WPS_NATIVE_HOST_CANDIDATE_REQUIRED")
    outputs = []
    for case in features["cases"]:
        p.require(case.get("status") == "passed" and case.get("job", {}).get("state") == "completed", "FEATURE_TERMINAL_SUCCESS_REQUIRED")
        outputs += [(feature_folder, r["filename"], r["sha256"]) for r in case["outputs"]]
    golden, fonts = checks["format-and-pdf-golden"], checks["five-font-visible-pdf-glyphs"]
    p.require(golden.get("status") == fonts.get("status") == "passed"
              and len(golden.get("visual_comparisons", [])) == 2 and all(r.get("passed") is True for r in golden["visual_comparisons"])
              and len(golden.get("host_proofs", [])) == 5 and len(fonts.get("native_host_proofs", [])) == 1,
              "GOLDEN_AND_FONT_NATIVE_PROOFS_REQUIRED")
    for proof in golden["host_proofs"] + fonts["native_host_proofs"]:
        p.require("wps" in proof.get("host_display_name", "").lower() and proof.get("feature") in {"format", "convert"}
                  and all(re.fullmatch(r"[a-f0-9]{64}", proof.get(k, "")) for k in ("input_sha256", "request_sha256", "response_sha256")),
                  "GOLDEN_NATIVE_HOST_REQUIRED")
    font_rows = fonts.get("fonts", [])
    p.require(len(font_rows) == 5 and {r.get("font") for r in font_rows} == {r[0] for r in formatter.FONT_WITNESSES}
              and font_rows == formatter.font_render_proof(path.parent / "five-font-witness.pdf"),
              "ACTUAL_FIVE_FONT_RENDER_REQUIRED")
    outputs.append((path.parent, "five-font-witness.pdf", fonts.get("pdf_sha256")))
    outputs.append((path.parent, "five-font-witness.docx", fonts.get("input_sha256")))
    jobs = []
    for job_path in path.parent.glob("job-*.json"):
        job = p.read(job_path)
        p.require(re.fullmatch(r"[a-f0-9]{32}", str(job.get("id", ""))) and job_path.name == "job-" + job["id"] + ".json",
                  "FORMATTER_JOB_ID_BINDING_INVALID")
        jobs.append(job)
    expected_features = ["convert"] if resume is not None else ["convert", "convert", "format"]
    p.require(len(jobs) == len(expected_features) and sorted(j.get("feature") for j in jobs) == expected_features
              and all(j.get("state") == "completed" for j in jobs), "GOLDEN_DOWNLOADED_JOB_EVIDENCE_REQUIRED")
    proof_key = lambda row: tuple(row.get(key) for key in ("input_sha256", "request_sha256", "response_sha256"))
    expected_proofs = fonts["native_host_proofs"] if resume is not None else golden["host_proofs"] + fonts["native_host_proofs"]
    expected_proofs = {proof_key(row): row for row in expected_proofs}
    observed = set()
    for job in jobs:
        p.require(len(job.get("host_proofs", [])) == len(job["outputs"]) > 0, "FORMATTER_JOB_NATIVE_OUTPUT_PROOFS_REQUIRED")
        for item, proof in zip(job["outputs"], job["host_proofs"]):
            key = proof_key(proof)
            native_outputs = proof.get("outputs", [])
            target = (path.parent / item["file"]).resolve(strict=True)
            p.require(key not in observed and expected_proofs.get(key) == proof and len(native_outputs) == 1
                      and target.parent == path.parent and target.stat().st_size == native_outputs[0].get("bytes")
                      and target.suffix.lower() == native_outputs[0].get("extension")
                      and p.digest(target) == item["sha256"] == native_outputs[0].get("sha256"), "FORMATTER_NATIVE_DOWNLOAD_BINDING_CHANGED")
            observed.add(key)
            outputs.append((path.parent, item["file"], item["sha256"]))
    p.require(observed == set(expected_proofs), "FORMATTER_NATIVE_JOB_SET_CHANGED")
    p.require(fonts["native_host_proofs"][0]["input_sha256"] == fonts.get("input_sha256")
              and fonts["native_host_proofs"][0]["outputs"][0]["sha256"] == fonts.get("pdf_sha256"), "FONT_NATIVE_DOWNLOAD_BINDING_CHANGED")
    if resume is not None:
        outputs += [(feature_folder, name, p.digest(feature_folder / name)) for name in
                    ("formatted-1.docx", "formatted-2.docx", "render-0.pdf", "render-1.pdf", "render-2.pdf")]
    for folder, name, sha in outputs:
        target = (folder / name).resolve(strict=True)
        p.require(target.parent == folder and target.stat().st_size > 0 and p.digest(target) == sha, "FORMATTER_DOWNLOADED_OUTPUT_CHANGED")
    result = {"summary_sha256": p.digest(path), "feature_evidence_sha256": p.digest(feature_file), "output_count": len(outputs)}
    if resume is not None:
        result["resume_sources"] = recomputed_sources
    return result


def formal_gate():
    gate, build = p.read(GATE), p.read(preparation.BUILD)
    source = gate.get("source_fingerprints", {}).get("package", {}).get("sha256")
    p.require(gate.get("status") == "passed" and gate.get("suite") == "scripts/test.ps1"
              and source == build.get("source_before") == build.get("source_after")
              and build.get("status") == "completed" and build.get("exit_code") == 0 and not build.get("errors")
              and build.get("package", {}).get("sha256") == receiver.SHA
              and build.get("package", {}).get("bytes") == receiver.SIZE
              and build.get("windows_payload", {}).get("package_sha256") == receiver.SHA,
              "CURRENT_WINDOWS_FORMAL_GATE_BUILD_BINDING_REQUIRED")
    return {"status": "passed", "tested_at": gate["tested_at"], "source_fingerprint": source,
            "gate_receipt_sha256": p.digest(GATE), "build_receipt_sha256": p.digest(preparation.BUILD),
            "package_sha256": receiver.SHA}


def acceptance_evidence(path=None):
    # 用户已授权验收通过后发布；这里只核事实证据，不追加人工审批文件或确认。
    path = path or HERE / "acceptance-evidence.json"
    p.require(Path(path).exists(), "BLOCKED_PENDING_REAL_HOST_WPS_ACCEPTANCE")
    path = Path(path).resolve(strict=True)
    p.require(path.parent == HERE, "EVIDENCE_INDEX_MUST_BE_IN_THIS_BATCH")
    value = p.read(path)
    p.require("windows_acceptance" not in value, "EXTERNAL_WINDOWS_ACCEPTANCE_OVERRIDE_REJECTED")
    names = {"host_acceptance": "first-host-approved-existing-root-submit.result.json",
             "personal_restore": ("restore-personal-submit.result.json", "restore-personal-observed-completion.json"), "retained_business": "retention-after-host-restore.json",
             "wps_acceptance": None}
    p.require(set(value) == set(names), "DISTINCT_HOST_RESTORE_RETENTION_WPS_EVIDENCE_REQUIRED")
    paths = [Path(value[name]["path"]).resolve(strict=True) for name in names]
    p.require(len(set(paths)) == len(names), "ACCEPTANCE_SOURCES_MUST_BE_DISTINCT")
    loaded = {name: local_evidence(value[name], expected) for name, expected in names.items()}
    host = loaded["host_acceptance"][1]; binding = host["candidate"]
    p.require(all(row[1].get("candidate") == binding for row in loaded.values()), "ACCEPTANCE_CANDIDATES_DIFFER")
    first = qa_driver("first-host-qa.py")
    status = host.get("host_status", {})
    p.require(host.get("status") == "bootstrap-page-observed" and host.get("case") == "approved-existing-root"
              and host.get("http_status") == 200 and host.get("bootstrap_admin_page") is True and host.get("public_error_codes") == []
              and status.get("present") is True and status.get("stage") == "ready" and status.get("code") == ""
              and type(status.get("pid")) is int and status["pid"] > 0 and host.get("user_mode") == host.get("system_mode") == "host"
              and host.get("qa_host_data_directory") == first.ROOT_HOST_DATA
              and host.get("original_personal_restore_directory") == first.PERSONAL_DATA
              and host.get("attempt_sha256") == p.digest(HERE.parent / "first-host-approved-existing-root-submit.attempt.json"),
              "REAL_TERMINAL_HOST_ACCEPTANCE_REQUIRED")
    restore = loaded["personal_restore"][1]; retention = loaded["retained_business"][1]
    business = qa_driver("verify-retained-business.py")
    restored = business.restored_personal(restore, binding, p.digest(HERE.parent / "restore-personal-submit.attempt.json"))
    intent = p.read(HERE.parent / "restore-personal-submit.attempt.json")
    p.require(intent.get("candidate") == binding and intent.get("host_success_sha256") == value["host_acceptance"]["sha256"],
              "RESTORE_HOST_EVIDENCE_BINDING_REQUIRED")
    p.require(retention.get("status") == "passed" and retention.get("phase") == "after-host-restore"
              and retention.get("personal_restore_completed_sha256") == value["personal_restore"]["sha256"]
              and retention.get("guest_uuid") == business.contract.UUID
              and retention.get("install_completed_sha256") == p.digest(HERE.parent / "overlay-install/completed.json"),
              "POST_RESTORE_BUSINESS_ACCEPTANCE_REQUIRED")
    baseline = p.read(HERE.parent / "retention-baseline.json")
    p.require(all(retention.get(key) == baseline[key] for key in ("retained", "calendar_id", "calendar_sha256")), "ORIGINAL_BUSINESS_BASELINE_CHANGED")
    business.same_restored_runtime(retention["runtime"], restored, binding)
    formatter = formatter_evidence(*loaded["wps_acceptance"])
    gate = formal_gate()
    row = {"id": "windows_amd64", "filename": receiver.NAME,
        "platform": "windows/amd64", "package_sha256": receiver.SHA, "size": receiver.SIZE,
        "source_fingerprint": gate["source_fingerprint"], "build_receipt_sha256": gate["build_receipt_sha256"],
        "build_provenance_status": "verified", "full_lifecycle_verified": False,
        "targets": [{"target": "win10-x64", "scope": "installed-candidate-host-uac-and-wps",
            "host_receipt_sha256": value["host_acceptance"]["sha256"], "wps_receipt_sha256": value["wps_acceptance"]["sha256"],
            "personal_restore_sha256": value["personal_restore"]["sha256"], "retained_business_sha256": value["retained_business"]["sha256"],
            "formatter_outputs": formatter,
            "full_lifecycle_verified": False}], "limitations": ["其他九包保留历史验收范围，不声明本轮重新完成全部平台生命周期。"]}
    p.require(row.get("id") == "windows_amd64" and row.get("filename") == receiver.NAME
              and row.get("package_sha256") == receiver.SHA and row.get("size") == receiver.SIZE
              and row.get("platform") == "windows/amd64" and row.get("full_lifecycle_verified") is False,
              "WINDOWS_ACCEPTANCE_SCOPE_CHANGED")
    return {**value, "windows_acceptance": row}


def compose(baseline, approved=None, generated_at=None):
    value = copy.deepcopy(baseline); value.pop("signature", None)
    value["assets"] = [{"filename": receiver.NAME, "size": receiver.SIZE, "sha256": receiver.SHA}
                       if row["filename"] == preparation.NAME else row for row in value["assets"]]
    blocked = {"id": "windows_amd64", "filename": receiver.NAME, "platform": "windows/amd64", "package_sha256": receiver.SHA,
        "size": receiver.SIZE, "acceptance_status": "blocked-pending-real-host-uac-and-wps-acceptance",
        "targets": [{"target": "win10-x64", "status": "blocked", "full_lifecycle_verified": False}],
        "full_lifecycle_verified": False, "limitations": ["不得以构建、覆盖和重启回执代替真实主机/UAC及WPS排版验收。"]}
    value["acceptance"]["packages"] = [copy.deepcopy(approved["windows_acceptance"] if approved else blocked)
        if row["id"] == "windows_amd64" else row for row in value["acceptance"]["packages"]]
    value.pop("kylin_publication_scope", None)
    value.update(batch_profile=receiver.PROFILE, baseline_manifest_sha256=preparation.BASELINE_SHA,
        windows_publication_scope=SCOPE, publication_authorized=approved is not None,
        publication_status="acceptance-evidence-complete-windows-only" if approved else "blocked-pending-real-host-uac-and-wps-acceptance",
        publication_authorization="existing-user-authorization-after-real-acceptance",
        formal_release_ready=approved is not None, formal_gate="FULL_FUNCTION_GATE_PASSED",
        formal_gate_evidence=formal_gate(), full_lifecycle_verified=False,
        generated_at=generated_at or now(), publication_tooling={"historical_baseline_manifest_sha256": preparation.BASELINE_SHA,
            "controller_sha256": p.digest(Path(__file__)), "windows_receiver_factory_sha256": p.digest(HERE / "windows_receiver_factory.py"),
            "pinned_protocol_factory_sha256": receiver.SOURCE_SHA})
    return value


def validate_manifest(value, baseline, approved):
    unsigned = copy.deepcopy(value); unsigned.pop("signature", None)
    for row in unsigned["assets"]:
        if row["filename"] == receiver.NAME: row.pop("signature", None)
    p.require(unsigned == compose(baseline, approved, value["generated_at"]), "WINDOWS_ONLY_MANIFEST_SCOPE_CHANGED")
    p.require([a for a in value["assets"] if a["filename"] != receiver.NAME] ==
              [a for a in baseline["assets"] if a["filename"] != preparation.NAME], "OTHER_NINE_ASSETS_CHANGED")
    p.require([a for a in value["acceptance"]["packages"] if a["id"] != "windows_amd64"] ==
              [a for a in baseline["acceptance"]["packages"] if a["id"] != "windows_amd64"], "OTHER_NINE_ACCEPTANCE_CHANGED")


def signed_manifest(path, approved):
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
    baseline, _ = inputs(); value = p.read(path); validate_manifest(value, baseline, approved)
    key = Ed25519PublicKey.from_public_bytes(p.trusted_key())
    p.verify_signature(baseline, key); p.verify_signature(value, key)
    p.require(value["public_key"] == baseline["public_key"], "TRUSTED_PUBLIC_KEY_CHANGED")
    for row in value["assets"]: p.verify_signature(row, key)
    return value


def package_link(folder):
    p.require(preparation.PACKAGE.stat().st_size == receiver.SIZE and p.digest(preparation.PACKAGE) == receiver.SHA, "FROZEN_WINDOWS_CHANGED")
    with preparation.PACKAGE.open("rb") as source: p.require(source.read(4).hex() == "4d5a5000", "WINDOWS_MAGIC_CHANGED")
    os.link(preparation.PACKAGE, folder / receiver.NAME)
    with (folder / (receiver.NAME + ".sha256")).open("x", encoding="utf-8", newline="\n") as stream:
        stream.write(receiver.SHA + "  " + receiver.NAME + "\n")


def prepare_bundle(manifest=None, evidence_path=None):
    approved = acceptance_evidence(evidence_path) if manifest else None
    baseline, retained = inputs()
    if manifest: signed_manifest(manifest, approved)
    folder = HERE / ("batch" if manifest else "draft")
    folder.mkdir(exist_ok=False)
    package_link(folder)
    if manifest:
        raw = Path(manifest).read_bytes(); name = MANIFEST_NAME
    else:
        raw = json.dumps(compose(baseline), ensure_ascii=False, indent=2).encode("utf-8"); name = "manifest-signing-input.json"
    with (folder / name).open("xb") as stream: stream.write(raw)
    digest = hashlib.sha256(raw).hexdigest()
    target = folder / "receiver"; target.mkdir()
    with (target / "_serve.js").open("x", encoding="utf-8", newline="\n") as stream:
        stream.write(receiver.build_receiver(retained, digest))
    p.write(target / "upload-metadata.json", {"receiver": receiver.PROFILE, "version": "1.4.5-rc.6",
        "manifest_sha256": digest, "baseline_manifest_sha256": preparation.BASELINE_SHA, "assets": retained + [receiver.candidate()]})
    with (target / "index.html").open("x", encoding="utf-8") as stream:
        stream.write('<!doctype html><meta charset="utf-8"><title>PartyOps 下载服务</title>')
    # blocked draft不生成令牌；正式signed batch也须先经过真实主控验收授权。
    if approved:
        fd = os.open(target / ".upload-token", os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as stream: json.dump({receiver.NAME: secrets.token_hex(32)}, stream)
    binding = {"scope": SCOPE, "receiver": receiver.PROFILE, "manifest_sha256": digest,
        "baseline_manifest_sha256": preparation.BASELINE_SHA, "filename": receiver.NAME, "bytes": receiver.SIZE,
        "package_sha256": receiver.SHA, "receiver_sha256": p.digest(target / "_serve.js"),
        "publication_authorized": approved is not None, "formal_release_ready": approved is not None,
        "formal_gate": "FULL_FUNCTION_GATE_PASSED"}
    p.write(folder / "binding.json", binding)
    p.write(folder / "preparation-receipt.json", {"status": "local-prepared-no-network", "binding": binding,
        "network_write_allowed": False, "formal_gate": "FULL_FUNCTION_GATE_PASSED", "old_windows_withdrawal_allowed": False, "retained_existing_files": 10,
        "retained_other_active_packages": 9, "token_created": approved is not None, "bridge_supported": False})
    return {"status": "local-prepared", "folder": str(folder), "publication_authorized": approved is not None, "network_write_allowed": False}


def signing_input(evidence_path):
    approved = acceptance_evidence(evidence_path) if evidence_path or (HERE / "acceptance-evidence.json").exists() else None
    baseline, _ = inputs(); target = HERE / "manifest-signing-input.json"
    p.write(target, compose(baseline, approved))
    return {"status": "awaiting-controller-signature" if approved else "blocked-unsigned-input", "path": str(target), "publication_authorized": approved is not None}


def checked_batch(evidence_path):
    approved = acceptance_evidence(evidence_path); folder = HERE / "batch"; signed_manifest(folder / MANIFEST_NAME, approved)
    binding = p.read(folder / "binding.json")
    _, retained = inputs()
    expected = {"scope": SCOPE, "receiver": receiver.PROFILE, "manifest_sha256": p.digest(folder / MANIFEST_NAME),
        "baseline_manifest_sha256": preparation.BASELINE_SHA, "filename": receiver.NAME, "bytes": receiver.SIZE,
        "package_sha256": receiver.SHA, "receiver_sha256": p.digest(folder / "receiver/_serve.js"),
        "publication_authorized": True, "formal_release_ready": True, "formal_gate": "FULL_FUNCTION_GATE_PASSED"}
    p.require(binding == expected and p.digest(folder / receiver.NAME) == receiver.SHA, "WINDOWS_BATCH_BINDING_CHANGED")
    p.require((folder / "receiver/_serve.js").read_text(encoding="utf-8") == receiver.build_receiver(retained, binding["manifest_sha256"]), "WINDOWS_RECEIVER_CHANGED")
    return folder, binding


def file_health(health, binding, retained):
    p.require(health.get("ok") is True and health.get("receiver") == receiver.PROFILE
              and health.get("manifest_sha256") == binding["manifest_sha256"]
              and set(health.get("files", {})) == {r["file_name"] for r in retained} | {receiver.NAME}, "WINDOWS_RECEIVER_HEALTH_CHANGED")
    for row in retained:
        actual = health["files"][row["file_name"]]
        p.require(actual.get("file_ready") is True and actual.get("reused") is True
                  and (actual.get("bytes"), actual.get("sha256")) == (row["bytes"], row["sha256"]), "EXISTING_PACKAGE_NOT_PRESERVED")
    current = health["files"][receiver.NAME]
    p.require((current.get("bytes"), current.get("sha256")) == (receiver.SIZE, receiver.SHA), "WINDOWS_REMOTE_ASSET_CHANGED")
    return current


def read_only_recover(execute=False, evidence_path=None):
    p.require(execute is True, "EXPLICIT_READ_ONLY_NETWORK_REQUIRED")
    folder, binding = checked_batch(evidence_path); _, retained = inputs()
    client = p.release_client()  # 加载已钉住的GET/全量回读协议；不读取令牌。
    status, _, raw = client.request("GET", BASE_URL + "/health")
    p.require(status == 200, "READ_ONLY_HEALTH_FAILED")
    health = json.loads(raw); current = file_health(health, binding, retained)
    result = {"status": "pending-candidate" if not current["file_ready"] else "candidate-present-needs-full-readback",
              "binding": binding, "observation_at": now(), "network_write_allowed": False,
              "formal_release_ready": binding["formal_release_ready"], "publication_completed": False}
    if current["file_ready"]:
        asset = {"filename": receiver.NAME, "size": receiver.SIZE, "sha256": receiver.SHA}
        client.verify_release = lambda *_a, **_kw: [asset]
        client.file_health = lambda h, *_a, **_kw: file_health(h, binding, retained)
        result["readback"] = client.verify_remote(folder, receiver.NAME, BASE_URL, profile=receiver.PROFILE,
                                                  expected_manifest_sha=binding["manifest_sha256"])
        result["status"] = "read-only-full-get-matched"
    target = folder / ("read-only-recovery-" + datetime.datetime.now().strftime("%Y%m%d%H%M%S%f") + ".json")
    p.write(target, result)
    return {"status": result["status"], "observation": str(target), "network_write_allowed": False}


def upload(evidence_path, execute=False):
    p.require(execute is True, "EXPLICIT_NETWORK_WRITE_REQUIRED")
    folder, binding = checked_batch(evidence_path)  # blocked时必须在令牌/客户端读取之前退出。
    deployment = p.read(folder / "deployment-receipt.json")
    p.require(deployment.get("status") == "received" and deployment.get("binding") == binding
              and deployment.get("base_url") == BASE_URL and deployment.get("current_backend_preflight_verified") is True,
              "CURRENT_WINDOWS_DEPLOYMENT_RECEIPT_REQUIRED")
    p.write(folder / "windows-upload.intent.json", {"binding": binding, "time_utc": now(), "deployment_sha256": p.digest(folder / "deployment-receipt.json")})
    client = p.release_client(); _, retained = inputs()
    asset = {"filename": receiver.NAME, "size": receiver.SIZE, "sha256": receiver.SHA}
    client.verify_release = lambda *_a, **_kw: [asset]
    client.file_health = lambda h, *_a, **_kw: file_health(h, binding, retained)
    result = client.upload_one(folder, receiver.NAME, BASE_URL, folder / "receiver/.upload-token", profile=receiver.PROFILE,
                               expected_manifest_sha=binding["manifest_sha256"])
    p.write(folder / "windows-domestic-readback.json", {"binding": binding, "result": result, "completed_at": now(),
        "other_nine_preserved": True, "old_windows_preserved": True, "formal_release_ready": binding["formal_release_ready"],
        "publication_completed": False})
    return {"status": "domestic-readback-recorded", "next_stage": "github-append-and-readback-before-website-under-existing-user-authorization"}


def refresh_draft_status():
    """只修正本地未授权draft，先保留原字节与摘要，不触碰prepared或历史发布。"""
    folder = HERE / "draft"
    p.require(folder.is_dir() and not (folder / "receiver/.upload-token").exists(), "ONLY_BLOCKED_LOCAL_DRAFT_CAN_REFRESH")
    old_binding = p.read(folder / "binding.json")
    p.require(old_binding.get("publication_authorized") is False, "AUTHORIZED_BATCH_CANNOT_BE_REFRESHED")
    archive = HERE / "formal-gate-correction-20261017"; archive.mkdir(exist_ok=False)
    names = ["manifest-signing-input.json", "binding.json", "preparation-receipt.json", "receiver/_serve.js", "receiver/upload-metadata.json"]
    old = {}
    for name in names:
        target = archive / name; target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(folder / name, target); old[name] = p.digest(target)
    baseline, retained = inputs()
    value = compose(baseline)
    (folder / "manifest-signing-input.json").write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    sha = p.digest(folder / "manifest-signing-input.json")
    (folder / "receiver/_serve.js").write_text(receiver.build_receiver(retained, sha), encoding="utf-8", newline="\n")
    meta = p.read(folder / "receiver/upload-metadata.json"); meta["manifest_sha256"] = sha
    (folder / "receiver/upload-metadata.json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
    old_binding.update(manifest_sha256=sha, receiver_sha256=p.digest(folder / "receiver/_serve.js"),
                       formal_gate="FULL_FUNCTION_GATE_PASSED", formal_release_ready=False)
    (folder / "binding.json").write_text(json.dumps(old_binding, ensure_ascii=False, indent=2), encoding="utf-8")
    receipt = p.read(folder / "preparation-receipt.json"); receipt.update(binding=old_binding,
        formal_gate="FULL_FUNCTION_GATE_PASSED", publication_block_reason="guest-host-and-wps-evidence-pending")
    (folder / "preparation-receipt.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=2), encoding="utf-8")
    p.write(archive / "correction.json", {"corrected_at": now(), "reason": "本轮正式全套门禁已通过；不得继承历史麒麟STALE例外。",
        "prior_bytes": old, "current_gate": formal_gate(), "network_write_allowed": False,
        "authorization": "用户已有验收通过后发布授权；只等待真实Guest证据，无新增审批。"})
    return {"status": "local-draft-gate-corrected", "formal_gate": "FULL_FUNCTION_GATE_PASSED",
            "formal_release_ready": False, "network_write_allowed": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["draft", "refresh-draft-status", "signing-input", "prepare-signed", "recover", "upload"])
    parser.add_argument("--evidence", type=Path); parser.add_argument("--manifest", type=Path)
    parser.add_argument("--execute-read-only", action="store_true"); parser.add_argument("--execute-network", action="store_true")
    args = parser.parse_args()
    if args.mode == "draft": result = prepare_bundle()
    elif args.mode == "refresh-draft-status": result = refresh_draft_status()
    elif args.mode == "signing-input": result = signing_input(args.evidence)
    elif args.mode == "prepare-signed":
        p.require(args.manifest is not None, "SIGNED_MANIFEST_REQUIRED"); result = prepare_bundle(args.manifest, args.evidence)
    elif args.mode == "recover": result = read_only_recover(args.execute_read_only, args.evidence)
    else: result = upload(args.evidence, args.execute_network)
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__": main()
