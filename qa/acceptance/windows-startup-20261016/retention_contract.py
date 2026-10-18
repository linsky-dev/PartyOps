"""原 Win10 验收绑定：只信正式构建回执及实际 QMP/Guest 证据。"""
from __future__ import annotations

import hashlib
import json
import socket
from datetime import datetime, timezone
from pathlib import Path

UUID = "f47cf265-72a2-4d51-9cf1-b1f5ae960cb2"
DISK = Path("D:/PartyOps-VM-Lab/vms/win10-x64/system.qcow2")
INSTALL = r"C:\PartyOps QA\中文 程序"


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def exclusive(path, value):
    """所有操作回执独占创建；断线后只能检查，不自动重发。"""
    with Path(path).open("x", encoding="utf-8") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)


def now():
    return datetime.now(timezone.utc).isoformat()


def bind_guest(lab, qmp):
    state = lab.state("win10-x64")
    if state.get("uuid") != UUID or Path(state.get("disk", "")).resolve() != DISK.resolve():
        raise RuntimeError("REGISTERED_WIN10_UUID_DISK_MISMATCH")
    if qmp(state["qmp_port"], "query-uuid").get("UUID") != UUID:
        raise RuntimeError("LIVE_WIN10_UUID_MISMATCH")
    # 只接受唯一实际块设备的顶层 system.qcow2；ISO 只读设备不计系统盘。
    blocks = qmp(state["qmp_port"], "query-block")
    writable = [b["inserted"] for b in blocks if b.get("inserted") and b["inserted"].get("ro") is False]
    if len(writable) != 1:
        raise RuntimeError("ONE_ACTUAL_SYSTEM_DISK_REQUIRED")
    inserted = writable[0]
    image = inserted.get("image", {})
    if (Path(inserted.get("file", "")).resolve() != DISK.resolve()
            or Path(image.get("filename", "")).resolve() != DISK.resolve()
            or image.get("format") != "qcow2"):
        raise RuntimeError("QMP_ACTUAL_SYSTEM_DISK_MISMATCH")
    return state


def candidate(lab, root, package, expected_sha):
    """映射只由正式构建控制器产生，绝不以安装后文件推导期望值。"""
    from lab import fingerprint
    from windows_build_payload import expected_payload
    package = Path(package).resolve(strict=True)
    if not package.is_relative_to((root / "artifacts").resolve()) or package.name != "PartyOps_1.4.5-rc.6_windows_amd64.exe":
        raise RuntimeError("THIS_WORKSPACE_WIN10_CANDIDATE_REQUIRED")
    if digest(package) != expected_sha.lower():
        raise RuntimeError("CANDIDATE_SHA256_MISMATCH")
    bound, payload, _ = expected_payload(lab, {"id": "windows_amd64", "version": "1.4.5-rc.6",
                                              "path": str(package), "sha256": expected_sha.lower()}, fingerprint())
    receipt = read(bound["build_receipt"])
    if receipt.get("status") != "completed" or receipt.get("errors"):
        raise RuntimeError("SUCCESSFUL_OFFICIAL_BUILD_REQUIRED")
    return {"package": bound, "manifest_sha256": payload["manifest"]["sha256"],
            "executable_sha256": payload["executables"]["PartyOps.exe"]["sha256"],
            "wizard_sha256": payload["executables"]["PartyOpsWizard.exe"]["sha256"]}


