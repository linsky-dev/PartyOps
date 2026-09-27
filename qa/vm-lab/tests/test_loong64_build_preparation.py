"""构建前置入口的身份、原始结果和续跑边界；不启动 Guest 或编译产品。"""
import importlib.util
import json
import shlex
import struct
import types
from pathlib import Path

import pytest
from lab import HERE


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, HERE / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


GUEST = load("loong64_guest_preparation", "guest/loong64-build-probe.py")
CONTROLLER = load("loong64_controller_preparation", "scripts/prepare-loong64-build.py")


def raw_identity(purpose="disposable-build"):
    return {"os": "linux", "arch": "loongarch64", "uid": 1000, "boot_id": "kernel-boot-1",
            "hardware_uuid": "bound-uuid", "package_arch": "loong64", "os_release_raw": 'ID=deepin\nVERSION_ID="25"\nBUILD_ID="25.2.0"\n',
            "marker": json.dumps({"uuid": "bound-uuid", "purpose": purpose})}


@pytest.fixture
def owned(tmp_path, monkeypatch):
    target = "build-deepin-deb-loong64"
    spec = {"os": "linux", "arch": "loongarch64", "distribution": "Deepin", "distribution_id": "deepin",
            "os_release": "25", "os_build": "25.2.0", "package_arch": "loong64", "ssh_port": 22503}
    state = {"uuid": "bound-uuid", "purpose": "disposable-build", "clone_source_binding": {"baseline": "sealed"}}
    lab = types.SimpleNamespace(root=tmp_path, matrix={"targets": {target: spec}},
                                state=lambda value: state, live=lambda value: True)
    monkeypatch.setattr(CONTROLLER, "runtime_binding", lambda lab, target: {"baseline": "sealed"})
    monkeypatch.setattr(CONTROLLER, "validate_firmware", lambda *args: {"code_sha256": "a" * 64, "vars_sha256": "b" * 64})
    monkeypatch.setattr(CONTROLLER, "fingerprint", lambda: "c" * 64)
    client = types.SimpleNamespace(load_host_keys=lambda path: None, set_missing_host_key_policy=lambda policy: None,
                                   connect=lambda *args, **kwargs: None, close=lambda: None)
    monkeypatch.setattr(CONTROLLER.paramiko, "SSHClient", lambda: client)
    calls = []
    def execute(client, command, **kwargs):
        calls.append(command)
        if "--phase" not in command:
            return 0, json.dumps(raw_identity(state["purpose"])), ""
        args = shlex.split(command)
        phase = args[args.index("--phase") + 1]
        assert kwargs["payload"] == (HERE / "guest/loong64-build-probe.py").read_text(encoding="utf-8")
        result = {"boot_id": "kernel-boot-1", "minimum_glibc_met": True} if phase == "system" else {}
        return 0, json.dumps({"guest_uuid": state["uuid"], "phase": phase, "runtime_environment_passed": False, "result": result}), ""
    monkeypatch.setattr(CONTROLLER, "execute", execute)
    return lab, target, state, calls


def test_read_only_preparation_records_raw_hashes_and_never_marks_runtime(owned):
    lab, target, _state, calls = owned
    result = CONTROLLER.prepare(lab, target, "all")
    assert result["status"] == "collected"
    assert result["runtime_environment_passed"] is False
    assert [step["phase"] for step in result["steps"]] == ["system", "repositories", "toolchain"]
    for step in result["steps"]:
        assert CONTROLLER.sha256(Path(result["report_path"]) / step["raw"]) == step["raw_sha256"]
    assert all("apt-get" not in command for command in calls)


@pytest.mark.parametrize("phase", ["smoke", "refresh-index", "install-build-tools"])
def test_acceptance_baseline_cannot_be_mutated(owned, phase):
    lab, target, state, calls = owned
    state["purpose"] = "disposable-qa"
    with pytest.raises(RuntimeError, match="REQUIRES_BUILD_CLONE"):
        CONTROLLER.prepare(lab, target, phase)
    assert calls == []


