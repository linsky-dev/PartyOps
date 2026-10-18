"""仅恢复已中断正常重启的开机步骤；不关机、不安装、不重发 start。"""
import argparse
import json
import uuid
from pathlib import Path

import controller as c
import reboot_evidence as evidence

OUTPUT = Path(__file__).resolve().parent
contract, require = c.contract, c.require


def stopped(lab, state, old_pid):
    import psutil
    require(old_pid == 2900, "OBSERVED_ORIGINAL_QEMU_PID_2900_REQUIRED")
    require(state.get("target") == "win10-x64" and state.get("uuid") == contract.UUID
            and Path(state.get("disk", "")).resolve() == contract.DISK.resolve()
            and state.get("status") == "stopped" and state.get("pid") is None
            and state.get("installation_media_detached") is True and not state.get("seed"), "ORIGINAL_STOPPED_VM_REQUIRED")
    require(not psutil.pid_exists(old_pid), "ORIGINAL_QEMU_PID_STILL_EXISTS")
    require(not lab.live(state) and not lab.process_active(state), "BOUND_VM_STILL_RUNNING")
    for process in psutil.process_iter(["name", "cmdline"]):
        if not (process.info.get("name") or "").lower().startswith("qemu-system-"):
            continue
        command = process.info.get("cmdline")
        require(command is not None, "QEMU_PROCESS_IDENTITY_UNAVAILABLE")
        require(contract.UUID not in command and not any(str(contract.DISK).casefold() in arg.casefold()
                or contract.DISK.as_posix().casefold() in arg.casefold() for arg in command), "ANOTHER_BOUND_QEMU_PROCESS_RUNNING")


