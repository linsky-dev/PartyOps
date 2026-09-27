"""PartyOps 多平台实验室主控；只输出实际证据，不推送或发布制品。"""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
import platform
import shutil
import sys
import uuid
from pathlib import Path

import jsonschema
import yaml
from evidence import aggregate, checked_id, evaluate, now, sha256, write_json
from macos import VMwareMacLab
from providers import QemuLab, qmp, run

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent


def load_configuration() -> tuple[dict, dict]:
    matrix = yaml.safe_load((HERE / "config/test-matrix.yaml").read_text(encoding="utf-8"))
    media = yaml.safe_load((HERE / "config/media.yaml").read_text(encoding="utf-8"))["media"]
    local = HERE / ".local/media.yaml"
    if local.exists():
        media.update(yaml.safe_load(local.read_text(encoding="utf-8"))["media"])
    for target in matrix["targets"].values():
        target["local_only"] = matrix.get("local_only", True)
    return matrix, media


def fingerprint() -> str:
    spec = importlib.util.spec_from_file_location("partyops_full_gate", REPO / "scripts/verify-full-function-gate.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.source_fingerprint(REPO, "package")[0]


def host_tool(lab: QemuLab, name: str) -> str | None:
    """定位宿主工具；实验室固定在 D 盘的 VMware 优先于系统默认目录。"""
    discovered = shutil.which(name)
    if discovered:
        return discovered
    executable = name + (".exe" if os.name == "nt" else "")
    candidates = [lab.root / "tools/VMware" / executable]
    if os.name == "nt" and name in {"vmrun", "vmware"}:
        for variable in ("ProgramFiles(x86)", "ProgramFiles"):
            base = os.environ.get(variable)
            if base:
                candidates.append(Path(base) / "VMware/VMware Workstation" / executable)
    for candidate in candidates:
        if candidate.is_file():
            return str(candidate.resolve())
    return None


def inventory(matrix: dict, artifact_root: Path) -> tuple[dict, dict]:
    packages, errors = {}, {}
    for package_id, spec in matrix["packages"].items():
        candidates = list(artifact_root.glob(spec["pattern"].format(version=matrix["version"])))
        if len(candidates) != 1:
            errors[package_id] = "MISSING_PACKAGE" if not candidates else "AMBIGUOUS_PACKAGE"
            continue
        path = candidates[0]
        try:
            before = path.stat()
            checksum = sha256(path)
            after = path.stat()
            if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
                errors[package_id] = "PACKAGE_CHANGED_DURING_INVENTORY"
                continue
            packages[package_id] = {"id": package_id, "version": matrix["version"],
                                    "path": str(path.resolve()), "sha256": checksum,
                                    "bytes": after.st_size,
                                    "provenance_status": "requires_installed_manifest_verification"}
        except OSError:
            # 正在生成的安装器可能独占文件；只阻断该包，继续其他平台。
            errors[package_id] = "PACKAGE_UNREADABLE_OR_BUILD_IN_PROGRESS"
    return packages, errors


def doctor(lab: QemuLab) -> dict:
    report = {"generated_at": now(), "host_os": platform.platform(), "host_arch": platform.machine(),
              "root": str(lab.root), "tools": {}, "disks": {}, "security_changes": []}
    for name in ("qemu-system-x86_64", "qemu-system-i386", "qemu-system-aarch64", "qemu-system-loongarch64", "qemu-img"):
        try:
            report["tools"][name] = run([lab.binary(name), "--version"]).splitlines()[0]
        except RuntimeError as exc:
            report["tools"][name] = str(exc)
    for name in ("vmrun", "vmware", "gh", "ssh"):
        report["tools"][name] = host_tool(lab, name)
    for disk in ("C:/", "D:/", "E:/"):
        if Path(disk).exists():
            usage = shutil.disk_usage(disk)
            report["disks"][disk] = {"total_gib": round(usage.total / 1024**3, 2), "free_gib": round(usage.free / 1024**3, 2)}
    if os.name == "nt":
        script = """
$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$dg = Get-CimInstance -Namespace root/Microsoft/Windows/DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction SilentlyContinue
$features = @(Get-WindowsOptionalFeature -Online | Where-Object { $_.FeatureName -in @('HypervisorPlatform','VirtualMachinePlatform','Microsoft-Hyper-V-All','Microsoft-Windows-Subsystem-Linux') } | Select-Object FeatureName,@{n='State';e={$_.State.ToString()}})
@{cpu=$cpu.Name; cores=$cpu.NumberOfCores; logical_processors=$cpu.NumberOfLogicalProcessors; total_memory_gib=[math]::Round($os.TotalVisibleMemorySize/1MB,2); free_memory_gib=[math]::Round($os.FreePhysicalMemory/1MB,2); build=$os.BuildNumber; features=$features; vbs_status=$dg.VirtualizationBasedSecurityStatus; hypervisor_present=(Get-CimInstance Win32_ComputerSystem).HypervisorPresent} | ConvertTo-Json -Depth 8 -Compress
"""
        report["windows"] = json.loads(run(["powershell.exe", "-NoProfile", "-Command", script], timeout=60))
    report["firmware"] = [str(path.relative_to(lab.qemu_home)) for path in lab.qemu_home.rglob("edk2-*-code.fd")]
    lab.initialize()
    write_json(lab.root / "state/doctor.json", report)
    return report


def describe_unverified_environment(lab: QemuLab, target: str, report: dict) -> None:
    """按实际后端展示库存；已有机器或实验记录均不替代 Guest 身份与生命周期证据。"""
    spec = lab.matrix["targets"][target]
    if spec["backend"] == "native-host":
        from native_windows import probe as native_probe
        report.update(environment_type="native-host", host_identity_status="unverified",
                      media_status="not_applicable_native_host", cold_boot_baseline_status="not_applicable_native_host")
        try:
            report["system"] = native_probe(lab, target)
            report["host_identity_status"] = "verified"
        except (RuntimeError, OSError, ValueError, KeyError) as exc:
            report["errors"].append(str(exc))
        report["errors"].append("NATIVE_WINDOWS_LIFECYCLE_EXECUTION_NOT_IMPLEMENTED")
        return
    if spec["backend"] == "vmware":
        mac = VMwareMacLab(lab)
        try:
            state = mac.state()
            if not isinstance(state, dict):
                raise TypeError("MACOS_VM_STATE_INVALID")
        except (RuntimeError, OSError, ValueError, TypeError) as exc:
            report.update(vm_status="not_created", guest_identity_status="unverified",
                          cold_boot_baseline_status="unverified")
            report["errors"].append(str(exc))
            return
        report.update(vm_status=state.get("status", "registered"),
                      environment_type=state.get("environment_type", "hardware-virtualized"),
                      vm_state_path=str(mac.state_path), guest_identity_status="unverified",
                      cold_boot_baseline_status="unverified")
        vmx = Path(state.get("vmx", ""))
        if not vmx.is_file():
            report["vm_status"] = "registered_files_missing"
            report["errors"].append("MACOS_REGISTERED_VMX_MISSING")
        else:
            try:
                report["vm_status"] = "running" if mac.running(state) else "stopped"
            except (RuntimeError, OSError, ValueError, KeyError) as exc:
                report["vm_status"] = "registered_runtime_unknown"
                report["errors"].append("MACOS_VM_STATUS_UNAVAILABLE:" + str(exc))
        # 恢复镜像的来源校验记录不等于实际已安装的 macOS 版本；不能读取 QEMU ISO 清单替代。
        recovery = state.get("recovery", {})
        if not isinstance(recovery, dict):
            recovery = {}
        media_path = Path(recovery.get("vmdk", ""))
        if media_path.is_file() and recovery.get("vmdk_sha256") and recovery.get("verified_at"):
            report["media_status"] = "recovery_verification_recorded"
        else:
            report["media_status"] = "recovery_evidence_missing"
            report["errors"].append("MACOS_RECOVERY_EVIDENCE_MISSING")
        # 本分支仅在完整证据缺失时执行；普通快照名称和 requested_version 不构成冷启动/系统身份。
        report["errors"].extend(["MACOS_GUEST_IDENTITY_UNVERIFIED", "MACOS_COLD_BOOT_BASELINE_UNVERIFIED"])
        return
    if spec["backend"] == "macos-arm64-experimental":
        report.update(vm_status="experiment_not_recorded", environment_type="experimental-runtime")
        path = lab.root / "reports/macos-arm64/local-experiment.json"
        if path.is_file():
            try:
                experiment = json.loads(path.read_text(encoding="utf-8"))
                if (not isinstance(experiment, dict) or not isinstance(experiment.get("stages"), dict)
                        or not isinstance(experiment.get("errors"), list)
                        or not all(isinstance(error, str) for error in experiment["errors"])):
                    raise ValueError("MACOS_EXPERIMENT_RECORD_MALFORMED")
                config = json.loads((HERE / "config/macos-arm64.json").read_text(encoding="utf-8"))
                if experiment.get("config") != config:
                    raise ValueError("MACOS_EXPERIMENT_RECORD_STALE")
                report.update(vm_status="experiment_recorded", experiment_report=str(path),
                              experiment_recorded_at=experiment.get("generated_at"),
                              recorded_experiment_stages=experiment.get("stages", {}))
                report["errors"].extend(experiment.get("errors", []))
            except (OSError, ValueError, TypeError) as exc:
                report["errors"].append("MACOS_EXPERIMENT_RECORD_INVALID:" + str(exc))
        else:
            report["errors"].append("MACOS_EXPERIMENT_EVIDENCE_MISSING")
        report["errors"].append("MACOS_ARM64_DESKTOP_AND_PKG_LIFECYCLE_UNAVAILABLE")
        return
    if spec["media"] not in lab.media:
        report["errors"].append("MISSING_MEDIA")
    elif lab.media[spec["media"]].get("blocked_reason"):
        report["errors"].append(lab.media[spec["media"]]["blocked_reason"])
    if spec.get("requires_uefi_secureboot_tpm2"):
        report["errors"].append("WINDOWS_ARM64_SECURE_BOOT_TPM2_PROVIDER_REQUIRED")
    try:
        state = lab.state(target)
        report["vm_status"] = "running" if lab.live(state) else state["status"]
        report["environment_type"] = state.get("environment_type")
    except RuntimeError:
        report["vm_status"] = "not_created"


def check_target(lab: QemuLab, target: str, packages: dict, package_errors: dict,
                 evidence_path: Path | None, source_fingerprint: str) -> dict:
    spec = lab.matrix["targets"][target]
    package_id = next(key for key, item in lab.matrix["packages"].items() if target in item["required_targets"])
    report = {"target": target, "generated_at": now(), "status": "blocked", "runtime_environment_passed": False,
              "real_environment_passed": False, "package_sha256": packages.get(package_id, {}).get("sha256"), "errors": []}
    package_bound = False
    if spec["os"] == "windows":
        from windows_execution import PROFILE_BLOCKERS
        if package_id in PROFILE_BLOCKERS:
            report["errors"].append(PROFILE_BLOCKERS[package_id])
    if package_id in package_errors:
        report["errors"].append(package_errors[package_id])
    elif package_id in packages:
        from provenance import bind_package
        try:
            packages[package_id] = bind_package(lab, packages[package_id], source_fingerprint)
            package_bound = True
        except (RuntimeError, OSError, ValueError, KeyError) as exc:
            report["errors"].append(str(exc))
    if evidence_path:
        if not evidence_path.is_file():
            report["errors"].append("MISSING_RESULT_FILE")
        elif package_id in packages:
            result = json.loads(evidence_path.read_text(encoding="utf-8"))
            if result.get("target") != target:
                report["errors"].append("RESULT_TARGET_MISMATCH")
            schema = json.loads((HERE / "schemas/result.schema.json").read_text())
            from identity import runtime_binding

            bound_spec = dict(spec)
            try:
                bound_spec["runtime_binding"] = runtime_binding(lab, target)
            except (RuntimeError, OSError, ValueError, KeyError) as exc:
                report["errors"].append(str(exc))
            report["errors"] += evaluate(result, bound_spec, packages[package_id], lab.matrix["required_cases"],
                                          evidence_path.parent, schema, source_fingerprint)
            report["evidence_path"] = str(evidence_path.resolve())
            report["reboot_actual"] = result.get("restart", {}).get("actual", False)
            report["environment_type"] = result.get("system", {}).get("environment_type")
            report["distribution_match"] = result.get("distribution_match")
            if not report["errors"]:
                report.update(status="passed", runtime_environment_passed=True, real_environment_passed=True)
    else:
        report["errors"].append("FULL_LIFECYCLE_EVIDENCE_MISSING")
        pointer = lab.root / "state" / f"run-{target}.json"
        if pointer.is_file():
            execution_state = json.loads(pointer.read_text(encoding="utf-8"))
            from evidence import safe_child
            directory = safe_child(lab.root / "reports" / target, Path(execution_state["directory"]))
            execution_file = directory / "execution.json"
            if execution_file.is_file():
                execution_result = json.loads(execution_file.read_text(encoding="utf-8"))
                report["execution_report"] = str(execution_file)
                report["execution_status"] = execution_result["status"]
                report["errors"].extend(execution_result.get("errors", []))
        describe_unverified_environment(lab, target, report)
    if spec["backend"] == "native-host":
        from native_diagnostics import summary as native_diagnostics_summary
        report["diagnostics_summary"] = native_diagnostics_summary(
            lab, target, packages.get(package_id, {}) if package_bound else {}, source_fingerprint, report.get("system"))
        if not report["runtime_environment_passed"]:
            report["errors"].extend(report["diagnostics_summary"]["binding_errors"])
            for stage in report["diagnostics_summary"]["stages"].values():
                if stage.get("status") in {"failed", "rejected"} and stage.get("error"):
                    report["errors"].append(stage["error"])
    return report


def reports(lab: QemuLab, targets: list[str], evidence_path: Path | None, artifact_root: Path) -> dict:
    lab.initialize()
    packages, errors = inventory(lab.matrix, artifact_root)
    source = fingerprint()
    results = {}
    for target in lab.matrix["targets"]:
        existing = lab.root / "state" / f"accepted-{target}.json"
        path = evidence_path if target in targets and evidence_path else None
        if path is None and existing.exists():
            path = Path(json.loads(existing.read_text(encoding="utf-8"))["evidence_path"])
        result = check_target(lab, target, packages, errors, path, source)
        if path and result["runtime_environment_passed"]:
            write_json(existing, {"evidence_path": str(path.resolve())})
        results[target] = result
    report = aggregate(lab.matrix, packages, results)
    report["source_fingerprint"] = source
    report["package_inventory"] = packages
    report["package_errors"] = errors
    report["signing_and_distribution_gate"] = "not_run"
    destination = lab.root / "reports" / ("matrix-" + uuid.uuid4().hex[:12])
    report["report_path"] = str(destination)
    aggregate_schema = json.loads(
        (HERE / "schemas/aggregate.schema.json").read_text(encoding="utf-8")
    )
    jsonschema.validate(report, aggregate_schema)
    write_json(destination / "qa-report.json", report)
    lines = ["# PartyOps 多平台运行验收", "", f"版本：{report['version']}；北京时间：{report['generated_at']}", "",
             f"通过：{report['passed_packages']}/{report['required_packages']}；门禁：{report['all_packages_runtime_gate']}", "",
             "| 目标环境 | 状态 | 原因 |", "|---|---|---|"]
    for target, result in results.items():
        lines.append(f"| {target} | {result['status']} | {'; '.join(result['errors'])} |")
    for target, result in results.items():
        diagnostic = result.get("diagnostics_summary")
        if diagnostic:
            lines += ["", f"## {target} 本机部分实测", "", "以下仅为绑定当前制品的诊断进度，不增加完整场景或制品通过数。", "",
                      "| 诊断阶段 | 状态 | 证据 |", "|---|---|---|"]
            for stage in diagnostic["stages"].values():
                lines.append(f"| {stage['label']} | {stage['status']} | {stage['evidence_path']} |")
            lines += ["", "剩余阻断：" + "; ".join(diagnostic["remaining_blockers"]), ""]
    lines += ["", "运行证据不等于签名、公证或生产发布许可；未执行项不算通过。", ""]
    (destination / "qa-report.md").write_text("\n".join(lines), encoding="utf-8")
    write_json(lab.root / "state/latest-report.json", report)
    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["doctor", "media", "create", "start", "stop", "snapshot", "restore", "baseline", "run", "run-all", "test", "test-all", "cleanup", "probe", "screenshot", "macos-doctor", "macos-arm64-inspect", "macos-x64-media", "macos-x64-bootstrap"])
    parser.add_argument("target", nargs="?")
    parser.add_argument("--root", type=Path)
    parser.add_argument("--name", default="clean-os")
    parser.add_argument("--accel", choices=["tcg", "whpx"], default="tcg")
    parser.add_argument("--provision-network", action="store_true")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--resume", action="store_true")
    parser.add_argument("--guest-timeout", type=int, default=45)
    parser.add_argument("--evidence", type=Path)
    parser.add_argument("--artifacts", type=Path, default=REPO / "artifacts")
    args = parser.parse_args(argv)
    matrix, media = load_configuration()
    lab = QemuLab(matrix, args.root or Path(matrix["defaults"]["primary_root"]), media)
    try:
        if args.command == "doctor":
            result = doctor(lab)
        elif args.command == "macos-doctor":
            result = VMwareMacLab(lab).doctor()
            result["arm64_local"] = "requires_experimental_qemu_and_apple_media"
            result["hosted_fallback"] = "disabled_by_user_local_only_policy"
        elif args.command == "macos-x64-media":
            result = VMwareMacLab(lab).prepare_recovery()
        elif args.command == "macos-x64-bootstrap":
            result = VMwareMacLab(lab).bootstrap()
        elif args.command == "macos-arm64-inspect":
            from macos_arm64 import inspect_experiment
            result = inspect_experiment(lab)
        elif args.command == "run-all":
            from execution import run_all
            result = run_all(lab, args.artifacts, resume=args.resume, acceleration=args.accel,
                             provision_network=args.provision_network, timeout=args.guest_timeout)
        elif args.command == "test-all":
            result = reports(lab, list(matrix["targets"]), None, args.artifacts)
        else:
            if not args.target:
                raise ValueError("TARGET_REQUIRED")
            checked_id(args.target)
            if args.command == "media":
                if matrix["targets"].get(args.target, {}).get("backend") == "native-host":
                    raise RuntimeError("NATIVE_HOST_COMMAND_NOT_ALLOWED:media")
                result = {"media": args.target, "verified_path": str(lab.fetch(args.target))}
            else:
                if args.target not in matrix["targets"]:
                    if args.target in matrix.get("historical_targets", {}) and args.command in ("stop", "screenshot", "cleanup"):
                        matrix["targets"][args.target] = matrix["historical_targets"][args.target]
                    else:
                        raise ValueError("UNKNOWN_OR_HISTORICAL_TARGET")
                backend = matrix["targets"][args.target]["backend"]
                if backend == "native-host" and args.command not in ("run", "test", "probe"):
                    raise RuntimeError("NATIVE_HOST_COMMAND_NOT_ALLOWED:" + args.command)
                mac = VMwareMacLab(lab) if backend == "vmware" else None
                if args.command == "run":
                    from execution import run_target
                    result = run_target(lab, args.target, args.artifacts, args.resume, args.accel,
                                        args.provision_network, args.guest_timeout)
                elif args.command == "baseline":
                    from identity import seal_baseline
                    result = seal_baseline(lab, args.target, args.accel, max(args.guest_timeout, 180))
                elif args.command == "create" and mac:
                    result = mac.bootstrap()
                elif args.command == "start" and mac:
                    result = mac.start(gui=False)
                elif args.command == "stop" and mac:
                    result = mac.stop(args.force)
                elif args.command in ("snapshot", "restore") and mac:
                    result = mac.snapshot(args.name, args.command == "restore")
                elif args.command in ("cleanup", "probe", "screenshot") and mac:
                    raise RuntimeError(f"MACOS_COMMAND_NOT_IMPLEMENTED:{args.command}")
                elif args.command == "create":
                    result = lab.create(args.target)
                elif args.command == "start":
                    result = lab.start(args.target, args.accel, args.provision_network)
                elif args.command == "stop":
                    result = lab.stop(args.target, args.force)
                elif args.command in ("snapshot", "restore"):
                    result = lab.snapshot(args.target, args.name, args.command == "restore")
                elif args.command == "cleanup":
                    result = lab.cleanup(args.target, args.force)
                elif args.command == "test":
                    result = reports(lab, [args.target], args.evidence, args.artifacts)
                elif args.command == "probe":
                    from identity import probe
                    result = probe(lab, args.target)
                elif args.command == "screenshot":
                    state = lab.state(args.target)
                    if not lab.live(state):
                        raise RuntimeError("VM_NOT_RUNNING")
                    path = lab.vm_dir(args.target) / f"screen-{uuid.uuid4().hex[:8]}.png"
                    qmp(state["qmp_port"], "screendump", {"filename": str(path), "format": "png"})
                    result = {"screenshot": str(path), "sha256": sha256(path)}
        print(json.dumps(result, ensure_ascii=False, indent=2))
        if args.command in ("test", "test-all"):
            return 0 if result["all_packages_runtime_gate"] == "passed" else 2
        if args.command in ("run", "run-all", "macos-arm64-inspect"):
            return 0 if result.get("runtime_environment_passed") else 2
        return 0
    except (OSError, ValueError, RuntimeError, KeyError) as exc:
        error = {"generated_at": now(), "command": args.command, "target": args.target,
                 "status": "blocked", "error": str(exc), "runtime_environment_passed": False}
        lab.initialize()
        write_json(lab.root / "state" / ("error-" + uuid.uuid4().hex + ".json"), error)
        print(json.dumps(error, ensure_ascii=False, indent=2))
        return 2


if __name__ == "__main__":
    sys.exit(main())
