"""Desktop reset must keep the original account and reject ambiguous local requests."""
import io
import json
import shlex
from types import SimpleNamespace

import pytest

from app import recovery_cli


def test_desktop_reset_denies_unowned_directory(monkeypatch, tmp_path):
    monkeypatch.setattr(recovery_cli, "trusted_data_directory", lambda path: path)
    monkeypatch.setattr(recovery_cli, "personal_owner_scope", lambda path: False)
    monkeypatch.setattr(recovery_cli, "is_os_administrator", lambda: False)
    monkeypatch.setattr(recovery_cli.sys, "stdin", io.StringIO(json.dumps({
        "password": "NewPassword123!", "confirmation": "本机应急恢复",
    })))
    assert recovery_cli.main(["desktop-reset", "--data-dir", str(tmp_path)]) == 2


def test_desktop_reset_uses_pipe_and_returns_username(monkeypatch, tmp_path, capsys):
    monkeypatch.setattr(recovery_cli, "trusted_data_directory", lambda path: path)
    monkeypatch.setattr(recovery_cli, "personal_owner_scope", lambda path: True)
    monkeypatch.setattr(recovery_cli.sys, "stdin", io.StringIO(json.dumps({
        "password": "NewPassword123!", "confirmation": "本机应急恢复",
    })))
    calls = []
    def run(path, action, identity, proof, **kwargs):
        calls.append((path, action, kwargs))
        return "admin"
    monkeypatch.setattr(recovery_cli, "_run", run)
    assert recovery_cli.main(["desktop-reset", "--data-dir", str(tmp_path)]) == 0
    assert json.loads(capsys.readouterr().out) == {"username": "admin"}
    assert calls == [(tmp_path, "desktop-reset", {
        "personal_scope": True, "new_password": "NewPassword123!",
    })]


@pytest.mark.parametrize("payload", [
    "",
    "[]",
    "{}",
    '{"password":42,"confirmation":"本机应急恢复"}',
    '{"password":"Synthetic123","confirmation":"incorrect"}',
    '{"password":"Synthetic123","confirmation":"本机应急恢复","extra":true}',
])
def test_desktop_reset_rejects_invalid_pipe_requests(monkeypatch, tmp_path, capsys, payload):
    monkeypatch.setattr(recovery_cli, "trusted_data_directory", lambda path: path)
    monkeypatch.setattr(recovery_cli, "personal_owner_scope", lambda path: True)
    monkeypatch.setattr(recovery_cli.sys, "stdin", io.StringIO(payload))
    monkeypatch.setattr(recovery_cli, "_run", lambda *a, **k: pytest.fail("invalid payload reached database"))
    assert recovery_cli.main(["desktop-reset", "--data-dir", str(tmp_path)]) == 2
    assert not capsys.readouterr().out


def test_desktop_reset_transaction_error_does_not_echo_password(monkeypatch, tmp_path, capsys):
    password = "Synthetic-Private-Password!"
    monkeypatch.setattr(recovery_cli, "trusted_data_directory", lambda path: path)
    monkeypatch.setattr(recovery_cli, "personal_owner_scope", lambda path: True)
    monkeypatch.setattr(recovery_cli.sys, "stdin", io.StringIO(json.dumps({
        "password": password, "confirmation": "本机应急恢复",
    })))
    def failure(*args, **kwargs):
        raise RuntimeError(password)
    monkeypatch.setattr(recovery_cli, "_run", failure)
    assert recovery_cli.main(["desktop-reset", "--data-dir", str(tmp_path)]) == 2
    result = capsys.readouterr()
    assert password not in result.out + result.err
    assert not result.out


