"""原始系统字段、硬件身份与冷启动事务反例。"""
import copy
import json
from types import SimpleNamespace

import identity
import pytest
from evidence import sha256, write_json
from identity import normalize_linux, runtime_binding, seal_baseline, validate_identity


@pytest.fixture
def owned(tmp_path):
    spec = {"os": "linux", "arch": "x86_64", "distribution": "UOS", "distribution_id": "uos",
            "os_release": "20", "os_build": "1070", "edition": "Professional", "media": "original"}
    system = {**spec, "boot_id": "boot-after", "hardware_uuid": "abcd-owned-uuid", "uid": 1000,
              "package_arch": "amd64", "installed_package": False,
              "marker": json.dumps({"uuid": "abcd-owned-uuid", "purpose": "disposable-qa"})}
    iso = tmp_path / "original.iso"
    iso.write_bytes(b"official media fixture")
    state = {"uuid": "abcd-owned-uuid", "snapshots": ["clean-original"], "base": str(iso),
             "source_sha256": sha256(iso), "installation_media_detached": True,
             "clean_baseline": {"name": "clean-original", "id": "baseline-id", "boot_id_before": "boot-before",
                                "cold_boot_verified": True, "snapshot_record": {"id": "1", "name": "clean-original"}}}
    write_json(tmp_path / "baseline-identity.json", system)
    state["clean_baseline"]["identity_sha256"] = sha256(tmp_path / "baseline-identity.json")
    def save(target, value):
        state.clear()
        state.update(copy.deepcopy(value))
    lab = SimpleNamespace(matrix={"targets": {"uos": spec}}, media={"original": {"sha256": sha256(iso)}},
                          state=lambda _: copy.deepcopy(state), vm_dir=lambda _: tmp_path, save=save,
                          snapshot_record=lambda target, name: {"id": "1", "name": name})
    return lab, spec, system, state


def test_uos_uses_original_fields_and_ignores_id_like():
    actual = normalize_linux({"os_release_raw": 'ID=deepin\nID_LIKE=uos\nNAME=Deepin\nVERSION_ID=20',
                              "os_version_raw": 'MinorVersion=1070\nEditionName=Professional'})
    assert actual["distribution"] == "Deepin"
    assert actual["distribution_id"] == "deepin"
    assert "os_build" not in actual
    actual = normalize_linux({"os_release_raw": 'ID=uos\nVERSION_ID="20"\nVERSION="20 Professional"',
                              "os_version_raw": 'MinorVersion=1070'})
    assert (actual["os_build"], actual["edition"]) == ("1070", "Professional")


def test_euler_sp_is_not_inferred_from_target():
    actual = normalize_linux({"os_release_raw": 'ID="openEuler"\nVERSION_ID="24.03"\nVERSION="24.03 (LTS-SP1)"'})
    assert actual["os_build"] == "LTS-SP1"


@pytest.mark.parametrize("marker", [{}, {"uuid": "foreign", "purpose": "disposable-qa"},
                                   {"uuid": "owned", "purpose": "disposable-build"}, "invalid"])
def test_windows_rejects_unowned_marker_despite_matching_hardware(marker):
    spec = {"os": "windows", "arch": "x86_64", "os_release": "10"}
    system = {**spec, "hardware_uuid": "owned", "boot_id": "boot", "marker": marker}
    with pytest.raises(RuntimeError, match="OWNERSHIP_MISMATCH"):
        validate_identity(system, spec, {"uuid": "owned"})


def test_windows_marker_accepts_seed_timezone_metadata():
    spec = {"os": "windows", "arch": "x86_64", "os_release": "10"}
    system = {**spec, "hardware_uuid": "owned", "boot_id": "boot",
              "marker": {"uuid": "owned", "purpose": "disposable-qa", "timezone": "Asia/Shanghai"}}
    validate_identity(system, spec, {"uuid": "owned"})


@pytest.mark.parametrize("field,value", [("os", "darwin"), ("arch", "aarch64"), ("os_build", "1050"),
                                        ("distribution", "Deepin"), ("hardware_uuid", "other"),
                                        ("uid", 0), ("package_arch", "arm64"), ("marker", "{}"),
                                        ("marker", json.dumps({"uuid": "abcd-owned-uuid", "purpose": "disposable-build"}))])
