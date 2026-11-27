"""设备安全监听器、证书轮换和失败重试的防御性覆盖。"""

from __future__ import annotations

import threading
from types import SimpleNamespace

import pytest
from fastapi import Request

from app import agent_tls, client_agent
from app import main as app_main
from app.database import db_runtime
from app.models import Device
from app.problems import ProblemException
from app.security import hash_token


def test_agent_listener_rejects_business_during_startup(client, monkeypatch) -> None:
    monkeypatch.setattr(app_main, "_runtime_ready", threading.Event())
    monkeypatch.setattr(app_main.settings, "tls_enabled", True)
    response = client.get(
        f"http://testserver:{app_main.settings.agent_port}/api/v1/devices/commands",
        headers={"X-PartyOps-Device-Token": "synthetic-device"},
    )
    assert response.status_code == 503
    assert response.json()["code"] == "HOST_STARTING"


def test_pending_rotation_preserves_old_certificate_until_new_proof(client, admin, monkeypatch) -> None:
    old_fingerprint, new_fingerprint = "1" * 64, "2" * 64
    token = "synthetic-rotation-credential"
    settings = SimpleNamespace(tls_enabled=True, agent_port=18766)
    monkeypatch.setattr(agent_tls, "get_settings", lambda: settings)
    issued = []

    def issue(_settings, device_id, csr):
        issued.append((device_id, csr))
        return {"certificate_fingerprint": new_fingerprint, "certificate_pem": "synthetic"}

    monkeypatch.setattr(agent_tls, "issue_device_certificate", issue)

    def connection(fingerprint):
        return Request({
            "type": "http", "server": ("localhost", settings.agent_port),
            "state": {"partyops_peer_cert_sha256": fingerprint},
        })

    with db_runtime.session_factory() as db:
        device = Device(
            name="轮换测试终端", agent_token_hash=hash_token(token),
            certificate_fingerprint=old_fingerprint, created_by=admin["id"],
        )
        db.add(device)
        db.commit()
        candidate = agent_tls.stage_device_certificate(db, device, "synthetic-csr")
        db.commit()
        assert agent_tls.stage_device_certificate(db, device, "synthetic-csr") == candidate
        assert len(issued) == 1
        agent_tls.check_device_tls(connection(old_fingerprint), db, token)
        assert device.certificate_fingerprint == old_fingerprint
        agent_tls.check_device_tls(connection(new_fingerprint), db, token)
        assert device.certificate_fingerprint == new_fingerprint
        with pytest.raises(ProblemException) as denied:
            agent_tls.check_device_tls(connection(old_fingerprint), db, token)
        assert denied.value.code == "DEVICE_CERTIFICATE_MISMATCH"


def test_rotation_config_write_failure_preserves_previous_keys(monkeypatch, tmp_path) -> None:
    path = tmp_path / "device.json"
    config = {"device_id": "synthetic-device", "key_file": "previous.key"}
    client_agent._save_config(path, config)
    previous = path.read_bytes()
    monkeypatch.setattr(client_agent, "_json_request", lambda *_a, **_k: {
        "certificate_pem": "synthetic-certificate", "ca_certificate_pem": "synthetic-ca"
    })
    monkeypatch.setattr(client_agent, "_create_ssl_context", lambda _config: None)

    def fail(*_args):
        raise OSError("合成配置写入错误")

    monkeypatch.setattr(client_agent, "_save_config", fail)
    with pytest.raises(OSError, match="合成配置写入错误"):
        client_agent.rotate_device_certificate("https://localhost", "synthetic", config, path)
    assert path.read_bytes() == previous
    assert config["key_file"] == "previous.key"
    assert not list((tmp_path / "pki").iterdir())



def test_business_device_version_lookup_runs_outside_event_loop(client, admin, monkeypatch):
    import asyncio

    calls = []
    def lookup(request, db, *, allow_ip_fallback=False):
        assert allow_ip_fallback is True
        with pytest.raises(RuntimeError, match="no running event loop"):
            asyncio.get_running_loop()
        calls.append(request.url.path)
        return None

    monkeypatch.setattr(app_main, "request_device", lookup)
    response = client.get("/api/v1/users")
    assert response.status_code == 200, response.text
    assert calls == ["/api/v1/users"]
