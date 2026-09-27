"""龙架构真实配置路径及 UEFI 快照反例；只用临时合成盘，不启动 QEMU。"""
import copy
import importlib.util
import json
import os
from pathlib import Path

import evidence
import firmware
import identity
import providers
import pytest
from execution import Journal
from lab import HERE, inventory, load_configuration
from providers import QemuLab

TARGET = "deepin-deb-loong64"


@pytest.fixture
def loong(tmp_path, monkeypatch):
    matrix, media = load_configuration()
    matrix["defaults"].update(primary_root=str(tmp_path / "lab"), fallback_root=str(tmp_path / "fallback"),
                              qemu_home=str(tmp_path / "qemu"), reserve_primary_gib=0, reserve_fallback_gib=0)
    lab = QemuLab(matrix, tmp_path / "lab", media)
    lab.initialize()
    share = lab.qemu_home / "share"
    share.mkdir(parents=True)
    config = matrix["targets"][TARGET]["firmware"]
    for field, payload in (("code", b"synthetic firmware code"), ("vars_template", b"synthetic blank vars")):
        path = lab.qemu_home / config[field]
        path.write_bytes(payload)
        config[field + "_sha256"] = evidence.sha256(path)
    for binary in ("qemu-img", "qemu-system-loongarch64"):
        (lab.qemu_home / (binary + (".exe" if os.name == "nt" else ""))).touch()
    iso = tmp_path / "synthetic.iso"
    iso.write_bytes(b"synthetic original ISO")
    media[matrix["targets"][TARGET]["media"]]["sha256"] = evidence.sha256(iso)
    directory = lab.vm_dir(TARGET)
    directory.mkdir(parents=True)
    disk = directory / "system.qcow2"
    disk.write_bytes(b"synthetic qcow2")
    state = {"uuid": "synthetic-loong-uuid", "target": TARGET, "disk": str(disk), "base": str(iso),
             "source_sha256": evidence.sha256(iso), "snapshots": [], "status": "created", "pid": None,
             "qmp_port": 12403, "vnc_display": 80, "temporary": True,
             "firmware": firmware.prepare(lab, TARGET)}
    lab.save(TARGET, state)
    calls = []
    def qemu_command(args, **kwargs):
        calls.append(args)
        if "info" in args:
            return json.dumps({"snapshots": [{"id": name, "name": name, "date-sec": 1, "date-nsec": 2, "vm-state-size": 0}
                                              for name in lab.state(TARGET)["snapshots"]]})
        return ""
    monkeypatch.setattr(lab, "live", lambda state: False)
    monkeypatch.setattr(lab, "process_active", lambda state: False)
    monkeypatch.setattr(providers, "run", qemu_command)
    return lab, state, calls


def system_fixture(state):
    return {"os": "linux", "arch": "loongarch64", "os_release": "25", "os_build": "25.2.0",
            "distribution": "Deepin", "distribution_id": "deepin", "package_arch": "loong64", "uid": 1000,
            "boot_id": "real-kernel-boot-after", "hardware_uuid": state["uuid"], "installed_package": False,
            "marker": json.dumps({"uuid": state["uuid"], "purpose": "disposable-qa"}),
            "environment_type": "full-system-emulated"}


def seal_fixture(lab):
    state = lab.snapshot(TARGET, "clean-original")
    path = lab.vm_dir(TARGET) / "baseline-identity.json"
    evidence.write_json(path, system_fixture(state))
    state.update(installation_media_detached=True, clean_baseline={
        "name": "clean-original", "id": "synthetic-baseline", "boot_id_before": "real-kernel-boot-before",
        "cold_boot_verified": True, "identity_sha256": evidence.sha256(path),
        "snapshot_record": lab.snapshot_record(TARGET, "clean-original")})
    lab.save(TARGET, state)
    return state


def change_vars(state):
    path = Path(state["firmware"]["vars"])
    value = path.read_bytes()
    path.write_bytes(bytes([value[0] ^ 1]) + value[1:])


