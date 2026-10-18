"""20261018 新候选离线发布准备；不连接 Guest，不执行部署或读取凭据。"""
from __future__ import annotations
import argparse
import copy
import datetime
import importlib.util
import json
import os
from pathlib import Path
import re
import sys
from contextlib import contextmanager
from types import SimpleNamespace

import frozen_domain as domain

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
OLD = HERE.parent / "publication-windows-20261017"
QA = ROOT / "qa/acceptance/windows-timeout-repair-20261018"
PRIOR = Path("C:/PartyOps-VM-Lab/records/kylin-20261012/acceptance/publication-20261016")
BASELINE = PRIOR / "batch/release-manifest-kylin-arm64-20261016.json"
BASELINE_SHA = "b52807452932502a5f05a10eb609c87345fabe4addb17d3ffeae3a673e0d0e57"
NAME = "PartyOps_1.4.5-rc.6_windows_amd64_startup-20261018.exe"
MANIFEST = "release-manifest-windows-startup-20261018.json"
PROFILE = "windows-startup-20261018-v1"
require, read, digest, evidence, exclusive = domain.require, domain.read, domain.digest, domain.evidence, domain.exclusive
sys.path.insert(0, str(ROOT / "qa/vm-lab"))
sys.path.insert(0, str(HERE.parent))


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def history():
    # 仅调用旧验证函数；不调用旧发布入口，不修改其 hold 或全局候选。
    sys.path.insert(0, str(OLD))
    try:
        return load("historical_windows_publication_readonly", OLD / "publication.py")
    finally:
        sys.path.remove(str(OLD))


def blocked_hold(sha):
    require(sha != domain.OLD_SHA, "HELD_PREVIOUS_CANDIDATE_REJECTED")
    require(not (HERE / "PUBLICATION_HOLD.json").exists(), "CURRENT_BATCH_HELD")
    hold = read(OLD / "PUBLICATION_HOLD.json")
    require(hold.get("blocked_package_sha256") != sha, "HELD_CANDIDATE_REJECTED")


def candidate(package, sha, receipt, gate_path):
    require(isinstance(sha, str) and re.fullmatch(r"[0-9a-f]{64}", sha) is not None, "EXPLICIT_ACTUAL_SHA_REQUIRED")
    blocked_hold(sha)
    package = Path(package).resolve(strict=True)
    require(package.is_relative_to((ROOT / "artifacts").resolve()) and package.stat().st_size > 0
            and digest(package) == sha, "ACTUAL_WORKSPACE_PACKAGE_REQUIRED")
    with package.open("rb") as stream:
        require(stream.read(4).hex() == "4d5a5000", "WINDOWS_PACKAGE_MAGIC_REQUIRED")
    record, _, _ = domain.official_payload(receipt, sha)
    gate = read(gate_path)
    from lab import fingerprint
    source = fingerprint()
    require(re.fullmatch(r"[0-9a-f]{64}", source) is not None
            and gate.get("status") == "passed" and gate.get("suite") == "scripts/test.ps1"
            and gate.get("source_fingerprints", {}).get("package", {}).get("sha256") == source
            and record.get("source_before") == record.get("source_after") == source
            and record["package"].get("bytes") == package.stat().st_size,
            "NEW_FORMAL_GATE_BUILD_SOURCE_BINDING_REQUIRED")
    import retention_contract as contract
    binding = contract.candidate(SimpleNamespace(root=domain.LAB), ROOT, package, sha)
    require(Path(binding["package"]["build_receipt"]).resolve() == Path(receipt).resolve(), "EXPLICIT_FORMAL_RECEIPT_REQUIRED")
    return {"candidate": binding, "package": evidence(package), "build": evidence(receipt),
            "gate": evidence(gate_path), "source_fingerprint": source,
            "fix_sources": [evidence(ROOT / "backend/app" / name) for name in ("setup_wizard.py", "windows_elevation.py")]}


