"""macOS 恢复介质校验与路径约束测试。"""
import hashlib
import json
import struct
import zipfile
from pathlib import Path
from types import SimpleNamespace

import macos as macos_module
import psutil
import pytest
from evidence import sha256
from lab import load_configuration
from macos import VMwareMacLab, verify_apple_recovery
from providers import QemuLab


def signed_digest_chunklist(payload: bytes) -> bytes:
    header_format = "<4sIBBBBQQQ"
    header_size = struct.calcsize(header_format)
    descriptor = struct.pack("<I", len(payload)) + hashlib.sha256(payload).digest()
    signature_offset = header_size + len(descriptor)
    header = struct.pack(header_format, b"CNKL", header_size, 1, 1, 2, 0, 1, header_size, signature_offset)
    signed = header + descriptor
    return signed + hashlib.sha256(signed).digest()


def empty_chunklist(signature_method: int, signature: bytes, *, magic=b"CNKL", chunk_offset=None, signature_offset=None) -> bytes:
    header_format = "<4sIBBBBQQQ"
    header_size = struct.calcsize(header_format)
    chunk_offset = header_size if chunk_offset is None else chunk_offset
    signature_offset = header_size if signature_offset is None else signature_offset
    header = struct.pack(header_format, magic, header_size, 1, 1, signature_method, 0, 0, chunk_offset, signature_offset)
    return header + signature


def test_verify_apple_recovery_digest_chunklist(tmp_path):
    payload = b"Apple recovery fixture only"
    dmg = tmp_path / "recovery.dmg"
    chunklist = tmp_path / "recovery.chunklist"
    dmg.write_bytes(payload)
    chunklist.write_bytes(signed_digest_chunklist(payload))
    result = verify_apple_recovery(dmg, chunklist)
    assert result["verified_bytes"] == len(payload)
    assert result["signature_method"] == 2


def test_verify_apple_recovery_rejects_changed_image(tmp_path):
    dmg = tmp_path / "recovery.dmg"
    chunklist = tmp_path / "recovery.chunklist"
    chunklist.write_bytes(signed_digest_chunklist(b"expected"))
    dmg.write_bytes(b"tampered")
    with pytest.raises(RuntimeError, match="HASH_MISMATCH"):
        verify_apple_recovery(dmg, chunklist)


@pytest.mark.parametrize(
    "payload,error",
    [
        (b"short", "CHUNKLIST_TRUNCATED"),
        (empty_chunklist(2, b"0" * 32, magic=b"FAIL"), "INVALID_HEADER"),
        (empty_chunklist(1, b"0"), "INVALID_SIGNATURE_SIZE"),
        (empty_chunklist(1, b"0" * 256), "SIGNATURE_INVALID"),
        (empty_chunklist(2, b"0" * 32), "DIGEST_INVALID"),
        (empty_chunklist(3, b"0" * 32), "METHOD_UNSUPPORTED"),
    ],
)
def test_verify_apple_recovery_rejects_invalid_chunklists(tmp_path, payload, error):
    dmg = tmp_path / "recovery.dmg"
    chunklist = tmp_path / "recovery.chunklist"
    dmg.write_bytes(b"")
    chunklist.write_bytes(payload)
    with pytest.raises(RuntimeError, match=error):
        verify_apple_recovery(dmg, chunklist)


def test_verify_apple_recovery_rejects_invalid_offsets_and_sizes(tmp_path):
    dmg = tmp_path / "recovery.dmg"
    chunklist = tmp_path / "recovery.chunklist"
    header_size = struct.calcsize("<4sIBBBBQQQ")
    # 声明一个描述符，但签名紧跟头部，偏移必然重叠。
    chunklist.write_bytes(struct.pack("<4sIBBBBQQQ", b"CNKL", header_size, 1, 1, 2, 0, 1, header_size, header_size))
    dmg.write_bytes(b"")
    with pytest.raises(RuntimeError, match="INVALID_OFFSETS"):
        verify_apple_recovery(dmg, chunklist)
    chunklist.write_bytes(signed_digest_chunklist(b"expected"))
    dmg.write_bytes(b"short")
    with pytest.raises(RuntimeError, match="CHUNK_TRUNCATED"):
        verify_apple_recovery(dmg, chunklist)
    dmg.write_bytes(b"expected-extra")
    with pytest.raises(RuntimeError, match="LARGER_THAN_CHUNKLIST"):
        verify_apple_recovery(dmg, chunklist)