@pytest.mark.parametrize("case", [
    "missing", "oversize", "duplicate", "empty-value", "relative",
    "wrong-mode", "different-path", "valid",
])
def test_team_desktop_reset_is_bound_to_saved_host_config(monkeypatch, tmp_path, capsys, case):
    data = tmp_path / "实际业务"
    data.mkdir()
    config = tmp_path / "PartyOps" / "partyops.env"
    config.parent.mkdir()
    content = (
        "PARTYOPS_MODE=host\n"
        + "PARTYOPS_DATA_DIR=" + shlex.quote(str(data)) + "\n"
        + "# unrelated setting must not be executed\nUNRELATED=synthetic\n"
    )
    if case == "oversize":
        content += "#" * 65537
    elif case == "duplicate":
        content += "PARTYOPS_MODE=host\n"
    elif case == "empty-value":
        content = "PARTYOPS_MODE=host\nPARTYOPS_DATA_DIR=\n"
    elif case == "relative":
        content = "PARTYOPS_MODE=host\nPARTYOPS_DATA_DIR=relative\n"
    elif case == "wrong-mode":
        content = content.replace("PARTYOPS_MODE=host", "PARTYOPS_MODE=personal")
    elif case == "different-path":
        other = tmp_path / "other"
        other.mkdir()
        content = "PARTYOPS_MODE=host\nPARTYOPS_DATA_DIR=" + shlex.quote(str(other)) + "\n"
    if case != "missing":
        config.write_text(content, encoding="utf-8")
    monkeypatch.setattr(recovery_cli, "os", SimpleNamespace(
        name="nt", getenv=lambda *args: str(tmp_path),
    ))
    monkeypatch.setattr(recovery_cli, "trusted_data_directory", lambda path: path)
    monkeypatch.setattr(recovery_cli, "personal_owner_scope", lambda path: False)
    monkeypatch.setattr(recovery_cli, "is_os_administrator", lambda: True)
    monkeypatch.setattr(recovery_cli.sys, "stdin", io.StringIO(json.dumps({
        "password": "Synthetic-Host-Password!",
        "confirmation": "本机应急恢复",
    })))
    calls = []
    def run(path, action, identity, proof, **kwargs):
        calls.append((path, kwargs))
        return "team-admin"
    monkeypatch.setattr(recovery_cli, "_run", run)
    result = recovery_cli.main(["desktop-reset", "--data-dir", str(data)])
    output = capsys.readouterr()
    if case == "valid":
        assert result == 0
        assert json.loads(output.out) == {"username": "team-admin"}
        assert calls == [(data, {
            "personal_scope": False, "new_password": "Synthetic-Host-Password!",
        })]
    else:
        assert result == 2 and not calls and not output.out
        assert "Synthetic-Host-Password!" not in output.err


def test_redirected_recovery_help_on_legacy_windows_codepage(monkeypatch):
    output = io.BytesIO()
    stream = io.TextIOWrapper(output, encoding="cp1252")
    monkeypatch.setattr(recovery_cli.sys, "stdout", stream)
    monkeypatch.setattr(recovery_cli.sys, "stdin", io.StringIO())
    monkeypatch.setattr(recovery_cli.sys, "stderr", io.StringIO())
    with pytest.raises(SystemExit) as result:
        recovery_cli.main(["--help"])
    assert result.value.code == 0
    stream.flush()
    assert "主机管理员账号恢复" in output.getvalue().decode("utf-8")
    assert "desktop-reset" in output.getvalue().decode("utf-8")


def test_recovery_utf8_redirected_input_and_interactive_stream(monkeypatch):
    value = '{"password":"合成密码123!","confirmation":"本机应急恢复"}'
    stream = io.TextIOWrapper(io.BytesIO(value.encode("utf-8")), encoding="cp1252")
    calls = []
    interactive = SimpleNamespace(isatty=lambda: True, reconfigure=lambda **kw: calls.append(kw))
    monkeypatch.setattr(recovery_cli.sys, "stdin", stream)
    monkeypatch.setattr(recovery_cli.sys, "stdout", interactive)
    monkeypatch.setattr(recovery_cli.sys, "stderr", None)
    recovery_cli._configure_recovery_stdio()
    assert stream.read() == value
    assert calls == []


