"""控制器隔离测试，不启动真实 VM、不访问用户目录。"""
import io
import json
import lzma
import os
import sys
from pathlib import Path
from types import SimpleNamespace

import media_download
import providers
import pytest
from evidence import sha256
from lab import load_configuration
from providers import QemuLab, free_port, process_exists, run


@pytest.fixture
def lab(tmp_path, monkeypatch):
    matrix, _ = load_configuration()
    # 历史 Win11 Guest 只保留底层种子/磁盘契约测试，不加入生产活动矩阵。
    matrix["targets"]["win11-x64"] = matrix["historical_targets"]["win11-x64"]
    matrix["defaults"].update(qemu_home=str(tmp_path / "qemu"), fallback_root=str(tmp_path / "fallback"), reserve_primary_gib=0, reserve_fallback_gib=0)
    root = tmp_path / "lab"
    q = QemuLab(matrix, root, {})
    q.initialize()
    q.qemu_home.mkdir()
    for name in ("qemu-img", "qemu-system-x86_64", "qemu-system-i386", "qemu-system-aarch64"):
        (q.qemu_home / (name + (".exe" if os.name == "nt" else ""))).touch()
    monkeypatch.setattr(providers, "process_exists", lambda pid: False)
    return q


def make_state(lab, target="openeuler-iso-x64"):
    directory = lab.vm_dir(target)
    directory.mkdir(parents=True, exist_ok=True)
    disk = directory / "system.qcow2"
    disk.write_bytes(b"test disk")
    state = {"uuid": "unit-test-uuid", "target": target, "disk": str(disk), "base": "base",
             "temporary": True, "snapshots": [], "pid": None, "qmp_port": 12000,
             "vnc_display": 99, "status": "created"}
    lab.save(target, state)
    return state


def media(lab, tmp_path, fmt="qcow2", payload=b"qcow2 fixture"):
    name = "source." + fmt
    source = tmp_path / name
    source.write_bytes(payload)
    item = {"filename": name, "sha256": sha256(source), "local_path": str(source), "format": fmt,
            "arch": "x86_64", "minimum_expanded_gib": 0}
    lab.media["openeuler-iso-x64"] = item
    return source, item


def test_run_success_failure_timeout(tmp_path):
    log = tmp_path / "nested/test.log"
    assert run([sys.executable, "-c", "print('ok')"], log=log).strip() == "ok"
    assert log.read_text().strip() == "ok"
    with pytest.raises(RuntimeError, match="PROCESS_FAILED"):
        run([sys.executable, "-c", "raise SystemExit(3)"])
    with pytest.raises(RuntimeError, match="PROCESS_TIMEOUT"):
        run([sys.executable, "-c", "import time; time.sleep(2)"], timeout=0.05, log=log)
    assert log.with_suffix(".timeout.json").exists()


def test_port_and_process_queries():
    assert free_port() > 0
    assert free_port(5900) >= 5900
    assert process_exists(None) is False
    assert process_exists(os.getpid()) is True
    assert process_exists(99999999) is False


def test_vnc_port_does_not_depend_on_host_ephemeral_range(monkeypatch):
    requested = []
    class TestSocket:
        def __enter__(self):
            return self
        def __exit__(self, *args):
            return False
        def bind(self, address):
            requested.append(address[1])
        def getsockname(self):
            return ("127.0.0.1", requested[-1] or 1024)
    monkeypatch.setattr(providers.socket, "socket", TestSocket)
    assert free_port(5900) >= 5900
    assert requested[0] >= 5900


def test_root_marker_binary_and_state(lab):
    assert (lab.root / ".partyops-lab.json").exists()
    lab.initialize()
    assert Path(lab.binary("qemu-img")).exists()
    with pytest.raises(RuntimeError, match="MISSING_TOOL"):
        lab.binary("missing")
    with pytest.raises(RuntimeError, match="VM_NOT_CREATED"):
        lab.state("missing")
    with pytest.raises(ValueError):
        QemuLab(lab.matrix, Path(lab.root.anchor), {})
    with pytest.raises(ValueError):
        lab.vm_dir("../../outside")


def test_space_checks(lab, monkeypatch):
    monkeypatch.setattr(providers.shutil, "disk_usage", lambda path: SimpleNamespace(free=0))
    with pytest.raises(RuntimeError, match="DISK_HEADROOM"):
        lab.check_space(lab.root / "nested/missing", 1)


