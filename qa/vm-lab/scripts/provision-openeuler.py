"""为官方非 cloud-init openEuler 基础镜像配置隔离测试账号。

只连接已经由本实验室启动、UUID 与来宾 DMI 完全一致的虚拟机。
官方初始口令仅用于本地回环首次配置，随后锁定 root 口令；不记录口令。
"""
from __future__ import annotations

import json
import shlex
import sys
from pathlib import Path

import paramiko

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, write_json
from lab import load_configuration
from providers import QemuLab


def provision(target: str) -> dict:
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    state = lab.state(target)
    if not lab.live(state) or not target.startswith("openeuler-"):
        raise RuntimeError("ONLY_OWNED_RUNNING_OPENEULER_VM_ALLOWED")
    try:
        existing = lab.ssh(target, "sudo -n cat /etc/partyops-vm-lab.json")
        if json.loads(existing) == {"uuid": state["uuid"], "purpose": "disposable-qa"}:
            return {"target": target, "status": "already_provisioned", "guest_uuid": state["uuid"]}
    except (RuntimeError, ValueError):
        pass
    client = paramiko.SSHClient()
    client.load_host_keys(str(lab.root / "keys/known_hosts"))
    client.set_missing_host_key_policy(paramiko.RejectPolicy())
    client.connect("127.0.0.1", port=matrix["targets"][target]["ssh_port"], username="root",
                   password="openEuler12#$", look_for_keys=False, allow_agent=False,
                   timeout=15, auth_timeout=15, banner_timeout=15)
    def execute(command: str) -> str:
        _, stdout, stderr = client.exec_command(command, timeout=45)
        text, error = stdout.read().decode(), stderr.read().decode()
        if stdout.channel.recv_exit_status() != 0:
            raise RuntimeError("GUEST_PROVISION_COMMAND_FAILED: " + error[-1000:])
        return text.strip()
    try:
        actual = execute("cat /sys/class/dmi/id/product_uuid").lower()
        if actual != state["uuid"].lower():
            raise RuntimeError("GUEST_UUID_MISMATCH")
        public = (lab.root / "keys/guest_ed25519.pub").read_text().strip()
        execute("id partyopsqa >/dev/null 2>&1 || useradd -m -s /bin/bash partyopsqa")
        execute("install -d -m 700 -o partyopsqa -g partyopsqa /home/partyopsqa/.ssh")
        # SFTP 写入的是实验室 Guest 配置，不修改宿主项目文件。
        with client.open_sftp() as sftp:
            with sftp.file("/home/partyopsqa/.ssh/authorized_keys", "w") as output:
                output.write(public + "\n")
            with sftp.file("/etc/sudoers.d/partyops-qa-lab", "w") as output:
                output.write("partyopsqa ALL=(ALL) NOPASSWD:ALL\n")
            with sftp.file("/etc/partyops-vm-lab.json", "w") as output:
                output.write(json.dumps({"uuid": state["uuid"], "purpose": "disposable-qa"}))
        execute("chown partyopsqa:partyopsqa /home/partyopsqa/.ssh/authorized_keys; chmod 600 /home/partyopsqa/.ssh/authorized_keys; chmod 440 /etc/sudoers.d/partyops-qa-lab; visudo -cf /etc/sudoers.d/partyops-qa-lab")
        execute("timedatectl set-timezone Asia/Shanghai")
        execute("hostnamectl set-hostname " + shlex.quote("partyops-qa-" + target))
        # 先证明确实能用专用密钥登录，再锁定公开的 root 默认口令。
        proof = lab.ssh(target, "id -u; sudo -n true")
        if proof.strip() in ("", "0"):
            raise RuntimeError("STANDARD_ACCOUNT_NOT_PROVEN")
        execute("passwd -l root >/dev/null")
        result = {"generated_at": now(), "target": target, "guest_uuid": actual,
                  "status": "provisioned", "root_default_password_locked": True,
                  "cloud_init_available": execute("command -v cloud-init || true") != ""}
        write_json(lab.root / "state" / f"provision-{target}.json", result)
        return result
    finally:
        client.close()


if __name__ == "__main__":
    print(json.dumps(provision(sys.argv[1]), ensure_ascii=False, indent=2))
