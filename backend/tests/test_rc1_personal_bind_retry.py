"""RC1 custom-directory personal startup: bounded bind retries."""
from pathlib import Path
from types import SimpleNamespace

import pytest

from app import setup_wizard as wizard


def test_personal_child_bind_failure_retries_without_changing_data(tmp_path, monkeypatch):
    data = tmp_path / "自定义 数据"
    data.mkdir()
    config = tmp_path / "personal.env"
    config.write_text("PARTYOPS_PORT=18775\n", encoding="utf-8")
    calls = []
    child = SimpleNamespace(pid=42, poll=lambda: 1)
    monkeypatch.setattr(wizard, "_loopback_port_available", lambda port: True)
    monkeypatch.setattr(wizard, "_select_alternative_personal_port", lambda port: port + 1)
    monkeypatch.setattr(wizard, "_rewrite_personal_port", lambda path, port: {
        "PARTYOPS_PORT": str(port), "PARTYOPS_DATA_DIR": str(data),
    })
    monkeypatch.setattr(wizard, "_spawn", lambda cmd, log, env: (
        calls.append((log, env["PARTYOPS_PORT"])) or child
    ))
    monkeypatch.setattr(wizard, "_record_personal_process", lambda *_: None)

    def health(host, port, **kwargs):
        if port == 18775:
            raise wizard.HostStartupError(wizard.PORT_IN_USE, "bind failure")
        return f"http://{host}:{port}"

    monkeypatch.setattr(wizard, "wait_for_host_health", health)
    result = wizard._launch_new_personal_process(
        config, Path("partyops"), data, 18775, {"PARTYOPS_PORT": "18775"},
    )
    assert result == "http://127.0.0.1:18776"
    assert calls == [(data / "launcher.log", "18775"), (data / "launcher.log", "18776")]


@pytest.mark.parametrize("code,exit_code", [
    (wizard.RUNTIME_PERMISSION_DENIED, 1), (wizard.PORT_IN_USE, None),
])
def test_personal_retry_never_restarts_live_child_or_unrelated_error(tmp_path, monkeypatch, code, exit_code):
    child = SimpleNamespace(pid=42, poll=lambda: exit_code)
    monkeypatch.setattr(wizard, "_loopback_port_available", lambda port: True)
    monkeypatch.setattr(wizard, "_spawn", lambda *_: child)
    def health(*args, **kwargs):
        raise wizard.HostStartupError(code, "failure")
    def unexpected(*args):
        pytest.fail("must not select another port")
    monkeypatch.setattr(wizard, "wait_for_host_health", health)
    monkeypatch.setattr(wizard, "_select_alternative_personal_port", unexpected)
    with pytest.raises(wizard.HostStartupError) as error:
        wizard._launch_new_personal_process(tmp_path / "personal.env", Path("partyops"), tmp_path, 18775, {})
    assert error.value.code == code


def test_port_rewrite_does_not_use_inherited_data_directory(tmp_path, monkeypatch):
    import shlex

    data = tmp_path / "中文 新目录"
    stale = tmp_path / "old-data"
    config = tmp_path / "personal.env"
    config.write_text(
        "PARTYOPS_MODE=personal\nPARTYOPS_PORT=18775\n"
        + "PARTYOPS_DATA_DIR=" + shlex.quote(str(data)) + "\n",
        encoding="utf-8",
    )
    monkeypatch.setenv("PARTYOPS_DATA_DIR", str(stale))
    env = wizard._rewrite_personal_port(config, 18779)
    assert env["PARTYOPS_DATA_DIR"] == str(data)
    assert env["PARTYOPS_PORT"] == "18779"
    assert env["PARTYOPS_AGENT_PORT"] == "18780"


def test_retry_uses_available_port_before_start(tmp_path, monkeypatch):
    child = SimpleNamespace(pid=42, poll=lambda: 0)
    calls = []
    monkeypatch.setattr(wizard, "_loopback_port_available", lambda port: port != 18775)
    monkeypatch.setattr(wizard, "_select_alternative_personal_port", lambda _: 18779)
    monkeypatch.setattr(wizard, "_rewrite_personal_port", lambda _, port: {"PARTYOPS_PORT": str(port)})
    monkeypatch.setattr(wizard, "_spawn", lambda cmd, log, env: calls.append(env["PARTYOPS_PORT"]) or child)
    monkeypatch.setattr(wizard, "_record_personal_process", lambda *_: None)
    monkeypatch.setattr(wizard, "wait_for_host_health", lambda host, port, **_: f"http://{host}:{port}")
    assert wizard._launch_new_personal_process(tmp_path / "personal.env", Path("partyops"), tmp_path, 18775, {}) == "http://127.0.0.1:18779"
    assert calls == ["18779"]


def test_retry_exhaustion_keeps_original_diagnostic(tmp_path, monkeypatch):
    child = SimpleNamespace(pid=42, poll=lambda: 1)
    calls = []
    monkeypatch.setattr(wizard, "_loopback_port_available", lambda _: True)
    monkeypatch.setattr(wizard, "_select_alternative_personal_port", lambda port: port + 1)
    monkeypatch.setattr(wizard, "_rewrite_personal_port", lambda _, port: {"PARTYOPS_PORT": str(port)})
    monkeypatch.setattr(wizard, "_spawn", lambda cmd, log, env: calls.append(env["PARTYOPS_PORT"]) or child)
    monkeypatch.setattr(wizard, "_record_personal_process", lambda *_: pytest.fail("unhealthy child recorded"))
    def failed(*args, **kwargs):
        raise wizard.HostStartupError(wizard.PORT_IN_USE, "occupied")
    monkeypatch.setattr(wizard, "wait_for_host_health", failed)
    with pytest.raises(wizard.HostStartupError) as error:
        wizard._launch_new_personal_process(tmp_path / "personal.env", Path("partyops"), tmp_path, 18775, {"PARTYOPS_PORT": "18775"})
    assert error.value.code == wizard.PORT_IN_USE
    assert calls == ["18775", "18776", "18777"]
