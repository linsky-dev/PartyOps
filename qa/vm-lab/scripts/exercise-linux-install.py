"""传输最终候选到已绑定 Guest，调用实际安装诊断并回收日志。"""
from __future__ import annotations

import json
import shlex
import stat
import sys
import uuid
from pathlib import Path

import paramiko

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import sha256, write_json
from lab import HERE, REPO, inventory, load_configuration
from providers import QemuLab


def exercise(target: str, *, lab=None, package=None) -> dict:
    if lab is None:
        matrix, media = load_configuration()
        lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    matrix = lab.matrix
    state = lab.state(target)
    if not lab.live(state) or matrix["targets"][target]["os"] != "linux":
        raise RuntimeError("OWNED_LINUX_GUEST_REQUIRED")
    if package is None:
        packages, _ = inventory(matrix, REPO / "artifacts")
        package_id = next(key for key, item in matrix["packages"].items() if target in item["required_targets"])
        package = packages[package_id]
    expected_sha256 = package["sha256"]
    package = Path(package["path"])
    if sha256(package) != expected_sha256:
        raise RuntimeError("PACKAGE_CHANGED_BEFORE_TRANSFER")
    run_id = "install-" + uuid.uuid4().hex[:12]
    local = lab.root / "reports" / target / run_id
    local.mkdir(parents=True)
    remote = "/home/partyopsqa/" + run_id
    client = paramiko.SSHClient()
    client.load_host_keys(str(lab.root / "keys/known_hosts"))
    client.set_missing_host_key_policy(paramiko.RejectPolicy())
    client.connect("127.0.0.1", port=matrix["targets"][target]["ssh_port"], username="partyopsqa",
                   key_filename=str(lab.root / "keys/guest_ed25519"), look_for_keys=False,
                   allow_agent=False, timeout=15)
    try:
        with client.open_sftp() as sftp:
            with sftp.file("/etc/partyops-vm-lab.json") as source:
                marker = json.load(source)
            if marker["uuid"] != state["uuid"]:
                raise RuntimeError("GUEST_UUID_MISMATCH")
            sftp.mkdir(remote)
            sftp.put(str(package), remote + "/" + package.name)
            sftp.put(str(HERE / "guest/linux-installed-probe.py"), remote + "/probe.py")
        command = ["python3", remote + "/probe.py", "--uuid", state["uuid"],
                   "--package", remote + "/" + package.name, "--sha256", sha256(package),
                   "--arch", matrix["targets"][target]["arch"], "--output", remote + "/evidence"]
        _, stdout, stderr = client.exec_command(shlex.join(command), timeout=3600)
        output, errors = stdout.read().decode(errors="replace"), stderr.read().decode(errors="replace")
        code = stdout.channel.recv_exit_status()
        (local / "controller.log").write_text(output + errors, encoding="utf-8")
        with client.open_sftp() as sftp:
            def pull_tree(remote_dir: str, local_dir: Path) -> None:
                """递归拉取 Guest 证据，保留向导自检产生的子目录。"""

                local_dir.mkdir(parents=True, exist_ok=True)
                if hasattr(sftp, "listdir_attr"):
                    entries = [
                        (item.filename, stat.S_ISDIR(item.st_mode))
                        for item in sftp.listdir_attr(remote_dir)
                    ]
                else:
                    # 精简测试传输端只提供 listdir；按普通文件处理以保留旧协议兼容性。
                    entries = [(name, False) for name in sftp.listdir(remote_dir)]
                for name, is_directory in entries:
                    if name in {"", ".", ".."} or Path(name).name != name:
                        raise RuntimeError("INVALID_GUEST_EVIDENCE_PATH")
                    remote_item = remote_dir.rstrip("/") + "/" + name
                    local_item = local_dir / name
                    if is_directory:
                        pull_tree(remote_item, local_item)
                    else:
                        sftp.get(remote_item, str(local_item))

            pull_tree(remote + "/evidence", local)
        result = {"exit_code": code, "target": target, "package_sha256": sha256(package),
                  "report_path": str(local), "scope": "installed-package-diagnostic-only",
                  "runtime_environment_passed": False}
        write_json(local / "controller.json", result)
        return result
    finally:
        client.close()


if __name__ == "__main__":
    print(json.dumps(exercise(sys.argv[1]), ensure_ascii=False, indent=2))
