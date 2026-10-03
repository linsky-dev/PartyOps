"""本地串行执行与可审计续跑；诊断、阻断与完整通过分别保存。"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import re
import uuid
from pathlib import Path

from evidence import now, safe_child, sha256, write_json
from identity import runtime_binding, seal_baseline, wait_probe, wait_stopped
from packaging.version import InvalidVersion, Version
from providers import qmp

HERE = Path(__file__).resolve().parent


def canonical_hash(value: dict) -> str:
    return hashlib.sha256(json.dumps(value, sort_keys=True, ensure_ascii=False).encode()).hexdigest()


def controller_fingerprint() -> str:
    """执行器变更也使续跑失效，避免旧操作结果被新逻辑追认。"""
    files = sorted([*HERE.glob("*.py"), *[path for folder in ("guest", "scripts", "config", "schemas")
                                        for path in (HERE / folder).iterdir() if path.is_file()]])
    return canonical_hash({str(path.relative_to(HERE)): sha256(path) for path in files})


def select_upgrade_baseline(matrix: dict, package_id: str, roots: list[Path]) -> dict:
    """只使用更低版本、同包类型/架构且旁路哈希可复核的实际发行包。"""
    if matrix.get("retain_obsolete_packages") is False:
        raise RuntimeError("OLDER_PACKAGE_REMOVED_BY_RETENTION_POLICY")
    current = Version(matrix["version"])
    spec = matrix["packages"][package_id]
    roots = [*roots, *[Path(value) for value in spec.get("upgrade_roots", [])]]
    suffix = spec["pattern"].split("{version}")[-1] if "{version}" in spec["pattern"] else None
    candidates = []
    for root in roots:
        for path in root.glob("PartyOps*"):
            if not path.is_file() or path.suffix not in (".exe", ".deb", ".rpm", ".pkg"):
                continue
            if suffix:
                match = re.fullmatch(r"PartyOps_(.+)" + re.escape(suffix), path.name)
                version = match[1] if match else ""
            else:
                arch = "aarch64" if package_id == "rpm_aarch64" else "x86_64"
                match = re.fullmatch(r"PartyOps-(\d+\.\d+\.\d+)-(.*)\." + arch + r"\.rpm", path.name)
                version = match[1] if match else ""
                if match and match[2].startswith("0.rc."):
                    version += "rc" + match[2].split(".")[2]
            try:
                parsed = Version(version)
            except InvalidVersion:
                continue
            if parsed >= current:
                continue
            checksum = path.with_suffix(path.suffix + ".sha256")
            if not checksum.is_file():
                continue
            tokens = checksum.read_text(encoding="utf-8-sig").strip().split()
            expected = tokens[0].lower() if tokens else ""
            if not re.fullmatch(r"[a-f0-9]{64}", expected) or sha256(path) != expected:
                continue
            candidates.append((parsed, {"id": package_id, "version": version, "path": str(path.resolve()),
                                         "sha256": expected, "checksum_file": str(checksum.resolve())}))
    if not candidates:
        raise RuntimeError("VERIFIABLE_OLDER_PACKAGE_MISSING")
    return max(candidates, key=lambda pair: (pair[0], pair[1]["path"]))[1]


class Journal:
    """步骤完成后立即落盘；续跑重新核对每一份产物，失效记录原样归档。"""

    def __init__(self, lab, target: str, context: dict, resume: bool):
        self.reports_root = lab.root / "reports"
        self.pointer = lab.root / "state" / f"run-{target}.json"
        self.context = context
        self.reused = False
        previous = json.loads(self.pointer.read_text(encoding="utf-8")) if self.pointer.exists() else None
        if resume and previous and previous.get("context") == context:
            self.directory = safe_child(lab.root / "reports" / target, Path(previous["directory"]))
            self.data = json.loads((self.directory / "execution.json").read_text(encoding="utf-8"))
            self.reused = True
        else:
            self.directory = lab.root / "reports" / target / ("run-" + uuid.uuid4().hex[:12])
            self.data = {"schema_version": 1, "target": target, "run_id": self.directory.name,
                         "started_at": now(), "context": context, "steps": {}, "status": "running",
                         "runtime_environment_passed": False,
                         "previous_run": previous.get("directory") if previous else None,
                         "resume_invalidated": bool(resume and previous)}
        self.save()

    def save(self):
        self.data["updated_at"] = now()
        write_json(self.directory / "execution.json", self.data)
        write_json(self.pointer, {"directory": str(self.directory), "context": self.context,
                                  "status": self.data["status"], "updated_at": self.data["updated_at"]})

    def step(self, name: str, operation):
        old = self.data["steps"].get(name)
        if self.reused and old and old["status"] == "completed":
            evidence = self.directory / old["path"]
            if evidence.is_file() and sha256(evidence) == old["sha256"]:
                result = json.loads(evidence.read_text(encoding="utf-8"))
                if self.dependencies(result) != old.get("dependencies", {}):
                    raise RuntimeError("RESUME_DEPENDENT_EVIDENCE_CHANGED:" + name)
                return result
            # 产物损坏后继续复用后继步骤是不安全的；强制从干净环境重跑。
            raise RuntimeError("RESUME_STEP_EVIDENCE_CHANGED:" + name)
        self.data["steps"][name] = {"status": "running", "started_at": now()}
        self.save()
        try:
            result = operation()
            path = self.directory / (name + ".json")
            write_json(path, result)
            if result.get("exit_code", 0) != 0:
                # 失败的真实执行保留独立回执，续跑时重试，不能缓存为已完成。
                self.data["steps"][name] = {"status": "blocked", "path": path.name,
                                             "sha256": sha256(path), "at": now(),
                                             "dependencies": self.dependencies(result),
                                             "reason": "EXECUTION_EXIT_NONZERO"}
                self.save()
                raise RuntimeError("EXECUTION_EXIT_NONZERO:" + name + ":" + str(result.get("report_path", path)))
            self.data["steps"][name] = {"status": "completed", "path": path.name,
                                         "sha256": sha256(path), "completed_at": now(),
                                         "dependencies": self.dependencies(result)}
            self.save()
            return result
        except (RuntimeError, OSError, ValueError, KeyError) as exc:
            self.data["steps"][name].update(status="blocked", reason=str(exc), at=now())
            self.save()
            raise

    def dependencies(self, result: dict) -> dict:
        """诊断 JSON 指向的实际日志也必须复核，不能只校验最外层指针文件。"""
        if not result.get("report_path"):
            return {}
        directory = safe_child(self.reports_root, Path(result["report_path"]))
        if not directory.is_dir():
            raise RuntimeError("STEP_REPORT_DIRECTORY_MISSING")
        files = {}
        for path in directory.rglob("*"):
            if path.is_file():
                safe_child(directory, path)
                files[str(path.relative_to(directory))] = sha256(path)
        if not files:
            raise RuntimeError("STEP_REPORT_DIRECTORY_EMPTY")
        return files


def snapshot_screen(lab, target: str, directory: Path) -> dict:
    state = lab.state(target)
    if not lab.live(state):
        return {"status": "stopped"}
    path = directory / ("guest-screen-" + uuid.uuid4().hex[:8] + ".png")
    qmp(state["qmp_port"], "screendump", {"filename": str(path), "format": "png"})
    return {"path": str(path), "sha256": sha256(path)}


def clean_start_binding(state: dict) -> dict:
    """记录本次执行实际准备的基线，不能只凭存在旧 journal 跳过恢复。"""
    return {"baseline_id": state["clean_baseline"]["id"],
            "restore_generation": state.get("restore_generation")}


def can_resume_clean_start(journal, state: dict) -> bool:
    if not journal.reused:
        return False
    prepared = journal.data.get("clean_start")
    if prepared == clean_start_binding(state):
        return True
    if journal.data.get("steps"):
        raise RuntimeError("RESUME_CLEAN_BASELINE_NOT_PROVEN")
    return False


def windows_lifecycle(lab, target, artifact_root, journal):
    """统一入口执行已有 Windows 安装、业务与重启驱动，局部失败不得追认通过。"""
    if lab.matrix["targets"][target].get("winrm_port"):
        from winrm_memory import prepare_win7_transport
        # 不缓存此检查；--resume 或恢复基础快照后也必须重新读取实际配额与身份。
        preparation = prepare_win7_transport(lab, target, journal.directory)
        journal.data.setdefault("transport_preparations", []).append(preparation)
        journal.save()
    modules = {}
    for name, filename in (("install", "exercise-windows-install.py"),
                           ("business", "exercise-windows-business.py"), ("reboot", "reboot-windows.py")):
        spec = importlib.util.spec_from_file_location("windows_lifecycle_" + name, HERE / "scripts" / filename)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        modules[name] = module
    journal.step("installed-package-diagnostic", lambda: modules["install"].exercise(target, artifact_root, lab=lab))
    if lab.matrix["targets"][target].get("winrm_port"):
        # 独立 prepare/probe 入口已支持 PS2 真实普通桌面；首次配置及业务仍不能落到 SSH 驱动。
        raise RuntimeError("WIN7_STANDARD_USER_PREPARE_AND_INTERACTIVE_PROBE_REQUIRED:exercise-win7-standard-user.py;FULL_BUSINESS_DRIVER_NOT_PORTED_TO_WINRM")
    previous = journal.data["steps"].get("business-configure", {})
    configure = "resume-configure" if journal.reused and previous.get("status") in {"running", "blocked"} else "configure"
    journal.step("business-configure", lambda: modules["business"].exercise(target, configure, lab=lab))
    for phase in ("business", "collaboration-business"):
        journal.step("business-" + phase, lambda phase=phase: modules["business"].exercise(target, phase, lab=lab))
    journal.step("actual-guest-reboot", lambda: modules["reboot"].reboot(target, lab=lab))
    journal.step("business-after-reboot", lambda: modules["business"].exercise(target, "after-reboot", lab=lab))
    failures = []
    # OCR 和模型彼此独立；一项故障保留后继续另一项，仍不生成完整生命周期通过。
    for phase in ("ocr", "models"):
        try:
            journal.step("business-" + phase, lambda phase=phase: modules["business"].exercise(target, phase, lab=lab))
        except (RuntimeError, OSError, ValueError, KeyError) as exc:
            failures.append(phase + ":" + str(exc))
    return failures


def run_target(lab, target: str, artifact_root: Path, resume: bool = False,
               acceleration: str = "tcg", provision_network: bool = False, timeout: int = 45) -> dict:
    from lab import fingerprint, inventory
    from macos import VMwareMacLab

    lab.initialize()
    spec = lab.matrix["targets"][target]
    packages, package_errors = inventory(lab.matrix, artifact_root)
    package_id = next(key for key, value in lab.matrix["packages"].items() if target in value["required_targets"])
    source = fingerprint()
    if package_id in packages:
        from provenance import bind_package
        try:
            packages[package_id] = bind_package(lab, packages[package_id], source)
        except (RuntimeError, OSError, ValueError, KeyError) as exc:
            package_errors[package_id] = str(exc)
    context = {"target": target, "target_config": spec, "source_fingerprint": source,
               "controller_fingerprint": controller_fingerprint(), "package": packages.get(package_id)}
    if spec["backend"] != "native-host":
        context["media_sha256"] = lab.media.get(spec["media"], {}).get("sha256")
    try:
        context["environment"] = runtime_binding(lab, target)
        if spec["backend"] != "native-host":
            context["restore_generation"] = lab.state(target).get("restore_generation")
    except (RuntimeError, OSError, ValueError, KeyError):
        context["environment"] = None
    failures = []
    if spec["os"] == "windows":
        from windows_execution import PROFILE_BLOCKERS
        if package_id in PROFILE_BLOCKERS:
            failures.append(PROFILE_BLOCKERS[package_id])
    if package_id in package_errors:
        failures.append(package_errors[package_id])
    baseline = None
    try:
        baseline = select_upgrade_baseline(lab.matrix, package_id,
                                           [artifact_root, artifact_root / "baselines", lab.root / "packages/baselines"])
    except RuntimeError as exc:
        failures.append(str(exc))
    # 升级包同样参与续跑身份；更换、补齐或删除基线后必须重新执行受影响步骤。
    context["upgrade_baseline"] = baseline
    journal = Journal(lab, target, context, resume)
    if baseline:
        write_json(journal.directory / "upgrade-baseline.json", baseline)
    try:
        if spec["backend"] == "native-host":
            from native_windows import readiness
            journal.step("native-readiness", lambda: readiness(lab, target))
            raise RuntimeError("NATIVE_WINDOWS_LIFECYCLE_EXECUTION_NOT_IMPLEMENTED")
        if spec["backend"] == "macos-arm64-experimental":
            from macos_arm64 import inspect_experiment
            journal.step("local-experiment", lambda: inspect_experiment(lab))
            raise RuntimeError("MACOS_ARM64_DESKTOP_AND_PKG_LIFECYCLE_UNAVAILABLE")
        if spec["backend"] == "vmware":
            mac = VMwareMacLab(lab)
            journal.step("vmware-inventory", mac.doctor)
            mac.start(gui=False)
            raise RuntimeError("MACOS_INTEL_GUEST_IDENTITY_AND_HELPER_PROVISIONING_REQUIRED")
        # 身份、介质、快照每次重新读取，不能由 journal 的完成标记替代。
        state = lab.create(target)
        if sha256(Path(state["base"])) != context["media_sha256"]:
            raise RuntimeError("VM_MEDIA_IDENTITY_MISMATCH")
        lab.start(target, acceleration, provision_network)
        try:
            system = wait_probe(lab, target, timeout)
        except RuntimeError:
            write_json(journal.directory / "installer-screen.json", snapshot_screen(lab, target, journal.directory))
            raise
        if not state.get("clean_baseline"):
            binding = seal_baseline(lab, target, acceleration, max(timeout, 180))
            # 基础快照的身份建立后生成新的上下文；此时还没有运行产品步骤。
            context["environment"] = binding
            journal.context = context
            journal.data["context"] = context
            journal.data["clean_start"] = clean_start_binding(lab.state(target))
            journal.save()
            lab.start(target, acceleration, False)
            system = wait_probe(lab, target, timeout)
        else:
            runtime_binding(lab, target)
            if not can_resume_clean_start(journal, lab.state(target)):
                lab.stop(target)
                wait_stopped(lab, target)
                lab.snapshot(target, state["clean_baseline"]["name"], restore=True)
                context["restore_generation"] = lab.state(target).get("restore_generation")
                journal.context = context
                journal.data["context"] = context
                journal.data["clean_start"] = clean_start_binding(lab.state(target))
                journal.save()
                lab.start(target, acceleration, False)
                system = wait_probe(lab, target, timeout)
        write_json(journal.directory / "guest-identity.json", system)
        if package_id in package_errors:
            raise RuntimeError(package_errors[package_id])
        if spec["os"] == "linux":
            module_spec = importlib.util.spec_from_file_location("installed_diagnostic", HERE / "scripts/exercise-linux-install.py")
            module = importlib.util.module_from_spec(module_spec)
            module_spec.loader.exec_module(module)
            journal.step("installed-package-diagnostic", lambda: module.exercise(target, lab=lab, package=packages[package_id]))
            business_spec = importlib.util.spec_from_file_location("business_diagnostic", HERE / "scripts/exercise-linux-business.py")
            business_module = importlib.util.module_from_spec(business_spec)
            business_spec.loader.exec_module(business_module)
            for phase in ("configure", "business"):
                journal.step("business-" + phase,
                             lambda phase=phase: business_module.exercise(target, phase, lab=lab))
            reboot_spec = importlib.util.spec_from_file_location("guest_reboot", HERE / "scripts/reboot-linux.py")
            reboot_module = importlib.util.module_from_spec(reboot_spec)
            reboot_spec.loader.exec_module(reboot_module)
            journal.step("actual-guest-reboot", lambda: reboot_module.reboot(target, lab=lab))
            journal.step("business-after-reboot", lambda: business_module.exercise(
                target, "after-reboot", lab=lab, reboot_execution=journal.directory / "execution.json"))
            # 已执行业务、真实重启也只提供对应证据；WPS、模型、协同与卸载仍须补齐。
            failures.append("FULL_LIFECYCLE_EVIDENCE_MISSING")
        else:
            failures.extend(windows_lifecycle(lab, target, artifact_root, journal))
            failures.append("WINDOWS_INSTALLED_LIFECYCLE_EVIDENCE_MISSING")
    except (RuntimeError, OSError, ValueError, KeyError) as exc:
        failures.append(str(exc))
    journal.data.update(status="blocked" if failures else "partial", errors=list(dict.fromkeys(failures)),
                        report_path=str(journal.directory), package_id=package_id,
                        upgrade_baseline=baseline, runtime_environment_passed=False)
    journal.save()
    return journal.data


def run_all(lab, artifact_root: Path, **options) -> dict:
    """固定顺序串行；一个平台阻断不会跳过后续平台的介质与前置检查。"""
    rows = {}
    for target in lab.matrix["run_order"]:
        print(json.dumps({"target": target, "stage": "run", "at": now()}, ensure_ascii=False), flush=True)
        rows[target] = run_target(lab, target, artifact_root, **options)
        write_json(lab.root / "state/run-all-progress.json", {"updated_at": now(), "targets": rows})
        # 只正常关机；若 Guest 不响应，后继运行由并发门禁阻断，仍保留各自诊断。
        if lab.matrix["targets"][target]["backend"] == "qemu":
            try:
                lab.stop(target)
                wait_stopped(lab, target, 60)
            except RuntimeError:
                pass
        elif lab.matrix["targets"][target]["backend"] == "vmware":
            from macos import VMwareMacLab
            try:
                VMwareMacLab(lab).stop()
            except RuntimeError as exc:
                rows[target]["errors"].append(str(exc))
                write_json(lab.root / "state/run-all-progress.json", {"updated_at": now(), "targets": rows})
    return {"status": "blocked", "targets": rows, "runtime_environment_passed": False}