def test_changed_baseline_clone_blocked_before_ssh(owned):
    lab, target, state, calls = owned
    state["clone_source_binding"] = {"baseline": "old"}
    with pytest.raises(RuntimeError, match="SOURCE_BINDING_CHANGED"):
        CONTROLLER.prepare(lab, target, "all")
    assert calls == []


def test_resume_reprobes_and_keeps_previous_raw_evidence(owned):
    lab, target, _state, calls = owned
    first = CONTROLLER.prepare(lab, target, "system")
    previous = Path(first["report_path"]) / "execution.json"
    raw = previous.read_bytes()
    second = CONTROLLER.prepare(lab, target, "system", resume=previous)
    assert second["status"] == "collected"
    assert len(calls) == 4
    assert previous.read_bytes() == raw
    assert first["run_id"] != second["run_id"]


def test_resume_wrong_vm_refused(owned):
    lab, target, state, _calls = owned
    first = CONTROLLER.prepare(lab, target, "system")
    state["uuid"] = "replacement-vm"
    with pytest.raises(RuntimeError, match="RESUME_TARGET_MISMATCH"):
        CONTROLLER.prepare(lab, target, "system", resume=Path(first["report_path"]) / "execution.json")


@pytest.mark.parametrize("changed", [{"arch": "aarch64"}, {"package_arch": "amd64"}, {"os_build": "25.1.0"},
                                    {"hardware_uuid": "other-vm"}, {"uid": 0}, {"boot_id": ""}])
def test_clone_identity_rejects_wrong_isa_build_uuid_or_user(owned, changed):
    lab, target, state, _calls = owned
    observed = CONTROLLER.normalize_linux(raw_identity())
    observed.update(changed)
    with pytest.raises(RuntimeError):
        CONTROLLER.validate_observed(observed, lab.matrix["targets"][target], state)


def test_repository_candidate_is_only_cached_availability(monkeypatch):
    def command(args):
        package = args[-1]
        return {"command": list(args), "exit_code": 0, "stdout": "Candidate: 2.0\n" if package == "python3-numpy" else "Candidate: (none)\n", "stderr": ""}
    monkeypatch.setattr(GUEST, "command", command)
    result = GUEST.repositories_probe()
    assert result["candidates"]["python3-numpy"]["candidate"] == "2.0"
    assert result["candidates"]["python3-onnxruntime"]["candidate"] is None
    assert result["bundled_runtime_verified"] is False
    assert "raw" in result["candidates"]["libreoffice"]


def test_repository_urls_do_not_disclose_userinfo_or_query():
    value = GUEST.redact_urls("500 https://user:secret@repo.deepin.com/community?token=secret\n")
    assert value == "500 https://repo.deepin.com/community\n"


@pytest.mark.parametrize("purpose,sources,error", [
    ("disposable-qa", {}, "BUILD_CLONE"),
    ("disposable-build", {"only_deepin_official_http_origins": False, "non_http_sources_present": False}, "VERIFIED"),
    ("disposable-build", {"only_deepin_official_http_origins": True, "non_http_sources_present": True}, "VERIFIED"),
])
def test_refresh_refuses_unverified_origins_or_original_guest(monkeypatch, purpose, sources, error):
    monkeypatch.setattr(GUEST, "sources_probe", lambda: sources)
    monkeypatch.setattr(GUEST, "command", lambda *args, **kwargs: pytest.fail("must not run apt"))
    with pytest.raises(RuntimeError, match=error):
        GUEST.refresh_index(purpose)


def test_approved_clone_refresh_has_bounded_network_attempt(monkeypatch):
    monkeypatch.setattr(GUEST, "sources_probe", lambda: {"only_deepin_official_http_origins": True, "non_http_sources_present": False})
    calls = []
    monkeypatch.setattr(GUEST, "command", lambda args, **kwargs: calls.append((args, kwargs)) or {"exit_code": 0})
    GUEST.refresh_index("disposable-build")
    assert calls[0][0][:3] == ["sudo", "-n", "apt-get"]
    assert "Acquire::Retries=0" in calls[0][0]
    assert calls[0][1]["timeout"] == 600


