"""CLI、清单、报告重验证和阻断行为测试。"""
import json
from pathlib import Path

import jsonschema
import lab as controller
import pytest
from evidence import write_json
from lab import check_target, host_tool, inventory, load_configuration, main, reports
from providers import QemuLab


@pytest.fixture
def setup(tmp_path, monkeypatch):
    import native_windows
    def no_host_probe():
        raise RuntimeError("UNIT_TEST_NO_REAL_HOST_PROBE")
    monkeypatch.setattr(native_windows, "read_host", no_host_probe)
    matrix, media = load_configuration()
    matrix["defaults"]["primary_root"] = str(tmp_path / "lab")
    matrix["defaults"]["qemu_home"] = str(tmp_path / "qemu")
    matrix["defaults"]["fallback_root"] = str(tmp_path / "lab")
    q = QemuLab(matrix, tmp_path / "lab", media)
    q.initialize()
    monkeypatch.setattr(controller, "load_configuration", lambda: (matrix, media))
    monkeypatch.setattr(controller, "fingerprint", lambda: "b" * 64)
    return q, tmp_path


def test_inventory_missing_and_ambiguous(tmp_path):
    matrix, _ = load_configuration()
    packages, errors = inventory(matrix, tmp_path)
    assert not packages and len(errors) == len(matrix["packages"])
    for key, spec in matrix["packages"].items():
        name = spec["pattern"].format(version=matrix["version"]).replace("*", "1")
        (tmp_path / name).write_bytes(key.encode())
    packages, errors = inventory(matrix, tmp_path)
    assert len(packages) == len(matrix["packages"]) and not errors
    (tmp_path / "PartyOps-1.4.5-0.rc.6.2.x86_64.rpm").write_bytes(b"ambiguous")
    assert inventory(matrix, tmp_path)[1]["rpm_x86_64"] == "AMBIGUOUS_PACKAGE"


def test_report_missing_packages_and_evidence(setup):
    q, path = setup
    result = reports(q, list(q.matrix["targets"]), None, path)
    assert result["passed_packages"] == 0
    assert (Path(result["report_path"]) / "qa-report.md").exists()
    assert (q.root / "state/latest-report.json").exists()
    target = check_target(q, "win11-x64-native", {}, {"windows_amd64": "MISSING_PACKAGE"}, path / "missing.json", "b" * 64)
    assert "MISSING_RESULT_FILE" in target["errors"]


@pytest.mark.parametrize("locked", [True, False])
def test_inventory_isolates_locked_or_changing_package(tmp_path, monkeypatch, locked):
    matrix = {"version": "fixture", "packages": {"building": {"pattern": "a.exe"}, "ready": {"pattern": "b.deb"}}}
    for name in ("a.exe", "b.deb"):
        (tmp_path / name).write_bytes(b"fixture")
    original = controller.sha256
    def hashing(path):
        if path.name == "a.exe":
            if locked:
                raise PermissionError("installer owns file")
            value = original(path)
            path.write_bytes(b"changed-file")
            return value
        return original(path)
    monkeypatch.setattr(controller, "sha256", hashing)
    packages, errors = inventory(matrix, tmp_path)
    assert set(packages) == {"ready"} and set(errors) == {"building"}
    assert errors["building"] == ("PACKAGE_UNREADABLE_OR_BUILD_IN_PROGRESS" if locked else "PACKAGE_CHANGED_DURING_INVENTORY")


def test_aggregate_report_is_schema_valid(setup):
    q, path = setup
    result = reports(q, list(q.matrix["targets"]), None, path)
    schema = json.loads(
        (Path(controller.HERE) / "schemas/aggregate.schema.json").read_text(encoding="utf-8")
    )
    jsonschema.validate(result, schema)
    saved = json.loads((q.root / "state/latest-report.json").read_text(encoding="utf-8"))
    jsonschema.validate(saved, schema)


def test_import_cannot_accept_wrong_target_or_missing_steps(setup):
    q, path = setup
    evidence = path / "result.json"
    write_json(evidence, {"target": "different-target"})
    result = check_target(q, "openeuler-iso-x64", {"rpm_x86_64": {"sha256": "a" * 64}}, {}, evidence, "b" * 64)
    assert "RESULT_TARGET_MISMATCH" in result["errors"]
    assert not result["runtime_environment_passed"]


def test_accepted_evidence_is_revalidated_on_every_report(setup, monkeypatch):
    import identity
    import provenance
    monkeypatch.setattr(provenance, "bind_package", lambda _lab, package, _source: package)
    monkeypatch.setattr(identity, "runtime_binding", lambda *a: {"unit_test_only": True})
    q, path = setup
    evidence = path / "result.json"
    write_json(evidence, {"target": "openeuler-iso-x64", "restart": {"actual": True}, "system": {"environment_type": "full-system-emulated"}, "distribution_match": True})
    monkeypatch.setattr(controller, "evaluate", lambda *a: [])
    spec = q.matrix["packages"]["rpm_x86_64"]
    (path / spec["pattern"].replace("*", "1")).write_bytes(b"unit-test-only")
    first = reports(q, ["openeuler-iso-x64"], evidence, path)
    assert first["passed_packages"] == 1
    monkeypatch.setattr(controller, "evaluate", lambda *a: ["STALE_SOURCE_FINGERPRINT"])
    second = reports(q, [], None, path)
    assert second["passed_packages"] == 0