def phase_links(rows, binding, contract, sha):
    """按现有控制器字段核对阶段关联；sha映射来自固定本批次文件的实际摘要。"""
    boot = rows["retention-boot-before.json"]
    attempt = rows["normal-reboot-attempt.json"]
    require(attempt.get("context") == contract.reboot_context(binding)
            and attempt.get("guest_uuid") == contract.UUID and attempt.get("disk") == str(contract.DISK.resolve())
            and attempt.get("boot_before_sha256") == sha["retention-boot-before.json"], "NEW_REBOOT_ATTEMPT_BINDING_REQUIRED")
    overlay = rows["retention-after-overlay.json"]
    after = rows["retention-after-reboot.json"]
    for row in (overlay, after):
        require(row.get("install_completed_sha256") == sha["overlay-install/completed.json"], "NEW_RETENTION_INSTALL_CHAIN_REQUIRED")
    require(overlay["runtime"].get("boot_id") == boot["boot_id"]
            and overlay["runtime"].get("probe_boot_id") == boot["probe_boot_id"], "NEW_OVERLAY_BOOT_CHAIN_REQUIRED")
    prepared = rows["first-host-prepare.result.json"]
    require(prepared.get("status") == "prepared"
            and prepared.get("attempt_sha256") == sha["first-host-prepare.attempt.json"],
            "NEW_HOST_PREPARE_CHAIN_REQUIRED")


@contextmanager
def qa_helpers():
    """QA helper 的路径及同名模块仅在验证期间绑定，退出或异常均恢复。"""
    original_path = sys.path[:]
    previous = {name: sys.modules.get(name) for name in ("controller", "reboot_evidence")}
    try:
        sys.path.insert(0, str(QA))
        c = load("new_candidate_offline_controller", QA / "controller.py")
        sys.modules["controller"] = c
        reboot = load("new_candidate_reboot_evidence", QA / "reboot_evidence.py")
        sys.modules["reboot_evidence"] = reboot
        yield c, reboot
    finally:
        sys.path[:] = original_path
        for name, module in previous.items():
            if module is None: sys.modules.pop(name, None)
            else: sys.modules[name] = module


def reboot_files():
    """显式二选一；恢复事件文件名须先校验，绝不读任意回执指定路径。"""
    standard = (QA / "normal-reboot-completed.json").exists()
    recovered = (QA / "reboot-recovery-completed.json").exists()
    require(standard != recovered, "EXACTLY_ONE_NEW_REBOOT_COMPLETION_REQUIRED")
    if standard: return ["normal-reboot-completed.json"]
    receipt = read(QA / "reboot-recovery-completed.json")
    filename = receipt.get("events_file")
    require(isinstance(filename, str) and re.fullmatch(r"reboot-recovery-events-[a-f0-9]{12}\.json", filename) is not None,
            "RECOVERY_SYSTEM_EVENTS_BASENAME_REQUIRED")
    path = (QA / filename).resolve(strict=True)
    require(path.parent == QA.resolve(), "RECOVERY_SYSTEM_EVENTS_MUST_STAY_IN_QA")
    return ["reboot-recovery-completed.json", "recover-start.attempt.json", "recover-start.result.json", filename]


def reboot_chain(rows, binding, boot, contract, reboot_helper):
    """保留原 QMP 门禁；恢复链交由共享适配核实际Windows事件及各摘要。"""
    if "reboot-recovery-completed.json" in rows:
        validator = load("new_reboot_offline_validator", ROOT / "qa/vm-lab/scripts/reboot-windows.py")
        state = read(contract.DISK.parent / "vm.json")
        reboot_helper.validate_retention(binding, rows["retention-after-reboot.json"], state, validator.validate_restart, QA)
        return
    reboot = rows["normal-reboot-completed.json"]
    restart = reboot.get("restart", {})
    require(reboot.get("attempt_sha256") == digest(QA / "normal-reboot-attempt.json")
            and reboot.get("boot_before_sha256") == digest(QA / "retention-boot-before.json")
            and restart.get("status") == "passed" and restart.get("actual") is True
            and boot["boot_id"] != rows["retention-after-reboot.json"]["runtime"]["boot_id"], "ACTUAL_NEW_NORMAL_REBOOT_REQUIRED")
    validator = load("new_reboot_offline_validator", ROOT / "qa/vm-lab/scripts/reboot-windows.py")
    validator.validate_restart(restart, rows["normal-reboot-attempt.json"]["context"], {"pid": restart.get("qemu_pid_after")})
    require(restart["identity_before"]["boot_id"] == boot["probe_boot_id"]
            and restart["identity_after"]["boot_id"] == rows["retention-after-reboot.json"]["runtime"]["probe_boot_id"]
            and rows["normal-reboot-attempt.json"].get("overlay_sha256") == digest(QA / "retention-after-overlay.json"), "NEW_REBOOT_CHAIN_REQUIRED")


