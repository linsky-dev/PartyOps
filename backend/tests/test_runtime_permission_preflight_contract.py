"""实际配置权限不得跳过，兼容发布线必须保留真实依赖闭包。"""
from __future__ import annotations

import errno
import hashlib
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace

import pytest

from app import setup_wizard as wizard
from app import windows_runtime_identity as identity

ROOT = next(parent for parent in Path(__file__).resolve().parents if (parent / "packaging/windows/PartyOps.iss").is_file())
spec = importlib.util.spec_from_file_location("existing_runtime_permission_fixtures", ROOT / "backend/tests/test_175_rc3_runtime_permission_preflight.py")
fixtures = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixtures)


def configured(tmp_path, monkeypatch):
    root = tmp_path / "用户配置"
    root.mkdir()
    data = tmp_path / "中文 空格数据"
    config = root / "personal.env"
    config.write_text(f"PARTYOPS_DATA_DIR={wizard.shlex.quote(str(data))}\nPARTYOPS_PORT=19875\n", encoding="utf-8")
    mode = root / "mode.json"
    mode.write_text(json.dumps({"mode": "personal", "config_path": str(config)}), encoding="utf-8")
    monkeypatch.setattr(wizard, "config_root", lambda: root)
    return mode, config, data


@pytest.mark.parametrize("step", ["mode-stat", "mode-read", "config-stat", "config-read"])
def test_existing_personal_config_acl_denial_is_not_skipped_as_pass(tmp_path, monkeypatch, step):
    mode, config, _ = configured(tmp_path, monkeypatch)
    target = mode if step.startswith("mode") else config
    operation = "lstat" if step.endswith("stat") else "read_text"
    original = getattr(Path, operation)
    def denied(path, *args, **kwargs):
        if path == target:
            raise PermissionError(errno.EACCES, "synthetic denied")
        return original(path, *args, **kwargs)
    monkeypatch.setattr(Path, operation, denied)
    with pytest.raises(wizard.HostStartupError) as caught:
        wizard.preflight_configured_personal_runtime_access()
    assert caught.value.code == "RUNTIME_PERMISSION_DENIED"
    assert str(target) in caught.value.detail


def test_missing_data_config_cannot_be_filled_from_controller_environment(tmp_path, monkeypatch):
    _, config, data = configured(tmp_path, monkeypatch)
    config.write_text("PARTYOPS_PORT=19875\n", encoding="utf-8")
    monkeypatch.setenv("PARTYOPS_DATA_DIR", str(data))
    monkeypatch.setattr(wizard, "_preflight_personal_runtime_access", lambda *_: pytest.fail("缺失配置不得检查继承目录"))
    result = wizard.preflight_configured_personal_runtime_access()
    assert result["checked"] is False
    assert result["reason"] == "personal-config-needs-repair"


@pytest.mark.parametrize("number,code", [(errno.EACCES, "RUNTIME_PERMISSION_DENIED"), (errno.ENOSPC, "DATA_DIR_FULL"), (errno.EIO, "DATABASE_IO_FAILED")])
def test_data_probe_distinguishes_acl_space_and_io_and_cleans_partial_temp(tmp_path, monkeypatch, number, code):
    executable, config, data = fixtures._runtime_files(tmp_path)
    monkeypatch.setattr(wizard, "_executable", lambda _: executable)
    def failed_write(path, _content):
        path.with_suffix(path.suffix + ".tmp").write_bytes(b"partial")
        raise OSError(number, "synthetic IO failure")
    monkeypatch.setattr(wizard, "_write_private", failed_write)
    with pytest.raises(wizard.HostStartupError) as caught:
        wizard._preflight_personal_runtime_access(config, data)
    assert caught.value.code == code
    assert not list(data.glob(".partyops-runtime-permission-*"))


def legacy_runtime(tmp_path, monkeypatch, process="x86", native="arm64", version="10.0.19045"):
    monkeypatch.setattr(wizard, "sys", SimpleNamespace(maxsize=2**31-1 if process == "x86" else 2**63-1,
                                                     platform="win32", frozen=True, version_info=SimpleNamespace(major=3, minor=8)))
    executable = fixtures._write_win7_runtime_fixture(tmp_path)
    pe = bytearray(128)
    pe[:2] = b"MZ"
    pe[60:64] = (64).to_bytes(4, "little")
    pe[64:68] = b"PE\0\0"
    pe[68:70] = (0x14C if process == "x86" else 0x8664).to_bytes(2, "little")
    executable.write_bytes(pe)
    manifest_path = tmp_path / "release-manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest.update(schema_version=1, product="PartyOps", runtime_profile="legacy-core" if process == "x86" else "legacy-smart")
    for entry in manifest["files"]:
        entry["size"] = (tmp_path / entry["path"]).stat().st_size
        entry["sha256"] = hashlib.sha256((tmp_path / entry["path"]).read_bytes()).hexdigest()
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    monkeypatch.setattr(identity, "query_machine_types", lambda _: (process, native))
    monkeypatch.setattr(wizard.stdlib_platform, "version", lambda: version)
    monkeypatch.setattr(wizard, "_missing_win7_loader_apis", lambda: pytest.fail("现代 OS 不应用 Win7 补丁门禁"))
    return executable, manifest_path


@pytest.mark.parametrize("process,native,version", [("x86", "x86", "10.0.19045"), ("x86", "arm64", "10.0.19045"), ("x86", "arm64", "10.0.26200"), ("amd64", "arm64", "10.0.26200")])
def test_verified_legacy_package_uses_python38_closure_on_modern_windows(tmp_path, monkeypatch, process, native, version):
    executable, _ = legacy_runtime(tmp_path, monkeypatch, process, native, version)
    wizard._preflight_windows_runtime_dependencies(executable, force=True, windows_version=(10, 0))


@pytest.mark.parametrize("change", ["profile", "pe", "hash", "native-arm-no-package", "win10-x64-emulation", "missing-source", "missing-python38"])
def test_compatibility_never_relaxes_identity_or_closure(tmp_path, monkeypatch, change):
    process = "amd64" if change == "win10-x64-emulation" else "x86"
    executable, manifest_path = legacy_runtime(tmp_path, monkeypatch, process=process)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if change == "profile":
        manifest["runtime_profile"] = "full"
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    elif change == "pe":
        executable.write_bytes(b"MZ-tampered")
    elif change == "hash":
        content = bytearray(executable.read_bytes())
        content[100] = 1
        executable.write_bytes(content)
    elif change == "native-arm-no-package":
        monkeypatch.setattr(identity, "query_machine_types", lambda _: ("arm64", "arm64"))
    elif change == "missing-source":
        (tmp_path / "ucrt-source.json").unlink()
    elif change == "missing-python38":
        (tmp_path / "_internal/python38.dll").unlink()
    with pytest.raises(wizard.HostStartupError) as caught:
        wizard._preflight_windows_runtime_dependencies(executable, force=True, windows_version=(10, 0))
    assert caught.value.code in {"RUNTIME_PACKAGE_MISMATCH", "RUNTIME_DEPENDENCY_MISSING"}