@pytest.mark.parametrize("arguments", [["create"], ["create", "unknown"], ["create", "../invalid"], ["test-all"], ["test", "win11-x64-native"]])
def test_cli_blocked_conditions(setup, arguments, capsys):
    _, path = setup
    assert main(arguments + ["--artifacts", str(path)]) == 2
    assert "passed" in capsys.readouterr().out or arguments[0] == "create"


@pytest.mark.parametrize("command", ["doctor", "media", "create", "start", "stop", "snapshot", "restore", "cleanup", "probe", "screenshot"])
def test_cli_dispatch(setup, monkeypatch, command, capsys):
    import identity
    monkeypatch.setattr(identity, "probe", lambda *a: {"scope": "dispatch-test-only"})
    q, path = setup
    state = {"qmp_port": 12000}
    monkeypatch.setattr(controller, "doctor", lambda _: {"host": "fixture"})
    monkeypatch.setattr(QemuLab, "fetch", lambda *a: path / "media")
    monkeypatch.setattr(QemuLab, "create", lambda *a: state)
    monkeypatch.setattr(QemuLab, "start", lambda *a: state)
    monkeypatch.setattr(QemuLab, "stop", lambda *a: state)
    monkeypatch.setattr(QemuLab, "snapshot", lambda *a: state)
    monkeypatch.setattr(QemuLab, "cleanup", lambda *a: state)
    monkeypatch.setattr(QemuLab, "state", lambda *a: state)
    monkeypatch.setattr(QemuLab, "live", lambda *a: True)
    q.vm_dir("openeuler-iso-x64").mkdir()
    monkeypatch.setattr(QemuLab, "ssh", lambda *a, **kw: json.dumps({"os": "linux", "arch": "x86_64"}))
    def fake_qmp(port, command, args):
        Path(args["filename"]).write_bytes(b"unit-test-only")
        return {}
    monkeypatch.setattr(controller, "qmp", fake_qmp)
    assert main([command, "openeuler-iso-x64"]) == 0
    assert capsys.readouterr().out


@pytest.mark.parametrize("command,method", [("macos-doctor", "doctor"), ("macos-x64-media", "prepare_recovery"), ("macos-x64-bootstrap", "bootstrap")])
def test_macos_cli_dispatch(setup, monkeypatch, command, method, capsys):
    _q, _path = setup
    monkeypatch.setattr(controller.VMwareMacLab, method, lambda _self: {"status": "fixture"})
    assert main([command]) == 0
    assert "fixture" in capsys.readouterr().out


@pytest.mark.parametrize("command,method", [("create", "bootstrap"), ("start", "start"), ("stop", "stop"), ("snapshot", "snapshot"), ("restore", "snapshot")])
def test_macos_target_lifecycle_dispatch(setup, monkeypatch, command, method, capsys):
    _q, _path = setup
    monkeypatch.setattr(controller.VMwareMacLab, method, lambda _self, *args, **kwargs: {"status": "fixture"})
    assert main([command, "macos-x64"]) == 0
    assert "fixture" in capsys.readouterr().out


def test_doctor_collects_features_without_changing_them(setup, monkeypatch):
    q, _ = setup
    monkeypatch.setattr(controller, "run", lambda args, **kw: json.dumps({"free_memory_gib": 16}) if "powershell.exe" == args[0] else "QEMU fixture\n")
    monkeypatch.setattr(QemuLab, "binary", lambda self, name: name)
    report = controller.doctor(q)
    assert report["security_changes"] == []
    assert "qemu-system-aarch64" in report["tools"]


def test_host_tool_finds_vmware_in_lab_root(setup, monkeypatch):
    q, _ = setup
    executable = q.root / "tools/VMware" / ("vmrun.exe" if controller.os.name == "nt" else "vmrun")
    executable.parent.mkdir(parents=True)
    executable.write_bytes(b"fixture")
    monkeypatch.setattr(controller.shutil, "which", lambda _name: None)
    assert host_tool(q, "vmrun") == str(executable.resolve())


def test_probe_rejects_wrong_guest(setup, monkeypatch):
    _q, _ = setup
    monkeypatch.setattr(QemuLab, "ssh", lambda *a, **kw: '{"os":"linux","arch":"aarch64"}')
    assert main(["probe", "openeuler-iso-x64"]) == 2


def test_fingerprint_uses_existing_gate():
    assert len(controller.fingerprint()) == 64