def guest_evidence(binding):
    """只读验证同一新候选实际链；旧主机回执不能充当新包验收。"""
    with qa_helpers() as (c, reboot_helper):
        return _guest_evidence(binding, c, reboot_helper)


def host_round(binding, c):
    """已确认失败事务恢复后，只消费独立新请求；旧failed及恢复链一同归档。"""
    recovered = (QA / "host-journal-recovery-verified.json").exists() or (QA / "after-journal-first-host-prepare.attempt.json").exists()
    if not recovered: return "", [], []
    helper = load("confirmed_host_recovery_validator", QA / "host-after-journal.py")
    value = helper.recovery(binding)
    source = value["launch_source"]
    names = ["first-host-prepare.attempt.json", "first-host-prepare.result.json",
             "first-host-approved-existing-root-submit.attempt.json", "first-host-approved-existing-root-submit.result.json",
             "host-journal-recovery-before.json", "host-journal-reopen.attempt.json", "host-journal-recovery-verified.json",
             source + ".attempt.json", source + ".result.json"]
    diagnostics = []
    if source in {"host-journal-resume-launch", "host-journal-cancelled-relaunch", "host-journal-cancelled-start"}:
        diagnostics = ["host-journal-reopen-readonly-diagnosis.json", "host-journal-reopen-failed-script.ps1"]
    if source in {"host-journal-cancelled-relaunch", "host-journal-cancelled-start"}:
        names += ["host-journal-resume-launch.attempt.json", "host-journal-resume-launch.result.json", "host-journal-cancelled-before.json"]
    if source == "host-journal-cancelled-start":
        names += ["host-journal-cancelled-relaunch.attempt.json", "host-journal-cancelled-relaunch.result.json",
                  "host-journal-cancelled-relaunch-readonly-diagnosis.json"]
    if (QA / "after-journal-restore-personal-recover-prepare.attempt.json").exists():
        restore_adapter = load("confirmed_recovered_personal_validator", QA / "restore-after-journal.py")
        restore_names, restore_diagnostics = restore_adapter.validate_recovered(binding)
        names += restore_names
        diagnostics += restore_diagnostics
        if (QA / "after-journal-restore-personal-observed-completion.json").exists():
            restore_adapter.validate_completion(binding)
            names += ["after-journal-restore-personal-verify-completion.attempt.json",
                      "after-journal-restore-personal-observed-completion.json",
                      "after-journal-restore-personal-after-submit-failure-inspect.json"]
    return helper.PREFIX, list(dict.fromkeys(names)), diagnostics


