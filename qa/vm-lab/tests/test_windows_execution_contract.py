"""新 Windows 必需矩阵与仿真反例；仅合成证据和本机语法解析，不启动 VM。"""
from __future__ import annotations

import base64
import copy
import io
import json
import os
import subprocess
from pathlib import Path

import pycdlib
import pytest
import windows_execution as contract
from evidence import aggregate, sha256, write_json
from lab import describe_unverified_environment, load_configuration
from providers import QemuLab


def sample(target="win11-arm64"):
    matrix, _ = load_configuration()
    spec = matrix["targets"][target]
    package_id = next(key for key, value in matrix["packages"].items() if target in value["required_targets"])
    package = {"id": package_id, "sha256": "a" * 64, "version": matrix["version"]}
    version = "10.0.26200" if target == "win11-arm64" else "10.0.19045"
    system = {"os": "windows", "arch": spec["arch"], "os_isa": spec["arch"], "os_version": version,
              "isa_probe_api": "IsWow64Process2", "boot_id": "boot-a", "secure_boot_enabled": True,
              "tpm_spec_version": "2.0", "installation_requirement_bypasses": False}
    environment = {"vm_uuid": "actual-vm-uuid"}
    record = {"schema_version": 1, "probe_api": "IsWow64Process2", "os_isa": spec["arch"], "os_version": version,
              "process_isa": spec["package_arch"], "pe_isa": spec["package_arch"], "package_isa": spec["package_arch"],
              "runtime_profile": spec["expected_runtime_profile"], "execution_kind": spec["application_execution"],
              "package_platform": package_id.rsplit("_", 1)[0], "package_version": matrix["version"],
              "boot_id": "boot-a", "guest_uuid": "actual-vm-uuid", "installer_sha256": "a" * 64,
              "installed_executable_sha256": "b" * 64, "manifest_executable_sha256": "b" * 64,
              "installed_manifest_sha256": "c" * 64, "installed_executable_size": 4096, "manifest_executable_size": 4096,
              "pid": 4012, "owner_sid": "S-1-5-21-1234-5678-9012-1001", "is_admin": False,
              "executable_path": "C:\\PartyOps QA\\中文 程序\\PartyOps.exe"}
    return record, spec, package, system, environment


def test_expanded_targets_are_required_without_inventing_packages():
    matrix, media = load_configuration()
    assert len(matrix["packages"]) == 10
    assert len(matrix["targets"]) == 14
    assert set(matrix["run_order"]) == set(matrix["targets"])
    assert matrix["packages"]["windows7_x86"]["required_targets"] == ["win7-x86", "win10-x86", "win10-arm64"]
    assert matrix["packages"]["windows_amd64"]["required_targets"] == ["win11-x64-native", "win10-x64", "win11-arm64"]
    assert media["win10-arm64"]["blocked_reason"] == "OFFICIAL_WINDOWS10_ARM64_MEDIA_UNAVAILABLE_UNDER_CURRENT_CONSTRAINTS"
    packages = {key: {"sha256": key} for key in matrix["packages"]}
    reports = {target: {"runtime_environment_passed": True, "package_sha256": key}
               for key, spec in matrix["packages"].items() for target in spec["required_targets"]}
    reports.pop("win10-arm64")
    assert aggregate(matrix, packages, reports)["passed_packages"] == 9


@pytest.mark.parametrize("target", ["win10-x86", "win10-arm64", "win11-arm64"])
def test_actual_package_execution_identity_can_be_validated_without_claiming_full_pass(target):
    record, spec, package, system, environment = sample(target)
    contract.validate_runtime(record, spec, package, system, environment)