def test_loongarch_is_independent_and_missing_package_remains_blocked(tmp_path):
    matrix, media = load_configuration()
    spec = matrix["targets"][TARGET]
    assert spec["arch"] == "loongarch64" and spec["package_arch"] == "loong64"
    assert spec["distribution_id"] == "deepin" and spec["os_build"] == "25.2.0"
    assert spec["ssh_port"] == 22403 and spec["vm_storage"] == "fallback"
    assert media[spec["media"]]["sha256"] == "1835726765d5839481572dbdaf8b8fdb5b2403ed8395cbe06f8d509891fb46c1"
    assert matrix["packages"]["linux_loong64"]["required_targets"] == [TARGET]
    assert matrix["packages"]["linux_amd64"]["required_targets"] == ["uos-deb-x64"]
    packages, errors = inventory(matrix, tmp_path)
    assert errors["linux_loong64"] == "MISSING_PACKAGE"
    summary = evidence.aggregate(matrix, packages, {})
    assert summary["required_packages"] == len(matrix["packages"]) >= 10
    assert summary["passed_packages"] == 0 and summary["all_packages_runtime_gate"] == "blocked"


def test_command_uses_loongarch_pflash_virtio_and_private_vars(loong):
    lab, state, _ = loong
    command = lab.command(TARGET, state, "tcg", False)
    joined = " ".join(command)
    assert Path(command[0]).name.startswith("qemu-system-loongarch64")
    assert command[command.index("-machine") + 1] == "virt-11.1"
    assert command[command.index("-cpu") + 1] == "la464"
    assert command[command.index("-accel") + 1] == "tcg"
    assert "if=pflash,unit=0,format=raw,readonly=on,file=" + state["firmware"]["code"] in command
    assert "if=pflash,unit=1,format=raw,file=" + state["firmware"]["vars"] in command
    assert "-bios" not in command and "q35" not in command and "qemu-system-x86_64" not in joined
    for device in ("virtio-blk-pci,drive=osdisk,bootindex=1", "virtio-gpu-pci", "qemu-xhci", "usb-kbd", "usb-tablet",
                   "virtio-scsi-pci,id=scsi0", "scsi-cd,drive=cd0,bus=scsi0.0,bootindex=0", "virtio-net-pci,netdev=net0"):
        assert device in command
    assert "restrict=on,hostfwd=tcp:127.0.0.1:22403-:22" in joined
    state["installation_media_detached"] = True
    detached = lab.command(TARGET, state, "tcg", False)
    assert "scsi-cd" not in " ".join(detached) and "-boot" not in detached


def test_wrong_accelerator_or_unknown_arch_never_falls_back_to_x86(loong):
    lab, state, _ = loong
    with pytest.raises(RuntimeError, match="LOONGARCH_FULL_SYSTEM_REQUIRES_TCG"):
        lab.command(TARGET, state, "whpx", False)
    lab.matrix["targets"][TARGET]["arch"] = "mips64el"
    with pytest.raises(RuntimeError, match="UNSUPPORTED_GUEST_ARCHITECTURE"):
        lab.command(TARGET, state, "tcg", False)


@pytest.mark.parametrize("mutation,error", [("missing", "FIRMWARE_MISSING"), ("code", "FIRMWARE_HASH_MISMATCH"),
                                           ("shared", "VARS_IDENTITY_MISMATCH"), ("size", "VARS_SIZE_CHANGED")])
def test_firmware_failures_are_not_repaired_silently(loong, mutation, error):
    lab, state, _ = loong
    if mutation == "missing":
        Path(state["firmware"]["code"]).unlink()
    elif mutation == "code":
        Path(state["firmware"]["code"]).write_bytes(b"changed")
    elif mutation == "shared":
        # 同一 VM 内的另一文件也不能冒充精确登记的变量盘。
        another = lab.vm_dir(TARGET) / "shared.fd"
        another.write_bytes(Path(state["firmware"]["vars"]).read_bytes())
        state["firmware"]["vars"] = str(another)
    else:
        Path(state["firmware"]["vars"]).write_bytes(b"short")
    with pytest.raises(RuntimeError, match=error):
        lab.command(TARGET, state, "tcg", False)


