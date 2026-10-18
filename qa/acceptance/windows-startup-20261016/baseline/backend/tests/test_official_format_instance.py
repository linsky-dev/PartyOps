"""实际本机独占套接字反例；只用临时目录、随机端口与合成票据。"""

from __future__ import annotations

import asyncio
import json
import os
import socket
from types import SimpleNamespace

import pytest
from fastapi import FastAPI

from app import official_format_instance as instance
from app import official_format_service as service
from app import setup_wizard, startup_selftest, windows_host_status
from app.official_format import OfficialFormatError
from app.problems import ProblemException
from app.routers import official_format as routes

SECRET_A = "a" * 64
SECRET_B = "b" * 64


def test_exclusive_formatter_collision_rebinds_without_touching_first(tmp_path):
    first = service.OfficialFormatLocalService(secret=SECRET_A, config_dir=tmp_path / "first", port=0).start()
    second = None
    try:
        with pytest.raises(OfficialFormatError, match="独占"):
            rejected = service.OfficialFormatLocalService(secret=SECRET_B, config_dir=tmp_path / "reject", port=first.port)
            try:
                rejected.start()
            finally:
                rejected.close()
        second = instance.start_instance_formatter(tmp_path / "第二个 中文目录", first.port, SECRET_B)
        assert first.ready and second.ready and second.port != first.port
        assert first.instance_id != second.instance_id
        startup_selftest._probe_owned_formatter(tmp_path / "第二个 中文目录", os.getpid())
        probe = startup_selftest._read_json(f"http://127.0.0.1:{first.port}/health")
        assert probe["instance_id"] == first.instance_id
    finally:
        if second is not None:
            second.close()
        first.close()


def test_record_is_port_hint_only_and_restart_has_new_identity(tmp_path):
    first = instance.start_instance_formatter(tmp_path, 0, SECRET_A)
    first_port, first_id = first.port, first.instance_id
    first.close()
    second = instance.start_instance_formatter(tmp_path, 0, SECRET_A)
    try:
        assert second.instance_id != first_id
        assert second.port == first_port  # 无外来占用时正常重启保留端口。
        record = json.loads(instance.endpoint_path(tmp_path).read_text(encoding="utf-8"))
        assert record["pid"] == os.getpid() and record["instance_id"] == second.instance_id
    finally:
        second.close()


@pytest.mark.parametrize("change", ["pid", "instance_id", "data_binding", "port"])
def test_startup_selftest_rejects_stale_or_foreign_endpoint(tmp_path, change):
    owned = instance.start_instance_formatter(tmp_path, 0, SECRET_A)
    try:
        path = instance.endpoint_path(tmp_path)
        payload = json.loads(path.read_text(encoding="utf-8"))
        payload[change] = {"pid": os.getpid() + 1, "instance_id": "f" * 32,
                           "data_binding": "f" * 64, "port": 80}[change]
        path.write_text(json.dumps(payload), encoding="utf-8")
        with pytest.raises(RuntimeError, match="实例|子进程|身份"):
            startup_selftest._probe_owned_formatter(tmp_path, os.getpid())
    finally:
        owned.close()


@pytest.mark.parametrize("previous,main,expected", [({}, 18825, 18827), ({}, 65534, 18768),
    ({"PARTYOPS_OFFICIAL_FORMAT_PORT": "19000"}, 18825, 19000),
    ({"PARTYOPS_OFFICIAL_FORMAT_PORT": "18825"}, 18825, 18827)])
def test_personal_and_upgrade_config_keep_independent_port(previous, main, expected):
    assert instance.configured_formatter_port(previous, main) == expected


@pytest.mark.parametrize("bad", ["garbage", "0", "65536"])
def test_invalid_explicit_formatter_config_fails_visibly(bad):
    with pytest.raises(ValueError, match="公文排版端口"):
        instance.configured_formatter_port({"PARTYOPS_OFFICIAL_FORMAT_PORT": bad}, 18825)


