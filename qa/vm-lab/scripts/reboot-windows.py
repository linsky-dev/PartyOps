"""Windows 真实关机后冷启动；WMI 时间变化不能单独作为系统重启证据。"""
from __future__ import annotations

import argparse
import json
import socket
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, write_json
from identity import probe, runtime_binding, wait_probe, wait_stopped
from lab import fingerprint, load_configuration
from providers import QemuLab
from windows_remote import session as winrm_session


def request_win7_shutdown(client, target, expected_uuid):
    """使用Guest正常关机；超时不重发，最终仍须独立QMP关机事件证明。"""
    if target not in {"win7-x64", "win7-x86"}:
        raise RuntimeError("ORIGINAL_WIN7_SHUTDOWN_TARGET_REQUIRED")
    script = "$expectedUuid='" + str(uuid.UUID(expected_uuid)) + "'\n" + r"""
if((Get-WmiObject Win32_ComputerSystemProduct).UUID -ne $expectedUuid){throw 'GUEST_SHUTDOWN_UUID_CHANGED'}
$shutdown=Join-Path $env:SystemRoot 'System32\shutdown.exe'
# /t 大于0会隐含强制关闭；明确使用0且不加/f，保留应用的正常退出机会。
& $shutdown /s /t 0
if($LASTEXITCODE -ne 0){throw ('GUEST_SHUTDOWN_COMMAND_FAILED:'+ $LASTEXITCODE)}
[Console]::WriteLine('shutdown-requested')
"""
    try:
        response = client.run_ps(script)
        return {"method": "guest-shutdown-exe", "arguments": ["/s", "/t", "0"],
                "command_acknowledged": response.status_code == 0 and response.std_out.decode("utf-8-sig").strip() == "shutdown-requested",
                "transport_status_code": response.status_code}
    except Exception as exc:
        # 正常关机可能先结束WinRM；只记请求回执不确定，禁止据此声称关机成功。
        return {"method": "guest-shutdown-exe", "arguments": ["/s", "/t", "0"],
                "command_acknowledged": False, "request_error_type": type(exc).__name__}


def validate_restart(receipt, context, state):
    if receipt.get("context") != context:
        raise RuntimeError("REBOOT_CONTEXT_CHANGED")
    event = receipt.get("shutdown_event", {})
    if event.get("event") != "SHUTDOWN" or event.get("data") != {"guest": True, "reason": "guest-shutdown"}:
        raise RuntimeError("NORMAL_GUEST_SHUTDOWN_NOT_PROVEN")
    before, after = receipt.get("qemu_pid_before"), receipt.get("qemu_pid_after")
    if not before or not after or before == after or after != state.get("pid"):
        raise RuntimeError("NEW_OWNED_QEMU_PROCESS_REQUIRED")
    if receipt.get("status") != "passed" or receipt.get("actual") is not True:
        raise RuntimeError("GUEST_REBOOT_NOT_COMPLETED")
    boot_before = receipt.get("identity_before", {}).get("boot_id")
    boot_after = receipt.get("identity_after", {}).get("boot_id")
    if not boot_before or not boot_after or boot_before == boot_after:
        raise RuntimeError("GUEST_BOOT_ID_CHANGE_REQUIRED")


def restart_context(lab, target, install_report=None, prerequisites=False):
    """安装后的普通用户准备可独立冷启动，不能伪造业务记录或覆盖业务重启指针。"""
    if prerequisites:
        if install_report is not None or target not in {"win7-x64", "win7-x86"}:
            raise RuntimeError("ORIGINAL_WIN7_PREREQUISITE_REBOOT_REQUIRED")
        context = {"target": target, "environment": runtime_binding(lab, target),
                   "restore_generation": lab.state(target).get("restore_generation")}
        return context, "original-win7-prerequisite-cold-start", "prerequisite-reboot-"
    if install_report is not None:
        from windows_win7_standard import installation_binding
        context = installation_binding(lab, target, Path(install_report), fingerprint())
        return context, "guest-shutdown-cold-start-for-standard-user-preparation", "standard-user-reboot-"
    pointer = json.loads((lab.root / "state" / ("business-" + target + ".json")).read_text(encoding="utf-8"))
    return pointer["context"], "guest-shutdown-cold-start-not-full-lifecycle", "reboot-"