def test_media_local_hash_cache_and_rejection(lab, tmp_path):
    source, item = media(lab, tmp_path)
    path = lab.fetch("openeuler-iso-x64")
    assert path.read_bytes() == source.read_bytes()
    assert lab.fetch("openeuler-iso-x64") == path
    path.write_bytes(b"tampered")
    with pytest.raises(RuntimeError, match="MEDIA_HASH_MISMATCH"):
        lab.fetch("openeuler-iso-x64")
    with pytest.raises(RuntimeError, match="MISSING_MEDIA"):
        lab.fetch("not-found")
    item["sha256"] = ""
    with pytest.raises(RuntimeError, match="MEDIA_CHECKSUM_REQUIRED"):
        lab.fetch("openeuler-iso-x64")


def test_media_download_retry_and_http_policy(lab, monkeypatch):
    import hashlib
    from urllib.error import HTTPError
    payload = b"downloaded"
    lab.media["remote"] = {"filename": "remote.qcow2", "sha256": hashlib.sha256(payload).hexdigest(), "url": "https://example.invalid/remote"}
    sleeps = []
    attempts = []
    def download(*args, **kwargs):
        attempts.append(1)
        if len(attempts) == 1:
            raise HTTPError("test", 429, "rate limited", {}, None)
        response = io.BytesIO(payload)
        response.headers = {"Content-Length": str(len(payload))}
        response.status = 200
        return response
    monkeypatch.setattr(media_download.urllib.request, "urlopen", download)
    monkeypatch.setattr(providers.time, "sleep", sleeps.append)
    assert lab.fetch("remote").read_bytes() == payload
    assert sleeps == [20]
    lab.media["bad"] = {**lab.media["remote"], "filename": "bad", "url": "http://example.invalid"}
    with pytest.raises(RuntimeError, match="HTTPS_REQUIRED"):
        lab.fetch("bad")
    lab.media["bad"]["url"] = "https://example.invalid"
    monkeypatch.setattr(media_download.urllib.request, "urlopen", lambda *a, **kw: (_ for _ in ()).throw(HTTPError("test", 404, "missing", {}, None)))
    with pytest.raises(RuntimeError, match="DOWNLOAD_FAILED"):
        lab.fetch("bad")


def test_large_media_reserves_declared_size_before_writing(lab, tmp_path, monkeypatch):
    _, item = media(lab, tmp_path)
    item["size_bytes"] = 7 * 1024**3 + 1
    checks = []
    def reject(directory, additional_gib=0):
        checks.append(additional_gib)
        raise RuntimeError("DISK_HEADROOM")
    monkeypatch.setattr(lab, "check_space", reject)
    with pytest.raises(RuntimeError, match="DISK_HEADROOM"):
        lab.fetch("openeuler-iso-x64")
    assert checks == [8]
    assert not (lab.root / "downloads" / item["filename"]).exists()


@pytest.mark.parametrize("size", [-1, "7000", True])
def test_media_rejects_invalid_declared_size(lab, tmp_path, size):
    _, item = media(lab, tmp_path)
    item["size_bytes"] = size
    with pytest.raises(RuntimeError, match="MEDIA_SIZE_INVALID"):
        lab.fetch("openeuler-iso-x64")


def test_media_can_use_declared_fallback_storage(lab, tmp_path):
    _source, item = media(lab, tmp_path, payload=b"fallback media")
    item["storage_root"] = "fallback"
    path = lab.fetch("openeuler-iso-x64")
    assert path.parent == Path(lab.defaults["fallback_root"]) / "downloads"
    item["storage_root"] = "unknown"
    with pytest.raises(RuntimeError, match="STORAGE_ROOT_INVALID"):
        lab.fetch("openeuler-iso-x64")


def test_media_checks_declared_length_even_with_matching_hash(lab, tmp_path):
    source, item = media(lab, tmp_path)
    item["size_bytes"] = source.stat().st_size + 1
    with pytest.raises(RuntimeError, match="MEDIA_SIZE_MISMATCH"):
        lab.fetch("openeuler-iso-x64")


