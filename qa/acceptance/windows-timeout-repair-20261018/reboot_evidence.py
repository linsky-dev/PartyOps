"""正常重启与中断后单次开机恢复证据适配；绝不补造原 QMP 事件。"""
from datetime import datetime, timedelta
from pathlib import Path

import controller as c

contract = c.contract
require = c.require
OUTPUT = Path(__file__).resolve().parent


def instant(value):
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    require(result.tzinfo is not None, "UTC_EVENT_TIME_REQUIRED")
    return result


def inputs(binding, output=OUTPUT):
    boot = contract.read(output / "retention-boot-before.json")
    attempt = contract.read(output / "normal-reboot-attempt.json")
    overlay = contract.read(output / "retention-after-overlay.json")
    require(boot.get("guest_uuid") == attempt.get("guest_uuid") == contract.UUID
            and boot.get("disk") == attempt.get("disk") == str(contract.DISK.resolve())
            and boot.get("candidate") == attempt.get("candidate") == overlay.get("candidate") == binding
            and boot.get("inputs") == overlay.get("inputs") == c.preserved_inputs()
            and boot.get("baseline_sha256") == c.preserved_inputs()["baseline_sha256"]
            and attempt.get("context") == contract.reboot_context(binding)
            and attempt.get("operation") == "normal-shutdown-start-once"
            and attempt.get("boot_before_sha256") == contract.digest(output / "retention-boot-before.json")
            and attempt.get("overlay_sha256") == contract.digest(output / "retention-after-overlay.json")
            and overlay.get("status") == "passed" and overlay.get("runtime", {}).get("boot_id") == boot.get("boot_id")
            and overlay.get("runtime", {}).get("probe_boot_id") == boot.get("probe_boot_id")
            and overlay.get("install_completed_sha256") == contract.digest(output / "overlay-install/completed.json"),
            "BOUND_INTERRUPTED_REBOOT_INPUTS_REQUIRED")
    contract.completed_install(output / "overlay-install", binding)
    return boot, attempt, overlay


def validate_events(events, boot, overlay, actual):
    """只认来源及事件元数据，不读取消息正文或账户字段。"""
    require(actual.get("guest_uuid") == contract.UUID and actual.get("purpose") == "disposable-qa"
            and actual.get("boot_id") != boot.get("boot_id"), "ACTUAL_RECOVERED_BOOT_CHANGE_REQUIRED")
    lower = max(instant(boot["boot_id"]), instant(overlay["runtime"]["processes"][0]["created"]))
    started = instant(actual["boot_id"])
    require(started > lower, "RECOVERED_BOOT_MUST_FOLLOW_OVERLAY_RUNTIME")
    require(isinstance(events, list) and 4 <= len(events) < 512, "SYSTEM_EVENT_SET_REQUIRED")
    allowed = {("Microsoft-Windows-Kernel-General", 12), ("Microsoft-Windows-Kernel-General", 13),
               ("Microsoft-Windows-Kernel-Power", 41), ("User32", 1074),
               ("EventLog", 6005), ("EventLog", 6006), ("EventLog", 6008)}
    seen = set()
    for event in events:
        require(event.get("log") == "System" and (event.get("provider"), event.get("id")) in allowed
                and type(event.get("record_id")) is int and event["record_id"] > 0
                and event["record_id"] not in seen, "SYSTEM_EVENT_IDENTITY_INVALID")
        seen.add(event["record_id"])
        instant(event["utc"])
    scoped = [e for e in events if instant(e["utc"]) > lower]
    require(not any(e["id"] in {41, 6008} for e in scoped), "ABNORMAL_SHUTDOWN_EVENT_REJECTED")
    stops = [e for e in scoped if e["id"] in {13, 6006} and instant(e["utc"]) < started]
    require({e["id"] for e in stops} == {13, 6006}, "NORMAL_SHUTDOWN_13_AND_6006_REQUIRED")
    starts = [e for e in scoped if e["id"] == 12]
    require(len(starts) == 1 and abs((instant(starts[0]["utc"]) - started).total_seconds()) <= 120,
            "SINGLE_KERNEL_START_MATCHING_ACTUAL_BOOT_REQUIRED")
    log_starts = [e for e in scoped if e["id"] == 6005 and started <= instant(e["utc"]) <= started + timedelta(minutes=5)]
    # Kernel provider 可能在新启动时延迟提交上次关机记录；RecordID只验证唯一性，不能作跨provider事件时间全序。
    require(len(log_starts) == 1 and max(instant(e["utc"]) for e in stops) < instant(starts[0]["utc"]) <= instant(log_starts[0]["utc"]),
            "ORDERED_SYSTEM_LOG_START_REQUIRED")
    require(not any(e["id"] in {13, 6006} and instant(e["utc"]) >= started for e in scoped),
            "FURTHER_SHUTDOWN_REQUIRES_INSPECTION")
    return {"kind": "windows-system-event-recovery", "normal_shutdown_event_ids": [13, 6006],
            "startup_event_ids": [12, 6005], "user32_1074_observed": any(e["id"] == 1074 for e in scoped),
            "qmp_shutdown_event_persisted": False, "abnormal_41_6008_observed": False}