def reboot(target, *, lab=None, install_report=None, prerequisites=False):
    if lab is None:
        matrix, media = load_configuration()
        lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    matrix = lab.matrix
    if matrix["targets"][target]["os"] != "windows":
        raise RuntimeError("WINDOWS_TARGET_REQUIRED")
    state = lab.state(target)
    before = probe(lab, target)
    context, scope, pointer_prefix = restart_context(lab, target, install_report, prerequisites)
    if context["environment"] != runtime_binding(lab, target) or context["restore_generation"] != state.get("restore_generation"):
        raise RuntimeError("REBOOT_BASELINE_CHANGED")
    record = {"generated_at": now(), "target": target, "context": context, "status": "pending", "actual": False,
              "qemu_pid_before": state["pid"], "identity_before": before,
              "scope": scope, "runtime_environment_passed": False}
    path = lab.root / "reports" / target / ("reboot-" + uuid.uuid4().hex[:12] + ".json")
    write_json(path, record)
    try:
        guest_shutdown = target in {"win7-x64", "win7-x86"}
        # QMP只接受一个控制连接；先完成session内的live/UUID检查，再独占事件连接。
        shutdown_client = winrm_session(lab, target, 90) if guest_shutdown else None
        # 先订阅QMP，再正常关机；Win7显式调用系统关机，避免电源按钮策略忽略ACPI。
        with socket.create_connection(("127.0.0.1", state["qmp_port"]), timeout=5) as sock:
            stream = sock.makefile("rwb")
            if "QMP" not in json.loads(stream.readline()):
                raise RuntimeError("INVALID_QMP_SERVER")
            commands = ("qmp_capabilities",) if guest_shutdown else ("qmp_capabilities", "system_powerdown")
            for command in commands:
                stream.write((json.dumps({"execute": command}) + "\n").encode())
                stream.flush()
                while True:
                    response = json.loads(stream.readline())
                    if "error" in response:
                        raise RuntimeError("QMP_SHUTDOWN_REQUEST_FAILED")
                    if "return" in response:
                        break
            if guest_shutdown:
                record["shutdown_request"] = request_win7_shutdown(shutdown_client, target, state["uuid"])
                write_json(path, record)
            sock.settimeout(240)
            while True:
                line = stream.readline()
                if not line:
                    raise RuntimeError("QEMU_EXIT_WITHOUT_GUEST_SHUTDOWN_EVENT")
                event = json.loads(line)
                if event.get("event") == "SHUTDOWN":
                    if event.get("data") != {"guest": True, "reason": "guest-shutdown"}:
                        raise RuntimeError("NORMAL_GUEST_SHUTDOWN_REQUIRED")
                    record["shutdown_event"] = event
                    break
        wait_stopped(lab, target, 120)
        record["stopped_at"] = now()
        write_json(path, record)
        lab.start(target, state["acceleration"], False)
        after = wait_probe(lab, target, 600)
        record.update(identity_after=after, qemu_pid_after=lab.state(target)["pid"], actual=True,
                      status="passed", completed_at=now())
        validate_restart(record, context, lab.state(target))
        write_json(lab.root / "state" / (pointer_prefix + target + ".json"), {"path": str(path), "receipt": record})
    except Exception as exc:
        record.update(status="failed", actual=False, error=str(exc))
        raise
    finally:
        write_json(path, record)
    return record


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--install-report", type=Path)
    mode.add_argument("--prerequisites", action="store_true")
    args = parser.parse_args()
    print(json.dumps(reboot(args.target, install_report=args.install_report, prerequisites=args.prerequisites), ensure_ascii=False, indent=2))
