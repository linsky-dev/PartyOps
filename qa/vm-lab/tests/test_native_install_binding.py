"""本机新包绑定反例使用合成构建目录；不运行安装器或修改账户。"""
from __future__ import annotations

import importlib.util
import json
import os
import shutil
import subprocess
from pathlib import Path
from types import SimpleNamespace

import pytest
from evidence import sha256, write_json
from native_install import prepare, verify
from windows_build_payload import capture_payload, expected_payload, manifest_entries


@pytest.fixture
def build_fixture(tmp_path):
    lab = SimpleNamespace(root=tmp_path / "lab")
    artifacts = tmp_path / "artifacts"
    artifacts.mkdir()
    package_path = artifacts / "PartyOps_test_windows_amd64.exe"
    package_path.write_bytes(b"synthetic package; not an installer")
    package = {"id": "windows_amd64", "version": "test", "path": str(package_path),
               "sha256": sha256(package_path), "bytes": package_path.stat().st_size}
    stage = artifacts / "PartyOps-test-windows-amd64"
    stage.mkdir()
    for name, content in (("PartyOps.exe", b"synthetic new executable"), ("PartyOpsWizard.exe", b"synthetic wizard")):
        (stage / name).write_bytes(content)
    manifest = {"schema_version": 1, "product": "PartyOps", "version": "test", "platform": "windows",
                "architecture": "amd64", "runtime_profile": "full", "files": [
                    {"path": name, "size": (stage / name).stat().st_size, "sha256": sha256(stage / name)}
                    for name in ("PartyOps.exe", "PartyOpsWizard.exe")]}
    write_json(stage / "release-manifest.json", manifest)
    report = lab.root / "reports/build-fixture"
    report.mkdir(parents=True)
    log = report / "build.log"
    log.write_text("synthetic local build fixture", encoding="utf-8")
    payload = capture_payload(lab, artifacts, package, report, 0)
    receipt = {"source_before": "a" * 64, "source_after": "a" * 64, "exit_code": 0,
               "output_created_during_build": True, "package": package,
               "log": {"path": str(log), "sha256": sha256(log)}, "windows_payload": payload}
    receipt_path = lab.root / "state/builds" / (package["sha256"] + ".json")
    write_json(receipt_path, receipt)
    run = lab.root / "reports/win11-x64-native/new-fixture"
    run.mkdir(parents=True)
    write_json(run / "protection.json", {"target": "win11-x64-native", "environment_type": "native-host"})
    return SimpleNamespace(lab=lab, artifacts=artifacts, package=package, stage=stage, manifest=manifest,
                           report=report, payload=payload, receipt=receipt, receipt_path=receipt_path, run=run)


def test_new_run_uses_build_snapshot_not_current_staging_or_installed_program(build_fixture):
    f = build_fixture
    bound = prepare(f.lab, f.run, f.package, "a" * 64)
    # 构建下一版后原 staging 可以变化；已经封存的本次清单仍独立可验证。
    (f.stage / "PartyOps.exe").write_bytes(b"different later build")
    (f.stage / "release-manifest.json").write_text("later mutable staging", encoding="utf-8")
    assert verify(f.lab, f.run, "a" * 64) == bound
    assert bound["windows_payload"]["executables"]["PartyOps.exe"]["sha256"] == f.manifest["files"][0]["sha256"]
    assert bound["runtime_environment_passed"] is False


def test_old_receipt_is_preserved_and_cannot_be_backfilled_from_live_staging(build_fixture):
    f = build_fixture
    f.receipt.pop("windows_payload")
    write_json(f.receipt_path, f.receipt)
    original = f.receipt_path.read_bytes()
    with pytest.raises(RuntimeError, match="WINDOWS_BUILD_PAYLOAD_RECEIPT_REQUIRED"):
        prepare(f.lab, f.run, f.package, "a" * 64)
    assert f.receipt_path.read_bytes() == original and not (f.run / "candidate.json").exists()