def test_personal_port_rewrite_migrates_old_config_and_preserves_upgrade_values(tmp_path):
    path = tmp_path / "personal.env"
    original = "# 用户注释\nPARTYOPS_PORT=18825\nPARTYOPS_BOOTSTRAP_TOKEN=unchanged\nPARTYOPS_DATA_DIR='D:/业务 数据'\n"
    path.write_text(original, encoding="utf-8")
    first = setup_wizard._rewrite_personal_port(path, 18825)
    assert first["PARTYOPS_OFFICIAL_FORMAT_PORT"] == "18827"
    second = setup_wizard._rewrite_personal_port(path, 18835)
    assert second["PARTYOPS_OFFICIAL_FORMAT_PORT"] == "18827"
    assert second["PARTYOPS_BOOTSTRAP_TOKEN"] == "unchanged" and second["PARTYOPS_DATA_DIR"] == "D:/业务 数据"
    assert "# 用户注释" in path.read_text(encoding="utf-8")


def test_local_ticket_requires_live_owned_formatter_not_stale_settings(monkeypatch, tmp_path):
    owned = instance.start_instance_formatter(tmp_path, 0, SECRET_A)
    state = SimpleNamespace(official_formatter=owned)
    request = SimpleNamespace(headers={"Origin": "http://127.0.0.1:18825"}, app=SimpleNamespace(state=state))
    monkeypatch.setattr(routes, "get_settings", lambda: SimpleNamespace(environment="production", official_format_port=18768))
    monkeypatch.setattr(routes, "request_device", lambda *_: None)
    monkeypatch.setattr(routes, "is_host_local_request", lambda *_: True)
    monkeypatch.setattr(routes, "ensure_device_context_secret", lambda *_: SECRET_A)
    db = SimpleNamespace(commit=lambda: None)
    payload = routes.LocalFormatTicketCreate(origin="http://127.0.0.1:18825")
    try:
        result = routes.create_local_format_ticket(payload, request, SimpleNamespace(id="fixture"), db)
        assert result["local_base_url"] == f"http://127.0.0.1:{owned.port}"
        state.official_formatter = None
        with pytest.raises(ProblemException) as error:
            routes.create_local_format_ticket(payload, request, SimpleNamespace(id="fixture"), db)
        assert error.value.code == "LOCAL_FORMAT_NOT_READY"
        state.official_formatter = owned
        owned.close()
        with pytest.raises(ProblemException):
            routes.create_local_format_ticket(payload, request, SimpleNamespace(id="fixture"), db)
    finally:
        if owned.ready:
            owned.close()


def test_config_record_failure_closes_new_listener_and_reports_terminal(monkeypatch, tmp_path):
    port = startup_selftest._reserve_loopback_port()
    monkeypatch.setattr(instance, "os", SimpleNamespace(getpid=os.getpid,
        replace=lambda *_: (_ for _ in ()).throw(PermissionError("fixture"))))
    with pytest.raises(OfficialFormatError) as error:
        instance.start_instance_formatter(tmp_path, port, SECRET_A)
    assert error.value.code == "LOCAL_FORMAT_CONFIG_WRITE_FAILED"
    assert windows_host_status.classify_runtime_failure("[LOCAL_FORMAT_CONFIG_WRITE_FAILED] fixture") == error.value.code
    with socket.socket() as check:
        check.bind(("127.0.0.1", port))


def test_formatter_config_directory_failure_is_not_reported_as_port_collision(tmp_path):
    blocked = tmp_path / "not-a-directory"
    blocked.write_text("fixture", encoding="utf-8")
    with pytest.raises(OfficialFormatError) as error:
        instance.start_instance_formatter(blocked, 0, SECRET_A)
    assert error.value.code == "LOCAL_FORMAT_CONFIG_WRITE_FAILED"


