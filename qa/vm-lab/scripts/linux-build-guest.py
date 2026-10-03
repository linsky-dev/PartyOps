"""从原版冷启动快照导出独立 Linux 构建盘；构建身份不进入必需验收矩阵。"""
from __future__ import annotations

import argparse
import copy
import json
import sys
import uuid
from pathlib import Path

import paramiko

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, safe_child, write_json
from identity import runtime_binding
from lab import load_configuration
from providers import QemuLab, run


def environment(source: str):
    matrix, media = load_configuration()
    if source not in matrix["targets"] or matrix["targets"][source]["os"] != "linux":
        raise RuntimeError("ORIGINAL_LINUX_SOURCE_REQUIRED")
    target = "build-" + source
    spec = copy.deepcopy(matrix["targets"][source])
    spec.update(ssh_port=spec["ssh_port"] + 100, vm_storage="fallback", vm_folder=target,
                installation_growth_gib=24)
    matrix["targets"][target] = spec
    return QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media), target


def create(lab, source: str, target: str) -> dict:
    original = lab.state(source)
    if lab.live(original) or lab.process_active(original):
        raise RuntimeError("BUILD_CLONE_REQUIRES_STOPPED_SOURCE")
    binding = runtime_binding(lab, source)
    directory = lab.vm_dir(target)
    if (directory / "vm.json").exists():
        state = lab.state(target)
        if state.get("clone_source_binding") != binding or state.get("purpose") != "disposable-build":
            raise RuntimeError("EXISTING_BUILD_CLONE_SOURCE_CHANGED")
        return state
    disk = safe_child(Path(lab.defaults["fallback_root"]) / "vms", directory / "system.qcow2")
    if disk.exists():
        raise RuntimeError("INCOMPLETE_BUILD_CLONE_RETAINED_FOR_REVIEW")
    lab.check_space(directory, max(24, Path(original["disk"]).stat().st_size / 1024**3))
    directory.mkdir(parents=True, exist_ok=True)
    state = {"schema_version": 1, "target": target, "uuid": str(uuid.uuid4()), "created_at": now(),
             "temporary": True, "purpose": "disposable-build", "disk": str(disk), "base": original["base"],
             "source_sha256": original["source_sha256"], "clone_source_binding": binding,
             "source_target": source, "seed": None, "status": "creating", "qmp_port": None, "pid": None,
             "snapshots": [], "installation_media_detached": True, "needs_identity_rebind": True}
    # 转换只读取指定的已封存快照，导出无 backing file 的独立盘，原盘后续运行不影响它。
    write_json(lab.root / "reports" / target / "creation.json", state)
    run([lab.binary("qemu-img"), "convert", "-l", original["clean_baseline"]["name"],
         "-f", "qcow2", "-O", "qcow2", original["disk"], str(disk)], timeout=1800)
    info = json.loads(run([lab.binary("qemu-img"), "info", "--output=json", str(disk)]))
    if info.get("backing-filename") or info.get("format") != "qcow2":
        raise RuntimeError("BUILD_CLONE_MUST_BE_STANDALONE_QCOW2")
    run([lab.binary("qemu-img"), "check", str(disk)], timeout=300)
    if lab.matrix["targets"][target].get("firmware"):
        import firmware
        name = original["clean_baseline"]["name"]
        record = firmware.snapshot_record(lab, source, original, name)
        state["firmware"] = firmware.prepare(lab, target, source_vars=firmware.snapshot_path(lab, source, name),
                                             source_sha256=record["vars_sha256"])
    state.update(status="created", created_disk_bytes=disk.stat().st_size)
    lab.save(target, state)
    write_json(lab.root / "reports" / target / "creation.json", state)
    return state


