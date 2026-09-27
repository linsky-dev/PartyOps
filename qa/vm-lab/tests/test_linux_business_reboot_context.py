"""业务链只接受同一已完成真实重启导致的单次 UEFI vars 变化。"""
from __future__ import annotations

import copy
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace

import pytest

from business_reboot_context import validate_reboot_transition
from evidence import sha256

BOOT_BEFORE = "11111111-1111-4111-8111-111111111111"
BOOT_AFTER = "22222222-2222-4222-8222-222222222222"


def _write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value), encoding="utf-8")
    return sha256(path)


@pytest.fixture
def proof(tmp_path):
    target = "deepin-deb-loong64"
    reports = tmp_path / "reports" / target
    run = reports / "run-example"
    business = reports / "business-business-example"
    guest_script = tmp_path / "linux-business-lifecycle.py"
    guest_script.write_text("print('example')", encoding="utf-8")
    old = {"target": target, "package": {"id": "linux_loong64", "sha256": "a" * 64,
                                          "source_fingerprint": "b" * 64,
                                          "build_receipt_sha256": "c" * 64},
           "environment": {"vm_uuid": "vm-example", "baseline_id": "baseline-example",
                           "media_sha256": "d" * 64, "baseline_vars_sha256": "e" * 64,
                           "firmware_vars_sha256": "1" * 64},
           "restore_generation": "generation-example"}
    current = copy.deepcopy(old)
    current["environment"]["firmware_vars_sha256"] = "2" * 64
    previous = {"context": old, "remote": "/home/partyopsqa/lifecycle-example",
                "last_phase": "business"}
    prior = {**old, "remote": previous["remote"], "script_sha256": sha256(guest_script),
             "guest_identity": {"boot_id": BOOT_BEFORE, "hardware_uuid": "vm-example"}}
    context_hash = _write(business / "context.json", prior)
    business_result = {"target": target, "phase": "business", "exit_code": 0,
                       "report_path": str(business)}
    business_hash = _write(run / "business-business.json", business_result)
    reboot = {"target": target, "status": "passed", "actual": True,
              "method": "guest-systemctl-reboot", "boot_id_before": BOOT_BEFORE,
              "boot_id_after": BOOT_AFTER}
    reboot_hash = _write(run / "actual-guest-reboot.json", reboot)
    _write(reports / ("reboot-" + BOOT_BEFORE + ".json"), reboot)
    initial = copy.deepcopy(old)
    initial["environment"]["firmware_vars_sha256"] = "0" * 64
    execution = {"target": target, "run_id": run.name, "context": initial,
                 "steps": {"business-business": {"status": "completed", "path": "business-business.json",
                                                 "sha256": business_hash,
                                                 "dependencies": {"context.json": context_hash}},
                           "actual-guest-reboot": {"status": "completed", "path": "actual-guest-reboot.json",
                                                   "sha256": reboot_hash}}}
    path = run / "execution.json"
    _write(path, execution)
    _write(tmp_path / "state" / ("run-" + target + ".json"),
           {"directory": str(run), "context": initial, "status": "blocked"})
    system = {"boot_id": BOOT_AFTER, "hardware_uuid": "vm-example"}
    return tmp_path, target, previous, current, system, path, guest_script


def _validate(proof):
    return validate_reboot_transition(*proof)


def test_same_business_chain_accepts_only_recorded_reboot_firmware_change(proof):
    result = _validate(proof)
    assert result["firmware_vars_transition"] == "changed"
    assert result["old_firmware_vars_sha256"] == "1" * 64
    assert result["new_firmware_vars_sha256"] == "2" * 64
    assert result["boot_id_before"] == BOOT_BEFORE
    assert result["boot_id_after"] == BOOT_AFTER
    assert result["execution_sha256"] == sha256(proof[5])