@pytest.mark.parametrize("mutation,error", [
    ("source", "FINGERPRINT"), ("package", "SHA256_CHANGED"), ("manifest", "MANIFEST_CHANGED"),
    ("exe-receipt", "EXECUTABLE_BINDING_CHANGED"), ("receipt-after-bind", "BINDING_CHANGED"),
    ("candidate", "BINDING_CHANGED"), ("protection", "PROTECTION_CHANGED"), ("old-target", "BINDING_MISMATCH"),
])
def test_preinstall_rejects_changed_source_package_receipt_manifest_or_run(build_fixture, mutation, error):
    f = build_fixture
    prepare(f.lab, f.run, f.package, "a" * 64)
    source = "a" * 64
    if mutation == "source":
        source = "b" * 64
        error = "BINDING_MISMATCH"
    elif mutation == "package":
        Path(f.package["path"]).write_bytes(b"changed")
    elif mutation == "manifest":
        Path(f.payload["manifest"]["path"]).write_text("changed", encoding="utf-8")
    elif mutation in {"exe-receipt", "receipt-after-bind"}:
        if mutation == "exe-receipt":
            f.receipt["windows_payload"]["executables"]["PartyOps.exe"]["sha256"] = "b" * 64
        else:
            f.receipt["extra"] = "receipt replaced"
        write_json(f.receipt_path, f.receipt)
    elif mutation == "candidate":
        candidate = json.loads((f.run / "candidate.json").read_text(encoding="utf-8"))
        candidate["version"] = "old"
        write_json(f.run / "candidate.json", candidate)
    elif mutation == "protection":
        write_json(f.run / "protection.json", {"changed": True})
    else:
        binding = json.loads((f.run / "install-binding.json").read_text(encoding="utf-8"))
        binding["target"] = "win11-x64"
        write_json(f.run / "install-binding.json", binding)
    with pytest.raises(RuntimeError, match=error):
        verify(f.lab, f.run, source)


def test_previous_run_evidence_cannot_be_replaced_by_new_candidate(build_fixture):
    f = build_fixture
    prepare(f.lab, f.run, f.package, "a" * 64)
    original = (f.run / "install-binding.json").read_bytes()
    with pytest.raises(RuntimeError, match="ALREADY_BOUND_USE_NEW_RUN"):
        prepare(f.lab, f.run, f.package, "a" * 64)
    assert (f.run / "install-binding.json").read_bytes() == original


@pytest.mark.parametrize("change", ["old-manifest", "old-exe", "exe-content", "wrong-architecture", "duplicate-case", "path-traversal"])
def test_build_capture_rejects_old_or_mismatching_staging(build_fixture, change):
    f = build_fixture
    started = 1
    if change == "old-manifest":
        os.utime(f.stage / "release-manifest.json", ns=(0, 0))
    elif change == "old-exe":
        os.utime(f.stage / "PartyOps.exe", ns=(0, 0))
    elif change == "exe-content":
        (f.stage / "PartyOps.exe").write_bytes(b"unexpected")
    else:
        if change == "wrong-architecture":
            f.manifest["architecture"] = "x86"
        elif change == "duplicate-case":
            f.manifest["files"].append({**f.manifest["files"][0], "path": "PARTYOPS.EXE"})
        else:
            f.manifest["files"][0]["path"] = "../PartyOps.exe"
        write_json(f.stage / "release-manifest.json", f.manifest)
    with pytest.raises(RuntimeError, match="WINDOWS_PAYLOAD"):
        capture_payload(f.lab, f.artifacts, f.package, f.lab.root / "reports/other-build", started)


def test_expected_payload_requires_two_independent_executable_entries(build_fixture):
    f = build_fixture
    f.manifest["files"] = f.manifest["files"][:1]
    with pytest.raises(RuntimeError, match="REQUIRED_EXECUTABLE_MISSING"):
        manifest_entries(f.manifest, f.package)
    assert expected_payload(f.lab, f.package, "a" * 64)[1] == f.payload


@pytest.mark.parametrize("schema", [True, 1.0, "1"])
def test_manifest_and_receipt_require_integer_schema_not_equal_bool_or_float(build_fixture, schema):
    f = build_fixture
    f.manifest["schema_version"] = schema
    with pytest.raises(RuntimeError, match="MANIFEST_IDENTITY_MISMATCH"):
        manifest_entries(f.manifest, f.package)
    f.receipt["windows_payload"]["schema_version"] = schema
    write_json(f.receipt_path, f.receipt)
    with pytest.raises(RuntimeError, match="PAYLOAD_RECEIPT_REQUIRED"):
        expected_payload(f.lab, f.package, "a" * 64)