def event_script(lower):
    evidence.instant(lower)
    # 不取Message/Properties；避免持久化事件内用户名、路径等正文。
    return "$lower=[DateTimeOffset]::Parse('" + lower + "').UtcDateTime\n" + r"""
$ErrorActionPreference='Stop'
$allowed=@{'Microsoft-Windows-Kernel-General'=@(12,13);'Microsoft-Windows-Kernel-Power'=@(41);'User32'=@(1074);'EventLog'=@(6005,6006,6008)}
$events=@(Get-WinEvent -FilterHashtable @{LogName='System';Id=@(12,13,41,1074,6005,6006,6008);StartTime=$lower} -ErrorAction Stop |
  Where-Object {$allowed.ContainsKey($_.ProviderName) -and $_.Id -in $allowed[$_.ProviderName]} |
  Sort-Object RecordId | ForEach-Object {@{log=$_.LogName;provider=$_.ProviderName;id=[int]$_.Id;record_id=[long]$_.RecordId;utc=$_.TimeCreated.ToUniversalTime().ToString('o')}})
@{events=$events}|ConvertTo-Json -Depth 5 -Compress
"""


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", nargs="?", default="check", choices=["check", "recover-start", "observe"])
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--sha256", required=True)
    parser.add_argument("--old-qemu-pid", type=int, default=2900)
    parser.add_argument("--execute", action="store_true")
    args = parser.parse_args(argv)
    require(args.execute == (args.action != "check"), "EXPLICIT_RECOVERY_EXECUTE_REQUIRED")
    if args.action == "recover-start":
        c.no_attempt(OUTPUT / "recover-start.attempt.json")
        c.no_attempt(OUTPUT / "recover-start.result.json")
    require(not (OUTPUT / "normal-reboot-completed.json").exists(), "STANDARD_REBOOT_ALREADY_COMPLETED")
    module, lab, target = c.environment()
    binding = contract.candidate(lab, c.ROOT, args.package, args.sha256)
    boot, attempt, overlay = evidence.inputs(binding)
    if args.action == "check":
        print(json.dumps({"status": "checked-offline", "candidate": binding,
            "normal_attempt_sha256": contract.digest(OUTPUT / "normal-reboot-attempt.json"),
            "qmp_shutdown_event_persisted": False, "guest_action_executed": False}, ensure_ascii=False))
        return
    if args.action == "recover-start":
        state = lab.state("win10-x64")
        stopped(lab, state, args.old_qemu_pid)
        contract.exclusive(OUTPUT / "recover-start.attempt.json", {"time_utc": contract.now(), "candidate": binding,
            "guest_uuid": contract.UUID, "disk": str(contract.DISK.resolve()), "old_qemu_pid": args.old_qemu_pid,
            "old_pid_source": "主控及Architect本会话实际工具观察；非历史落盘PID回执",
            "old_pid_absent": True, "no_bound_qemu_process": True, "qmp_unreachable": True,
            "normal_attempt_sha256": contract.digest(OUTPUT / "normal-reboot-attempt.json"),
            "overlay_sha256": contract.digest(OUTPUT / "retention-after-overlay.json"),
            "boot_before_sha256": contract.digest(OUTPUT / "retention-boot-before.json"),
            "stopped_vm_state_sha256": contract.digest(lab.vm_dir("win10-x64") / "vm.json"),
            "operation": "resume-interrupted-normal-reboot-start-only-once", "qmp_shutdown_event_persisted": False})
        # 唯一副作用：复用原UUID/盘的正常开机；绝不调用关机/安装/快照。
        started = lab.start("win10-x64", state["acceleration"], False)
        require(started.get("uuid") == contract.UUID and Path(started.get("disk", "")).resolve() == contract.DISK.resolve(),
                "STARTED_VM_BINDING_CHANGED")
        contract.exclusive(OUTPUT / "recover-start.result.json", {"status": "started", "candidate": binding,
            "attempt_sha256": contract.digest(OUTPUT / "recover-start.attempt.json"), "new_qemu_pid": started["pid"],
            "start_count": 1, "time_utc": contract.now(), "reboot_evidence": "not-yet-verified"})
        print("唯一恢复开机已记录；等待Guest正常启动后执行observe及controller verify-reboot。")
        return
    state = contract.bind_guest(lab, module.qmp)
    start = contract.read(OUTPUT / "recover-start.result.json")
    require(start.get("candidate") == binding and start.get("status") == "started"
            and start.get("new_qemu_pid") == state["pid"]
            and start.get("attempt_sha256") == contract.digest(OUTPUT / "recover-start.attempt.json"), "BOUND_SINGLE_START_REQUIRED")
    c.no_attempt(OUTPUT / "reboot-recovery-completed.json")
    client = module.windows.connect(lab, target)
    try:
        from identity import probe as identity_probe
        actual = contract.probe(client, module.windows.execute)
        actual["probe_boot_id"] = identity_probe(lab, "win10-x64")["boot_id"]
        c.identity_matches(actual, binding)
        contract.runtime_matches(actual, binding)
        value = json.loads(module.windows.execute(client, event_script(overlay["runtime"]["processes"][0]["created"])))
        events_name = "reboot-recovery-events-" + uuid.uuid4().hex[:12] + ".json"
        contract.exclusive(OUTPUT / events_name, {"candidate": binding, "runtime": actual,
            "collected_at": contract.now(), "events": value["events"], "source": "actual-Windows-System-event-log"})
        summary = evidence.validate_events(value["events"], boot, overlay, actual)
        final = contract.probe(client, module.windows.execute)
        require(final["boot_id"] == actual["boot_id"] and final["processes"] == actual["processes"], "BOOT_OR_RUNTIME_CHANGED_DURING_EVENT_READ")
        contract.bind_guest(lab, module.qmp)
        contract.exclusive(OUTPUT / "reboot-recovery-completed.json", {"status": "passed", "kind": "windows-system-event-recovery",
            "candidate": binding, "guest_uuid": contract.UUID, "disk": str(contract.DISK.resolve()),
            "normal_attempt_sha256": contract.digest(OUTPUT / "normal-reboot-attempt.json"),
            "recover_start_attempt_sha256": contract.digest(OUTPUT / "recover-start.attempt.json"),
            "recover_start_result_sha256": contract.digest(OUTPUT / "recover-start.result.json"),
            "events_file": events_name, "events_sha256": contract.digest(OUTPUT / events_name), "event_validation": summary,
            "qmp_shutdown_event_persisted": False, "actual_boot_id": actual["boot_id"], "probe_boot_id": actual["probe_boot_id"],
            "completed_at": contract.now(), "limitation": "原QMP正常关机事件仅内存返回，未落盘；本次依据实际Windows系统事件及boot变化"})
    finally:
        client.close()
    evidence.validate_recovery(binding, actual, state)


if __name__ == "__main__":
    main()