@pytest.mark.parametrize("field,value,reason", [
    ("execution_kind", "native", "WINDOWS_EMULATION_MISREPORTED_AS_NATIVE"),
    ("process_isa", "arm64", "WINDOWS_RUNTIME_ISA_MISMATCH"),
    ("pe_isa", "arm64", "WINDOWS_RUNTIME_ISA_MISMATCH"),
    ("package_isa", "arm64", "WINDOWS_RUNTIME_ISA_MISMATCH"),
    ("os_isa", "x86_64", "WINDOWS_RUNTIME_GUEST_IDENTITY_MISMATCH"),
    ("guest_uuid", "previous-target", "WINDOWS_RUNTIME_GUEST_IDENTITY_MISMATCH"),
    ("boot_id", "previous-boot", "WINDOWS_RUNTIME_GUEST_IDENTITY_MISMATCH"),
    ("runtime_profile", "legacy-core", "WINDOWS_RUNTIME_PROFILE_MISMATCH"),
    ("package_version", "1.4.4", "WINDOWS_INSTALLED_PACKAGE_METADATA_MISMATCH"),
    ("package_platform", "windows7", "WINDOWS_INSTALLED_PACKAGE_METADATA_MISMATCH"),
    ("installer_sha256", "d" * 64, "WINDOWS_RUNTIME_INSTALLER_HASH_MISMATCH"),
    ("manifest_executable_sha256", "d" * 64, "WINDOWS_INSTALLED_BINARY_MANIFEST_MISMATCH"),
    ("installed_manifest_sha256", "", "WINDOWS_INSTALLED_BINARY_BINDING_MISSING"),
    ("manifest_executable_size", 1, "WINDOWS_INSTALLED_BINARY_MANIFEST_MISMATCH"),
    ("is_admin", True, "WINDOWS_STANDARD_USER_PROCESS_NOT_PROVEN"),
    ("pid", True, "WINDOWS_STANDARD_USER_PROCESS_NOT_PROVEN"),
    ("owner_sid", "S-1-5-18", "WINDOWS_STANDARD_USER_PROCESS_NOT_PROVEN"),
    ("executable_path", "C:\\wrong\\python.exe", "WINDOWS_INSTALLED_PROCESS_PATH_INVALID"),
    ("probe_api", "environment-variable", "WINDOWS_RUNTIME_PROCESS_API_NOT_PROVEN"),
])
def test_wrong_package_or_os_or_fake_native_runtime_is_rejected(field, value, reason):
    record, spec, package, system, environment = sample()
    record[field] = value
    with pytest.raises(RuntimeError, match=reason):
        contract.validate_runtime(record, spec, package, system, environment)


@pytest.mark.parametrize("field,value", [("secure_boot_enabled", False), ("tpm_spec_version", "1.2"), ("installation_requirement_bypasses", True)])
def test_arm_hardware_qualifications_cannot_be_bypassed(field, value):
    record, spec, package, system, environment = sample()
    system[field] = value
    with pytest.raises(RuntimeError, match="WINDOWS_ARM64_SECURE_BOOT_TPM2_NOT_PROVEN"):
        contract.validate_runtime(record, spec, package, system, environment)


def test_old_x64_os_evidence_does_not_become_arm_by_changing_target():
    _, spec, _, system, _ = sample()
    system["os_isa"] = "x86_64"
    with pytest.raises(RuntimeError, match="WINDOWS_NATIVE_OS_ISA_NOT_PROVEN"):
        contract.validate_os_identity(system, spec)
    system["os_isa"] = "arm64"
    system.pop("isa_probe_api")
    with pytest.raises(RuntimeError, match="WINDOWS_NATIVE_OS_API_OR_VERSION_NOT_PROVEN"):
        contract.validate_os_identity(system, spec)


def test_legacy_core_missing_ai_is_not_a_successful_skip(tmp_path):
    record, spec, package, system, environment = sample("win10-x86")
    path = tmp_path / "windows-runtime.json"
    write_json(path, record)
    result = {"windows_runtime_evidence": {"path": path.name, "sha256": sha256(path)}, "system": system, "environment": environment}
    assert contract.evidence_errors(result, spec, package, tmp_path) == ["WINDOWS_LEGACY_CORE_OFFLINE_AI_NOT_BUNDLED"]
    assert contract.evidence_errors({}, {"os": "windows"}, {"id": "windows7_amd64"}, tmp_path) == ["WINDOWS_LEGACY_SMART_LOCAL_LLM_NOT_BUNDLED"]


