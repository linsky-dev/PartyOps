"""按阶段采集原版 Loong64 依赖和真实编译证据；不安装或冒充最终 PartyOps 包。"""
from __future__ import annotations

import argparse
import importlib.util
import json
import shlex
import sys
import uuid
from pathlib import Path

import paramiko

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, safe_child, sha256, write_json
from firmware import validate as validate_firmware
from identity import LINUX_PROBE, normalize_linux, runtime_binding, validate_identity
from lab import HERE, fingerprint, load_configuration
from providers import QemuLab

SOURCE = "deepin-deb-loong64"
PHASES = ("system", "repositories", "toolchain", "refresh-index", "install-build-tools", "smoke", "all")


def execute(client, command, *, payload=None, timeout=180):
    stdin, stdout, stderr = client.exec_command(command, timeout=timeout)
    if payload is not None:
        stdin.write(payload)
        stdin.channel.shutdown_write()
    output, errors = stdout.read().decode(errors="replace"), stderr.read().decode(errors="replace")
    code = stdout.channel.recv_exit_status()
    return code, output, errors


def validate_observed(system, spec, state):
    if state.get("purpose") != "disposable-build":
        validate_identity(system, spec, state)
        return
    if state.get("needs_identity_rebind"):
        raise RuntimeError("BUILD_CLONE_IDENTITY_REBIND_REQUIRED")
    # 构建克隆使用独立目的和 UUID；不把原始 marker 伪装成验收 Guest 来通过校验。
    for field in ("os", "arch", "distribution", "distribution_id", "os_release", "os_build", "package_arch"):
        if spec.get(field) and system.get(field) != spec[field]:
            raise RuntimeError("BUILD_CLONE_IDENTITY_MISMATCH:" + field)
    if not system.get("boot_id") or system.get("uid") in {None, 0}:
        raise RuntimeError("BUILD_CLONE_BOOT_OR_USER_IDENTITY_MISSING")
    if (str(system.get("hardware_uuid", "")).lower() != state["uuid"].lower()
            or json.loads(system.get("marker", "{}")) != {"uuid": state["uuid"], "purpose": "disposable-build"}):
        raise RuntimeError("BUILD_CLONE_OWNERSHIP_MISMATCH")


def environment(build_clone):
    if build_clone:
        spec = importlib.util.spec_from_file_location("loong_build_clone", HERE / "scripts/linux-build-guest.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module.environment(SOURCE)
    matrix, media = load_configuration()
    return QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media), SOURCE