def test_recovery_paths_keep_full_version(tmp_path):
    matrix, media = load_configuration()
    matrix["defaults"]["fallback_root"] = str(tmp_path / "active")
    lab = QemuLab(matrix, tmp_path / "lab", media)
    mac = VMwareMacLab(lab)
    names = [path.name for path in mac.recovery_paths()]
    assert names == ["macos-15.7.4-recovery.dmg", "macos-15.7.4-recovery.chunklist", "macos-15.7.4-recovery.vmdk"]


def test_vmware_vmx_is_discovered_in_x64_directory(tmp_path):
    matrix, media = load_configuration()
    lab = QemuLab(matrix, tmp_path / "lab", media)
    executable = lab.root / "tools/VMware/x64/vmware-vmx.exe"
    executable.parent.mkdir(parents=True)
    executable.write_bytes(b"fixture")
    assert VMwareMacLab(lab).vmware_tool("vmware-vmx") == executable.resolve()


def make_mac_lab(tmp_path):
    matrix, media = load_configuration()
    matrix["defaults"].update(fallback_root=str(tmp_path / "active"), qemu_home=str(tmp_path / "qemu"))
    lab = QemuLab(matrix, tmp_path / "lab", media)
    lab.initialize()
    lab.qemu_home.mkdir()
    (lab.qemu_home / "qemu-img.exe").touch()
    return lab, VMwareMacLab(lab)


def write_zip(path, entries):
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w") as archive:
        for name, payload in entries.items():
            archive.writestr(name, payload)


def test_template_manifest_verifies_archive_and_extraction(tmp_path, monkeypatch):
    _lab, mac = make_mac_lab(tmp_path)
    names = ("macos.nvram", "macos.plist", "macos.vmdk", "macos.vmx", "opencore.iso", "opencore.vmdk")
    entries = {f"oc4vm/vmware/intel/{name}": name.encode() for name in names}
    write_zip(mac.oc4vm_archive, entries)
    template = mac.oc4vm_root / "vmware/intel"
    template.mkdir(parents=True)
    for name in names:
        (template / name).write_bytes(entries[f"oc4vm/vmware/intel/{name}"])
    monkeypatch.setattr(macos_module, "OC4VM_ARCHIVE_SHA256", sha256(mac.oc4vm_archive))
    result = mac.template_manifest()
    assert set(result["files"]) == set(names)
    (template / "macos.vmx").write_bytes(b"changed")
    with pytest.raises(RuntimeError, match="EXTRACTED_FILE_HASH_MISMATCH"):
        mac.template_manifest()


def test_verified_archive_rejects_missing_and_changed(tmp_path):
    _lab, mac = make_mac_lab(tmp_path)
    with pytest.raises(RuntimeError, match="MISSING_FIXTURE_ARCHIVE"):
        mac._verified_archive(tmp_path / "missing.zip", "0" * 64, "FIXTURE")
    changed = tmp_path / "changed.zip"
    changed.write_bytes(b"changed")
    with pytest.raises(RuntimeError, match="HASH_MISMATCH"):
        mac._verified_archive(changed, "0" * 64, "FIXTURE")


def test_extracted_file_rejects_missing_and_ambiguous_entries(tmp_path):
    _lab, mac = make_mac_lab(tmp_path)
    archive = tmp_path / "archive.zip"
    extracted = tmp_path / "tool.exe"
    write_zip(archive, {"one/tool.exe": b"one", "two/tool.exe": b"two"})
    with pytest.raises(RuntimeError, match="MISSING_EXTRACTED_FILE"):
        mac._verify_extracted_file(archive, "tool.exe", extracted)
    extracted.write_bytes(b"one")
    with pytest.raises(RuntimeError, match="ARCHIVE_ENTRY_AMBIGUOUS"):
        mac._verify_extracted_file(archive, "tool.exe", extracted)


def test_download_recovery_reuses_and_independently_verifies(tmp_path, monkeypatch):
    _lab, mac = make_mac_lab(tmp_path)
    tool_bytes = b"verified release tool"
    write_zip(mac.recovery_archive, {"recovery/windows/amd64/macrecovery.exe": tool_bytes})
    mac.recovery_tool.parent.mkdir(parents=True)
    mac.recovery_tool.write_bytes(tool_bytes)
    dmg, chunklist, _vmdk = mac.recovery_paths()
    dmg.parent.mkdir(parents=True)
    dmg.write_bytes(b"dmg")
    chunklist.write_bytes(b"chunklist")
    monkeypatch.setattr(macos_module, "RECOVERYOS_ARCHIVE_SHA256", sha256(mac.recovery_archive))
    monkeypatch.setattr(macos_module, "verify_apple_recovery", lambda *_: {"verified_bytes": 3})
    result = mac.download_recovery()
    assert result["source"] == "Apple Internet Recovery"
    assert result["verified_bytes"] == 3
    assert json.loads((mac.root / "state/macos-x64-recovery-media.json").read_text())["board_id"]


