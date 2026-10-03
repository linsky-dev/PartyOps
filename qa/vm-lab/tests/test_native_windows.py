"""本机后端只读边界与反例；全部系统和包均为隔离的合成夹具。"""
import copy
import json
import subprocess
from pathlib import Path
from types import SimpleNamespace

import evidence
import execution
import identity
import lab as controller
import native_windows as native
import provenance
import pytest
from providers import QemuLab

TARGET = "win11-x64-native"
BEFORE = "2026-09-08T00:00:00.0000000Z"
AFTER = "2026-09-08T01:00:00.0000000Z"


@pytest.fixture
def raw():
    return {"os": "windows", "architecture": "AMD64", "caption": "Microsoft Windows 11 Pro",
            "version": "10.0.26200", "build": "26200", "update_build_revision": "9168",
            "product_type": 1, "display_version": "25H2", "edition": "Professional",
            "machine_guid": "de9d78d2-6340-495b-a1ef-089e7d9a06e1",
            "hardware_uuid": "f132d948-b43b-43b4-810f-70931e6a2b94",
            "boot_id": BEFORE, "user_sid": "S-1-5-21-123-456-789-1003", "is_admin": False}


@pytest.fixture
def native_lab(tmp_path, monkeypatch, raw):
    matrix, media = controller.load_configuration()
    matrix["defaults"].update(primary_root=str(tmp_path / "lab"), fallback_root=str(tmp_path / "fallback"), qemu_home=str(tmp_path / "qemu"))
    lab = QemuLab(matrix, tmp_path / "lab", media)
    lab.initialize()
    system = native.normalize(raw)
    monkeypatch.setattr(native, "read_host", lambda: copy.deepcopy(system))
    monkeypatch.setattr(controller, "load_configuration", lambda: (matrix, media))
    monkeypatch.setattr(controller, "fingerprint", lambda: "b" * 64)
    return lab, system


def test_active_matrix_keeps_native_win11_and_requires_separate_arm_compatibility():
    matrix, _ = controller.load_configuration()
    assert len(matrix["targets"]) >= 10 and len(matrix["packages"]) >= 9
    assert matrix["packages"]["windows_amd64"]["required_targets"] == [TARGET, "win10-x64", "win11-arm64"]
    assert TARGET in matrix["run_order"] and "win11-x64" not in matrix["run_order"]
    assert "win11-x64" not in matrix["targets"] and "win11-x64" in matrix["historical_targets"]
    assert not {"media", "ssh_port", "winrm_port", "vm_storage"}.intersection(matrix["targets"][TARGET])


def test_identity_is_actual_version_and_stable_machine_hash(raw):
    first = native.normalize(raw)
    assert first["os_release"] == "11" and first["os_build"] == "26200.9168" and first["arch"] == "x86_64"
    assert first["is_admin"] is False and not {"machine_guid", "hardware_uuid", "marker", "vm_uuid"}.intersection(first)
    changed = native.normalize({**raw, "boot_id": AFTER, "is_admin": True, "user_sid": "S-1-5-18"})
    assert native.identity_digest(first) == native.identity_digest(changed)
    for field, value in [("update_build_revision", "9169"), ("edition", "Enterprise"), ("architecture", "ARM64"), ("machine_guid", "ed22f0f5-512b-4c47-9d99-d5489e69ad43")]:
        assert native.identity_digest(native.normalize({**raw, field: value})) != native.identity_digest(first)


@pytest.mark.parametrize("field,value,error", [
    ("product_type", 3, "CLIENT_VERSION"), ("version", "10.0.19045", "BUILD_MISMATCH"),
    ("hardware_uuid", "00000000-0000-0000-0000-000000000000", "MACHINE_IDENTITY"),
    ("machine_guid", "", "MACHINE_IDENTITY"), ("architecture", "unknown", "IDENTITY_INCOMPLETE"),
    ("boot_id", "process-pid-1234", "BOOT_ID"), ("edition", "", "IDENTITY_INCOMPLETE"),
    ("is_admin", "False", "CURRENT_USER"), ("user_sid", "Administrator", "CURRENT_USER"),
])
def test_incomplete_host_identity_is_not_guessed(raw, field, value, error):
    with pytest.raises(RuntimeError, match=error):
        native.normalize({**raw, field: value})