@pytest.mark.parametrize("case,expected", [
    ("valid", True), ("mac", True), ("windows", True), ("missing", False),
    ("wrong-owner", False), ("world-writable", False), ("link", False),
    ("oversize-mode", False), ("oversize-config", False), ("wrong-mode", False),
    ("wrong-config", False), ("duplicate", False), ("empty-value", False),
    ("relative", False), ("wrong-data", False), ("malformed", False),
])
def test_personal_scope_binds_config_and_private_owner_on_each_os(monkeypatch, tmp_path, case, expected):
    import stat
    from pathlib import Path

    data = tmp_path / "业务 数据"
    data.mkdir()
    (data / "partyops.db").write_bytes(b"synthetic")
    platform = "darwin" if case == "mac" else "linux"
    root = (tmp_path / "Library" / "Application Support" / "PartyOps" / "Config"
            if platform == "darwin" else tmp_path / ".config" / "partyops")
    root.mkdir(parents=True)
    mode, config = root / "mode.json", root / "personal.env"
    mode_text = json.dumps({"mode": "personal", "config_path": str(config)})
    config_text = "PARTYOPS_MODE=personal\nPARTYOPS_DATA_DIR=" + shlex.quote(str(data)) + "\n"
    if case == "wrong-mode":
        mode_text = '{"mode":"host"}'
    elif case == "wrong-config":
        mode_text = json.dumps({"mode": "personal", "config_path": str(root / "other.env")})
    elif case == "malformed":
        mode_text = "[]"
    elif case == "oversize-mode":
        mode_text += " " * 8193
    elif case == "oversize-config":
        config_text += " " * 65537
    elif case == "duplicate":
        config_text += "PARTYOPS_MODE=personal\n"
    elif case == "empty-value":
        config_text = "PARTYOPS_MODE=personal\nPARTYOPS_DATA_DIR=\n"
    elif case == "relative":
        config_text = "PARTYOPS_MODE=personal\nPARTYOPS_DATA_DIR=relative\n"
    elif case == "wrong-data":
        other = tmp_path / "other"
        other.mkdir()
        config_text = "PARTYOPS_MODE=personal\nPARTYOPS_DATA_DIR=" + shlex.quote(str(other)) + "\n"
    if case != "missing":
        mode.write_text(mode_text, encoding="utf-8")
    config.write_text(config_text, encoding="utf-8")
    original_lstat = Path.lstat
    owned = {root, mode, config, data, data / "partyops.db"}

    def synthetic_lstat(p, *args, **kwargs):
        result = original_lstat(p, *args, **kwargs)
        if p not in owned:
            return result
        # Windows cannot reproduce POSIX mode bits; keep real existence/size
        # while simulating only Unix ownership and file type.
        bits = (stat.S_IFDIR if p in {root, data} else stat.S_IFREG) | 0o700
        if p == data and case == "world-writable":
            bits |= 0o002
        if p == data and case == "link":
            bits = stat.S_IFLNK | 0o700
        return SimpleNamespace(st_mode=bits, st_uid=123 if p == data and case == "wrong-owner" else 456,
                               st_size=result.st_size)

    monkeypatch.setattr(recovery_cli, "os", SimpleNamespace(
        name="nt" if case == "windows" else "posix", getenv=lambda key, default: default,
        geteuid=lambda: 456,
    ))
    monkeypatch.setattr(recovery_cli, "sys", SimpleNamespace(platform=platform))
    monkeypatch.setattr(Path, "home", classmethod(lambda cls: tmp_path))
    monkeypatch.setattr(Path, "lstat", synthetic_lstat)
    monkeypatch.setattr(recovery_cli, "windows_personal_owner_scope", lambda p: p == data)
    assert recovery_cli.personal_owner_scope(data) is expected

@pytest.mark.parametrize("mode_text,expected", [
    (None,True), ('{"mode":"host"}',True), ('{"mode":"personal"}',False),
    ('{"mode":"client"}',False), ('[]',False), ('bad json',False),
])
def test_machine_host_config_legacy_and_inactive_mode(monkeypatch, tmp_path, mode_text, expected):
    from pathlib import Path
    root = tmp_path / "PartyOps"
    root.mkdir()
    data = tmp_path / "业务 数据"
    data.mkdir()
    (root / "partyops.env").write_text("PARTYOPS_MODE=host\nPARTYOPS_DATA_DIR=" + shlex.quote(str(data)) + "\n", encoding="utf-8")
    if mode_text is not None:
        (root / "mode.json").write_text(mode_text, encoding="utf-8")
    monkeypatch.setattr(recovery_cli, "os", SimpleNamespace(name="nt", getenv=lambda *args: str(tmp_path)))
    assert recovery_cli.windows_host_recovery_available() is expected
    if expected:
        assert recovery_cli.windows_host_recovery_config() == (root / "partyops.env", data)


@pytest.mark.parametrize("case", ["posix", "bad-mode-binding", "mode-directory", "oversize-mode", "unrelated-config-line"])
def test_machine_host_config_rejects_unsafe_saved_mode(monkeypatch, tmp_path, case):
    root = tmp_path / "PartyOps"
    root.mkdir()
    data = tmp_path / "data"
    data.mkdir()
    config = root / "partyops.env"
    config.write_text("# comment\nIGNORED=x\nPARTYOPS_MODE=host\nPARTYOPS_DATA_DIR=" + shlex.quote(str(data)) + "\n", encoding="utf-8-sig")
    mode = root / "mode.json"
    if case == "bad-mode-binding":
        mode.write_text(json.dumps({"mode": "host", "config_path": str(root / "other.env")}), encoding="utf-8")
    elif case == "mode-directory":
        mode.mkdir()
    elif case == "oversize-mode":
        mode.write_text(" " * 8193, encoding="utf-8")
    elif case == "unrelated-config-line":
        mode.write_text(json.dumps({"mode": "host", "config_path": str(config)}), encoding="utf-8")
    monkeypatch.setattr(recovery_cli, "os", SimpleNamespace(name="posix" if case == "posix" else "nt", getenv=lambda *args: str(tmp_path)))
    if case == "unrelated-config-line":
        assert recovery_cli.windows_host_recovery_config() == (config, data)
    else:
        with pytest.raises(recovery_cli.RecoveryCLIError):
            recovery_cli.windows_host_recovery_config()