@pytest.mark.parametrize("machine,flags,valid", [(258, 3, True), (62, 3, False), (183, 3, False), (258, 1, False)])
def test_smoke_requires_actual_loong64_lp64d_elf(tmp_path, machine, flags, valid):
    raw = bytearray(64)
    raw[:6] = b"\x7fELF\x02\x01"
    struct.pack_into("<H", raw, 18, machine)
    struct.pack_into("<I", raw, 48, flags)
    binary = tmp_path / "synthetic-probe"
    binary.write_bytes(raw)
    if valid:
        assert GUEST.elf_identity(binary)["machine"] == 258
    else:
        with pytest.raises(RuntimeError, match="ELF_MACHINE_OR_LP64D"):
            GUEST.elf_identity(binary)


def test_compiler_smoke_cannot_run_on_acceptance_guest():
    with pytest.raises(RuntimeError, match="BUILD_CLONE"):
        GUEST.smoke("disposable-qa", "loong-prep-123456789abc")


def test_build_tool_install_cannot_modify_acceptance_guest():
    with pytest.raises(RuntimeError, match="BUILD_CLONE"):
        GUEST.install_build_tools("disposable-qa")


@pytest.mark.parametrize("origin", ["https://repo.deepin.com/community", "https://com-store-packages.uniontech.com/appstore-V25"])
def test_build_tool_install_pins_candidates_and_keeps_transaction_evidence(monkeypatch, origin):
    monkeypatch.setattr(GUEST.shutil, "disk_usage", lambda path: types.SimpleNamespace(free=7 * 1024**3))
    monkeypatch.setattr(GUEST, "sources_probe", lambda: {"only_deepin_official_http_origins": True, "non_http_sources_present": False})
    monkeypatch.setattr(GUEST, "repositories_probe", lambda: {
        "index_policy": {"stdout": "500 " + origin + " crimson/main loong64 Packages"},
        "candidates": {name: {"candidate": "1:2.3-4"} for name in GUEST.BUILD_TOOLS}})
    calls = []
    monkeypatch.setattr(GUEST, "command", lambda args, **kwargs: calls.append(list(args)) or {"exit_code": 0, "stdout": "raw transaction"})
    result = GUEST.install_build_tools("disposable-build")
    assert result["status"] == "installed"
    assert calls[0][:2] == ["apt-get", "--simulate"]
    assert "gcc=1:2.3-4" in calls[1]
    assert result["simulation"]["stdout"] == "raw transaction"
    assert result["runtime_bundling_verified"] is False


def test_original_appstore_origin_does_not_accept_lookalike_hosts():
    assert GUEST.official_repository("https://com-store-packages.uniontech.com/appstore-V25")
    assert not GUEST.official_repository("https://com-store-packages.uniontech.com.example.org/appstore-V25")
    assert not GUEST.official_repository("https://unknown.uniontech.com/appstore-V25")


def test_build_tool_install_rejects_stale_third_party_index(monkeypatch):
    monkeypatch.setattr(GUEST.shutil, "disk_usage", lambda path: types.SimpleNamespace(free=7 * 1024**3))
    monkeypatch.setattr(GUEST, "sources_probe", lambda: {"only_deepin_official_http_origins": True, "non_http_sources_present": False})
    monkeypatch.setattr(GUEST, "repositories_probe", lambda: {"index_policy": {"stdout": "500 https://unknown.example/deepin Packages"}})
    monkeypatch.setattr(GUEST, "command", lambda *args, **kwargs: pytest.fail("must not install"))
    with pytest.raises(RuntimeError, match="INDEX_ORIGIN_NOT_VERIFIED"):
        GUEST.install_build_tools("disposable-build")
