"""在当前运行的实验室 Guest 中真正重启，并核对内核 boot_id 变化。"""
import json
import socket
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, write_json
from identity import wait_stopped
from lab import load_configuration
from providers import QemuLab


def poweroff_event(lab, target, port):
    """先订阅 QMP，只有 Guest 发出的正常关机事件才允许再次开机。"""
    # QEMU 的本机 QMP 仅接受一个连接；lab.ssh 自身会探测 QMP。
    # 先由 systemd 登记延迟关机，再占用监视连接，避免自锁且保持正常关机。
    lab.ssh(target, "sudo -n systemd-run --on-active=10 /bin/systemctl poweroff", timeout=20)
    with socket.create_connection(("127.0.0.1", port), timeout=5) as sock:
        stream = sock.makefile("rwb")
        if "QMP" not in json.loads(stream.readline()):
            raise RuntimeError("INVALID_QMP_SERVER")
        stream.write(b'{"execute":"qmp_capabilities"}\n')
        stream.flush()
        while "return" not in json.loads(stream.readline()):
            pass
        sock.settimeout(180)
        while True:
            line = stream.readline()
            if not line:
                raise RuntimeError("QEMU_EXIT_WITHOUT_GUEST_SHUTDOWN_EVENT")
            event = json.loads(line)
            if event.get("event") == "SHUTDOWN":
                data = event.get("data", {})
                if data.get("guest") is not True or data.get("reason") != "guest-shutdown":
                    raise RuntimeError("NORMAL_GUEST_SHUTDOWN_REQUIRED")
                return event


def reboot(target, *, lab=None):
    if lab is None:
        matrix, media = load_configuration()
        lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    matrix = lab.matrix
    state = lab.state(target)
    if not lab.live(state) or matrix["targets"][target]["os"] != "linux":
        raise RuntimeError("OWNED_RUNNING_LINUX_GUEST_REQUIRED")
    marker = json.loads(lab.ssh(target, "sudo -n cat /etc/partyops-vm-lab.json"))
    if marker.get("uuid") != state["uuid"]:
        raise RuntimeError("GUEST_UUID_MISMATCH")
    before = lab.ssh(target, "cat /proc/sys/kernel/random/boot_id").strip()
    record = {"target": target, "generated_at": now(), "status": "pending", "actual": False,
              "boot_id_before": before, "boot_id_after": "", "scope": "guest-reboot-only-not-full-acceptance"}
    # 本机 WHPX 的 legacy IRQ Guest 曾在热重启时出现 VP exit 4。
    # 使用操作系统正常关机再开机；不以 QMP reset、杀进程或强制断电发放重启证据。
    cold = state.get("acceleration") == "whpx" and matrix["targets"][target].get("whpx_legacy_irq") is True
    record["method"] = "guest-poweroff-and-cold-start" if cold else "guest-systemctl-reboot"
    path = lab.root / "reports" / target / ("reboot-" + before + ".json")
    write_json(path, record)
    if cold:
        try:
            record["shutdown_event"] = poweroff_event(lab, target, state["qmp_port"])
            wait_stopped(lab, target, 180)
            record.update(normal_shutdown_observed=True, powered_off_at=now())
            write_json(path, record)
            lab.start(target, state["acceleration"], state.get("provisioning_network", False))
        except (RuntimeError, OSError) as exc:
            record.update(status="failed", error=str(exc))
            write_json(path, record)
            raise
    else:
        try:
            lab.ssh(target, "sudo -n systemctl reboot", timeout=20)
        except RuntimeError:
            # 正常 reboot 也会先关闭 SSH；后续必须以新的内核 boot_id 确认。
            pass
    deadline = time.monotonic() + 600
    while time.monotonic() < deadline:
        try:
            after = lab.ssh(target, "cat /proc/sys/kernel/random/boot_id", timeout=12).strip()
            if after and after != before:
                marker = json.loads(lab.ssh(target, "sudo -n cat /etc/partyops-vm-lab.json"))
                if marker.get("uuid") != state["uuid"]:
                    record.update(status="failed", error="GUEST_UUID_MISMATCH_AFTER_REBOOT")
                    write_json(path, record)
                    raise ValueError("GUEST_UUID_MISMATCH_AFTER_REBOOT")
                record.update(actual=True, status="passed", boot_id_after=after, completed_at=now())
                write_json(path, record)
                return record
        except RuntimeError:
            pass
        time.sleep(3)
    record.update(status="failed", error="GUEST_REBOOT_TIMEOUT")
    write_json(path, record)
    raise RuntimeError("GUEST_REBOOT_TIMEOUT")


if __name__ == "__main__":
    print(json.dumps(reboot(sys.argv[1]), ensure_ascii=False, indent=2))
