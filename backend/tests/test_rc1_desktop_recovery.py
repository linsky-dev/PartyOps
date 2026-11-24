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