def validate_recovery(binding, actual, state, output=OUTPUT):
    boot, attempt, overlay = inputs(binding, output)
    start_file = output / "recover-start.attempt.json"
    start = contract.read(start_file)
    result_file = output / "recover-start.result.json"
    result = contract.read(result_file)
    receipt_file = output / "reboot-recovery-completed.json"
    receipt = contract.read(receipt_file)
    require(not (output / "normal-reboot-completed.json").exists(), "AMBIGUOUS_STANDARD_AND_RECOVERY_RECEIPTS")
    require(start.get("candidate") == result.get("candidate") == receipt.get("candidate") == binding
            and start.get("guest_uuid") == receipt.get("guest_uuid") == state.get("uuid") == contract.UUID
            and start.get("disk") == receipt.get("disk") == state.get("disk") == str(contract.DISK.resolve())
            and start.get("normal_attempt_sha256") == receipt.get("normal_attempt_sha256") == contract.digest(output / "normal-reboot-attempt.json")
            and start.get("overlay_sha256") == contract.digest(output / "retention-after-overlay.json")
            and start.get("boot_before_sha256") == contract.digest(output / "retention-boot-before.json")
            and start.get("operation") == "resume-interrupted-normal-reboot-start-only-once"
            and start.get("old_qemu_pid") == 2900 and start.get("old_pid_absent") is True
            and start.get("no_bound_qemu_process") is True and start.get("qmp_unreachable") is True
            and result.get("status") == "started" and result.get("attempt_sha256") == contract.digest(start_file)
            and result.get("new_qemu_pid") == state.get("pid") and result.get("start_count") == 1
            and receipt.get("status") == "passed" and receipt.get("kind") == "windows-system-event-recovery"
            and receipt.get("recover_start_attempt_sha256") == contract.digest(start_file)
            and receipt.get("recover_start_result_sha256") == contract.digest(result_file)
            and receipt.get("qmp_shutdown_event_persisted") is False
            and receipt.get("actual_boot_id") == actual["boot_id"]
            and receipt.get("probe_boot_id") == actual.get("probe_boot_id"), "BOUND_RECOVERED_START_RECEIPTS_REQUIRED")
    events_file = output / receipt["events_file"]
    require(events_file.parent.resolve() == output.resolve() and events_file.name.startswith("reboot-recovery-events-")
            and receipt.get("events_sha256") == contract.digest(events_file), "BOUND_SYSTEM_EVENT_FILE_REQUIRED")
    captured = contract.read(events_file)
    sampled = captured.get("runtime", {})
    require(captured.get("candidate") == binding and captured.get("source") == "actual-Windows-System-event-log"
            and sampled.get("guest_uuid") == contract.UUID and sampled.get("purpose") == "disposable-qa"
            and sampled.get("boot_id") == receipt["actual_boot_id"]
            and sampled.get("probe_boot_id") == receipt["probe_boot_id"], "BOUND_SYSTEM_EVENT_CAPTURE_REQUIRED")
    # 采集时进程必须真实有效；后续主机/个人切换可改变PID，不能要求历史采集PID等同后续PID。
    contract.runtime_matches(sampled, binding)
    summary = validate_events(captured["events"], boot, overlay, actual)
    require(receipt.get("event_validation") == summary, "RECOVERED_EVENT_VALIDATION_CHANGED")
    contract.runtime_matches(actual, binding)
    return {"kind": receipt["kind"], "file": receipt_file.name, "sha256": contract.digest(receipt_file),
            "normal_attempt_sha256": contract.digest(output / "normal-reboot-attempt.json")}


def validate(binding, actual, state, validate_restart, output=OUTPUT):
    if (output / "reboot-recovery-completed.json").exists():
        return validate_recovery(binding, actual, state, output)
    boot, attempt, _ = inputs(binding, output)
    boot["receipt_sha256"] = contract.digest(output / "retention-boot-before.json")
    attempt["receipt_sha256"] = contract.digest(output / "normal-reboot-attempt.json")
    contract.validate_reboot(boot, attempt, contract.read(output / "normal-reboot-completed.json"),
                             actual, binding, state, validate_restart)
    return {"kind": "qmp-normal-reboot", "file": "normal-reboot-completed.json",
            "sha256": contract.digest(output / "normal-reboot-completed.json"), "normal_attempt_sha256": attempt["receipt_sha256"]}


def validate_retention(binding, report, state, validate_restart, output=OUTPUT):
    """主机/发布离线复核使用同一真实证据链，不从历史QMP标准构造恢复回执。"""
    require(report.get("status") == "passed" and report.get("candidate") == binding
            and report.get("guest_uuid") == contract.UUID and report.get("disk") == str(contract.DISK.resolve())
            and report.get("inputs") == c.preserved_inputs()
            and report.get("install_completed_sha256") == contract.digest(output / "overlay-install/completed.json"),
            "BOUND_REBOOT_RETENTION_REPORT_REQUIRED")
    validated = validate(binding, report["runtime"], state, validate_restart, output)
    require(report.get("reboot_evidence") == validated, "RETENTION_REBOOT_EVIDENCE_LINK_CHANGED")
    return validated