def _remove_firmware_from_both_sides(proof):
    proof[2]["context"]["environment"].pop("firmware_vars_sha256")
    proof[3]["environment"].pop("firmware_vars_sha256")
    business_path = proof[5].parent.parent / "business-business-example" / "context.json"
    prior = json.loads(business_path.read_text(encoding="utf-8"))
    prior["environment"].pop("firmware_vars_sha256")
    dependency_hash = _write(business_path, prior)
    execution = json.loads(proof[5].read_text(encoding="utf-8"))
    execution["context"]["environment"].pop("firmware_vars_sha256")
    execution["steps"]["business-business"]["dependencies"]["context.json"] = dependency_hash
    _write(proof[5], execution)
    pointer = proof[0] / "state" / ("run-" + proof[1] + ".json")
    reference = json.loads(pointer.read_text(encoding="utf-8"))
    reference["context"] = execution["context"]
    _write(pointer, reference)


def test_no_firmware_target_still_requires_real_reboot_proof(proof):
    _remove_firmware_from_both_sides(proof)
    result = _validate(proof)
    assert result["firmware_vars_transition"] == "not-applicable"
    assert "old_firmware_vars_sha256" not in result
    assert "new_firmware_vars_sha256" not in result
    assert result["boot_id_before"] == BOOT_BEFORE
    assert result["boot_id_after"] == BOOT_AFTER
    reboot = proof[5].parent / "actual-guest-reboot.json"
    reboot.unlink()
    with pytest.raises(RuntimeError, match="PROOF_STEP_CHANGED"):
        _validate(proof)


@pytest.mark.parametrize("side", ["old", "new"])
def test_single_sided_missing_firmware_hash_is_rejected(proof, side):
    context = proof[2]["context"] if side == "old" else proof[3]
    context["environment"].pop("firmware_vars_sha256")
    with pytest.raises(RuntimeError, match="FIRMWARE_HASH_PRESENCE_MISMATCH|BUSINESS_CONTEXT_CHANGED"):
        _validate(proof)


@pytest.mark.parametrize("value", ["", "not-a-hash", "A" * 64])
def test_invalid_firmware_hash_is_rejected(proof, value):
    proof[3]["environment"]["firmware_vars_sha256"] = value
    with pytest.raises(RuntimeError, match="FIRMWARE_HASH_INVALID"):
        _validate(proof)


@pytest.mark.parametrize("part,key,value", [
    ("package", "sha256", "f" * 64),
    ("package", "build_receipt_sha256", "f" * 64),
    ("environment", "vm_uuid", "another-vm"),
    ("environment", "baseline_id", "another-baseline"),
    ("environment", "media_sha256", "f" * 64),
    ("root", "restore_generation", "another-generation"),
])
def test_other_context_changes_are_rejected(proof, part, key, value):
    current = proof[3]
    if part == "root":
        current[key] = value
    else:
        current[part][key] = value
    with pytest.raises(RuntimeError, match="BUSINESS_CONTEXT_CHANGED"):
        _validate(proof)


def test_missing_or_changed_reboot_proof_is_rejected(proof):
    execution_path = proof[5]
    execution = json.loads(execution_path.read_text(encoding="utf-8"))
    del execution["steps"]["actual-guest-reboot"]
    _write(execution_path, execution)
    with pytest.raises(RuntimeError, match="PROOF_STEP_MISSING"):
        _validate(proof)


@pytest.mark.parametrize("field,value", [("actual", False), ("boot_id_after", BOOT_BEFORE),
                                          ("target", "other-target")])
def test_fake_or_unrelated_reboot_is_rejected(proof, field, value):
    reboot = proof[5].parent / "actual-guest-reboot.json"
    payload = json.loads(reboot.read_text(encoding="utf-8"))
    payload[field] = value
    digest = _write(reboot, payload)
    execution = json.loads(proof[5].read_text(encoding="utf-8"))
    execution["steps"]["actual-guest-reboot"]["sha256"] = digest
    _write(proof[5], execution)
    with pytest.raises(RuntimeError, match="PROOF_INVALID|BOOT_IDENTITY_MISMATCH|RAW_PROOF_CHANGED"):
        _validate(proof)


def test_remote_script_and_single_use_are_bound(proof):
    previous = proof[2]
    previous["remote"] = "/home/partyopsqa/another-lifecycle"
    with pytest.raises(RuntimeError, match="BUSINESS_CONTEXT_MISMATCH"):
        _validate(proof)
    previous["remote"] = "/home/partyopsqa/lifecycle-example"
    proof[6].write_text("print('changed')", encoding="utf-8")
    with pytest.raises(RuntimeError, match="BUSINESS_CONTEXT_MISMATCH"):
        _validate(proof)
    previous["last_phase"] = "after-reboot"
    with pytest.raises(RuntimeError, match="BUSINESS_CONTEXT_CHANGED"):
        _validate(proof)


