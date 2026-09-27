"""把安装版业务检查分阶段运行于已登记原版 Guest，回收独立证据。

phase 依次为 configure、business、after-reboot；最后一阶段前须真实重启 Guest。
此入口不把局部通过导入完整十三场景门禁，默认策略安装失败也必须另外保留。
"""
from __future__ import annotations

import argparse
import json
import shlex
import stat
import sys
import uuid
from pathlib import Path

import paramiko

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, sha256, write_json
from identity import probe, runtime_binding
from lab import HERE, REPO, fingerprint, inventory, load_configuration
from provenance import bind_package
from providers import QemuLab
from business_reboot_context import validate_reboot_transition


def exercise(target: str, phase: str, *, lab=None, reboot_execution: Path | None = None) -> dict:
    if reboot_execution is not None and phase != "after-reboot":
        raise RuntimeError("BUSINESS_REBOOT_PROOF_ONLY_AFTER_REBOOT")
    if lab is None:
        matrix, media = load_configuration()
        lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    matrix = lab.matrix
    spec = matrix["targets"][target]
    if spec["os"] != "linux":
        raise RuntimeError("LINUX_TARGET_REQUIRED")
    system = probe(lab, target)
    if system.get("installed_package") is not True:
        raise RuntimeError("ACTUAL_PACKAGE_INSTALL_REQUIRED")
    packages, _ = inventory(matrix, REPO / "artifacts")
    package_id = next(key for key, value in matrix["packages"].items() if target in value["required_targets"])
    package = bind_package(lab, packages[package_id], fingerprint())
    context = {"target": target, "package": package, "environment": runtime_binding(lab, target),
               "restore_generation": lab.state(target).get("restore_generation")}
    pointer = lab.root / "state" / ("business-" + target + ".json")
    runtime_phase = phase in {"ocr", "models", "intent"}
    script = HERE / "guest" / ("linux-ocr-models.py" if runtime_phase else "linux-business-lifecycle.py")
    transition = None
    if phase == "configure":
        remote = "/home/partyopsqa/lifecycle-" + uuid.uuid4().hex[:12]
    else:
        previous = json.loads(pointer.read_text(encoding="utf-8"))
        if phase == "after-reboot":
            if reboot_execution is None:
                raise RuntimeError("BUSINESS_REBOOT_PROOF_REQUIRED")
            transition = validate_reboot_transition(lab.root, target, previous, context, system,
                                                    reboot_execution, script)
        elif previous["context"] != context:
            raise RuntimeError("BUSINESS_CONTEXT_CHANGED_RESTART_FROM_CLEAN_BASELINE")
        remote = previous["remote"]
    # after-reboot 的新 firmware vars 绑定只有远端步骤成功后才可推进。
    if phase != "after-reboot":
        write_json(pointer, {"context": context, "remote": remote, "last_phase": phase, "updated_at": now()})
    local = lab.root / "reports" / target / ("business-" + phase + "-" + uuid.uuid4().hex[:12])
    local.mkdir(parents=True)
    write_json(local / "context.json", {**context, "guest_identity": system, "script_sha256": sha256(script),
                                        "remote": remote, "firmware_transition": transition})
    client = paramiko.SSHClient()
    client.load_host_keys(str(lab.root / "keys/known_hosts"))
    client.set_missing_host_key_policy(paramiko.RejectPolicy())
    client.connect("127.0.0.1", port=spec["ssh_port"], username="partyopsqa",
                   key_filename=str(lab.root / "keys/guest_ed25519"), look_for_keys=False, allow_agent=False, timeout=15)
    try:
        with client.open_sftp() as sftp:
            if phase == "configure":
                sftp.mkdir(remote)
            sftp.put(str(HERE / "guest/linux-business-lifecycle.py"), remote + "/lifecycle.py")
            if runtime_phase:
                sftp.put(str(script), remote + "/ocr-models.py")
        command = ["python3", remote + ("/ocr-models.py" if runtime_phase else "/lifecycle.py"), "--uuid", context["environment"]["vm_uuid"],
                   "--package-sha256", package["sha256"], "--work", remote, "--phase", phase]
        _, stdout, stderr = client.exec_command(shlex.join(command), timeout=1800)
        output, errors = stdout.read().decode(errors="replace"), stderr.read().decode(errors="replace")
        code = stdout.channel.recv_exit_status()
        (local / "controller.log").write_text(output + errors, encoding="utf-8")
        with client.open_sftp() as sftp:
            # 只拉取 evidence 普通文件。state.local.json 含凭据，始终留在 Guest 内。
            for item in sftp.listdir_attr(remote + "/evidence"):
                if not stat.S_ISREG(item.st_mode) or Path(item.filename).name != item.filename:
                    raise RuntimeError("UNEXPECTED_GUEST_EVIDENCE_ENTRY")
                sftp.get(remote + "/evidence/" + item.filename, str(local / item.filename))
        result = {"target": target, "phase": phase, "exit_code": code, "generated_at": now(),
                  "report_path": str(local), "runtime_environment_passed": False,
                  "files": {path.name: sha256(path) for path in local.iterdir() if path.is_file()}}
        write_json(local / "controller.json", result)
        if phase == "after-reboot" and code == 0:
            write_json(pointer, {"context": context, "remote": remote, "last_phase": phase,
                                 "firmware_transition": transition, "updated_at": now()})
        return result
    finally:
        client.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("target")
    parser.add_argument("phase", choices=("configure", "business", "after-reboot", "collaboration-business", "ocr", "models", "intent", "uninstall-keep",
                                          "after-reinstall", "uninstall-remove-test-data"))
    parser.add_argument("--reboot-execution", type=Path, help="同一业务链已完成真实重启的 execution.json")
    args = parser.parse_args()
    result = exercise(args.target, args.phase, reboot_execution=args.reboot_execution)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    raise SystemExit(result["exit_code"])
