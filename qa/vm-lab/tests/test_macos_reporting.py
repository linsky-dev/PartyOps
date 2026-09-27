"""macOS 库存与真实验收分离：有 VMware 盘或实验 shell 记录都不能自动通过。"""
import json
from pathlib import Path

import pytest
from evidence import write_json
from lab import HERE, check_target, load_configuration
from macos import VMwareMacLab
from providers import QemuLab


@pytest.fixture
def lab(tmp_path, monkeypatch):
    matrix, media = load_configuration()
    matrix["defaults"]["fallback_root"] = str(tmp_path / "fallback")
    result = QemuLab(matrix, tmp_path / "lab", media)
    result.initialize()
    # 只读库存不能启动机器，也不能把 VMware/实验环境交给 QEMU 读取。
    def forbidden(*args, **kwargs):
        raise AssertionError("原版 macOS 报告不得启动机器或调用 QEMU 状态")
    monkeypatch.setattr(VMwareMacLab, "start", forbidden)
    monkeypatch.setattr(QemuLab, "state", forbidden)
    return result


def report(lab, target="macos-x64"):
    package = "macos_x86_64" if target == "macos-x64" else "macos_arm64"
    return check_target(lab, target, {}, {package: "MISSING_PACKAGE"}, None, "a" * 64)


def registered_vm(lab):
    directory = Path(lab.defaults["fallback_root"]) / "vms/macos-x64"
    directory.mkdir(parents=True)
    vmx = directory / "macos.vmx"
    vmx.write_text("fixture", encoding="utf-8")
    recovery = lab.root / "downloads/recovery.vmdk"
    recovery.write_bytes(b"fixture")
    state = {"status": "snapshot_created", "vmx": str(vmx),
             "snapshots": ["installed-baseline-20260902"], "stop_mode": "hard",
             "recovery": {"vmdk": str(recovery), "vmdk_sha256": "b" * 64,
                          "requested_version": "15.7.4", "verified_at": "2026-09-02T12:30:33+08:00"}}
    VMwareMacLab(lab).save(state)
    return state


@pytest.mark.parametrize("running", [True, False])
def test_registered_vmware_uses_own_state_but_stays_unverified(lab, monkeypatch, running):
    registered_vm(lab)
    monkeypatch.setattr(VMwareMacLab, "running", lambda *args: running)
    result = report(lab)
    assert result["vm_status"] == ("running" if running else "stopped")
    assert result["media_status"] == "recovery_verification_recorded"
    assert "MISSING_MEDIA" not in result["errors"]
    assert result["guest_identity_status"] == result["cold_boot_baseline_status"] == "unverified"
    assert {"MISSING_PACKAGE", "FULL_LIFECYCLE_EVIDENCE_MISSING", "MACOS_GUEST_IDENTITY_UNVERIFIED",
            "MACOS_COLD_BOOT_BASELINE_UNVERIFIED"} <= set(result["errors"])
    assert not result["runtime_environment_passed"] and not result["real_environment_passed"]


def test_vm_record_with_missing_vmx_is_not_reported_as_running(lab, monkeypatch):
    state = registered_vm(lab)
    Path(state["vmx"]).unlink()
    monkeypatch.setattr(VMwareMacLab, "running", lambda *args: pytest.fail("丢失 VMX 不应查询运行状态"))
    result = report(lab)
    assert result["vm_status"] == "registered_files_missing"
    assert "MACOS_REGISTERED_VMX_MISSING" in result["errors"]


def test_missing_vmware_record_remains_not_created(lab):
    result = report(lab)
    assert result["vm_status"] == "not_created"
    assert "MACOS_VM_NOT_BOOTSTRAPPED" in result["errors"]


def test_missing_vmrun_does_not_erase_registered_vm(lab, monkeypatch):
    state = registered_vm(lab)
    Path(state["recovery"]["vmdk"]).unlink()
    def unavailable(*args):
        raise RuntimeError("MISSING_VMWARE_TOOL:vmrun")
    monkeypatch.setattr(VMwareMacLab, "running", unavailable)
    result = report(lab)
    assert result["vm_status"] == "registered_runtime_unknown"
    assert "MACOS_RECOVERY_EVIDENCE_MISSING" in result["errors"]
    assert "MACOS_VM_STATUS_UNAVAILABLE:MISSING_VMWARE_TOOL:vmrun" in result["errors"]


@pytest.mark.parametrize("record_kind", ["valid", "stale", "malformed", "missing"])
def test_arm_experiment_is_separate_from_qemu_and_never_full_pass(lab, record_kind):
    if record_kind != "missing":
        config = json.loads((HERE / "config/macos-arm64.json").read_text(encoding="utf-8"))
        if record_kind == "stale":
            config["device"] = "iPhone16,1"
        record = {"config": config, "generated_at": "2026-09-06T10:03:49+08:00",
                  "stages": {"kernel": "passed", "userspace": "not_run", "full_lifecycle": "blocked"},
                  "errors": ["MACOS_FIRMWARE_COMPONENT_MISSING:ramdisk.tc"]}
        write_json(lab.root / "reports/macos-arm64/local-experiment.json", [] if record_kind == "malformed" else record)
    result = report(lab, "macos-arm64")
    assert "MACOS_ARM64_DESKTOP_AND_PKG_LIFECYCLE_UNAVAILABLE" in result["errors"]
    assert "MISSING_MEDIA" not in result["errors"]
    assert not result["runtime_environment_passed"] and not result["real_environment_passed"]
    if record_kind == "valid":
        assert result["vm_status"] == "experiment_recorded"
        assert result["recorded_experiment_stages"]["kernel"] == "passed"
        assert "MACOS_FIRMWARE_COMPONENT_MISSING:ramdisk.tc" in result["errors"]
    else:
        assert result["vm_status"] == "experiment_not_recorded"
        assert "recorded_experiment_stages" not in result