def prepare(lab, target, phase, *, resume=None):
    if phase not in PHASES or lab.matrix["targets"][target].get("arch") != "loongarch64":
        raise RuntimeError("LOONG64_BUILD_PHASE_OR_TARGET_REQUIRED")
    state, spec = lab.state(target), lab.matrix["targets"][target]
    if not lab.live(state):
        raise RuntimeError("OWNED_LOONG64_GUEST_NOT_RUNNING")
    purpose = state.get("purpose", "disposable-qa")
    if purpose not in {"disposable-build", "disposable-qa"}:
        raise RuntimeError("LOONG64_BUILD_GUEST_PURPOSE_MISMATCH")
    if phase in {"refresh-index", "install-build-tools", "smoke"} and purpose != "disposable-build":
        raise RuntimeError("MUTATING_PREPARATION_REQUIRES_BUILD_CLONE")
    source_binding = runtime_binding(lab, SOURCE)
    if purpose == "disposable-build" and state.get("clone_source_binding") != source_binding:
        raise RuntimeError("BUILD_CLONE_SOURCE_BINDING_CHANGED")
    code = HERE / "guest/loong64-build-probe.py"
    firmware = validate_firmware(lab, target, state)
    source_fingerprint = fingerprint()
    binding = {"target": target, "vm_uuid": state["uuid"], "source": source_binding,
               "firmware_code_sha256": firmware["code_sha256"], "firmware_vars_sha256": firmware["vars_sha256"],
               "source_fingerprint": source_fingerprint, "probe_sha256": sha256(code)}
    report_root = lab.root / "reports" / target
    if resume:
        previous = json.loads(safe_child(report_root, resume).read_text(encoding="utf-8"))
        if previous.get("target") != target or previous.get("binding", {}).get("vm_uuid") != state["uuid"]:
            raise RuntimeError("LOONG_PREPARATION_RESUME_TARGET_MISMATCH")
    else:
        previous = None
    run_id = "loong-prep-" + uuid.uuid4().hex[:12]
    directory = safe_child(report_root, report_root / run_id)
    directory.mkdir(parents=True, exist_ok=False)
    record = {"schema_version": 1, "at": now(), "target": target, "phase": phase, "binding": binding,
              "run_id": run_id, "report_path": str(directory), "status": "running", "steps": [],
              "scope": "build-preparation-only", "runtime_environment_passed": False,
              "resume_of": str(resume) if resume else None,
              "previous_binding_changed": bool(previous and previous.get("binding") != binding)}
    write_json(directory / "execution.json", record)
    client = paramiko.SSHClient()
    client.load_host_keys(str(lab.root / "keys/known_hosts"))
    client.set_missing_host_key_policy(paramiko.RejectPolicy())
    try:
        client.connect("127.0.0.1", port=spec["ssh_port"], username="partyopsqa",
                       key_filename=str(lab.root / "keys/guest_ed25519"), look_for_keys=False, allow_agent=False, timeout=15)
        code_exit, output, errors = execute(client, "python3 -c " + shlex.quote(LINUX_PROBE))
        (directory / "identity.raw.log").write_text(output + errors, encoding="utf-8")
        if code_exit:
            raise RuntimeError("LOONG_BUILD_IDENTITY_PROBE_FAILED")
        observed = normalize_linux(json.loads(output))
        validate_observed(observed, spec, state)
        write_json(directory / "identity.json", observed)
        record["observed_boot_id"] = observed["boot_id"]
        # 续跑也重新采集真实系统；历史记录只供定位，不缓存成功结果。
        phases = ["system", "repositories", "toolchain"] if phase == "all" else ["system", phase]
        for current in dict.fromkeys(phases):
            args = ["python3", "-", "--uuid", state["uuid"], "--purpose", purpose,
                    "--phase", current, "--run-id", run_id]
            code_exit, output, errors = execute(client, shlex.join(args), payload=code.read_text(encoding="utf-8"),
                                                timeout=2100 if current == "install-build-tools" else 900)
            raw = directory / (current + ".raw.log")
            raw.write_text(output + errors, encoding="utf-8")
            step = {"phase": current, "exit_code": code_exit, "raw": raw.name, "raw_sha256": sha256(raw)}
            record["steps"].append(step)
            if code_exit:
                raise RuntimeError("LOONG_BUILD_PHASE_FAILED:" + current)
            result = json.loads(output)
            if result.get("guest_uuid") != state["uuid"] or result.get("phase") != current or result.get("runtime_environment_passed") is not False:
                raise RuntimeError("LOONG_BUILD_PHASE_IDENTITY_MISMATCH")
            write_json(directory / (current + ".json"), result)
            step["result"] = current + ".json"
            step["result_sha256"] = sha256(directory / step["result"])
            if current == "system" and result["result"].get("boot_id") != observed["boot_id"]:
                raise RuntimeError("LOONG_BUILD_GUEST_REBOOTED_DURING_PROBE")
            if current == "system" and phase == "smoke" and result["result"].get("minimum_glibc_met") is not True:
                raise RuntimeError("LOONG_BUILD_GLIBC_BELOW_ARCHITECTURE_BASELINE")
            if current == "smoke" and result["result"].get("status") != "passed":
                raise RuntimeError("LOONG_TOOLCHAIN_SMOKE_FAILED")
            if current == "refresh-index" and result["result"].get("exit_code") != 0:
                raise RuntimeError("LOONG_APT_INDEX_REFRESH_FAILED")
            if current == "install-build-tools" and result["result"].get("status") != "installed":
                raise RuntimeError("LOONG_BUILD_TOOLS_INSTALL_INCOMPLETE")
            write_json(directory / "execution.json", record)
        if fingerprint() != source_fingerprint or sha256(code) != binding["probe_sha256"]:
            raise RuntimeError("LOONG_BUILD_PREPARATION_SOURCE_CHANGED")
        record["status"] = "collected"
    except (RuntimeError, OSError, ValueError, paramiko.SSHException) as exc:
        record.update(status="failed", error=str(exc))
    finally:
        client.close()
        record["completed_at"] = now()
        write_json(directory / "execution.json", record)
    return record


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("phase", choices=PHASES, default="all", nargs="?")
    parser.add_argument("--build-clone", action="store_true")
    parser.add_argument("--resume", type=Path)
    args = parser.parse_args()
    lab, target = environment(args.build_clone)
    result = prepare(lab, target, args.phase, resume=args.resume)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    raise SystemExit(0 if result["status"] == "collected" else 2)


if __name__ == "__main__":
    main()
