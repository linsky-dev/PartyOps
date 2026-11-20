"""Agent mTLS 信任传递、设备 CSR 与两阶段换证的回环边界回归。"""

from __future__ import annotations

import hashlib
import json
import socket
import ssl
import threading
import time
import urllib.error
from datetime import datetime, timedelta
from pathlib import Path
from uuid import uuid4

import pytest
import uvicorn
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.x509.oid import NameOID
from fastapi import Request

from app import agent_tls, client_agent
from app.agent_tls import ClientCertificateH11Protocol
from app.config import get_settings
from app.database import db_runtime
from app.models import Device, SystemSetting, utcnow
from app.pki import ensure_tls_material, issue_device_certificate
from app.problems import ProblemException
from app.security import hash_token


def _make_csr(common_name: str, key: rsa.RSAPrivateKey) -> str:
    return (
        x509.CertificateSigningRequestBuilder()
        .subject_name(x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, common_name)]))
        .sign(key, hashes.SHA256())
        .public_bytes(serialization.Encoding.PEM)
        .decode("ascii")
    )


def _request(fingerprint: str, port: int) -> Request:
    return Request(
        {
            "type": "http",
            "server": ("127.0.0.1", port),
            "state": {"partyops_peer_cert_sha256": fingerprint},
        }
    )