def test_bases_integrity_and_compression(lab, tmp_path, monkeypatch):
    media(lab, tmp_path, "qcow2.xz", lzma.compress(b"expanded"))
    monkeypatch.setattr(providers, "run", lambda args, **kw: json.dumps({"format": "qcow2"}) if "info" in args else "")
    base = lab.prepare_base("openeuler-iso-x64")
    assert base.read_bytes() == b"expanded"
    assert lab.prepare_base("openeuler-iso-x64") == base
    base.write_bytes(b"changed")
    with pytest.raises(RuntimeError, match="BASE_IMAGE_CHANGED"):
        lab.prepare_base("openeuler-iso-x64")


def test_base_rejects_nested_backing_chain(lab, tmp_path, monkeypatch):
    media(lab, tmp_path)
    monkeypatch.setattr(providers, "run", lambda args, **kw: json.dumps({"format": "qcow2", "backing-filename": "C:/business-data"}))
    with pytest.raises(RuntimeError, match="UNTRUSTED_IMAGE_BACKING_CHAIN"):
        lab.prepare_base("openeuler-iso-x64")


def test_iso_passthrough_and_unknown_format(lab, tmp_path):
    media(lab, tmp_path, "iso")
    assert lab.prepare_base("openeuler-iso-x64").suffix == ".iso"
    lab.media.clear()
    media(lab, tmp_path, "invalid")
    with pytest.raises(RuntimeError, match="UNSUPPORTED_MEDIA_FORMAT"):
        lab.prepare_base("openeuler-iso-x64")


def test_seed_contains_cloud_configuration(lab, tmp_path):
    import pycdlib
    key = lab.root / "keys/guest_ed25519"
    key.write_text("test private key never used")
    key.with_suffix(".pub").write_text("ssh-ed25519 UNIT_TEST_ONLY")
    iso_path = lab.cloud_seed(tmp_path, "unit-test")
    iso = pycdlib.PyCdlib()
    iso.open(str(iso_path))
    output = io.BytesIO()
    iso.get_file_from_iso_fp(output, rr_path="/user-data")
    assert b"Asia/Shanghai" in output.getvalue()
    assert b"UNIT_TEST_ONLY" in output.getvalue()
    iso.close()


def test_windows_seed_contains_unattend_timezone_identity_and_key(lab, tmp_path):
    import pycdlib
    key = lab.root / "keys/guest_ed25519"
    key.write_text("test private key never used")
    key.with_suffix(".pub").write_text("ssh-ed25519 UNIT_TEST_ONLY")
    iso_path = lab.windows_seed(tmp_path, "windows-unit-uuid", "win11-x64")
    iso = pycdlib.PyCdlib()
    iso.open(str(iso_path))
    unattend = io.BytesIO()
    script = io.BytesIO()
    iso.get_file_from_iso_fp(unattend, joliet_path="/Autounattend.xml")
    iso.get_file_from_iso_fp(script, joliet_path="/setup.ps1")
    iso.close()
    assert b"China Standard Time" in unattend.getvalue()
    assert b"BypassTPMCheck" in unattend.getvalue()
    assert b"Windows 11 Pro" in unattend.getvalue()
    assert b"windows-unit-uuid" in script.getvalue()
    assert b"UNIT_TEST_ONLY" in script.getvalue()
    assert b"LocalAccountTokenFilterPolicy" in script.getvalue()
    receipt = json.loads((tmp_path / "windows-unattend.json").read_text())
    assert receipt["remote_transport"] == "ssh-public-key"
    assert "password" not in json.dumps(receipt).lower()


def test_create_resume_and_wrong_media(lab, tmp_path, monkeypatch):
    source, _ = media(lab, tmp_path)
    monkeypatch.setattr(lab, "prepare_base", lambda _: source)
    monkeypatch.setattr(providers, "run", lambda *a, **kw: "")
    state = lab.create("openeuler-iso-x64")
    assert state["temporary"]
    assert lab.create("openeuler-iso-x64")["uuid"] == state["uuid"]
    with pytest.raises(RuntimeError, match="NATIVE_BACKEND_REQUIRED"):
        lab.create("macos-x64")
    with pytest.raises(RuntimeError, match="MISSING_MEDIA"):
        lab.create("win11-x64")
    lab.media["openeuler-iso-arm64"] = lab.media["openeuler-iso-x64"]
    with pytest.raises(RuntimeError, match="MEDIA_ARCHITECTURE_MISMATCH"):
        lab.create("openeuler-iso-arm64")