def _guest_evidence(binding, c, reboot_helper):
    contract = c.contract
    inputs, boot = c.boot_inputs()
    require(boot.get("candidate") == binding, "NEW_BOOT_BINDING_REQUIRED")
    contract.completed_install(QA / "overlay-install", binding)
    baseline = read(c.OLD / "retention-baseline.json")
    prefix, recovered_names, diagnostics = host_round(binding, c)
    host_names = ["first-host-prepare.attempt.json", "first-host-prepare.result.json",
             "first-host-approved-existing-root-submit.attempt.json", "first-host-approved-existing-root-submit.result.json",
             "restore-personal-submit.attempt.json", "restore-personal-submit.result.json", "retention-after-host-restore.json"]
    names = ["retention-boot-before.json", "overlay-install/attempt.json", "overlay-install/completed.json",
             "retention-after-overlay.json", "normal-reboot-attempt.json",
             "retention-after-reboot.json"] + [prefix + name for name in host_names] + reboot_files() + recovered_names
    names = list(dict.fromkeys(names))
    rows = {name: read(QA / name) for name in names}
    require(all(row.get("candidate") == binding for row in rows.values()), "NEW_CANDIDATE_RECEIPTS_REQUIRED")
    # 仅内存中映射成功轮次的固定字段名；返回/签名证据仍保留真实独立文件名和摘要。
    selected = {**rows, **{name: rows[prefix + name] for name in host_names}}
    sha = {name: digest(QA / name) for name in names}
    sha.update({name: digest(QA / (prefix + name)) for name in host_names})
    phase_links(selected, binding, contract, sha)
    if prefix:
        require(selected["first-host-prepare.attempt.json"].get("recovery_verified_sha256") == digest(QA / "host-journal-recovery-verified.json")
                and selected["first-host-approved-existing-root-submit.attempt.json"].get("recovery_verified_sha256") == digest(QA / "host-journal-recovery-verified.json"),
                "INDEPENDENT_HOST_REQUEST_AFTER_CONFIRMED_RECOVERY_REQUIRED")
    rows = selected
    for name in ("retention-after-overlay.json", "retention-after-reboot.json", "retention-after-host-restore.json"):
        row = rows[name]
        require(row.get("status") == "passed" and row.get("inputs") == inputs
                and all(row.get(key) == baseline[key] for key in ("retained", "calendar_id", "calendar_sha256")),
                "NEW_ORIGINAL_BUSINESS_RETENTION_REQUIRED")
        contract.runtime_matches(row["runtime"], binding)
    reboot_chain(rows, binding, boot, contract, reboot_helper)
    host = load("new_host_offline_validator", QA / "host-personal.py")
    first = c.load("new_first_offline", c.OLD / "first-host-qa.py")
    host_file = "first-host-approved-existing-root-submit.result.json"
    host.host_success(rows[host_file], binding, digest(QA / (prefix + host_file.replace(".result", ".attempt"))), first)
    verify = c.load("new_restore_offline_validator", c.OLD / "verify-retained-business.py")
    restore_completion = prefix + "restore-personal-submit.result.json"
    if prefix and (QA / (prefix + "restore-personal-observed-completion.json")).exists():
        restore_adapter = load("publication_observed_personal_validator", QA / "restore-after-journal.py")
        restored = restore_adapter.validate_completion(binding)
        restore_completion = prefix + "restore-personal-observed-completion.json"
    else:
        restored = verify.restored_personal(rows["restore-personal-submit.result.json"], binding,
                                           digest(QA / (prefix + "restore-personal-submit.attempt.json")))
    require(rows["restore-personal-submit.attempt.json"].get("host_success_sha256") == digest(QA / (prefix + host_file)), "NEW_HOST_RESTORE_CHAIN_REQUIRED")
    final = rows["retention-after-host-restore.json"]
    require(final.get("personal_restore_completed_sha256") == digest(QA / restore_completion)
            and final.get("host_success_sha256") == digest(QA / (prefix + host_file)), "NEW_FINAL_RESTORE_CHAIN_REQUIRED")
    verify.same_restored_runtime(final["runtime"], restored, binding)
    return [evidence(QA / name) for name in names + diagnostics]


def wps_evidence(binding, summary):
    """必须新包完整实际排版；冻结域子集快照不作为等价证明。"""
    path = Path(summary).resolve(strict=True)
    require(path.parent.parent == HERE.parent and path.name == "summary.json", "NEW_REAL_WPS_SUMMARY_REQUIRED")
    value = read(path)
    require(value.get("candidate") == binding and value.get("resume_sources") is None,
            "NEW_CANDIDATE_FULL_WPS_RUN_REQUIRED")
    verifier = load("new_formatter_strict_verifier", HERE / "formatter_validation.py")
    result = verifier.formatter_evidence(path, value, Path(binding["package"]["build_receipt"]))
    return {"summary": evidence(path), "validation": result, "scope": "new-candidate-full-installed-wps-acceptance",
            "new_package_wps_live_tested": True, "historical_evidence_reused": False}


def baseline():
    require(digest(BASELINE) == BASELINE_SHA, "ORIGINAL_TEN_PACKAGE_BASELINE_CHANGED")
    h = history(); p = h.p
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
    value = read(BASELINE); key = Ed25519PublicKey.from_public_bytes(p.trusted_key())
    p.verify_signature(value, key)
    require(len(value["assets"]) == 10 and len(value["acceptance"]["packages"]) == 10, "TEN_ORIGINAL_PACKAGES_REQUIRED")
    for row in value["assets"]:
        p.verify_signature(row, key)
    return value