def test_rotation_retries_over_real_loopback_mtls_with_the_old_certificate(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    """失败的候选轮换不能切走旧证书，后续请求仍经同一 CA 和旧客户端证书。"""

    settings = get_settings().model_copy(
        update={"data_dir": tmp_path / "host", "environment": "test"}
    )
    material = ensure_tls_material(settings)
    device_id = f"synthetic-{uuid4().hex}"
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    bundle = issue_device_certificate(settings, device_id, _make_csr(device_id, key))
    client_pki = tmp_path / "client-pki"
    client_pki.mkdir()
    cert_path = client_pki / "device.pem"
    key_path = client_pki / "device.key"
    ca_path = client_pki / "ca.pem"
    cert_path.write_text(bundle["certificate_pem"], encoding="ascii")
    key_path.write_bytes(
        key.private_bytes(
            serialization.Encoding.PEM,
            serialization.PrivateFormat.PKCS8,
            serialization.NoEncryption(),
        )
    )
    ca_path.write_text(bundle["ca_certificate_pem"], encoding="ascii")

    observed_fingerprints: list[str] = []

    async def rotate_probe(scope, _receive, send) -> None:
        observed_fingerprints.append(scope["state"]["partyops_peer_cert_sha256"])
        body = json.dumps({"code": "SYNTHETIC_ROTATION_BUSY"}).encode("utf-8")
        await send(
            {
                "type": "http.response.start",
                "status": 503,
                "headers": [
                    (b"content-type", b"application/json"),
                    (b"content-length", str(len(body)).encode()),
                ],
            }
        )
        await send({"type": "http.response.body", "body": body})

    config = uvicorn.Config(
        rotate_probe,
        http=ClientCertificateH11Protocol,
        interface="asgi3",
        ws="none",
        lifespan="off",
        log_level="critical",
        ssl_certfile=str(settings.tls_cert_file),
        ssl_keyfile=str(settings.tls_key_file),
        ssl_ca_certs=str(material["ca_path"]),
        ssl_cert_reqs=ssl.CERT_REQUIRED,
    )
    server = uvicorn.Server(config)
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.bind(("127.0.0.1", 0))
        port = listener.getsockname()[1]
        worker = threading.Thread(
            target=server.run, kwargs={"sockets": [listener]}, daemon=True
        )
        worker.start()
        try:
            deadline = time.monotonic() + 5
            while not server.started and worker.is_alive() and time.monotonic() < deadline:
                time.sleep(0.01)
            assert server.started

            monkeypatch.setattr(client_agent, "_ACTIVE_SSL_CONTEXT", None)
            config_values: dict[str, object] = {
                "device_id": device_id,
                "key_file": str(key_path),
                "certificate_file": str(cert_path),
                "ca_file": str(ca_path),
            }
            client_agent.configure_ssl_context(config_values)
            old_context = client_agent._ACTIVE_SSL_CONTEXT
            original_key = key_path.read_bytes()
            original_cert = cert_path.read_bytes()
            config_path = tmp_path / "client.json"

            for _ in range(2):
                with pytest.raises(urllib.error.HTTPError) as failure:
                    client_agent.rotate_device_certificate(
                        f"https://127.0.0.1:{port}",
                        "synthetic-token",
                        config_values,
                        config_path,
                    )
                assert failure.value.code == 503
                failure.value.close()

            assert observed_fingerprints == [bundle["certificate_fingerprint"]] * 2
            assert client_agent._ACTIVE_SSL_CONTEXT is old_context
            assert config_values["certificate_file"] == str(cert_path)
            assert config_values["key_file"] == str(key_path)
            assert key_path.read_bytes() == original_key
            assert cert_path.read_bytes() == original_cert
            assert not config_path.exists()
            assert not (tmp_path / "pki").exists()
        finally:
            server.should_exit = True
            worker.join(timeout=6)
        assert not worker.is_alive()


def test_stage_device_certificate_distinguishes_invalid_csr_from_missing_ca(
    admin: dict, monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    invalid_settings = get_settings().model_copy(
        update={"data_dir": tmp_path / "invalid-csr", "environment": "test"}
    )
    missing_ca_settings = get_settings().model_copy(
        update={"data_dir": tmp_path / "missing-ca", "environment": "test"}
    )
    (missing_ca_settings.secrets_dir / "pki").mkdir(parents=True)
    monkeypatch.setattr(agent_tls, "get_settings", lambda: invalid_settings)
    token = "synthetic-csr-boundary-token"
    device = Device(
        name=f"synthetic-csr-boundary-{uuid4().hex}",
        agent_token_hash=hash_token(token),
        created_by=admin["id"],
    )
    with db_runtime.session_factory() as db:
        db.add(device)
        db.commit()
        with pytest.raises(ProblemException) as bad_csr:
            agent_tls.stage_device_certificate(db, device, "not a certificate request")
        assert bad_csr.value.status == 422
        assert bad_csr.value.code == "DEVICE_CSR_INVALID"

        monkeypatch.setattr(agent_tls, "get_settings", lambda: missing_ca_settings)
        key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
        csr = _make_csr(device.name, key)
        with pytest.raises(ProblemException) as missing_ca:
            agent_tls.stage_device_certificate(db, device, csr)
        assert missing_ca.value.status == 503
        assert missing_ca.value.code == "DEVICE_CA_UNAVAILABLE"
        assert not (missing_ca_settings.secrets_dir / "pki" / "ca.key").exists()


def test_expired_pending_rotation_is_not_promoted_and_old_certificate_still_works(
    admin: dict, monkeypatch: pytest.MonkeyPatch
) -> None:
    old_fingerprint = "a" * 64
    expired_fingerprint = "b" * 64
    replacement_fingerprint = "c" * 64
    token = "synthetic-expired-rotation-token"
    settings = type(
        "SyntheticAgentSettings", (), {"tls_enabled": True, "agent_port": 18766}
    )()
    monkeypatch.setattr(agent_tls, "get_settings", lambda: settings)
    issuance: list[tuple[str, str]] = []

    def issue(_settings, device_id: str, csr: str) -> dict[str, str]:
        issuance.append((device_id, csr))
        return {
            "certificate_fingerprint": replacement_fingerprint,
            "certificate_pem": "synthetic-replacement-certificate",
        }

    monkeypatch.setattr(agent_tls, "issue_device_certificate", issue)
    csr = "synthetic-repeatable-csr"
    with db_runtime.session_factory() as db:
        device = Device(
            name=f"synthetic-expired-rotation-{uuid4().hex}",
            agent_token_hash=hash_token(token),
            certificate_fingerprint=old_fingerprint,
            created_by=admin["id"],
        )
        db.add(device)
        db.flush()
        pending = {
            "certificate_fingerprint": expired_fingerprint,
            "certificate_pem": "synthetic-expired-certificate",
            "previous_fingerprint": old_fingerprint,
            "token_hash": device.agent_token_hash,
            "csr_hash": hashlib.sha256(csr.encode("utf-8")).hexdigest(),
            "expires_at": (utcnow() - timedelta(seconds=1)).isoformat(),
        }
        db.add(SystemSetting(key=agent_tls._pending_key(device), value=pending))
        db.commit()

        with pytest.raises(ProblemException) as rejected:
            agent_tls.check_device_tls(
                _request(expired_fingerprint, settings.agent_port), db, token
            )
        assert rejected.value.code == "DEVICE_CERTIFICATE_MISMATCH"
        assert device.certificate_fingerprint == old_fingerprint

        agent_tls.check_device_tls(_request(old_fingerprint, settings.agent_port), db, token)
        refreshed = agent_tls.stage_device_certificate(db, device, csr)
        db.commit()
        assert issuance == [(device.id, csr)]
        assert refreshed["certificate_fingerprint"] == replacement_fingerprint
        assert datetime.fromisoformat(refreshed["expires_at"]) > utcnow()

        agent_tls.check_device_tls(_request(old_fingerprint, settings.agent_port), db, token)
        assert device.certificate_fingerprint == old_fingerprint