def test_prepare_recovery_converts_and_checks_vmdk(tmp_path, monkeypatch):
    _lab, mac = make_mac_lab(tmp_path)
    dmg, chunklist, vmdk = mac.recovery_paths()
    dmg.parent.mkdir(parents=True)
    dmg.write_bytes(b"dmg")
    chunklist.write_bytes(b"chunklist")
    monkeypatch.setattr(mac, "download_recovery", lambda: {"verified_at": "fixture"})

    def fake_run(args, **_kwargs):
        if "convert" in args:
            # convert 的最后一个参数是临时目标。
            Path(args[-1]).write_bytes(b"vmdk")
            return ""
        if "info" in args:
            return json.dumps({"format": "vmdk", "virtual-size": 10})
        return ""

    monkeypatch.setattr(macos_module, "run", fake_run)
    result = mac.prepare_recovery()
    assert vmdk.read_bytes() == b"vmdk"
    assert result["vmdk_info"]["format"] == "vmdk"


def test_prepare_recovery_rejects_non_vmdk(tmp_path, monkeypatch):
    _lab, mac = make_mac_lab(tmp_path)
    vmdk = mac.recovery_paths()[2]
    vmdk.parent.mkdir(parents=True)
    vmdk.write_bytes(b"fixture")
    monkeypatch.setattr(mac, "download_recovery", dict)
    monkeypatch.setattr(macos_module, "run", lambda *_args, **_kwargs: json.dumps({"format": "raw"}))
    with pytest.raises(RuntimeError, match="VMDK_INVALID"):
        mac.prepare_recovery()


def test_doctor_reports_bootstrap_readiness(tmp_path, monkeypatch):
    _lab, mac = make_mac_lab(tmp_path)
    receipt = mac.root / "state/vmware-workstation-17.6.4-install.json"
    receipt.write_text(json.dumps({"version": "17.6.4", "build": "24832109"}))
    tools = {}
    for name in ("vmrun", "vmware", "vmware-vmx"):
        path = tmp_path / f"{name}.exe"
        path.write_bytes(name.encode())
        tools[name] = path
    cpuid = mac.oc4vm_root / "tools/windows/cpuid.exe"
    cpuid.parent.mkdir(parents=True)
    cpuid.write_bytes(b"fixture")
    for path in mac.recovery_paths():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"fixture")
    monkeypatch.setattr(mac, "vmware_tool", lambda name: tools[name])
    monkeypatch.setattr(mac, "template_manifest", lambda: {"version": "fixture"})
    monkeypatch.setattr(macos_module, "run", lambda *_args, **_kwargs: "AVX instructions\nAdvanced Vector Extensions 2.0 (AVX2)\n16-bit FP conversion instructions\nRDRAND instruction\nHyper-V detected")
    result = mac.doctor()
    assert result["ready_to_bootstrap"] is True
    assert result["cpu_features"]["hypervisor_detected"] is True


def test_doctor_records_tool_cpuid_and_template_errors(tmp_path, monkeypatch):
    _lab, mac = make_mac_lab(tmp_path)
    monkeypatch.setattr(mac, "vmware_tool", lambda name: (_ for _ in ()).throw(RuntimeError(f"missing:{name}")))
    monkeypatch.setattr(mac, "template_manifest", lambda: (_ for _ in ()).throw(RuntimeError("bad template")))
    monkeypatch.setattr(macos_module, "run", lambda *_args, **_kwargs: (_ for _ in ()).throw(OSError("cpuid failed")))
    result = mac.doctor()
    assert result["ready_to_bootstrap"] is False
    assert "error" in result["cpu_features"]
    assert "error" in result["oc4vm"]


def test_bootstrap_copies_only_verified_template_to_active_root(tmp_path, monkeypatch):
    _lab, mac = make_mac_lab(tmp_path)
    template = mac.oc4vm_root / "vmware/intel"
    template.mkdir(parents=True)
    (template / "macos.vmx").write_text('displayName = "macOS INTEL"\nsata0:1.autodetect = "TRUE"\nsata0:1.deviceType = "cdrom-raw"\nsata0:1.fileName = "auto detect"\nsata0:1.startConnected = "FALSE"\n')
    for name in ("macos.nvram", "macos.plist", "macos.vmdk", "opencore.iso", "opencore.vmdk"):
        (template / name).write_bytes(name.encode())
    recovery = mac.recovery_paths()[2]
    recovery.parent.mkdir(parents=True)
    recovery.write_bytes(b"recovery")
    monkeypatch.setattr(mac, "prepare_recovery", lambda: {"vmdk_sha256": "a" * 64})
    monkeypatch.setattr(mac, "template_manifest", lambda: {"version": "fixture"})
    monkeypatch.setattr(macos_module.shutil, "disk_usage", lambda _path: SimpleNamespace(free=100 * 1024**3))
    result = mac.bootstrap()
    vmx = mac.active_root / "vms/macos-x64/macos.vmx"
    text = vmx.read_text()
    assert result["status"] == "bootstrapped"
    assert 'displayName = "PartyOps macOS 15 Intel Acceptance"' in text
    assert 'sata0:1.fileName = "recovery.vmdk"' in text
    assert mac.bootstrap()["vmx"] == str(vmx)