def test_qemu_commands_arch_network_and_firmware(lab):
    state = make_state(lab)
    lab.media["openeuler-iso-x64"] = {"format": "qcow2"}
    command = lab.command("openeuler-iso-x64", state, "tcg", False)
    assert "restrict=on" in " ".join(command)
    assert "127.0.0.1" in " ".join(command)
    arm = make_state(lab, "openeuler-iso-arm64")
    lab.media["openeuler-iso-arm64"] = {"format": "qcow2"}
    with pytest.raises(RuntimeError, match="REQUIRES_TCG"):
        lab.command("openeuler-iso-arm64", arm, "whpx", False)
    with pytest.raises(RuntimeError, match="MISSING_AARCH64_UEFI"):
        lab.command("openeuler-iso-arm64", arm, "tcg", False)
    (lab.qemu_home / "edk2-aarch64-code.fd").touch()
    arm["seed"] = "fixture.iso"
    arm_command = lab.command("openeuler-iso-arm64", arm, "tcg", True)
    assert "virt" in arm_command
    assert "virtio-scsi-pci,id=scsi0" in arm_command
    arm_devices = [arm_command[index + 1] for index, value in enumerate(arm_command[:-1]) if value == "-device"]
    assert "qemu-xhci" in arm_devices
    assert "usb-tablet" in arm_devices
    assert "qemu-xhci,id=qa-input-usb" not in arm_devices
    assert "-cdrom" not in arm_command

    win = make_state(lab, "win11-x64")
    win["seed"] = "windows-unattend.iso"
    lab.media["win11-x64"] = {"format": "iso"}
    win_command = lab.command("win11-x64", win, "tcg", True)
    joined = " ".join(win_command)
    assert joined.index(str(win["base"])) < joined.index("windows-unattend.iso")
    assert "hostfwd=tcp:127.0.0.1:22111-:22" in joined
    assert win_command[win_command.index("-rtc") + 1] == "base=localtime,clock=host"


@pytest.mark.parametrize("acceleration,binary", [("tcg", "qemu-system-i386"), ("whpx", "qemu-system-x86_64")])
def test_windows_i686_accelerator_binary(lab, acceleration, binary):
    state = make_state(lab, "win10-x86")
    lab.media["win10-x86"] = {"format": "iso"}
    command = lab.command("win10-x86", state, acceleration, True)
    assert Path(command[0]).stem == binary
    assert command[command.index("-accel") + 1].split(",")[0] == acceleration
    assert lab.matrix["targets"]["win10-x86"]["arch"] == "i686"
    devices = [command[index + 1] for index, value in enumerate(command[:-1]) if value == "-device"]
    assert "qemu-xhci,id=qa-input-usb" in devices
    assert "usb-tablet,bus=qa-input-usb.0" in devices


@pytest.mark.parametrize("target", ["win10-x64", "win7-x86"])
def test_win10_x86_usb_tablet_override_is_not_added_to_other_windows_targets(lab, target):
    state = make_state(lab, target)
    spec = lab.matrix["targets"][target]
    lab.media[spec["media"]] = {"format": "qcow2"}

    command = lab.command(target, state, "tcg", True)

    devices = [command[index + 1] for index, value in enumerate(command[:-1]) if value == "-device"]
    assert "qemu-xhci,id=qa-input-usb" not in devices
    assert "usb-tablet,bus=qa-input-usb.0" not in devices


def test_qmp_live_identity_and_failure(lab, monkeypatch):
    state = make_state(lab)
    monkeypatch.setattr(providers, "qmp", lambda *a: {"UUID": "unit-test-uuid"})
    assert lab.live(state)
    monkeypatch.setattr(providers, "qmp", lambda *a: {"UUID": "other-vm"})
    assert not lab.live(state)
    monkeypatch.setattr(providers, "qmp", lambda *a: (_ for _ in ()).throw(OSError("closed")))
    assert not lab.live(state)
    assert not lab.live({})


def test_snapshot_restore_and_clean_safety(lab, monkeypatch):
    state = make_state(lab)
    monkeypatch.setattr(lab, "live", lambda _: False)
    monkeypatch.setattr(providers, "run", lambda *a, **kw: "")
    assert "clean-os" in lab.snapshot(state["target"], "clean-os")["snapshots"]
    assert lab.snapshot(state["target"], "clean-os")["snapshots"] == ["clean-os"]
    assert lab.snapshot(state["target"], "clean-os", True)["status"] == "snapshot_restored"
    with pytest.raises(RuntimeError, match="SNAPSHOT_NOT_FOUND"):
        lab.snapshot(state["target"], "missing", True)
    assert not lab.cleanup(state["target"], False)["deleted"]
    assert lab.vm_dir(state["target"]).exists()
    assert lab.cleanup(state["target"], True)["deleted"]
    assert not lab.vm_dir(state["target"]).exists()
    assert (lab.root / "bases").exists()


