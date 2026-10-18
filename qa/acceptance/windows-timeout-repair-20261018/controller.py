"""原 Win10 第二候选的最小保留验收；所有动作须显式选择，未知结果只读检查。"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = Path(__file__).resolve().parent
OLD = OUTPUT.parent / "windows-startup-20261016"
LAB_ROOT = Path("D:/PartyOps-VM-Lab")
PREVIOUS_SHA = "3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27"
sys.path.insert(0, str(OLD))
sys.path.insert(0, str(ROOT / "qa/vm-lab"))
import retention_contract as contract


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def require(value, message):
    if not value:
        raise RuntimeError(message)


def preserved_inputs():
    """只读引用原业务基线；本目录不创建第二份基线。"""
    baseline = contract.read(OLD / "retention-baseline.json")
    backup = ROOT / ".qa/windows-retention/before-overlay.partyops-backup"
    require(baseline.get("status") == "passed" and baseline.get("guest_uuid") == contract.UUID,
            "ORIGINAL_PASSED_BASELINE_REQUIRED")
    require(baseline.get("backup", {}).get("verified") is True
            and backup.stat().st_size == baseline["backup"]["size"]
            and contract.digest(backup) == baseline["backup"]["sha256"], "ORIGINAL_BACKUP_CHANGED")
    return {"baseline_sha256": contract.digest(OLD / "retention-baseline.json"),
            "backup_sha256": baseline["backup"]["sha256"]}


def previous_binding():
    """已安装起点来自原成功回执及其固定摘要，不需要保留旧安装包。"""
    from evidence import safe_child
    from windows_build_payload import manifest_entries
    report = contract.read(OLD / "retention-after-host-restore.json")
    binding = report["candidate"]
    receipt_path = LAB_ROOT / "state/builds" / (PREVIOUS_SHA + ".json")
    require(report.get("status") == "passed" and binding["package"]["sha256"] == PREVIOUS_SHA
            and Path(binding["package"]["build_receipt"]).resolve() == receipt_path.resolve()
            and contract.digest(receipt_path) == binding["package"]["build_receipt_sha256"],
            "PREVIOUS_OFFICIAL_RECEIPT_CHANGED")
    receipt = contract.read(receipt_path)
    require(receipt.get("status") == "completed" and receipt.get("exit_code") == 0
            and not receipt.get("errors") and receipt.get("output_created_during_build") is True
            and receipt["package"]["sha256"] == PREVIOUS_SHA, "PREVIOUS_BUILD_NOT_SUCCESSFUL")
    log = safe_child(LAB_ROOT / "reports", Path(receipt["log"]["path"]))
    require(log.stat().st_size > 0 and contract.digest(log) == receipt["log"]["sha256"], "PREVIOUS_LOG_CHANGED")
    payload = receipt["windows_payload"]
    manifest = safe_child(LAB_ROOT / "reports", Path(payload["manifest"]["path"]))
    require(payload.get("capture") == "controller-after-successful-build"
            and payload.get("package_sha256") == PREVIOUS_SHA
            and contract.digest(manifest) == payload["manifest"]["sha256"]
            and manifest.stat().st_size == payload["manifest"]["bytes"], "PREVIOUS_PAYLOAD_CHANGED")
    entries = manifest_entries(contract.read(manifest), receipt["package"])
    expected = {"manifest_sha256": payload["manifest"]["sha256"],
                "executable_sha256": entries["partyops.exe"]["sha256"],
                "wizard_sha256": entries["partyopswizard.exe"]["sha256"]}
    require(all(binding[k] == v for k, v in expected.items()), "PREVIOUS_BINDING_CHANGED")
    return binding


def identity_matches(actual, binding):
    for key in ("manifest_sha256", "executable_sha256", "wizard_sha256"):
        require(actual.get(key) == binding[key], "INSTALLED_IDENTITY_MISMATCH:" + key)


def no_attempt(path):
    require(not path.exists(), "ALREADY_ATTEMPTED_READ_ONLY_INSPECT_REQUIRED")


def boot_inputs():
    boot = contract.read(OUTPUT / "retention-boot-before.json")
    inputs = preserved_inputs()
    require(boot.get("guest_uuid") == contract.UUID and boot.get("disk") == str(contract.DISK.resolve())
            and boot.get("baseline_sha256") == inputs["baseline_sha256"]
            and boot.get("inputs") == inputs and boot.get("previous") == previous_binding(),
            "BOUND_CURRENT_START_REQUIRED")
    return inputs, boot


def environment():
    module = load("timeout_business", ROOT / "qa/vm-lab/scripts/exercise-windows-business.py")
    matrix, media = module.load_configuration()
    matrix["defaults"].update(primary_root=str(LAB_ROOT), fallback_root=str(LAB_ROOT))
    media.setdefault("win10-x64", {"format": "iso", "arch": "x86_64"})
    return module, module.QemuLab(matrix, LAB_ROOT, media), matrix["targets"]["win10-x64"]


def retention(module, lab, client, phase, actual, binding):
    """复用旧安装版 HTTP 读取流程，仅比较既有账号、事项、附件、日历。"""
    old_verify = load("timeout_old_verify", OLD / "verify-retained-business.py")
    with client.open_sftp() as fs:
        with fs.file("C:/ProgramData/PartyOps-VM-Lab/private/lifecycle-82dbfdf1894f.json", "rb") as stream:
            state = json.loads(stream.read())
    require(state.get("business_verified") is True
            and old_verify.PureWindowsPath(state["data_dir"]) == old_verify.PureWindowsPath(old_verify.PERSONAL_DATA),
            "ORIGINAL_SYNTHETIC_PERSONAL_DATA_REQUIRED")
    runner = module.WindowsRun(client, lab, "win10-x64", phase, state["context"]["package"],
                               state["context"], "lifecycle-82dbfdf1894f", OUTPUT)
    retained = runner.check_business_data()
    calendar = next((item for item in runner.request("/api/v1/work-calendar?year=2026")
                     if item["date_key"] == "2026-12-03" and item["kind"] == "qa-retention"), None)
    require(calendar is not None, "ORIGINAL_CALENDAR_REQUIRED")
    data = {"retained": retained, "calendar_id": calendar["id"],
            "calendar_sha256": hashlib.sha256(json.dumps(calendar, ensure_ascii=False, sort_keys=True).encode()).hexdigest()}
    baseline = contract.read(OLD / "retention-baseline.json")
    for key, value in data.items():
        require(value == baseline[key], "ORIGINAL_RETENTION_MISMATCH:" + key)
    final = contract.probe(client, module.windows.execute)
    contract.runtime_matches(final, binding)
    require(final["boot_id"] == actual["boot_id"] and final["processes"] == actual["processes"],
            "RUNTIME_CHANGED_DURING_RETENTION_READ")
    return data


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["preflight", "record-start", "inspect", "stop-current", "install",
                                          "launch", "verify-overlay", "normal-reboot", "verify-reboot"])
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--sha256", required=True)
    parser.add_argument("--allow-qa-process-termination", action="store_true")
    parser.add_argument("--execute-normal-reboot", action="store_true")
    args = parser.parse_args(argv)
    require(args.execute_normal_reboot == (args.action == "normal-reboot"), "EXPLICIT_NORMAL_REBOOT_REQUIRED")
    require(not args.allow_qa_process_termination or args.action == "stop-current", "TERMINATION_ONLY_FOR_STOP")
    inputs, previous = preserved_inputs(), previous_binding()
    module, lab, target = environment()
    binding = contract.candidate(lab, ROOT, args.package, args.sha256)
    require(binding["package"]["sha256"] != PREVIOUS_SHA, "NEW_CANDIDATE_REQUIRED")
    if args.action == "preflight":
        print(json.dumps({"status": "preflight-passed", "inputs": inputs, "previous": previous, "candidate": binding}, ensure_ascii=False))
        return
    folder = OUTPUT / "overlay-install"
    if args.action == "install":
        no_attempt(folder / "attempt.json")
    if args.action == "normal-reboot":
        no_attempt(OUTPUT / "normal-reboot-attempt.json")
    # 所有 Guest 操作都在主控显式选择子命令后发生。
    state = contract.bind_guest(lab, module.qmp)
    if args.action in {"stop-current", "launch"}:
        _, boot = boot_inputs()
        require(boot.get("candidate") == binding, "START_BOUND_TO_DIFFERENT_CANDIDATE")
        transition = load("timeout_runtime", OLD / "runtime-transition.py")
        transition.OUTPUT = OUTPUT
        transition.OLD_MANIFEST = previous["manifest_sha256"]
        transition.preserved_inputs = boot_inputs
        original_target = transition.old_target

        def checked_target(actual, inventory, desktop):
            identity_matches(actual, previous)
            return original_target(actual, inventory, desktop)

        transition.old_target = checked_target
        action = "stop-old" if args.action == "stop-current" else "launch-candidate"
        sys.argv = [str(OUTPUT / "controller.py"), action, "--package", str(args.package), "--sha256", args.sha256]
        if args.allow_qa_process_termination:
            sys.argv.append("--allow-qa-process-termination")
        transition.main()
        return
    client = module.windows.connect(lab, target)
    try:
        actual = contract.probe(client, module.windows.execute)
        from identity import probe as identity_probe
        actual["probe_boot_id"] = identity_probe(lab, "win10-x64")["boot_id"]
        if args.action == "inspect":
            # 无写入 Guest；不能据观察结果生成安装/重启完成回执。
            contract.exclusive(OUTPUT / ("observation-" + contract.now().replace(":", "-") + ".json"),
                               {"time_utc": contract.now(), "runtime": actual, "candidate": binding,
                                "previous": previous, "inputs": inputs, "scope": "read-only-not-completion"})
            return
        if args.action == "record-start":
            no_attempt(folder / "attempt.json")
            identity_matches(actual, previous)
            contract.runtime_matches(actual, previous)
            data = retention(module, lab, client, "before-timeout-overlay", actual, previous)
            require(preserved_inputs() == inputs and previous_binding() == previous, "START_INPUTS_CHANGED")
            contract.exclusive(OUTPUT / "retention-boot-before.json", {
                "status": "recorded", "guest_uuid": contract.UUID, "disk": str(contract.DISK.resolve()),
                "time_utc": contract.now(), "inputs": inputs, "previous": previous, "candidate": binding,
                "baseline_sha256": inputs["baseline_sha256"], "boot_id": actual["boot_id"],
                "probe_boot_id": actual["probe_boot_id"], "runtime": actual, **data})
            return
        _, boot = boot_inputs()
        require(boot.get("candidate") == binding, "START_BOUND_TO_DIFFERENT_CANDIDATE")
        if args.action == "install":
            identity_matches(actual, previous)
            require(actual["boot_id"] == boot["boot_id"], "BOOT_CHANGED_BEFORE_INSTALL")
            inventory_module = load("timeout_inventory", OLD / "runtime-transition.py")
            inventory = json.loads(module.windows.execute(client, inventory_module.INVENTORY))
            require(inventory.get("products") == [] and inventory.get("installer_count") == 0
                    and actual.get("processes") == [] and actual.get("http_owners") == [], "STOP_CURRENT_BEFORE_INSTALL")
            contract.bind_guest(lab, module.qmp)
            require(contract.digest(args.package) == args.sha256.lower(), "PACKAGE_CHANGED_BEFORE_INSTALL")
            folder.mkdir(exist_ok=True)
            attempt = folder / "attempt.json"
            contract.exclusive(attempt, {"time_utc": contract.now(), "guest_uuid": contract.UUID,
                "disk": str(contract.DISK.resolve()), "candidate": binding, "previous": previous,
                "inputs": inputs, "boot_before_sha256": contract.digest(OUTPUT / "retention-boot-before.json"),
                "operation": "overlay-preserve-existing-data"})
            old_install = load("timeout_installer", OLD / "install-candidate-once.py")
            identity, result = old_install.installer.install_modern(lab, target, state,
                {"path": str(args.package.resolve()), "sha256": args.sha256.lower()}, folder)
            contract.exclusive(folder / "completed.json", {"status": "completed", "candidate": binding,
                "attempt_sha256": contract.digest(attempt), "identity": identity, "install": result,
                "business_retention": "not-yet-verified"})
            contract.completed_install(folder, binding)
            return
        contract.completed_install(folder, binding)
        contract.runtime_matches(actual, binding, contract.read(folder / "attempt.json")["time_utc"])
        if args.action in {"verify-overlay", "normal-reboot"}:
            require(actual["boot_id"] == boot["boot_id"] and actual["probe_boot_id"] == boot["probe_boot_id"], "UNEXPECTED_REBOOT")
        if args.action == "normal-reboot":
            overlay_file = OUTPUT / "retention-after-overlay.json"
            overlay = contract.read(overlay_file)
            require(overlay.get("status") == "passed" and overlay.get("candidate") == binding
                    and overlay.get("install_completed_sha256") == contract.digest(folder / "completed.json"), "PASSED_OVERLAY_REQUIRED")
            attempt_file = OUTPUT / "normal-reboot-attempt.json"
            contract.exclusive(attempt_file, {"time_utc": contract.now(), "guest_uuid": contract.UUID,
                "disk": str(contract.DISK.resolve()), "candidate": binding, "context": contract.reboot_context(binding),
                "boot_before_sha256": contract.digest(OUTPUT / "retention-boot-before.json"),
                "overlay_sha256": contract.digest(overlay_file), "operation": "normal-shutdown-start-once"})
            client.close()
            client = None
            restart = contract.reboot_once(lab, binding, module.qmp, module.restart.validate_restart)
            contract.exclusive(OUTPUT / "normal-reboot-completed.json", {"candidate": binding, "restart": restart,
                "attempt_sha256": contract.digest(attempt_file),
                "boot_before_sha256": contract.digest(OUTPUT / "retention-boot-before.json")})
            return
        if args.action == "verify-reboot":
            import reboot_evidence
            reboot_receipt = reboot_evidence.validate(binding, actual, state, module.restart.validate_restart, OUTPUT)
        data = retention(module, lab, client, args.action, actual, binding)
        require(preserved_inputs() == inputs, "BASELINE_CHANGED_DURING_VERIFICATION")
        contract.bind_guest(lab, module.qmp)
        phase = "after-overlay" if args.action == "verify-overlay" else "after-reboot"
        report = {
            "status": "passed", "time_utc": contract.now(), "candidate": binding, "inputs": inputs,
            "guest_uuid": contract.UUID, "disk": str(contract.DISK.resolve()),
            "runtime": actual, "install_completed_sha256": contract.digest(folder / "completed.json"), **data}
        if args.action == "verify-reboot": report["reboot_evidence"] = reboot_receipt
        contract.exclusive(OUTPUT / ("retention-" + phase + ".json"), report)
    finally:
        if client is not None:
            client.close()


if __name__ == "__main__":
    main()
