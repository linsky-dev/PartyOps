"""Desktop reset must keep the original account and reject ambiguous local requests."""
import io
import json

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