def test_never_cleanup_or_snapshot_running_or_unknown_pid(lab, monkeypatch):
    state = make_state(lab)
    monkeypatch.setattr(lab, "live", lambda _: False)
    monkeypatch.setattr(providers, "process_exists", lambda _: True)
    monkeypatch.setattr(lab, "process_active", lambda _: True)
    for function in (lambda: lab.cleanup(state["target"], True), lambda: lab.snapshot(state["target"], "test"), lambda: lab.start(state["target"])):
        with pytest.raises(RuntimeError):
            function()
    assert lab.vm_dir(state["target"]).exists()


def test_start_and_stop_state(lab, monkeypatch):
    state = make_state(lab)
    lab.media["openeuler-iso-x64"] = {"format": "qcow2"}
    running = []
    monkeypatch.setattr(lab, "live", lambda _: bool(running))
    monkeypatch.setattr(providers, "run", lambda *a, **kw: str(32 * 1024**2))
    def spawn(*a, **kw):
        running.append(True)
        return SimpleNamespace(pid=1234, poll=lambda: None)
    monkeypatch.setattr(providers.subprocess, "Popen", spawn)
    monkeypatch.setattr(providers, "qmp", lambda *a, **kw: {})
    assert lab.start(state["target"])["status"] == "running"
    assert lab.start(state["target"])["pid"] == 1234
    assert lab.stop(state["target"], True)["status"] == "stop_requested"
    running.clear()
    assert lab.stop(state["target"])["status"] == "stopped"


@pytest.mark.skipif(providers.os.name != "nt", reason="宿主物理内存门禁仅在 Windows 使用")
def test_tcg_overhead_cannot_consume_required_host_reserve(lab, monkeypatch):
    state = make_state(lab)
    monkeypatch.setattr(lab, "live", lambda _: False)
    monkeypatch.setattr(lab, "process_active", lambda _: False)
    lab.defaults["host_free_memory_gib"] = 8
    lab.defaults["memory_mib"] = 8192
    # 12 GiB 仅够 4 GiB Guest + 8 GiB 保留；加入实测 TCG 开销后必须阻断。
    monkeypatch.setattr(providers, "run", lambda *a, **kw: str(12 * 1024**2))
    monkeypatch.setattr(providers.subprocess, "Popen", lambda *a, **kw: pytest.fail("内存不足不得启动"))
    with pytest.raises(RuntimeError, match="HOST_MEMORY_HEADROOM"):
        lab.start(state["target"], "tcg")


@pytest.mark.skipif(providers.os.name != "nt", reason="宿主物理内存门禁仅在 Windows 使用")
def test_win10_x86_can_start_with_its_configured_3gib_memory(lab, monkeypatch):
    state = make_state(lab, "win10-x86")
    state["installation_media_detached"] = True
    lab.save("win10-x86", state)
    lab.media["win10-x86"] = {"format": "iso", "arch": "i686"}
    lab.defaults["host_free_memory_gib"] = 8
    monkeypatch.setattr(lab, "live", lambda _: bool(getattr(lab, "_mock_running", False)))
    monkeypatch.setattr(lab, "process_active", lambda _: False)
    # 14 GiB 空闲内存扣除8 GiB宿主保留和TCG开销后，仍可分配配置的3 GiB。
    monkeypatch.setattr(providers, "run", lambda *a, **kw: str(14 * 1024**2))
    monkeypatch.setattr(providers, "free_port", lambda minimum=0: max(12001, minimum))
    calls = []

    def spawn(command, **kwargs):
        calls.append(command)
        lab._mock_running = True
        return SimpleNamespace(pid=1234, poll=lambda: None)

    monkeypatch.setattr(providers.subprocess, "Popen", spawn)
    result = lab.start("win10-x86", "tcg")
    assert result["status"] == "running"
    assert result["memory_mib"] == 3072
    assert calls[0][calls[0].index("-m") + 1] == "3072"


