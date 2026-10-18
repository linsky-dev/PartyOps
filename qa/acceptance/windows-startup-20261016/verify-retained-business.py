"""原 Win10 合成数据验收；显式一次正常重启，断线后不自动重发。"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import sys
from pathlib import Path
from datetime import datetime
from pathlib import PureWindowsPath
import retention_contract as contract

ROOT = Path(__file__).resolve().parents[3]
LAB_ROOT = Path("D:/PartyOps-VM-Lab")
OUTPUT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "qa/vm-lab"))
PERSONAL_DATA = r"C:\Users\partyopsuser\Documents\PartyOps QA\lifecycle-82dbfdf1894f\中文 空格业务数据"


def restored_personal(value, binding, attempt_sha):
    """只接受本次产品恢复成功证据，不能用旧主机失败/回滚阶段替代。"""
    services = value.get("services", [])
    observed = value.get("completion_kind") == "read-only-observed-after-submit"
    if observed:
        spec = importlib.util.spec_from_file_location("retention_completion_contract", OUTPUT / "restore-personal-qa.py")
        recovery = importlib.util.module_from_spec(spec); spec.loader.exec_module(recovery)
        _, links = recovery.completion_sources(binding)
        verify_attempt = OUTPUT / "restore-personal-verify-completion.attempt.json"
        intent = contract.read(verify_attempt)
        if (value.get("http_terminal_observed") is not False or value.get("original_data_directory_verified") is not True
                or value.get("system_mode") != "personal" or value.get("submit_attempt_sha256") != attempt_sha
                or value.get("attempt_sha256") != contract.digest(verify_attempt) or intent.get("candidate") != binding
                or intent.get("operation") != "read-only-verify-completion"
                or any(value.get(k) != v or intent.get(k) != v for k, v in links.items())):
            raise RuntimeError("BOUND_OBSERVED_RESTORE_COMPLETION_REQUIRED")
    if (value.get("status") != "restored-ready" or value.get("candidate") != binding
            or (not observed and value.get("attempt_sha256") != attempt_sha) or value.get("user_mode") != "personal"
            or value.get("pending_mode_switch") is not False or value.get("user_journal_present") is not False
            or PureWindowsPath(value.get("original_personal_data_directory", "")) != PureWindowsPath(PERSONAL_DATA)
            or len(services) != 2 or {s.get("name") for s in services} != {"PartyOpsHost", "PartyOpsUpdateService"}
            or any(s.get("state") != "Stopped" or s.get("start_mode") != "Manual" for s in services)):
        raise RuntimeError("SUCCESSFUL_BOUND_PERSONAL_RESTORE_REQUIRED")
    contract.runtime_matches(value.get("runtime", {}), binding)
    return value["runtime"]


def same_restored_runtime(actual, restored, binding):
    contract.runtime_matches(actual, binding)
    current, previous = actual["processes"][0], restored["processes"][0]
    if (actual["boot_id"] != restored["boot_id"] or current["pid"] != previous["pid"]
            or current["path"].casefold() != previous["path"].casefold()
            or datetime.fromisoformat(current["created"].replace("Z", "+00:00"))
            != datetime.fromisoformat(previous["created"].replace("Z", "+00:00"))):
        raise RuntimeError("RESTORED_PERSONAL_RUNTIME_CHANGED")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("phase", choices=["baseline", "boot-before", "after-overlay", "normal-reboot", "after-reboot", "after-host-rollback", "after-host-restore"])
    parser.add_argument("--package", type=Path)
    parser.add_argument("--sha256")
    parser.add_argument("--restore-evidence", choices=["submit", "observed-completion"], default="submit",
                        help="after-host-restore显式选择成功提交或已完整绑定的只读结案回执")
    parser.add_argument("--expected-manifest-sha256", help="兼容旧参数，只能匹配正式回执")
    parser.add_argument("--execute-normal-reboot", action="store_true", help="明确一次正常关机再启动")
    args = parser.parse_args()
    if (args.phase == "normal-reboot") != args.execute_normal_reboot:
        raise RuntimeError("EXPLICIT_NORMAL_REBOOT_REQUEST_REQUIRED")
    spec = importlib.util.spec_from_file_location("retention_windows_business", ROOT / "qa/vm-lab/scripts/exercise-windows-business.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    from providers import qmp
    from identity import probe as identity_probe
    matrix, media = module.load_configuration()
    matrix["defaults"].update(fallback_root=str(LAB_ROOT), primary_root=str(LAB_ROOT))
    media.setdefault("win10-x64", {"format": "iso", "arch": "x86_64"})
    lab = module.QemuLab(matrix, LAB_ROOT, media)
    vm = contract.bind_guest(lab, qmp)
    baseline_file = OUTPUT / "retention-baseline.json"
    boot_file = OUTPUT / "retention-boot-before.json"
    folder = OUTPUT / "overlay-install"
    if args.phase not in {"baseline", "boot-before"}:
        if contract.read(boot_file).get("baseline_sha256") != contract.digest(baseline_file):
            raise RuntimeError("PRE_OVERLAY_BASELINE_CHANGED")
    binding = None; restore_runtime = None; restore_sha = None
    if args.phase not in {"baseline", "boot-before"}:
        if not args.package or not args.sha256:
            raise RuntimeError("OFFICIAL_CANDIDATE_PACKAGE_AND_SHA256_REQUIRED")
        binding = contract.candidate(lab, ROOT, args.package, args.sha256)
        if args.expected_manifest_sha256 and args.expected_manifest_sha256.lower() != binding["manifest_sha256"]:
            raise RuntimeError("CALLER_MANIFEST_DIFFERS_FROM_OFFICIAL_BUILD")
        contract.completed_install(folder, binding)
        install_started = contract.read(folder / "attempt.json")["time_utc"]
        if args.phase == "after-host-restore":
            restore_file = OUTPUT / ("restore-personal-observed-completion.json" if args.restore_evidence == "observed-completion" else "restore-personal-submit.result.json")
            restore_sha = contract.digest(restore_file)
            restore_runtime = restored_personal(contract.read(restore_file), binding,
                contract.digest(OUTPUT / "restore-personal-submit.attempt.json"))
        if args.phase == "after-host-rollback":
            # 只在本次主机配置已结束且原个人模式已恢复后读取原合成业务；不重提配置。
            host_result = contract.read(OUTPUT / "first-host-root-directory-submit.result.json")
            if (host_result.get("candidate") != binding
                    or host_result.get("status") != "host-first-run-failed"
                    or host_result.get("user_mode") != "personal"
                    or host_result.get("system_mode") != "missing"):
                raise RuntimeError("TERMINAL_HOST_FAILURE_AND_PERSONAL_ROLLBACK_REQUIRED")
    if args.phase == "baseline" and baseline_file.exists():
        raise RuntimeError("EXISTING_BASELINE_MUST_NOT_CHANGE")
    client = module.windows.connect(lab, matrix["targets"]["win10-x64"])
    try:
        actual = contract.probe(client, module.windows.execute)
        actual["probe_boot_id"] = identity_probe(lab, "win10-x64")["boot_id"]
        if binding:
            contract.runtime_matches(actual, binding, install_started)
            if restore_runtime is not None: same_restored_runtime(actual, restore_runtime, binding)
            if actual["manifest_sha256"] == contract.read(baseline_file)["installed_manifest_sha256"]:
                raise RuntimeError("OLD_INSTALLATION_CANNOT_COUNT_AS_OVERLAY")
        if args.phase == "boot-before":
            baseline = contract.read(baseline_file)
            if baseline.get("status") != "passed" or actual["manifest_sha256"] != baseline["installed_manifest_sha256"]:
                raise RuntimeError("UNCHANGED_PASSED_BASELINE_REQUIRED")
            if (folder / "attempt.json").exists():
                raise RuntimeError("BOOT_BEFORE_MUST_PRECEDE_INSTALL_ATTEMPT")
            contract.exclusive(boot_file, {"time_utc": contract.now(), "guest_uuid": contract.UUID,
                "disk": str(contract.DISK.resolve()), "baseline_sha256": contract.digest(baseline_file),
                "boot_id": actual["boot_id"], "probe_boot_id": actual["probe_boot_id"],
                "operation": "read-only-pre-overlay-boot", "status": "recorded"})
            print("原 Guest 启动时间已只读采集；原保留基线未改写。")
            return
        if args.phase == "normal-reboot":
            before = contract.read(boot_file)
            overlay_file = OUTPUT / "retention-after-overlay.json"
            overlay = contract.read(overlay_file)
            if (overlay.get("status") != "passed" or overlay.get("candidate") != binding
                    or overlay.get("install_completed_sha256") != contract.digest(folder / "completed.json")
                    or actual["boot_id"] != before["boot_id"] or actual["probe_boot_id"] != before["probe_boot_id"]):
                raise RuntimeError("PASSED_OVERLAY_AND_UNCHANGED_BOOT_REQUIRED")
            context = contract.reboot_context(binding)
            attempt_file = OUTPUT / "normal-reboot-attempt.json"
            contract.exclusive(attempt_file, {"time_utc": contract.now(), "guest_uuid": contract.UUID,
                "disk": str(contract.DISK.resolve()),
                "candidate": binding, "context": context, "boot_before_sha256": contract.digest(boot_file),
                "overlay_sha256": contract.digest(overlay_file), "operation": "normal-guest-shutdown-cold-start-once"})
            # 本次薄包装复用官方系统探测和重启断言，不依赖旧介质/干净快照指针。
            try:
                restart = contract.reboot_once(lab, binding, qmp, module.restart.validate_restart)
            except Exception as exc:
                contract.exclusive(OUTPUT / "normal-reboot-failed.json", {
                    "status": "failed", "attempt_sha256": contract.digest(attempt_file),
                    "error_type": type(exc).__name__, "automatic_retry": False})
                raise
            contract.bind_guest(lab, qmp)
            contract.exclusive(OUTPUT / "normal-reboot-completed.json", {"candidate": binding,
                "boot_before_sha256": contract.digest(boot_file), "attempt_sha256": contract.digest(attempt_file),
                "restart": restart})
            print("正常重启已留下回执；仍须 after-reboot 核对候选启动及原数据。")
            return
        if args.phase == "after-reboot":
            before = contract.read(boot_file)
            before["receipt_sha256"] = contract.digest(boot_file)
            attempt_file = OUTPUT / "normal-reboot-attempt.json"
            attempt = contract.read(attempt_file)
            attempt["receipt_sha256"] = contract.digest(attempt_file)
            if attempt.get("overlay_sha256") != contract.digest(OUTPUT / "retention-after-overlay.json"):
                raise RuntimeError("PRE_REBOOT_OVERLAY_EVIDENCE_CHANGED")
            contract.validate_reboot(before, attempt, contract.read(OUTPUT / "normal-reboot-completed.json"),
                                     actual, binding, vm, module.restart.validate_restart)
        with client.open_sftp() as fs:
            # 既有合成数据 API 会话只在内存使用，不输出凭据及正文。
            state = json.loads(fs.file("C:/ProgramData/PartyOps-VM-Lab/private/lifecycle-82dbfdf1894f.json").read())
            installed = json.loads(fs.file("C:/PartyOps QA/中文 程序/release-manifest.json").read())
        if state.get("business_verified") is not True or "lifecycle-82dbfdf1894f" not in state.get("data_dir", ""):
            raise RuntimeError("EXISTING_SYNTHETIC_BASELINE_REQUIRED")
        if args.phase == "after-host-restore" and PureWindowsPath(state.get("data_dir", "")) != PureWindowsPath(PERSONAL_DATA):
            raise RuntimeError("ORIGINAL_PERSONAL_DIRECTORY_REQUIRED")
        run = module.WindowsRun(client, lab, "win10-x64", args.phase, state["context"]["package"], state["context"], "lifecycle-82dbfdf1894f", OUTPUT)
        retained = run.check_business_data()
        entries = run.request("/api/v1/work-calendar?year=2026")
        calendar = next((e for e in entries if e["date_key"] == "2026-12-03" and e["kind"] == "qa-retention"), None)
        if args.phase == "baseline" and calendar is None:
            calendar = run.request("/api/v1/work-calendar", "POST", {"date_key": "2026-12-03", "title": "Win10 覆盖安装日历保留验收", "kind": "qa-retention", "is_workday": True, "note": "仅当前 QA Guest 的合成数据"})
        if calendar is None:
            raise RuntimeError("CALENDAR_NOT_PRESERVED")
        report = {"phase": args.phase, "time_utc": contract.now(), "guest_uuid": contract.UUID,
            "disk": str(contract.DISK.resolve()), "boot_id": actual["boot_id"], "probe_boot_id": actual["probe_boot_id"],
            "installed_source_commit": installed.get("source_commit"), "installed_manifest_sha256": actual["manifest_sha256"],
            "code_scope": "安装版真实HTTP；控制器不运行产品源码", "retained": retained,
            "calendar_id": calendar["id"], "calendar_sha256": hashlib.sha256(json.dumps(calendar, ensure_ascii=False, sort_keys=True).encode()).hexdigest()}
        if args.phase == "baseline":
            backup = run.request("/api/v1/backups", "POST")
            if backup["status"] != "completed":
                raise RuntimeError("BACKUP_NOT_COMPLETED")
            verified = run.request("/api/v1/admin/backups/" + backup["id"] + "/verify", "POST")
            content = run.request("/api/v1/backups/" + backup["id"] + "/download")
            digest = hashlib.sha256(content).hexdigest()
            if not verified["valid"] or digest != verified["sha256"]:
                raise RuntimeError("BACKUP_INTEGRITY_FAILED")
            private = ROOT / ".qa/windows-retention"
            private.mkdir(parents=True, exist_ok=True)
            with (private / "before-overlay.partyops-backup").open("xb") as stream:
                stream.write(content)
            report["backup"] = {"sha256": digest, "size": len(content), "verified": True}
        else:
            before = contract.read(baseline_file)
            for key in ("retained", "calendar_id", "calendar_sha256"):
                if report[key] != before[key]:
                    raise RuntimeError("DATA_RETENTION_MISMATCH:" + key)
            contract.bind_guest(lab, qmp)
            final_actual = contract.runtime_matches(contract.probe(client, module.windows.execute), binding, install_started)
            if restore_runtime is not None:
                same_restored_runtime(final_actual, restore_runtime, binding)
                if contract.digest(restore_file) != restore_sha:
                    raise RuntimeError("PERSONAL_RESTORE_EVIDENCE_CHANGED")
                report["personal_restore_completed_sha256"] = restore_sha
                report["personal_restore_evidence"] = restore_file.name
            if final_actual["boot_id"] != actual["boot_id"]:
                raise RuntimeError("GUEST_REBOOTED_DURING_VERIFICATION")
            report.update(candidate=binding, runtime=final_actual,
                          install_completed_sha256=contract.digest(folder / "completed.json"))
            if args.phase == "after-reboot":
                report["normal_reboot_completed_sha256"] = contract.digest(OUTPUT / "normal-reboot-completed.json")
        report["status"] = "passed"
        contract.exclusive(OUTPUT / ("retention-" + args.phase + ".json"), report)
        print(json.dumps({"phase": args.phase, "status": "passed", "account_task_attachment": True, "calendar": True}, ensure_ascii=False))
    finally:
        client.close()


if __name__ == "__main__":
    main()