@pytest.mark.parametrize("keep_receipt", [False, True])
def test_build_controller_captures_payload_and_never_overwrites_existing_receipt(build_fixture, monkeypatch, keep_receipt):
    f = build_fixture
    spec = importlib.util.spec_from_file_location("native_record_build_test", Path(__file__).resolve().parents[1] / "scripts/record-build.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    existing = f.receipt_path.read_bytes()
    if not keep_receipt:
        Path(f.package["path"]).write_bytes(b"new synthetic installer output")
        f.package.update(sha256=sha256(Path(f.package["path"])), bytes=Path(f.package["path"]).stat().st_size)
    f.lab.initialize = lambda: None
    matrix = {"packages": {"windows_amd64": {}}, "defaults": {"primary_root": str(f.lab.root)}}
    monkeypatch.setattr(module, "load_configuration", lambda: (matrix, {}))
    monkeypatch.setattr(module, "QemuLab", lambda *_: f.lab)
    monkeypatch.setattr(module, "REPO", f.artifacts.parent)
    inventories = iter([({}, {}), ({"windows_amd64": f.package}, {})])
    monkeypatch.setattr(module, "inventory", lambda *_: next(inventories))
    monkeypatch.setattr(module, "fingerprint", lambda: "a" * 64)
    monkeypatch.setattr(module.time, "time_ns", lambda: 0)
    def fake_command(*_args, **kwargs):
        kwargs["stdout"].write(b"synthetic controller command\n")
        return SimpleNamespace(returncode=0)
    monkeypatch.setattr(module.subprocess, "run", fake_command)
    result = module.build(["windows_amd64"], ["synthetic-build-only"])
    assert f.receipt_path.read_bytes() == existing
    if keep_receipt:
        assert result["status"] == "failed" and "EXISTING_BUILD_RECEIPT_PRESERVED:windows_amd64" in result["errors"]
    else:
        assert result["status"] == "completed"
        receipt = json.loads((f.lab.root / "state/builds" / (f.package["sha256"] + ".json")).read_text(encoding="utf-8"))
        assert receipt["windows_payload"]["package_sha256"] == f.package["sha256"]
        assert Path(receipt["windows_payload"]["manifest"]["path"]).read_bytes() == (f.stage / "release-manifest.json").read_bytes()


def test_real_powershell_postinstall_predicate_uses_preinstall_binding_only():
    shell = shutil.which("pwsh")
    if not shell:
        pytest.skip("PowerShell 7 unavailable")
    script = Path(__file__).resolve().parents[1] / "scripts/exercise-windows-native-install.ps1"
    code = r'''
$errors=$null;$tokens=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($args[0],[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'PARSE_FAILED'}
$function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-NativeInstalledCandidate'},$true)
. ([scriptblock]::Create($function.Extent.Text))
$values=@()
foreach($case in @('match','old-exe','self-report','manifest','version','path','exit')){
  $registration=@{DisplayVersion='new';InstallLocation='E:\PartyOps1\PartyOps'}
  $result=@{installer_exit_code=0;installed_executable_sha256='new-exe';installed_manifest_sha256='new-manifest'}
  $binding=@{windows_payload=@{executables=@{'PartyOps.exe'=@{sha256='new-exe'}};manifest=@{sha256='new-manifest'}}}
  switch($case){
    'old-exe' {$result.installed_executable_sha256='old'}
    'self-report' {$result.installed_executable_sha256='self';$result.expected_payload=@{executables=@{'PartyOps.exe'=@{sha256='self'}}}}
    'manifest' {$result.installed_manifest_sha256='old'}
    'version' {$registration.DisplayVersion='old'}
    'path' {$registration.InstallLocation='E:\Other'}
    'exit' {$result.installer_exit_code=1}
  }
  $values+=Test-NativeInstalledCandidate $registration $result @{version='new'} $binding 'E:\PartyOps1\PartyOps'
}
$values|ConvertTo-Json -Compress
'''
    command = subprocess.run([shell, "-NoProfile", "-NonInteractive", "-Command", "& {" + code + "} '" + str(script).replace("'", "''") + "'"], capture_output=True, text=True, check=False)
    assert command.returncode == 0, command.stderr
    assert json.loads(command.stdout) == [True, False, False, False, False, False, False]