@pytest.mark.skipif(providers.os.name != "nt", reason="宿主物理内存门禁仅在 Windows 使用")
def test_win10_x86_still_rejects_insufficient_memory(lab, monkeypatch):
    state = make_state(lab, "win10-x86")
    state["installation_media_detached"] = True
    lab.save("win10-x86", state)
    lab.media["win10-x86"] = {"format": "iso", "arch": "i686"}
    lab.defaults["host_free_memory_gib"] = 8
    monkeypatch.setattr(lab, "live", lambda _: False)
    monkeypatch.setattr(lab, "process_active", lambda _: False)
    monkeypatch.setattr(providers, "run", lambda *a, **kw: str(12 * 1024**2))
    monkeypatch.setattr(providers.subprocess, "Popen", lambda *a, **kw: pytest.fail("内存不足不得启动"))
    with pytest.raises(RuntimeError, match="HOST_MEMORY_HEADROOM"):
        lab.start("win10-x86", "tcg")


@pytest.mark.skipif(providers.os.name != "nt", reason="宿主物理内存门禁仅在 Windows 使用")
def test_64bit_windows_target_keeps_4gib_minimum(lab, monkeypatch):
    state = make_state(lab, "win10-x64")
    state["installation_media_detached"] = True
    lab.save("win10-x64", state)
    lab.media["win10-x64"] = {"format": "iso", "arch": "x86_64"}
    lab.defaults["host_free_memory_gib"] = 8
    monkeypatch.setattr(lab, "live", lambda _: False)
    monkeypatch.setattr(lab, "process_active", lambda _: False)
    # 13 GiB空闲只允许向该目标分配3 GiB，仍低于既有4 GiB最低值。
    monkeypatch.setattr(providers, "run", lambda *a, **kw: str(13 * 1024**2))
    monkeypatch.setattr(providers.subprocess, "Popen", lambda *a, **kw: pytest.fail("64位最低内存不得降低"))
    with pytest.raises(RuntimeError, match="HOST_MEMORY_HEADROOM"):
        lab.start("win10-x64", "tcg")


def test_ssh_requires_owned_live_vm(lab, monkeypatch):
    state = make_state(lab)
    monkeypatch.setattr(lab, "live", lambda _: False)
    with pytest.raises(RuntimeError, match="VM_NOT_RUNNING"):
        lab.ssh(state["target"], "true")
    monkeypatch.setattr(lab, "live", lambda _: True)
    calls = []
    monkeypatch.setattr(providers, "run", lambda args, **kw: calls.append(args) or "ok")
    assert lab.ssh(state["target"], "true") == "ok"
    assert "BatchMode=yes" in calls[0]
    assert calls[0][-2] == "partyopsqa@127.0.0.1"


def test_windows_cold_boot_does_not_regenerate_seed_or_password(lab, monkeypatch):
    state = make_state(lab, "win7-x64")
    state.update(installation_media_detached=True, seed=None)
    lab.save(state["target"], state)
    lab.media["win7-x64"] = {"format": "iso"}
    running = []
    monkeypatch.setattr(lab, "live", lambda _: bool(running))
    monkeypatch.setattr(providers, "run", lambda *a, **kw: str(32 * 1024**2))
    def forbidden_seed(*args):
        pytest.fail("冷启动不得重新插入安装种子或更换本地凭据")
    monkeypatch.setattr(lab, "windows_seed", forbidden_seed)
    def spawn(command, **kwargs):
        assert not any("media=cdrom" in arg for arg in command)
        running.append(True)
        return SimpleNamespace(pid=1234, poll=lambda: None)
    monkeypatch.setattr(providers.subprocess, "Popen", spawn)
    assert lab.start(state["target"])["seed"] is None