def compose(original, checked, generated_at):
    value = copy.deepcopy(original); value.pop("signature", None)
    sha = checked["candidate"]["package"]["sha256"]; size = checked["package"]["bytes"]
    value["assets"] = [{"filename": NAME, "size": size, "sha256": sha} if row["filename"] == "PartyOps_1.4.5-rc.6_windows_amd64.exe" else row for row in value["assets"]]
    acceptance = {"id": "windows_amd64", "filename": NAME, "platform": "windows/amd64", "package_sha256": sha,
        "size": size, "source_fingerprint": checked["source_fingerprint"], "build_receipt_sha256": checked["build"]["sha256"],
        "build_provenance_status": "verified", "full_lifecycle_verified": False,
        "targets": [{"target": "win10-x64", "scope": "new-overlay-reboot-host-personal-restore-business-and-live-wps",
                     "actual_receipts": checked["guest_receipts"], "wps_evidence_sha256": checked["wps_evidence_sha256"],
                     "installed_uac_timeout_live_tested": False, "new_package_wps_live_tested": True}],
        "limitations": ["安装版真实UAC超时未实测；源码修复由正式门禁验证。", "其他九包保留原历史验收范围，未声明本轮全平台生命周期。"]}
    value["acceptance"]["packages"] = [acceptance if row["id"] == "windows_amd64" else row for row in value["acceptance"]["packages"]]
    value.pop("kylin_publication_scope", None)
    value.update(batch_profile=PROFILE, baseline_manifest_sha256=BASELINE_SHA, generated_at=generated_at,
        publication_authorized=True, formal_release_ready=True, publication_status="new-candidate-evidence-complete-windows-only",
        full_lifecycle_verified=False, formal_gate="FULL_FUNCTION_GATE_PASSED", windows_publication_scope="windows-startup-20261018-only",
        formal_gate_evidence=checked["gate"], publication_evidence_sha256=checked["evidence_sha256"])
    return value


def checked_preparation():
    config = read(HERE / "candidate-inputs.json")
    checked = candidate(config["package"], config["sha256"], config["build_receipt"], config["gate"])
    checked["guest_receipts"] = guest_evidence(checked["candidate"])
    stored = read(HERE / "acceptance-evidence.json")
    require({key: stored[key] for key in checked} == checked, "PREPARED_ACCEPTANCE_CHANGED")
    wps = wps_evidence(checked["candidate"], config["wps_summary"])
    require(read(HERE / "wps-actual-acceptance.json") == wps, "WPS_DOMAIN_OR_EVIDENCE_CHANGED")
    checked["wps_evidence_sha256"] = digest(HERE / "wps-actual-acceptance.json")
    checked["evidence_sha256"] = digest(HERE / "acceptance-evidence.json")
    require(stored["wps_evidence_sha256"] == checked["wps_evidence_sha256"], "WPS_REUSE_RECORD_CHANGED")
    return checked


def prepare(args):
    checked = candidate(args.package, args.sha256, args.build_receipt, args.gate)
    checked["guest_receipts"] = guest_evidence(checked["candidate"])
    wps = wps_evidence(checked["candidate"], args.wps_summary)
    original = baseline()
    for name in ("candidate-inputs.json", "acceptance-evidence.json", "wps-actual-acceptance.json", "manifest-signing-input.json"):
        require(not (HERE / name).exists(), "PREPARATION_EXISTS_VERIFY_ONLY")
    exclusive(HERE / "wps-actual-acceptance.json", wps)
    checked["wps_evidence_sha256"] = digest(HERE / "wps-actual-acceptance.json")
    exclusive(HERE / "acceptance-evidence.json", checked)
    checked["evidence_sha256"] = digest(HERE / "acceptance-evidence.json")
    exclusive(HERE / "candidate-inputs.json", {"package": str(Path(args.package).resolve()), "sha256": args.sha256,
        "build_receipt": str(Path(args.build_receipt).resolve()), "gate": str(Path(args.gate).resolve()), "wps_summary": str(Path(args.wps_summary).resolve())})
    exclusive(HERE / "manifest-signing-input.json", compose(original, checked, datetime.datetime.now(datetime.timezone.utc).isoformat()))
    return {"status": "awaiting-new-manifest-signature", "network_write_performed": False}