def test_stopped_vars_tampering_blocks_start_before_memory_or_process(loong, monkeypatch):
    lab, state, _ = loong
    change_vars(state)
    monkeypatch.setattr(providers.subprocess, "Popen", lambda *a, **k: pytest.fail("不得启动"))
    with pytest.raises(RuntimeError, match="VARS_CHANGED_WHILE_STOPPED"):
        lab.start(TARGET)


def test_snapshot_preserves_and_restores_vars_together_with_disk(loong):
    lab, state, calls = loong
    initial = Path(state["firmware"]["vars"]).read_bytes()
    state = lab.snapshot(TARGET, "clean-original")
    assert firmware.snapshot_path(lab, TARGET, "clean-original").read_bytes() == initial
    change_vars(state)
    restored = lab.snapshot(TARGET, "clean-original", restore=True)
    assert Path(restored["firmware"]["vars"]).read_bytes() == initial
    assert restored["restore_generation"] and not restored.get("firmware_restore_pending")
    assert any("-c" in call for call in calls) and any("-a" in call for call in calls)
    assert Path(state["firmware"]["vars_template"]).read_bytes() == initial


def test_partial_restore_blocks_boot_and_can_resume_same_verified_snapshot(loong, monkeypatch):
    lab, _state, _calls = loong
    lab.snapshot(TARGET, "clean-original")
    restore = firmware.restore_snapshot
    def failed(*args):
        raise OSError("synthetic interrupted vars restore")
    monkeypatch.setattr(firmware, "restore_snapshot", failed)
    with pytest.raises(OSError):
        lab.snapshot(TARGET, "clean-original", restore=True)
    with pytest.raises(RuntimeError, match="UEFI_SNAPSHOT_RESTORE_INCOMPLETE"):
        lab.command(TARGET, lab.state(TARGET), "tcg", False)
    monkeypatch.setattr(firmware, "restore_snapshot", restore)
    assert not lab.snapshot(TARGET, "clean-original", restore=True).get("firmware_restore_pending")


@pytest.mark.parametrize("mutation,error", [("old", "BINDING_MISSING"), ("changed", "SNAPSHOT_VARS_CHANGED")])
def test_old_or_changed_firmware_snapshot_cannot_bind(loong, mutation, error):
    lab, _state, _ = loong
    state = seal_fixture(lab)
    if mutation == "old":
        state.pop("firmware_snapshots")
        lab.save(TARGET, state)
    else:
        firmware.snapshot_path(lab, TARGET, "clean-original").write_bytes(b"tampered")
    with pytest.raises(RuntimeError, match=error):
        identity.runtime_binding(lab, TARGET)


def test_completed_boot_vars_are_checkpointed_and_invalidate_resume(loong):
    lab, _state, _ = loong
    state = seal_fixture(lab)
    first = identity.runtime_binding(lab, TARGET)
    journal = Journal(lab, TARGET, {"environment": first}, False)
    journal.step("diagnostic", lambda: {"synthetic": True})
    change_vars(state)
    state.update(pid=1234, started_at=evidence.now(), status="running")
    lab.save(TARGET, state)
    identity.wait_stopped(lab, TARGET, 1)
    second = identity.runtime_binding(lab, TARGET)
    assert first["baseline_vars_sha256"] == second["baseline_vars_sha256"]
    assert first["firmware_vars_sha256"] != second["firmware_vars_sha256"]
    resumed = Journal(lab, TARGET, {"environment": second}, True)
    assert resumed.data["resume_invalidated"] and not resumed.reused