def bind(lab, source: str, target: str) -> dict:
    state = lab.state(target)
    if not lab.live(state) or state.get("purpose") != "disposable-build":
        raise RuntimeError("OWNED_RUNNING_BUILD_GUEST_REQUIRED")
    known_hosts = lab.root / "keys/known_hosts"
    source_address = "[127.0.0.1]:" + str(lab.matrix["targets"][source]["ssh_port"])
    target_address = "[127.0.0.1]:" + str(lab.matrix["targets"][target]["ssh_port"])
    # SSH 服务端密钥来自克隆的已验证基础盘；不接受任意网络端点的新密钥。
    keys = paramiko.HostKeys(str(known_hosts))
    source_keys = keys.lookup(source_address)
    if not source_keys:
        raise RuntimeError("VERIFIED_SOURCE_SSH_HOST_KEY_MISSING")
    for kind, key in source_keys.items():
        keys.add(target_address, kind, key)
    client = paramiko.SSHClient()
    client.get_host_keys().update(keys)
    client.set_missing_host_key_policy(paramiko.RejectPolicy())
    client.connect("127.0.0.1", port=lab.matrix["targets"][target]["ssh_port"], username="partyopsqa",
                   key_filename=str(lab.root / "keys/guest_ed25519"), look_for_keys=False, allow_agent=False, timeout=15)
    try:
        script = """import json,platform,subprocess
from pathlib import Path
marker=Path('/etc/partyops-vm-lab.json')
existing=json.loads(marker.read_text())
hardware=subprocess.check_output(['dmidecode','-s','system-uuid'],universal_newlines=True).strip().lower()
if hardware!=NEW_UUID or existing not in [OLD_MARKER,NEW_MARKER]:
 raise RuntimeError('CLONE_HARDWARE_OR_SOURCE_IDENTITY_MISMATCH')
marker.write_text(json.dumps(NEW_MARKER)+'\\n')
print(json.dumps({'uuid':hardware,'marker':NEW_MARKER,'arch':platform.machine(),'boot_id':Path('/proc/sys/kernel/random/boot_id').read_text().strip(),'os_release':Path('/etc/os-release').read_text()}))
"""
        script = script.replace("NEW_UUID", repr(state["uuid"]))
        script = script.replace("OLD_MARKER", repr({"uuid": state["clone_source_binding"]["vm_uuid"], "purpose": "disposable-qa"}))
        script = script.replace("NEW_MARKER", repr({"uuid": state["uuid"], "purpose": "disposable-build"}))
        stdin, stdout, stderr = client.exec_command("sudo -n python3 -", timeout=60)
        stdin.write(script)
        stdin.channel.shutdown_write()
        output, errors = stdout.read().decode(), stderr.read().decode()
        if stdout.channel.recv_exit_status():
            raise RuntimeError("BUILD_CLONE_IDENTITY_REBIND_FAILED:" + errors[-1000:])
        result = json.loads(output)
        result["arch"] = {"loong64": "loongarch64"}.get(result["arch"], result["arch"])
        if result["arch"] != lab.matrix["targets"][target]["arch"]:
            raise RuntimeError("BUILD_GUEST_ARCH_MISMATCH")
        keys.save(str(known_hosts))
        state.update(needs_identity_rebind=False, build_identity=result)
        lab.save(target, state)
        write_json(lab.root / "reports" / target / "identity.json", {**result, "at": now(), "runtime_environment_passed": False})
        return result
    finally:
        client.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("create", "start", "bind", "stop", "status"))
    parser.add_argument("source")
    args = parser.parse_args()
    lab, target = environment(args.source)
    if args.command == "create":
        result = create(lab, args.source, target)
    elif args.command == "start":
        result = lab.start(target, "tcg" if lab.matrix["targets"][target]["arch"] in {"aarch64", "arm64", "loongarch64"} else "whpx", True)
    elif args.command == "bind":
        result = bind(lab, args.source, target)
    elif args.command == "stop":
        result = lab.stop(target)
    else:
        result = lab.state(target)
    print(json.dumps(result, ensure_ascii=False, indent=2))