def test_proof_outside_target_report_root_is_rejected(proof, tmp_path):
    outside = tmp_path / "unrelated" / "execution.json"
    _write(outside, json.loads(proof[5].read_text(encoding="utf-8")))
    with pytest.raises(RuntimeError, match="PROOF_PATH_INVALID"):
        validate_reboot_transition(*proof[:5], outside, proof[6])


def test_proof_from_unreferenced_journal_is_rejected(proof):
    pointer = proof[0] / "state" / ("run-" + proof[1] + ".json")
    reference = json.loads(pointer.read_text(encoding="utf-8"))
    reference["directory"] = str(proof[0] / "reports" / proof[1] / "run-other")
    _write(pointer, reference)
    with pytest.raises(RuntimeError, match="JOURNAL_POINTER_MISMATCH"):
        _validate(proof)


@pytest.mark.parametrize("exit_code,advanced", [(2, False), (0, True)])
def test_after_reboot_pointer_advances_only_after_guest_success(tmp_path, monkeypatch, exit_code, advanced):
    module_path = Path(__file__).resolve().parents[1] / "scripts/exercise-linux-business.py"
    spec = importlib.util.spec_from_file_location("business_reboot_test", module_path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    target = "deepin-deb-loong64"
    package = {"id": "linux_loong64", "sha256": "a" * 64}
    old_context = {"target": target, "package": package,
                   "environment": {"vm_uuid": "vm", "firmware_vars_sha256": "1" * 64},
                   "restore_generation": "generation"}
    old_pointer = {"context": old_context, "remote": "/home/partyopsqa/lifecycle-example",
                   "last_phase": "business", "updated_at": "before"}
    pointer = tmp_path / "state" / ("business-" + target + ".json")
    _write(pointer, old_pointer)

    class FakeSftp:
        def __enter__(self):
            return self

        def __exit__(self, *_args):
            pass

        def put(self, *_args):
            pass

        def listdir_attr(self, *_args):
            return []

    class FakeClient:
        def load_host_keys(self, *_args):
            pass

        def set_missing_host_key_policy(self, *_args):
            pass

        def connect(self, *_args, **_kwargs):
            pass

        def open_sftp(self):
            return FakeSftp()

        def exec_command(self, *_args, **_kwargs):
            output = SimpleNamespace(read=lambda: b"", channel=SimpleNamespace(recv_exit_status=lambda: exit_code))
            return None, output, SimpleNamespace(read=lambda: b"")

        def close(self):
            pass

    lab = SimpleNamespace(root=tmp_path, matrix={"targets": {target: {"os": "linux", "ssh_port": 1}},
                          "packages": {"linux_loong64": {"required_targets": [target]}}},
                          state=lambda _target: {"restore_generation": "generation"})
    monkeypatch.setattr(module, "probe", lambda *_args: {"installed_package": True})
    monkeypatch.setattr(module, "inventory", lambda *_args: ({"linux_loong64": package}, []))
    monkeypatch.setattr(module, "bind_package", lambda *_args: package)
    monkeypatch.setattr(module, "fingerprint", lambda: "source")
    monkeypatch.setattr(module, "runtime_binding", lambda *_args: {"vm_uuid": "vm", "firmware_vars_sha256": "2" * 64})
    monkeypatch.setattr(module, "validate_reboot_transition", lambda *_args: {"old": "1", "new": "2"})
    monkeypatch.setattr(module.paramiko, "SSHClient", FakeClient)
    result = module.exercise(target, "after-reboot", lab=lab, reboot_execution=tmp_path / "execution.json")
    assert result["exit_code"] == exit_code
    saved = json.loads(pointer.read_text(encoding="utf-8"))
    assert (saved["last_phase"] == "after-reboot") is advanced
    assert (saved["context"]["environment"]["firmware_vars_sha256"] == "2" * 64) is advanced