def signed_manifest(path, checked):
    value = read(path); unsigned = copy.deepcopy(value); unsigned.pop("signature", None)
    for row in unsigned["assets"]:
        if row["filename"] == NAME: row.pop("signature", None)
    require(unsigned == compose(baseline(), checked, value["generated_at"]), "NEW_SIGNED_MANIFEST_SCOPE_CHANGED")
    p = history().p
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
    key = Ed25519PublicKey.from_public_bytes(p.trusted_key()); p.verify_signature(value, key)
    for row in value["assets"]: p.verify_signature(row, key)
    return value


def bundle(path):
    checked = checked_preparation(); signed_manifest(path, checked)
    folder = HERE / "batch"; folder.mkdir(exist_ok=False)
    os.link(checked["package"]["path"], folder / NAME)
    (folder / (NAME + ".sha256")).write_text(checked["candidate"]["package"]["sha256"] + "  " + NAME + "\n", encoding="utf-8")
    (folder / MANIFEST).write_bytes(Path(path).read_bytes())
    binding = {"filename": NAME, "bytes": checked["package"]["bytes"], "package_sha256": checked["candidate"]["package"]["sha256"],
        "manifest_sha256": digest(folder / MANIFEST), "baseline_manifest_sha256": BASELINE_SHA, "receiver": PROFILE,
        "publication_authorized": True, "formal_release_ready": True, "evidence_sha256": checked["evidence_sha256"]}
    exclusive(folder / "binding.json", binding)
    receiver = load("new_windows_receiver_factory", HERE / "windows_receiver_factory.py")
    retained = read(PRIOR / "batch/post-switch-receiver/upload-metadata.json")
    require(retained["manifest_sha256"] == BASELINE_SHA, "ORIGINAL_RECEIVER_BASELINE_CHANGED")
    target = folder / "receiver"; target.mkdir()
    (target / "_serve.js").write_text(receiver.build_receiver(retained["assets"], binding["manifest_sha256"], binding), encoding="utf-8")
    (target / "index.html").write_text('<!doctype html><meta charset="utf-8"><title>PartyOps 下载服务</title>', encoding="utf-8")
    exclusive(target / "upload-metadata.json", {"receiver": PROFILE, "version": "1.4.5-rc.6", "manifest_sha256": binding["manifest_sha256"],
        "baseline_manifest_sha256": BASELINE_SHA, "assets": retained["assets"] + [receiver.candidate(binding)]})
    return {"status": "local-bundle-prepared", "binding": binding, "network_write_performed": False,
            "next": "主控官方WorkBuddy一次部署；此入口不生成/读取凭据、不部署。"}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("prepare", "verify", "bundle"))
    parser.add_argument("--package", type=Path); parser.add_argument("--sha256")
    parser.add_argument("--build-receipt", type=Path)
    parser.add_argument("--gate", type=Path, default=ROOT / ".release-gates/full-function-tests.json")
    parser.add_argument("--signed-manifest", type=Path)
    parser.add_argument("--wps-summary", type=Path)
    args = parser.parse_args(argv)
    try:
        if args.action == "prepare":
            require(all((args.package, args.sha256, args.build_receipt, args.wps_summary)), "EXPLICIT_ACTUAL_PACKAGE_SHA_FORMAL_RECEIPT_WPS_REQUIRED")
            result = prepare(args)
        elif args.action == "bundle":
            require(args.signed_manifest is not None, "ACTUAL_SIGNED_MANIFEST_REQUIRED"); result = bundle(args.signed_manifest)
        else:
            checked_preparation(); result = {"status": "verified-offline", "network_write_performed": False}
        print(json.dumps(result, ensure_ascii=False)); return 0
    except Exception as error:
        code = str(error)
        print(json.dumps({"status": "blocked", "code": code if isinstance(error, RuntimeError) and re.fullmatch(r"[A-Z0-9_]+", code) else type(error).__name__, "network_write_performed": False})); return 2


if __name__ == "__main__":
    raise SystemExit(main())
