"""在已通过原版校验的 UOS 安装器中登记安装后实验室账户；不修改 ISO。"""
from __future__ import annotations

import argparse
import json
import secrets
import shlex
import sys
from pathlib import Path

import paramiko

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, sha256, write_json
from lab import load_configuration
from providers import QemuLab


def prepare(target: str) -> dict:
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    spec, state = matrix["targets"][target], lab.state(target)
    if spec.get("distribution_id") != "uos" or not lab.live(state) or state.get("clean_baseline"):
        raise RuntimeError("OWNED_UOS_INSTALLER_REQUIRED")
    if sha256(Path(state["base"])) != media[spec["media"]]["sha256"]:
        raise RuntimeError("UOS_ORIGINAL_MEDIA_CHANGED")
    client = paramiko.SSHClient()
    client.load_host_keys(str(lab.vm_dir(target) / "live-known-hosts"))
    client.connect("127.0.0.1", port=spec["ssh_port"], username="uos",
                   key_filename=str(lab.root / "keys/guest_ed25519"),
                   look_for_keys=False, allow_agent=False, timeout=15)
    def execute(command):
        _, stdout, stderr = client.exec_command(command, timeout=45)
        result, error = stdout.read().decode(), stderr.read().decode()
        if stdout.channel.recv_exit_status():
            raise RuntimeError("UOS_LIVE_PREPARATION_FAILED:" + error[-500:])
        return result.strip()
    try:
        if execute("sudo dmidecode -s system-uuid").lower() != state["uuid"].lower():
            raise RuntimeError("GUEST_HARDWARE_UUID_MISMATCH")
        execute("test -d /usr/lib/live/mount/rootfs/filesystem.squashfs && grep -qx 'ID=uos' /etc/os-release && grep -qx 'MinorVersion=1070' /etc/os-version")
        credential_path = lab.vm_dir(target) / "guest-credential.local.json"
        if credential_path.exists():
            credential = json.loads(credential_path.read_text(encoding="utf-8"))
            if credential["uuid"] != state["uuid"]:
                raise RuntimeError("UOS_CREDENTIAL_IDENTITY_MISMATCH")
        else:
            write_json(credential_path, {"username": "partyopsqa", "password": "Pq7!" + secrets.token_hex(8), "uuid": state["uuid"]})
        public = (lab.root / "keys/guest_ed25519.pub").read_text().strip()
        marker = json.dumps({"uuid": state["uuid"], "purpose": "disposable-qa"})
        hook = f'''#!/bin/bash
# 原版安装器 user_config 后置钩子，只配置当前一次性 Guest。
set -euo pipefail
test "$(dmidecode -s system-uuid | tr '[:upper:]' '[:lower:]')" = {shlex.quote(state['uuid'].lower())}
id partyopsqa
install -d -m 700 -o partyopsqa -g partyopsqa /home/partyopsqa/.ssh
printf '%s\\n' {shlex.quote(public)} > /home/partyopsqa/.ssh/authorized_keys
chown partyopsqa:partyopsqa /home/partyopsqa/.ssh/authorized_keys
chmod 600 /home/partyopsqa/.ssh/authorized_keys
printf '%s\\n' 'partyopsqa ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/partyops-qa-lab
chmod 440 /etc/sudoers.d/partyops-qa-lab
visudo -cf /etc/sudoers.d/partyops-qa-lab
printf '%s\\n' {shlex.quote(marker)} > /etc/partyops-vm-lab.json
systemctl enable ssh
'''
        local = lab.vm_dir(target) / "99_partyops_lab.job"
        local.write_text(hook, encoding="utf-8", newline="\n")
        with client.open_sftp() as sftp:
            sftp.put(str(local), "/home/uos/99_partyops_lab.job")
        execute("sudo install -m 755 /home/uos/99_partyops_lab.job /usr/share/deepin-installer/tools/hooks/user_config/99_partyops_lab.job")
        result = {"at": now(), "target": target, "vm_uuid": state["uuid"], "hook_sha256": sha256(local),
                  "stage": "installer-prepared", "runtime_environment_passed": False}
        write_json(lab.root / "reports" / target / "installer-preparation.json", result)
        return result
    finally:
        client.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("target", choices=["uos-deb-x64", "uos-deb-arm64"])
    print(json.dumps(prepare(parser.parse_args().target), ensure_ascii=False, indent=2))