@pytest.mark.parametrize("target,arch", [("win7-x64", "amd64"), ("win7-x86", "x86")])
def test_win7_seed_uses_only_supported_oobe_and_transport(lab, target, arch):
    import pycdlib
    key = lab.root / "keys/guest_ed25519"
    key.write_text("fixture")
    key.with_suffix(".pub").write_text("ssh-ed25519 AAAATEST qa-fixture")
    directory = lab.vm_dir(target)
    directory.mkdir(parents=True)
    iso = pycdlib.PyCdlib()
    iso.open(str(lab.windows_seed(directory, "fixture-uuid", target)))
    xml, script = io.BytesIO(), io.BytesIO()
    iso.get_file_from_iso_fp(xml, joliet_path="/Autounattend.xml")
    iso.get_file_from_iso_fp(script, joliet_path="/setup.ps1")
    iso.close()
    assert ('processorArchitecture="' + arch + '"').encode() in xml.getvalue()
    for unsupported in (b"HideLocalAccountScreen", b"HideOnlineAccountScreens", b"Get-Volume"):
        assert unsupported not in xml.getvalue()
    for unsupported in (b"Set-TimeZone", b"Get-WindowsCapability", b"New-NetFirewallRule", b"Import-Module Microsoft.WSMan.Management",
                         b"New-Item WSMan:"):
        assert unsupported not in script.getvalue()
    assert b"WinRM" in script.getvalue() and b"Windows 7 ULTIMATE" in xml.getvalue()
    assert providers.WIN7_WSMAN_PROVIDER_SETUP.encode("utf-8") in script.getvalue()
    assert providers.WIN7_WINRM_SERVICE_SETUP.encode("utf-8") in script.getvalue()
    assert providers.WIN7_WINRM_LISTENER_SETUP.encode("utf-8") in script.getvalue()


@pytest.mark.skipif(os.name != "nt", reason="隔离模拟只使用 Windows 宿主 PowerShell，不操作真实 WSMan")
@pytest.mark.parametrize("scenario,expected", [
    ("loaded", "loaded:0"),
    ("registered", "registered:1"),
    ("missing", "WINRM_WSMAN_SNAPIN_NOT_REGISTERED"),
    ("broken", "WINRM_WSMAN_PROVIDER_UNAVAILABLE"),
])
def test_win7_wsman_uses_provider_or_registered_snapin_without_module(scenario, expected):
    import base64

    # 所有 provider/snapin 命令均为进程内测试替身；不加载宿主模块或变更 WSMan 配置。
    harness = "$scenario='" + scenario + "';$script:addCount=0;$script:available=($scenario -eq 'loaded')\n" + r"""
function Get-PSProvider {param($PSProvider,$ErrorAction) if($script:available) {'WSMan'}}
function Get-PSSnapin {param([switch]$Registered,$Name,$ErrorAction) if($scenario -ne 'missing') {'Microsoft.WSMan.Management'}}
function Add-PSSnapin {param($Name,$ErrorAction) $script:addCount++;if($scenario -eq 'registered') {$script:available=$true}}
function Get-PSDrive {param($Name,$ErrorAction) if($script:available) {'WSMan'}}
function Import-Module {throw 'TEST_UNEXPECTED_MODULE_IMPORT'}
try {
""" + providers.WIN7_WSMAN_PROVIDER_SETUP + "\nWrite-Output ($scenario+':'+$script:addCount)\n} catch {Write-Output $_.Exception.Message}\n"
    encoded = base64.b64encode(harness.encode("utf-16le")).decode("ascii")
    assert providers.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded]).strip() == expected


@pytest.mark.skipif(os.name != "nt", reason="隔离模拟只使用宿主 PowerShell，不操作实际 Listener")
@pytest.mark.parametrize("scenario,expected", [("existing", "get,get"), ("missing", "get,create,get"),
                                              ("create-failed", "WINRM_LISTENER_CREATE_FAILED"),
                                              ("verify-failed", "WINRM_LISTENER_VERIFY_FAILED")])
def test_win7_native_listener_is_idempotent_and_checks_native_exit_codes(scenario, expected):
    import base64

    # Join-Path 仅在本子进程被替身覆盖，将固定 WinRM 路径解析到模拟函数。
    harness = "$scenario='" + scenario + "';$script:calls=@();$script:exists=($scenario -eq 'existing')\n" + r"""
function Join-Path {param($Path,$ChildPath) 'Invoke-FakeWinRM'}
function Test-Path {param($LiteralPath,$PathType) $true}
function Invoke-FakeWinRM {
  param($operation,$resource)
  if($resource -ne 'winrm/config/listener?Address=*+Transport=HTTP') {throw 'TEST_WRONG_LISTENER'}
  $script:calls+=@($operation)
  $global:LASTEXITCODE=0
  if($operation -eq 'create') {
    if($scenario -eq 'create-failed') {$global:LASTEXITCODE=5} else {$script:exists=$true}
  }elseif((-not $script:exists) -or ($scenario -eq 'verify-failed')) {$global:LASTEXITCODE=1}
  'fixture output'
}
try {
""" + providers.WIN7_WINRM_LISTENER_SETUP + "\nWrite-Output ($script:calls -join ',')\n} catch {Write-Output $_.Exception.Message}\n"
    encoded = base64.b64encode(harness.encode("utf-16le")).decode("ascii")
    assert providers.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded]).strip() == expected