def test_non_object_probe_cannot_be_normalized():
    with pytest.raises(TypeError, match="PROBE_NOT_OBJECT"):
        native.normalize([])


def test_collector_uses_only_readonly_powershell_and_preserves_json(raw, monkeypatch):
    calls = []
    def run(args, **options):
        calls.append((args, options))
        return SimpleNamespace(returncode=0, stdout=json.dumps(raw))
    monkeypatch.setattr(native.os, "name", "nt")
    monkeypatch.setattr(native.subprocess, "run", run)
    assert native.read_host()["boot_id"] == BEFORE
    assert calls[0][0][:3] == ["powershell.exe", "-NoProfile", "-NonInteractive"]
    assert calls[0][1]["timeout"] == 45 and calls[0][1]["capture_output"]
    assert "[Console]::WriteLine" in native.HOST_PROBE
    for forbidden in ("Restart-Computer", "Stop-Process", "Set-ItemProperty", "Remove-Item", "Start-Process", "disposable-qa"):
        assert forbidden not in native.HOST_PROBE


@pytest.mark.parametrize("outcome,error", [("timeout", "TimeoutExpired"), ("exit", "PROBE_EXIT:7"), ("json", "PROBE_INVALID")])
def test_collector_failures_do_not_leak_process_output(monkeypatch, outcome, error):
    def run(*args, **kwargs):
        if outcome == "timeout":
            raise subprocess.TimeoutExpired("secret-command", 45, output="secret-output")
        return SimpleNamespace(returncode=7 if outcome == "exit" else 0, stdout="private-invalid-data", stderr="private-error")
    monkeypatch.setattr(native.os, "name", "nt")
    monkeypatch.setattr(native.subprocess, "run", run)
    with pytest.raises(RuntimeError, match=error) as caught:
        native.read_host()
    assert "private" not in str(caught.value) and "secret" not in str(caught.value)


def test_probe_and_binding_never_read_vm_or_media(native_lab, monkeypatch):
    lab, system = native_lab
    def forbidden(*args, **kwargs):
        pytest.fail("原生目标不得读取或操作 VM")
    for method in ("state", "vm_dir", "create", "start", "stop", "snapshot", "ssh", "fetch"):
        monkeypatch.setattr(lab, method, forbidden)
    assert identity.probe(lab, TARGET) == system
    binding = identity.runtime_binding(lab, TARGET)
    assert binding["host_id"] == system["host_id"] and binding["current_boot_id"] == BEFORE
    assert not native.VM_FIELDS.intersection(binding)
    identity.validate_identity(system, lab.matrix["targets"][TARGET], {})
    with pytest.raises(RuntimeError, match="COMMAND_NOT_ALLOWED"):
        identity.seal_baseline(lab, TARGET)


def test_malformed_observed_boot_history_is_not_silently_replaced(native_lab):
    lab, _system = native_lab
    path = lab.root / "reports" / TARGET / "native-observed-boots.json"
    evidence.write_json(path, [{"boot_id": "process-id"}])
    with pytest.raises(RuntimeError, match="NATIVE_BOOT_OBSERVATIONS_INVALID"):
        native.runtime_binding(lab, TARGET)
    assert json.loads(path.read_text()) == [{"boot_id": "process-id"}]


@pytest.mark.parametrize("command", ["create", "start", "stop", "snapshot", "restore", "baseline", "cleanup", "screenshot", "media"])
def test_cli_rejects_all_native_guest_commands(native_lab, command, capsys, monkeypatch):
    def forbidden(*args, **kwargs):
        pytest.fail("不得向 VM 派发命令")
    for method in ("create", "start", "stop", "snapshot", "cleanup", "fetch"):
        monkeypatch.setattr(QemuLab, method, forbidden)
    assert controller.main([command, TARGET]) == 2
    assert "NATIVE_HOST_COMMAND_NOT_ALLOWED" in capsys.readouterr().out


