"""为官方 DVD 的全新空盘安装生成独立 Kickstart 光盘，不改动官方 ISO。"""
from __future__ import annotations

import argparse
import io
import json
import secrets
import sys
from pathlib import Path

import pycdlib

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, sha256, write_json
from lab import load_configuration
from providers import QemuLab, run


def prepare(target: str) -> dict:
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    state = lab.state(target)
    spec = matrix["targets"][target]
    if spec.get("distribution_id") != "openeuler" or state["status"] != "created" or lab.process_active(state):
        raise RuntimeError("NEW_STOPPED_OPENEULER_ISO_VM_REQUIRED")
    if sha256(Path(state["base"])) != media[spec["media"]]["sha256"]:
        raise RuntimeError("ORIGINAL_MEDIA_CHANGED")
    blocks = json.loads(run([lab.binary("qemu-img"), "map", "--output=json", state["disk"]]))
    if not blocks or any(block.get("data") or not block.get("zero") for block in blocks):
        raise RuntimeError("ONLY_EMPTY_NEW_SYSTEM_DISK_ALLOWED")
    directory = lab.vm_dir(target)
    seed = directory / "openeuler-kickstart.iso"
    if seed.exists():
        raise RuntimeError("KICKSTART_ALREADY_PREPARED")
    password = "Pq7!" + secrets.token_hex(10)
    write_json(directory / "guest-credential.local.json", {"uuid": state["uuid"], "username": "partyopsqa", "password": password})
    public = (lab.root / "keys/guest_ed25519.pub").read_text().strip()
    marker = json.dumps({"uuid": state["uuid"], "purpose": "disposable-qa"})
    kickstart = f'''# 原版 DVD 安装；仅接受本控制器创建的空白 vda。
text
cdrom
lang zh_CN.UTF-8
keyboard --vckeymap=us --xlayouts=us
timezone Asia/Shanghai --utc
network --bootproto=dhcp --device=link --activate --hostname=partyops-{target}
rootpw --lock
user --name=partyopsqa --groups=wheel --password={password} --plaintext
firstboot --disable
services --enabled=sshd
firewall --service=ssh
ignoredisk --only-use=vda
zerombr
clearpart --all --initlabel --drives=vda
autopart --type=plain --fstype=ext4
bootloader --boot-drive=vda
poweroff
%packages
@core
@standard
@fonts
@x11
openssh-server
sudo
python3
dmidecode
firefox
%end
%pre --erroronfail
test "$(cat /sys/class/dmi/id/product_uuid | tr '[:upper:]' '[:lower:]')" = '{state['uuid'].lower()}'
%end
%post --erroronfail
test "$(dmidecode -s system-uuid | tr '[:upper:]' '[:lower:]')" = '{state['uuid'].lower()}'
install -d -m 700 -o partyopsqa -g partyopsqa /home/partyopsqa/.ssh
printf '%s\\n' '{public}' > /home/partyopsqa/.ssh/authorized_keys
chown partyopsqa:partyopsqa /home/partyopsqa/.ssh/authorized_keys
chmod 600 /home/partyopsqa/.ssh/authorized_keys
printf '%s\\n' 'partyopsqa ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/partyops-qa-lab
chmod 440 /etc/sudoers.d/partyops-qa-lab
visudo -cf /etc/sudoers.d/partyops-qa-lab
printf '%s\\n' '{marker}' > /etc/partyops-vm-lab.json
restorecon -RF /home/partyopsqa/.ssh /etc/sudoers.d/partyops-qa-lab
%end
'''
    raw = kickstart.encode("utf-8")
    iso = pycdlib.PyCdlib()
    iso.new(interchange_level=3, joliet=3, rock_ridge="1.09", vol_ident="PARTYOPS_KS")
    iso.add_fp(io.BytesIO(raw), len(raw), iso_path="/KS.CFG;1", rr_name="ks.cfg", joliet_path="/ks.cfg")
    iso.write(str(seed))
    iso.close()
    state.update(seed=str(seed), installer_seed_sha256=sha256(seed))
    lab.save(target, state)
    result = {"at": now(), "target": target, "uuid": state["uuid"], "seed_sha256": sha256(seed),
              "boot_option": "inst.ks=cdrom:/dev/sr1:/ks.cfg", "status": "prepared",
              "source": "official-standard-dvd", "runtime_environment_passed": False}
    write_json(lab.root / "reports" / target / "kickstart-preparation.json", result)
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", choices=["openeuler-iso-x64", "openeuler-iso-arm64"])
    print(json.dumps(prepare(parser.parse_args().target), ensure_ascii=False, indent=2))