def test_bootstrap_blocks_without_disk_headroom(tmp_path, monkeypatch):
    _lab, mac = make_mac_lab(tmp_path)
    monkeypatch.setattr(mac, "prepare_recovery", dict)
    monkeypatch.setattr(mac, "template_manifest", dict)
    monkeypatch.setattr(macos_module.shutil, "disk_usage", lambda _path: SimpleNamespace(free=59 * 1024**3))
    with pytest.raises(RuntimeError, match="DISK_HEADROOM"):
        mac.bootstrap()


def test_bootstrap_preserves_incomplete_existing_directory(tmp_path, monkeypatch):
    _lab, mac = make_mac_lab(tmp_path)
    monkeypatch.setattr(mac, "prepare_recovery", dict)
    monkeypatch.setattr(mac, "template_manifest", dict)
    monkeypatch.setattr(macos_module.shutil, "disk_usage", lambda _path: SimpleNamespace(free=100 * 1024**3))
    (mac.active_root / "vms/macos-x64").mkdir(parents=True)
    with pytest.raises(RuntimeError, match="INCOMPLETE_MACOS_VM_CREATE"):
        mac.bootstrap()


def test_vmware_lifecycle_start_stop_snapshot_and_restore(tmp_path, monkeypatch):
    _lab, mac = make_mac_lab(tmp_path)
    vmx = mac.active_root / "vms/macos-x64/macos.vmx"
    vmx.parent.mkdir(parents=True)
    vmx.write_text("fixture")
    mac.save({"target": "macos-x64", "vmx": str(vmx), "status": "bootstrapped", "snapshots": []})
    vmrun = tmp_path / "vmrun.exe"
    vmrun.write_bytes(b"fixture")
    monkeypatch.setattr(mac, "vmware_tool", lambda _name: vmrun)
    monkeypatch.setattr(mac, "running_vms", list)
    monkeypatch.setattr(_lab, "process_active", lambda _state: False)
    monkeypatch.setattr(_lab, "check_space", lambda *args: None)
    monkeypatch.setattr(psutil, "virtual_memory", lambda: SimpleNamespace(available=32 * 1024**3))
    calls = []
    monkeypatch.setattr(macos_module, "run", lambda args, **_kwargs: calls.append(args) or "")
    assert mac.start()["status"] == "running"
    assert "start" in calls[-1]
    monkeypatch.setattr(mac, "running", lambda _state=None: True)
    assert mac.stop()["stop_mode"] == "soft"
    assert "stop" in calls[-1]
    monkeypatch.setattr(mac, "running", lambda _state=None: False)
    assert mac.snapshot("clean-os")["snapshots"] == ["clean-os"]
    assert "snapshot" in calls[-1]
    assert mac.snapshot("clean-os", True)["status"] == "snapshot_restored"
    assert "revertToSnapshot" in calls[-1]


def test_vmware_lifecycle_blocks_invalid_snapshot_and_concurrency(tmp_path, monkeypatch):
    lab, mac = make_mac_lab(tmp_path)
    vmx = mac.active_root / "vms/macos-x64/macos.vmx"
    vmx.parent.mkdir(parents=True)
    vmx.write_text("fixture")
    mac.save({"target": "macos-x64", "vmx": str(vmx), "status": "stopped", "snapshots": []})
    monkeypatch.setattr(mac, "running", lambda _state=None: True)
    with pytest.raises(RuntimeError, match="REQUIRES_STOPPED_VM"):
        mac.snapshot("clean-os")
    monkeypatch.setattr(mac, "running", lambda _state=None: False)
    with pytest.raises(RuntimeError, match="SNAPSHOT_NOT_FOUND"):
        mac.snapshot("clean-os", True)
    other_dir = lab.root / "vms/other"
    other_dir.mkdir(parents=True)
    (other_dir / "vm.json").write_text(json.dumps({"target": "other", "pid": 1}))
    monkeypatch.setattr(lab, "live", lambda _state: True)
    monkeypatch.setattr(mac, "running_vms", list)
    with pytest.raises(RuntimeError, match="CONCURRENCY_LIMIT"):
        mac.start()