def test_direct_vm_provider_rejects_native_even_with_old_registration(native_lab):
    lab, _ = native_lab
    old = lab.root / "vms" / TARGET / "vm.json"
    evidence.write_json(old, {"uuid": "old-vm", "purpose": "disposable-qa", "pid": 123})
    for operation in (lambda: lab.create(TARGET), lambda: lab.start(TARGET), lambda: lab.stop(TARGET),
                      lambda: lab.cleanup(TARGET, True), lambda: lab.windows_seed(old.parent, "old-vm", TARGET)):
        with pytest.raises(RuntimeError, match="NATIVE_HOST_VM_OPERATION_NOT_ALLOWED"):
            operation()
    assert old.is_file()


def test_native_run_stops_after_readiness_and_resume_rechecks_boot(native_lab, tmp_path, monkeypatch):
    lab, system = native_lab
    package = {"id": "windows_amd64", "sha256": "a" * 64, "version": lab.matrix["version"]}
    monkeypatch.setattr(controller, "inventory", lambda *args: ({"windows_amd64": package}, {}))
    monkeypatch.setattr(provenance, "bind_package", lambda _lab, package, _source: package)
    monkeypatch.setattr(execution, "controller_fingerprint", lambda: "c" * 64)
    for method in ("create", "start", "stop", "snapshot", "ssh"):
        monkeypatch.setattr(lab, method, lambda *a, **k: pytest.fail("不得执行宿主生命周期"))
    first = execution.run_target(lab, TARGET, tmp_path)
    assert first["status"] == "blocked" and first["runtime_environment_passed"] is False
    assert first["errors"] == ["OLDER_PACKAGE_REMOVED_BY_RETENTION_POLICY", "NATIVE_WINDOWS_LIFECYCLE_EXECUTION_NOT_IMPLEMENTED"]
    assert list(first["steps"]) == ["native-readiness"]
    diagnostic = json.loads((Path(first["report_path"]) / "native-readiness.json").read_text(encoding="utf-8"))
    assert set(diagnostic["lifecycle_cases"].values()) == {"not_run"} and diagnostic["host_changes"] == []
    assert "media_sha256" not in first["context"] and "restore_generation" not in first["context"]
    assert execution.run_target(lab, TARGET, tmp_path, resume=True)["run_id"] == first["run_id"]
    system["boot_id"] = AFTER
    second = execution.run_target(lab, TARGET, tmp_path, resume=True)
    assert second["run_id"] != first["run_id"] and second["resume_invalidated"]


@pytest.fixture
def native_evidence(native_lab, tmp_path):
    lab, system = native_lab
    native.probe(lab, TARGET)
    log = tmp_path / "synthetic.log"
    log.write_text("仅测试夹具，无实际系统操作或安装证明。", encoding="utf-8")
    package = {"id": "windows_amd64", "sha256": "a" * 64, "version": lab.matrix["version"],
               "source_fingerprint": "b" * 64, "provenance_status": "verified"}
    before = copy.deepcopy(system)
    system["boot_id"] = AFTER
    binding = native.runtime_binding(lab, TARGET)
    result = {"schema_version": 2, "run_id": "unit-test-only", "generated_at": evidence.now(),
              "target": TARGET, "status": "passed", "source_fingerprint": "b" * 64,
              "package": package, "system": before, "distribution_match": True,
              "environment": {key: binding[key] for key in ("backend", "host_id", "identity_sha256")},
              "restart": {"actual": True, "boot_id_before": BEFORE, "boot_id_after": AFTER},
              "cases": [{"id": case, "status": "passed", "evidence": [{"path": log.name, "sha256": evidence.sha256(log)}]} for case in lab.matrix["required_cases"]]}
    schema = json.loads((Path(controller.HERE) / "schemas/result.schema.json").read_text())
    def evaluate():
        target = {**lab.matrix["targets"][TARGET], "runtime_binding": native.runtime_binding(lab, TARGET)}
        return evidence.evaluate(result, target, package, lab.matrix["required_cases"], tmp_path, schema, "b" * 64)
    return lab, result, package, evaluate