def test_identity_rejects_foreign_guest(owned, field, value):
    _, spec, system, state = owned
    system[field] = value
    with pytest.raises(RuntimeError):
        validate_identity(system, spec, state)


def test_binding_requires_cold_boot_and_unchanged_identity(owned):
    lab, _, _, state = owned
    assert runtime_binding(lab, "uos")["vm_uuid"] == state["uuid"]
    state["clean_baseline"]["boot_id_before"] = "boot-after"
    with pytest.raises(RuntimeError, match="COLD_BOOT_NOT_PROVEN"):
        runtime_binding(lab, "uos")
    state["clean_baseline"]["boot_id_before"] = "boot-before"
    (lab.vm_dir("uos") / "baseline-identity.json").write_text('{}')
    with pytest.raises(RuntimeError, match="BASELINE_IDENTITY_CHANGED"):
        runtime_binding(lab, "uos")


def test_baseline_detaches_optical_before_cold_start(owned, monkeypatch):
    lab, _, system, state = owned
    state.pop("clean_baseline")
    state.update(installation_media_detached=False, snapshots=[])
    events = []
    lab.stop = lambda target: events.append("stop")
    def start(target, acceleration, network):
        assert state["installation_media_detached"] and not state.get("seed") and not network
        events.append("cold-start")
    lab.start = start
    def snapshot(target, name):
        events.append("snapshot")
        state["snapshots"].append(name)
        return copy.deepcopy(state)
    lab.snapshot = snapshot
    monkeypatch.setattr(identity, "probe", lambda *a: {**system, "boot_id": "boot-before"})
    monkeypatch.setattr(identity, "wait_probe", lambda *a: system)
    monkeypatch.setattr(identity, "wait_stopped", lambda *a: events.append("confirmed-stopped"))
    result = seal_baseline(lab, "uos")
    assert result["vm_uuid"] == state["uuid"]
    assert events == ["stop", "confirmed-stopped", "cold-start", "stop", "confirmed-stopped", "snapshot"]
    assert state["temporary"] is False


def test_baseline_does_not_seal_process_restart(owned, monkeypatch):
    lab, _, system, state = owned
    state.pop("clean_baseline")
    lab.stop = lambda *a: None
    lab.start = lambda *a: None
    monkeypatch.setattr(identity, "probe", lambda *a: system)
    monkeypatch.setattr(identity, "wait_probe", lambda *a: system)
    monkeypatch.setattr(identity, "wait_stopped", lambda *a: None)
    with pytest.raises(RuntimeError, match="COLD_BOOT_NOT_PROVEN"):
        seal_baseline(lab, "uos")
    assert "clean_baseline" not in state


@pytest.mark.parametrize("wrapped", [False, True])
def test_win7_ps2_probe_preserves_long_json_and_rejects_formatter_line_breaks(tmp_path, wrapped):
    import base64

    uuid = "406ef8ee-e342-405f-933f-7ddce0a55f1b"
    system = {"os": "windows", "arch": "x86_64", "os_release": "7 SP1", "boot_id": "20260908093222.500000+480",
              "hardware_uuid": uuid, "installed_package": False,
              "marker": {"uuid": uuid, "purpose": "disposable-qa", "note": "原版环境长标记" * 30}}
    raw = json.dumps(system, ensure_ascii=False)
    if wrapped:
        position = raw.index("原版环境长标记") + 80
        raw = raw[:position] + "\n" + raw[position:]

    def output(target, command, timeout):
        script = base64.b64decode(command.split("-EncodedCommand ", 1)[1]).decode("utf-16le")
        assert "[Console]::WriteLine(" in script and "[Console]::OutputEncoding=" in script
        return raw + "\r\n"
    lab = SimpleNamespace(matrix={"targets": {"win7-x64": {"os": "windows", "arch": "x86_64", "os_release": "7 SP1"}}},
                          state=lambda _: {"uuid": uuid, "environment_type": "full-system-emulation"},
                          live=lambda _: True, vm_dir=lambda _: tmp_path, ssh=output)
    if wrapped:
        with pytest.raises(json.JSONDecodeError):
            identity.probe(lab, "win7-x64")
        assert not (tmp_path / "guest-system.json").exists()
    else:
        result = identity.probe(lab, "win7-x64")
        assert result["marker"]["note"] == system["marker"]["note"]
        assert result["boot_id"] == system["boot_id"]