def test_runtime_evidence_is_hash_bound_and_cannot_escape_report(tmp_path):
    record, spec, package, system, environment = sample()
    path = tmp_path / "windows-runtime.json"
    write_json(path, record)
    result = {"windows_runtime_evidence": {"path": path.name, "sha256": sha256(path)}, "system": system, "environment": environment}
    assert contract.evidence_errors(result, spec, package, tmp_path) == []
    result["windows_runtime_evidence"]["sha256"] = "f" * 64
    assert "WINDOWS_RUNTIME_EVIDENCE_HASH_MISMATCH" in contract.evidence_errors(result, spec, package, tmp_path)
    result["windows_runtime_evidence"]["path"] = "../elsewhere.json"
    assert contract.evidence_errors(result, spec, package, tmp_path) == ["WINDOWS_RUNTIME_EVIDENCE_MISSING_OR_INVALID"]


def test_win10_arm_cannot_execute_x64_or_turn_into_a_native_arm_package():
    with pytest.raises(RuntimeError, match="WINDOWS_APPLICATION_EXECUTION_UNSUPPORTED"):
        contract.execution_kind("arm64", "x86_64", "10.0.19045")
    assert contract.execution_kind("arm64", "i686", "10.0.19045") == "windows-x86-emulation"


def make_lab(tmp_path):
    matrix, media = load_configuration()
    matrix = copy.deepcopy(matrix)
    matrix["defaults"].update(qemu_home=str(tmp_path / "qemu"), fallback_root=str(tmp_path / "fallback"), reserve_primary_gib=0, reserve_fallback_gib=0)
    lab = QemuLab(matrix, tmp_path / "lab", media)
    lab.initialize()
    (lab.root / "keys/guest_ed25519").write_text("synthetic-private-key-for-unit-only")
    (lab.root / "keys/guest_ed25519.pub").write_text("ssh-ed25519 synthetic-unit-key")
    return lab


def test_win10_x86_unattend_uses_actual_x86_windows_components(tmp_path):
    lab = make_lab(tmp_path)
    path = lab.windows_seed(tmp_path, "synthetic-win10-x86-uuid", "win10-x86")
    iso = pycdlib.PyCdlib()
    iso.open(str(path))
    output = io.BytesIO()
    iso.get_file_from_iso_fp(output, joliet_path="/Autounattend.xml")
    iso.close()
    xml = output.getvalue()
    assert b'processorArchitecture="x86"' in xml
    assert b'processorArchitecture="amd64"' not in xml
    assert b"Windows 10 Pro" in xml


def test_arm_creation_cannot_emit_x86_disk_or_bypass_seed(tmp_path, monkeypatch):
    lab = make_lab(tmp_path)
    monkeypatch.setattr(lab, "prepare_base", lambda _media: pytest.fail("ARM 缺组件时不能创建盘"))
    with pytest.raises(RuntimeError, match="OFFICIAL_WINDOWS10_ARM64_MEDIA_UNAVAILABLE"):
        lab.create("win10-arm64")
    with pytest.raises(RuntimeError, match="WINDOWS_ARM64_SECURE_BOOT_TPM2_PROVIDER_REQUIRED"):
        lab.create("win11-arm64")
    with pytest.raises(RuntimeError, match="WINDOWS_ARM64_SECURE_BOOT_TPM2_PROVIDER_REQUIRED"):
        lab.windows_seed(tmp_path, "synthetic-arm-uuid", "win11-arm64")
    assert not (tmp_path / "windows-unattend.iso").exists()


def test_missing_arm_media_stays_visible_in_unverified_summary(tmp_path):
    lab = make_lab(tmp_path)
    report = {"errors": []}
    describe_unverified_environment(lab, "win10-arm64", report)
    assert "OFFICIAL_WINDOWS10_ARM64_MEDIA_UNAVAILABLE_UNDER_CURRENT_CONSTRAINTS" in report["errors"]
    assert report["vm_status"] == "not_created"