def test_complete_native_contract_does_not_require_fictional_vm_fields(native_evidence):
    _lab, _result, _package, evaluate = native_evidence
    assert evaluate() == []


@pytest.mark.parametrize("mutation,error", [
    ("vm", "RUNTIME_BACKEND_MISMATCH"), ("hybrid", "INVALID_RESULT_SCHEMA"),
    ("machine", "RUNTIME_BINDING_MISMATCH: host_id"), ("os_version", "NATIVE_REPORTED_SYSTEM_IDENTITY_MISMATCH"),
    ("source", "STALE_SOURCE_FINGERPRINT"), ("package", "PACKAGE_SHA256_MISMATCH"),
    ("reboot", "REBOOT_NOT_PROVEN"), ("foreign_boot", "NATIVE_REBOOT_DESTINATION_NOT_CURRENT"),
    ("unobserved_boot", "NATIVE_REBOOT_SOURCE_NOT_OBSERVED"),
    ("partial", "MISSING_CASE"), ("diagnostic", "RESULT_NOT_PASSED"),
])
def test_native_negative_evidence_contract(native_evidence, mutation, error):
    _lab, result, _package, evaluate = native_evidence
    if mutation == "vm":
        result["system"]["environment_type"] = "hardware-virtualized"
        result["environment"] = {"vm_uuid": "old-vm-uuid", "media_sha256": "c" * 64, "baseline_id": "old", "identity_sha256": "d" * 64}
    elif mutation == "hybrid":
        result["environment"]["vm_uuid"] = "old-vm-uuid"
    elif mutation == "machine":
        result["environment"]["host_id"] = "f" * 64
    elif mutation == "os_version":
        result["system"]["os_build"] = "26200.9999"
    elif mutation == "source":
        result["source_fingerprint"] = "c" * 64
    elif mutation == "package":
        result["package"] = {**result["package"], "sha256": "c" * 64}
    elif mutation == "reboot":
        result["restart"]["boot_id_after"] = BEFORE
    elif mutation == "foreign_boot":
        result["restart"]["boot_id_after"] = "process-id-1234"
    elif mutation == "unobserved_boot":
        result["restart"]["boot_id_before"] = "2026-09-07T00:00:00.0000000Z"
        result["system"]["boot_id"] = result["restart"]["boot_id_before"]
    elif mutation == "partial":
        result["cases"].pop()
    else:
        result["status"] = "partial"
    assert any(error in message for message in evaluate())


def test_check_target_rejects_old_guest_target_and_never_counts_readiness(native_evidence, monkeypatch, tmp_path):
    lab, result, package, _evaluate = native_evidence
    monkeypatch.setattr(provenance, "bind_package", lambda _lab, package, _source: package)
    path = tmp_path / "old-guest-result.json"
    result["target"] = "win11-x64"
    evidence.write_json(path, result)
    report = controller.check_target(lab, TARGET, {"windows_amd64": package}, {}, path, "b" * 64)
    assert "RESULT_TARGET_MISMATCH" in report["errors"] and not report["runtime_environment_passed"]
    report = controller.check_target(lab, TARGET, {"windows_amd64": package}, {}, None, "b" * 64)
    assert report["host_identity_status"] == "verified" and not report["runtime_environment_passed"]
    assert "FULL_LIFECYCLE_EVIDENCE_MISSING" in report["errors"]
    assert evidence.aggregate(lab.matrix, {"windows_amd64": package}, {TARGET: report})["passed_packages"] == 0