def test_loong_identity_reads_actual_distribution_version_and_package_arch(loong):
    lab, state, _ = loong
    system = system_fixture(state)
    system.update(arch="loong64", os_release_raw='ID=deepin\nNAME=Deepin\nVERSION_ID=25',
                  os_version_raw='MinorVersion=25.2.0')
    actual = identity.normalize_linux(system)
    assert actual["arch"] == "loongarch64" and actual["os_build"] == "25.2.0"
    identity.validate_identity(actual, lab.matrix["targets"][TARGET], state)
    for field, value in (("package_arch", "amd64"), ("arch", "aarch64"), ("os_build", "25.1.0"), ("distribution_id", "uos")):
        with pytest.raises(RuntimeError, match="MISMATCH"):
            identity.validate_identity({**actual, field: value}, lab.matrix["targets"][TARGET], state)
    missing = identity.normalize_linux({"os": "linux", "arch": "loongarch64", "os_build": "25.2.0", "os_release_raw": 'ID=deepin\nVERSION_ID=25'})
    assert "os_build" not in missing
    with pytest.raises(RuntimeError, match="os_build"):
        identity.validate_identity(missing, lab.matrix["targets"][TARGET], state)
    with pytest.raises(RuntimeError, match="VERSION_FIELDS_CONFLICT"):
        identity.normalize_linux({"os_release_raw": 'ID=deepin\nVERSION_ID=25.1.0', "os_version_raw": 'MinorVersion=25.2.0'})


def test_complete_evidence_requires_all_four_firmware_bindings(loong, tmp_path):
    lab, _state, _ = loong
    state = seal_fixture(lab)
    binding = identity.runtime_binding(lab, TARGET)
    log = tmp_path / "synthetic.log"
    log.write_text("合成测试证据，禁止导入真实验收。", encoding="utf-8")
    package = {"id": "linux_loong64", "sha256": "a" * 64, "version": lab.matrix["version"],
               "provenance_status": "verified", "source_fingerprint": "b" * 64}
    result = {"schema_version": 2, "run_id": "synthetic-only", "generated_at": evidence.now(), "target": TARGET,
              "status": "passed", "source_fingerprint": "b" * 64, "package": package, "system": system_fixture(state),
              "environment": dict(binding), "distribution_match": True,
              "restart": {"actual": True, "boot_id_before": "real-kernel-boot-after", "boot_id_after": "next-kernel-boot"},
              "cases": [{"id": case, "status": "passed", "evidence": [{"path": log.name, "sha256": evidence.sha256(log)}]}
                        for case in lab.matrix["required_cases"]]}
    schema = json.loads((HERE / "schemas/result.schema.json").read_text())
    target = {**lab.matrix["targets"][TARGET], "runtime_binding": binding}
    def check():
        return evidence.evaluate(result, target, package, lab.matrix["required_cases"], tmp_path, schema, "b" * 64)
    assert check() == []
    for field in firmware.FIELDS:
        result["environment"].pop(field)
        assert "RUNTIME_BINDING_MISMATCH: " + field in check()
        result["environment"][field] = binding[field]
    result["system"]["arch"] = "x86_64"
    assert "GUEST_OS_ISA_MISMATCH" in check()


def test_build_clone_copies_snapshot_vars_and_keeps_source_independent(loong, monkeypatch):
    lab, _state, _ = loong
    original = seal_fixture(lab)
    target = "build-" + TARGET
    lab.matrix["targets"][target] = copy.deepcopy(lab.matrix["targets"][TARGET])
    spec = importlib.util.spec_from_file_location("loong_build_test", HERE / "scripts/linux-build-guest.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    def run(args, **kwargs):
        if "convert" in args:
            Path(args[-1]).write_bytes(b"synthetic independent qcow2")
        if "info" in args:
            return '{"format":"qcow2"}'
        return ""
    monkeypatch.setattr(module, "run", run)
    cloned = module.create(lab, TARGET, target)
    assert Path(cloned["firmware"]["vars"]) != Path(original["firmware"]["vars"])
    assert evidence.sha256(Path(cloned["firmware"]["vars"])) == original["firmware_snapshots"]["clean-original"]["vars_sha256"]
    change_vars(cloned)
    assert evidence.sha256(Path(original["firmware"]["vars"])) == original["firmware"]["vars_sha256"]
