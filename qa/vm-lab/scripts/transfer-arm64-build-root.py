"""把已有 ARM64 构建依赖流式迁入完整原版 Guest，不使用用户态 ISA 模拟。"""
import hashlib
import importlib.util
import json
import os
import shlex
import subprocess
import sys
from pathlib import Path

import paramiko

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, write_json


def main():
    spec = importlib.util.spec_from_file_location("build_guest", Path(__file__).with_name("linux-build-guest.py"))
    driver = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(driver)
    lab, target = driver.environment("uos-deb-arm64")
    state = lab.state(target)
    if state.get("needs_identity_rebind") or state.get("purpose") != "disposable-build":
        raise RuntimeError("BOUND_BUILD_GUEST_REQUIRED")
    destination = "/data/partyops-build-root"
    record = {"at": now(), "target": target, "uuid": state["uuid"], "destination": destination,
              "source": "OracleLinux_7_9:/opt/manylinux2014-aarch64-rootfs", "status": "running"}
    report = lab.root / "reports" / target / "dependency-transfer-20260907.json"
    previous = json.loads(report.read_text(encoding="utf-8")) if report.exists() else {}
    retry_empty = (previous.get("status") == "failed" and previous.get("transferred_bytes") == 0
                   and previous.get("uuid") == state["uuid"] and previous.get("destination") == destination)
    if previous:
        write_json(report.with_name("dependency-transfer-previous-20260907.json"), previous)
    client = paramiko.SSHClient()
    client.load_host_keys(str(lab.root / "keys/known_hosts"))
    client.set_missing_host_key_policy(paramiko.RejectPolicy())
    client.connect("127.0.0.1", port=lab.matrix["targets"][target]["ssh_port"], username="partyopsqa",
                   key_filename=str(lab.root / "keys/guest_ed25519"), look_for_keys=False, allow_agent=False, timeout=30)
    try:
        validation = "import json,os;from pathlib import Path;" + (
            "assert json.loads(Path('/etc/partyops-vm-lab.json').read_text())==" + repr({"uuid": state["uuid"], "purpose": "disposable-build"}) + ";"
            "assert os.uname().machine=='aarch64';p=Path('/data/partyops-build-root');"
            "assert not p.is_symlink() and (not p.exists() or (" + repr(retry_empty) + " and p.is_dir() and not any(p.iterdir())));"
            "s=os.statvfs('/data');assert s.f_bavail*s.f_frsize>12*1024**3;print('owned-build-root-ready')")
        _, output, errors = client.exec_command("python3 -c " + shlex.quote(validation), timeout=30)
        if output.channel.recv_exit_status():
            raise RuntimeError("DEPENDENCY_DESTINATION_NOT_READY:" + errors.read().decode())
        members = ["bin", "sbin", "lib", "lib64", "etc", "usr", "var/lib/rpm", "var/lib/yum",
                   "opt/_internal", "opt/python", "opt/rh", "opt/gcc-11.5.0-aarch64",
                   "opt/partyops-python-3.11.15-arm64", "tmp/partyops-native-tools-arm64"]
        record["included_paths"] = members
        write_json(report, record)
        # 旧 GNU tar 的 --use-compress-program 不拆分参数；用 gzip 标准环境选择级别。
        command = ["wsl.exe", "-d", "OracleLinux_7_9", "--exec", "env", "GZIP=-1", "tar", "-C", "/opt/manylinux2014-aarch64-rootfs",
                   "--exclude=usr/bin/qemu-*", "-czf", "-", *members]
        flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
        with report.with_suffix(".log").open("wb") as log:
            process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=log, creationflags=flags)
            stdin, output, errors = client.exec_command(
                "sudo -n mkdir -p /data/partyops-build-root && sudo -n tar -xzf - -C /data/partyops-build-root", timeout=7200)
            digest, count = hashlib.sha256(), 0
            try:
                for chunk in iter(lambda: process.stdout.read(1024 * 1024), b""):
                    stdin.write(chunk)
                    digest.update(chunk)
                    count += len(chunk)
                    if count // (32 * 1024**2) != (count - len(chunk)) // (32 * 1024**2):
                        record.update(transferred_bytes=count, progress_at=now())
                        write_json(report, record)
                stdin.channel.shutdown_write()
                tar_exit, guest_exit = process.wait(timeout=60), output.channel.recv_exit_status()
                record.update(archive_sha256=digest.hexdigest(), transferred_bytes=count,
                              source_tar_exit=tar_exit, guest_tar_exit=guest_exit, guest_stderr=errors.read().decode())
                if tar_exit or guest_exit:
                    raise RuntimeError("DEPENDENCY_STREAM_TRANSFER_FAILED")
            finally:
                if process.poll() is None:
                    process.terminate()
                    process.wait(timeout=20)
        record.update(status="completed", completed_at=now(), runtime_environment_passed=False)
    except Exception as exc:
        record.update(status="failed", error=type(exc).__name__ + ": " + str(exc))
        raise
    finally:
        write_json(report, record)
        client.close()
    print(json.dumps(record, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
