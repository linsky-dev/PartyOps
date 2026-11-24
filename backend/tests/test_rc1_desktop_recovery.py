"""Desktop reset must keep the original account and reject ambiguous local requests."""
import io
import json

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