def test_capture_requires_valid_owned_target_before_any_remote_command(tmp_path, monkeypatch):
    lab = make_lab(tmp_path)
    lab.ssh = lambda *_args, **_kwargs: pytest.fail("不得使用没有合同的旧/原生目标")
    with pytest.raises(RuntimeError, match="WINDOWS_EXECUTION_CONTRACT_TARGET_REQUIRED"):
        contract.capture(lab, "win11-x64-native", {}, 22, "C:\\PartyOps\\PartyOps.exe", tmp_path)


def test_capture_observes_an_existing_process_only(tmp_path, monkeypatch):
    import identity
    record, spec, package, system, environment = sample()
    package["path"] = str(tmp_path / "PartyOps_current_windows_amd64.exe")
    lab = make_lab(tmp_path)
    monkeypatch.setattr(identity, "probe", lambda *_args: system)
    monkeypatch.setattr(identity, "runtime_binding", lambda *_args: environment)
    def ssh(_target, command, timeout):
        script = base64.b64decode(command.split("-EncodedCommand ")[1]).decode("utf-16le")
        assert "$targetPid=4012" in script
        assert "Start-Process" not in script and "shutdown" not in script
        return json.dumps(record)
    lab.ssh = ssh
    destination = lab.root / "reports/win11-arm64/synthetic-capture"
    result = contract.capture(lab, "win11-arm64", package, 4012, record["executable_path"], destination)
    assert result["sha256"] == sha256(destination / result["path"])
    assert result["runtime_environment_passed"] is False


@pytest.mark.parametrize("pid,executable", [(True, "C:\\PartyOps.exe"), (1, "relative.exe"), (1, "\\\\server\\PartyOps.exe")])
def test_capture_command_rejects_ambiguous_pid_or_remote_paths(pid, executable):
    with pytest.raises(ValueError):
        contract.runtime_script(pid, executable, "C:\\incoming\\installer.exe")


@pytest.mark.skipif(os.name != "nt", reason="仅 Windows 宿主提供 PowerShell 解析器；跨平台纯合同测试仍执行")
def test_runtime_powershell_parses_without_executing_application(tmp_path):
    script = tmp_path / "runtime-probe.ps1"
    script.write_text(contract.runtime_script(4012, "C:\\PartyOps QA\\中文 程序\\PartyOps.exe", "C:\\incoming\\installer.exe"), encoding="utf-8-sig")
    command = "$tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile('" + str(script).replace("'", "''") + "',[ref]$tokens,[ref]$errors);if($errors.Count){$errors|ForEach-Object{$_.Message};exit 1}"
    result = subprocess.run(["powershell.exe", "-NoProfile", "-EncodedCommand", base64.b64encode(command.encode("utf-16le")).decode()], capture_output=True, text=True, timeout=30)
    assert result.returncode == 0, result.stdout + result.stderr


@pytest.mark.skipif(os.name != "nt", reason="本地 C# 编译检查需要 Windows PowerShell；不作为 Guest 身份证据")
def test_machine_probe_csharp_compiles_in_windows_powershell(tmp_path):
    command = "$ErrorActionPreference='Stop'\n" + contract.MACHINE_PROBE.split("$machineParts=", 1)[0]
    environment = dict(os.environ)
    environment["PSModulePath"] = str(Path(os.environ["SystemRoot"]) / "System32/WindowsPowerShell/v1.0/Modules")
    environment.update(TEMP=str(tmp_path), TMP=str(tmp_path))
    result = subprocess.run(["powershell.exe", "-NoProfile", "-EncodedCommand", base64.b64encode(command.encode("utf-16le")).decode()],
                            capture_output=True, text=True, timeout=30, env=environment)
    assert result.returncode == 0, result.stdout + result.stderr