@pytest.mark.skipif(os.name != "nt", reason="隔离模拟只使用宿主 PowerShell，不操作实际服务")
@pytest.mark.parametrize("persist,expected", [(True, "automatic,clear-delay,start"), (False, "WINRM_DELAYED_START_RESET_FAILED")])
def test_win7_service_clears_delayed_flag_before_starting(persist, expected):
    import base64

    harness = "$persist=$" + str(persist).lower() + ";$script:delayed=1;$script:calls=@()\n" + r"""
function Set-Service {param($Name,$StartupType) if($Name -ne 'WinRM' -or $StartupType -ne 'Automatic') {throw 'TEST_WRONG_SERVICE'};$script:calls+=@('automatic')}
function New-ItemProperty {
  param($Path,$Name,$PropertyType,$Value,[switch]$Force)
  if($Path -ne 'HKLM:\SYSTEM\CurrentControlSet\Services\WinRM' -or $Name -ne 'DelayedAutoStart' -or $Value -ne 0) {throw 'TEST_WRONG_SERVICE_FLAG'}
  $script:calls+=@('clear-delay');if($persist){$script:delayed=0}
}
function Get-ItemProperty {param($Path,$Name) @{DelayedAutoStart=$script:delayed}}
function Start-Service {param($Name) if($script:delayed -ne 0) {throw 'TEST_DELAY_NOT_CLEARED'};$script:calls+=@('start')}
try {
""" + providers.WIN7_WINRM_SERVICE_SETUP + "\nWrite-Output ($script:calls -join ',')\n} catch {Write-Output $_.Exception.Message}\n"
    encoded = base64.b64encode(harness.encode("utf-16le")).decode("ascii")
    assert providers.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded]).strip() == expected


def test_reused_pid_is_not_an_owned_qemu_process(lab, monkeypatch):
    state = make_state(lab)
    state["pid"] = 42
    monkeypatch.setattr(providers, "process_exists", lambda _: True)
    monkeypatch.setattr(providers.psutil, "Process", lambda _: SimpleNamespace(cmdline=lambda: ["postgres.exe"]))
    assert not lab.process_active(state)
    monkeypatch.setattr(providers.psutil, "Process", lambda _: SimpleNamespace(cmdline=lambda: ["qemu-system-x86_64.exe", "-uuid", "someone-else"]))
    assert not lab.process_active(state)
    monkeypatch.setattr(providers.psutil, "Process", lambda _: SimpleNamespace(cmdline=lambda: ["qemu-system-x86_64.exe", "-uuid", state["uuid"]]))
    assert lab.process_active(state)


@pytest.mark.parametrize("target,edition", [("win10-x64", "Windows 10 Pro"), ("win11-x64", "Windows 11 Pro")])
def test_modern_windows_seed_matches_setup_key_and_image_edition(lab, target, edition):
    import xml.etree.ElementTree as ET

    import pycdlib
    key = lab.root / "keys/guest_ed25519"
    key.write_text("fixture")
    key.with_suffix(".pub").write_text("ssh-ed25519 AAAATEST qa-fixture")
    directory = lab.vm_dir(target)
    directory.mkdir(parents=True)
    iso = pycdlib.PyCdlib()
    iso.open(str(lab.windows_seed(directory, "fixture-uuid", target)))
    xml, script = io.BytesIO(), io.BytesIO()
    iso.get_file_from_iso_fp(xml, joliet_path="/Autounattend.xml")
    iso.get_file_from_iso_fp(script, joliet_path="/setup.ps1")
    iso.close()
    root = ET.fromstring(xml.getvalue())
    ns = {"u": "urn:schemas-microsoft-com:unattend"}
    assert root.findtext(".//u:UserData/u:ProductKey/u:Key", namespaces=ns) == "W269N-WFGWX-YVC9B-4J6C9-T83GX"
    assert root.findtext(".//u:InstallFrom/u:MetaData/u:Value", namespaces=ns) == edition
    assert b"/IMAGE/INDEX" not in xml.getvalue()
    assert b"*S-1-5-32-544:F" in script.getvalue()
    assert b"slmgr" not in script.getvalue()