def test_personal_configuration_upgrade_preserves_formatter_port_and_business_path(monkeypatch, tmp_path):
    config = tmp_path / "config"
    data = tmp_path / "中文 业务"
    monkeypatch.setattr(setup_wizard, "config_root", lambda: config)
    monkeypatch.setattr(setup_wizard, "_validate_personal_data_dir", lambda value: value.resolve())
    monkeypatch.setattr(setup_wizard, "deactivate_windows_host_for_user_mode", lambda: None)
    monkeypatch.setattr(setup_wizard, "clear_windows_client_autostart", lambda: None)
    monkeypatch.setattr(setup_wizard, "install_windows_personal_autostart", lambda: None)
    path = setup_wizard.write_personal_config(data, 18825)
    original = setup_wizard.load_host_environment(path)
    assert original["PARTYOPS_OFFICIAL_FORMAT_PORT"] == "18827"
    setup_wizard.write_personal_config(data, 18835)
    upgraded = setup_wizard.load_host_environment(path)
    assert upgraded["PARTYOPS_OFFICIAL_FORMAT_PORT"] == "18827"
    assert upgraded["PARTYOPS_DATA_DIR"] == original["PARTYOPS_DATA_DIR"]
    assert upgraded["PARTYOPS_BOOTSTRAP_TOKEN"] == original["PARTYOPS_BOOTSTRAP_TOKEN"]
    assert "PARTYOPS_OFFICIAL_FORMAT_PORT" in setup_wizard.LINUX_LAUNCH_ENV_KEYS


def test_startup_formatter_failure_is_visible_and_releases_lock(monkeypatch, tmp_path):
    from app import main

    calls = []
    class Lock:
        def __init__(self, _path):
            pass

        def acquire(self):
            calls.append("acquire")

        def release(self):
            calls.append("release")

    class Session:
        def __enter__(self):
            return self

        def __exit__(self, *_args):
            return False

    def fail(*_args):
        raise OfficialFormatError("LOCAL_FORMAT_PORT_IN_USE", "排版启动失败", "fixture")

    monkeypatch.setattr(main, "DataDirectoryInstanceLock", Lock)
    monkeypatch.setattr(main, "_initialize_runtime", lambda: None)
    monkeypatch.setattr(main, "settings", SimpleNamespace(data_dir=tmp_path, official_format_port=18768))
    monkeypatch.setattr(main, "db_runtime", SimpleNamespace(session_factory=Session, dispose=lambda: calls.append("dispose")))
    monkeypatch.setattr(main, "ensure_device_context_secret", lambda *_: SECRET_A)
    monkeypatch.setattr(instance, "start_instance_formatter", fail)
    monkeypatch.setattr(windows_host_status, "write_service_status", lambda _path, **kwargs: calls.append(kwargs))
    app = FastAPI()
    async def exercise():
        async with main.lifespan(app):
            pytest.fail("排版启动失败时不能继续发布主进程健康接口")
    with pytest.raises(OfficialFormatError):
        asyncio.run(exercise())
    assert getattr(app.state, "official_formatter", None) is None
    assert calls[-2:] == ["dispose", "release"]
    assert any(isinstance(item, dict) and item["code"] == "LOCAL_FORMAT_PORT_IN_USE" for item in calls)


def test_client_ticket_stays_on_request_computer_default_port(monkeypatch):
    monkeypatch.setattr(routes, "get_settings", lambda: SimpleNamespace(environment="production", official_format_port=19876))
    monkeypatch.setattr(routes, "request_device", lambda *_: SimpleNamespace(
        id="device", credential_state="active", agent_token_hash=SECRET_A))
    payload = routes.LocalFormatTicketCreate(origin="http://host.example:18765")
    result = routes.create_local_format_ticket(payload, SimpleNamespace(headers={"Origin": payload.origin}),
        SimpleNamespace(id="user"), SimpleNamespace(commit=lambda: None))
    assert result["local_base_url"] == "http://127.0.0.1:18768"


def test_browser_csp_contains_actual_local_and_client_formatter_ports(client):
    from app import main

    formatter = main.app.state.official_formatter
    assert formatter.ready and main.settings.official_format_port == formatter.port
    response = client.get("/api/v1/health")
    policy = response.headers["Content-Security-Policy"]
    assert f"http://127.0.0.1:{formatter.port}" in policy
    assert "http://127.0.0.1:18768" in policy