def probe(client, execute):
    # 只读取系统启动时间、QA 标记、程序哈希及 HTTP 监听归属，不读取用户内容。
    script = r"""$ErrorActionPreference='Stop'
$os=Get-CimInstance Win32_OperatingSystem
$identity=Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw|ConvertFrom-Json
$dir='C:\PartyOps QA\中文 程序'
$exe=Join-Path $dir 'PartyOps.exe'
$processes=@(Get-CimInstance Win32_Process -Filter "Name='PartyOps.exe'"|ForEach-Object {
  @{pid=[int]$_.ProcessId;path=$_.ExecutablePath;created=$_.CreationDate.ToUniversalTime().ToString('o')}
})
$owners=@(Get-NetTCPConnection -State Listen -LocalPort 18775 -ErrorAction SilentlyContinue|Select-Object -ExpandProperty OwningProcess -Unique)
@{guest_uuid=$identity.uuid;purpose=$identity.purpose;boot_id=$os.LastBootUpTime.ToUniversalTime().ToString('o');manifest_sha256=(Get-FileHash -LiteralPath (Join-Path $dir 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant();executable_sha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant();wizard_sha256=(Get-FileHash -LiteralPath (Join-Path $dir 'PartyOpsWizard.exe') -Algorithm SHA256).Hash.ToLowerInvariant();processes=$processes;http_owners=$owners}|ConvertTo-Json -Depth 5 -Compress
"""
    value = json.loads(execute(client, script))
    if value.get("guest_uuid") != UUID or value.get("purpose") != "disposable-qa" or not value.get("boot_id"):
        raise RuntimeError("ACTUAL_QA_GUEST_IDENTITY_REQUIRED")
    return value


def runtime_matches(actual, binding, earliest_start=None):
    for key in ("manifest_sha256", "executable_sha256", "wizard_sha256"):
        if actual.get(key) != binding.get(key):
            raise RuntimeError("ACTUAL_CANDIDATE_IDENTITY_MISMATCH:" + key)
    processes = actual.get("processes", [])
    owners = actual.get("http_owners", [])
    if not isinstance(owners, list):
        owners = [owners]
    # 多份 PartyOps 同时运行会混淆 HTTP 数据来源，也必须阻断。
    if (len(processes) != 1 or processes[0].get("path", "").casefold() != (INSTALL + r"\PartyOps.exe").casefold()
            or owners != [processes[0].get("pid")]):
        raise RuntimeError("CANDIDATE_RUNNING_HTTP_OWNER_REQUIRED")
    created = datetime.fromisoformat(processes[0]["created"].replace("Z", "+00:00"))
    threshold = datetime.fromisoformat((earliest_start or actual["boot_id"]).replace("Z", "+00:00"))
    if created.tzinfo is None or threshold.tzinfo is None or created < threshold:
        raise RuntimeError("CANDIDATE_PROCESS_MUST_START_AFTER_BOUND_OPERATION")
    return actual


def completed_install(folder, binding):
    attempt = read(folder / "attempt.json")
    completed = read(folder / "completed.json")
    boot_file = folder.parent / "retention-boot-before.json"
    if (attempt.get("guest_uuid") != UUID or attempt.get("disk") != str(DISK.resolve())
            or attempt.get("boot_before_sha256") != digest(boot_file)
            or attempt.get("candidate") != binding or completed.get("candidate") != binding
            or completed.get("attempt_sha256") != digest(folder / "attempt.json")
            or completed.get("status") != "completed" or completed.get("install", {}).get("exit_code") != 0
            or completed.get("identity", {}).get("uuid") != UUID):
        raise RuntimeError("SUCCESSFUL_BOUND_OVERLAY_REQUIRED")
    required = ("app_exists", "launcher_exists", "formatter_exists", "uninstaller_exists", "install_log_exists")
    if not all(completed["install"].get(key) is True for key in required):
        raise RuntimeError("COMPLETE_INSTALLED_LAYOUT_REQUIRED")
    return completed


def validate_reboot(before, attempt, receipt, actual, binding, state, validate_restart):
    if (before.get("guest_uuid") != UUID or before.get("disk") != str(DISK.resolve())
            or attempt.get("guest_uuid") != UUID or attempt.get("disk") != str(DISK.resolve())
            or attempt.get("candidate") != binding or receipt.get("candidate") != binding
            or receipt.get("attempt_sha256") != attempt["receipt_sha256"]
            or attempt.get("boot_before_sha256") != before["receipt_sha256"]
            or receipt.get("boot_before_sha256") != before["receipt_sha256"]):
        raise RuntimeError("BOUND_NORMAL_REBOOT_RECEIPTS_REQUIRED")
    restart = receipt["restart"]
    validate_restart(restart, attempt["context"], state)
    if (restart["identity_before"]["boot_id"] != before["probe_boot_id"]
            or restart["identity_after"]["boot_id"] != actual["probe_boot_id"]
            or before["boot_id"] == actual["boot_id"]):
        raise RuntimeError("ACTUAL_BOOT_CHANGE_REQUIRED")
    runtime_matches(actual, binding)


def request_normal_shutdown(state):
    """取自官方 reboot-windows.py 的最小 QMP 事件流程；只发一次 ACPI 正常关机。"""
    with socket.create_connection(("127.0.0.1", state["qmp_port"]), timeout=5) as sock:
        stream = sock.makefile("rwb")
        if "QMP" not in json.loads(stream.readline()):
            raise RuntimeError("INVALID_QMP_SERVER")
        shutdown_event = None
        for command in ("qmp_capabilities", "system_powerdown"):
            stream.write((json.dumps({"execute": command}) + "\n").encode())
            stream.flush()
            while True:
                response = json.loads(stream.readline())
                if "error" in response:
                    raise RuntimeError("QMP_SHUTDOWN_REQUEST_FAILED")
                if response.get("event") == "SHUTDOWN":
                    if command != "system_powerdown" or response.get("data") != {"guest": True, "reason": "guest-shutdown"}:
                        raise RuntimeError("NORMAL_GUEST_SHUTDOWN_REQUIRED")
                    shutdown_event = response
                if "return" in response:
                    break
        if shutdown_event:
            return shutdown_event
        sock.settimeout(240)
        while True:
            line = stream.readline()
            if not line:
                raise RuntimeError("QEMU_EXIT_WITHOUT_GUEST_SHUTDOWN_EVENT")
            event = json.loads(line)
            if event.get("event") == "SHUTDOWN":
                if event.get("data") != {"guest": True, "reason": "guest-shutdown"}:
                    raise RuntimeError("NORMAL_GUEST_SHUTDOWN_REQUIRED")
                return event


def reboot_context(binding):
    return {"target": "win10-x64", "guest_uuid": UUID, "disk": str(DISK.resolve()), "candidate": binding,
            "scope": "existing-install-overlay-normal-cold-start-not-clean-baseline-or-full-lifecycle"}


def reboot_once(lab, binding, qmp, validate_restart):
    """本次薄包装不读旧业务/重启指针，不伪造原介质或干净快照。"""
    from identity import probe as identity_probe, wait_probe, wait_stopped
    state = bind_guest(lab, qmp)
    before = identity_probe(lab, "win10-x64")
    record = {"generated_at": now(), "target": "win10-x64", "context": reboot_context(binding),
              "status": "pending", "actual": False, "qemu_pid_before": state["pid"], "identity_before": before}
    record["shutdown_event"] = request_normal_shutdown(state)
    wait_stopped(lab, "win10-x64", 120)
    stopped = lab.state("win10-x64")
    # 已关机时不能查询 QMP；只允许固定登记磁盘的原 VM 正常启动一次。
    if stopped.get("uuid") != UUID or Path(stopped.get("disk", "")).resolve() != DISK.resolve():
        raise RuntimeError("STOPPED_VM_UUID_DISK_CHANGED")
    lab.start("win10-x64", state["acceleration"], False)
    after = wait_probe(lab, "win10-x64", 600)
    current = bind_guest(lab, qmp)
    record.update(identity_after=after, qemu_pid_after=current["pid"], actual=True, status="passed", completed_at=now())
    validate_restart(record, reboot_context(binding), current)
    return record
