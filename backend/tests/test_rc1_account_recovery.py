"""覆盖账号恢复与设备加密的权威主机边界。"""

from __future__ import annotations

import json
import os
import secrets
import socket
import sqlite3
import ssl
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from contextlib import closing, contextmanager
from datetime import timedelta
from http.client import HTTPSConnection
from pathlib import Path
from uuid import uuid4

import pytest
import uvicorn
from cryptography import x509
from cryptography.exceptions import UnsupportedAlgorithm
from cryptography.fernet import Fernet
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.x509.oid import NameOID
from fastapi import Request
from fastapi.testclient import TestClient
from sqlalchemy import select

from app import account_recovery, local_secrets, recovery_cli
from app.agent_tls import ClientCertificateH11Protocol, check_device_tls
from app.config import get_settings
from app.database import Base, DatabaseRuntime, db_runtime
from app.enums import UserRole
from app.main import app
from app.models import AuditLog, Device, RecoveryCredential, SystemSetting, User, utcnow
from app.pki import ensure_tls_material, issue_device_certificate
from app.problems import ProblemException
from app.security import CSRF_COOKIE, SESSION_COOKIE, hash_token
from app.startup_selftest import verify_sm_runtime


@contextmanager
def _client(**kwargs):
    # 模块隔离 Cookie，共用 conftest 已就绪的数据库。
    client = TestClient(app, **kwargs)
    try:
        yield client
    finally:
        client.close()


def _create(admin_client, *, role="staff"):
    password = f"Synthetic!{uuid4().hex}"
    result = admin_client.post("/api/v1/admin/users", json={
        "username": f"rc1_{uuid4().hex[:12]}",
        "display_name": "账号恢复合成用户",
        "password": password,
        "role": role,
    })
    assert result.status_code == 201, result.text
    return result.json(), password


def _log_in(client, username, password, **kwargs):
    return client.post("/api/v1/auth/login", json={"username": username, "password": password}, **kwargs)


@pytest.fixture
def rc1_admin(admin):
    account_recovery.recovery_throttle.reset()
    account_recovery.credential_change_throttle.reset()
    with _client() as browser:
        assert _log_in(browser, "admin", "PartyOps@2026").status_code == 200
        yield browser
    account_recovery.recovery_throttle.reset()
    account_recovery.credential_change_throttle.reset()


def _issue(browser, user):
    result = browser.post(f"/api/v1/admin/users/{user['id']}/recovery")
    assert result.status_code == 201, result.text
    assert result.headers["cache-control"] == "no-store"
    return result.json()["credential"]


def _redeem(browser, token, **changes):
    return browser.post("/api/v1/auth/recovery/redeem", json={"credential": token, **changes})


def test_rc1_recovery_is_single_use_and_preserves_identity(rc1_admin, caplog):
    user, old_password = _create(rc1_admin)
    original = _issue(rc1_admin, user)
    token = _issue(rc1_admin, user)
    new_password = f"Synthetic!{uuid4().hex}"
    new_name = f"RENAMED_{uuid4().hex[:8]}"
    with _client() as browser, _client() as anonymous, _client() as api:
        assert _log_in(browser, user["username"], old_password).status_code == 200
        bearer = {"Authorization": f"Bearer {browser.cookies[SESSION_COOKIE]}"}
        previous = _redeem(anonymous, original)
        assert previous.json()["code"] == "RECOVERY_INVALID"
        recovered = _redeem(anonymous, token, username=new_name, new_password=new_password)
        assert recovered.status_code == 200, recovered.text
        assert recovered.json() == {"username": new_name.lower()}
        assert _redeem(anonymous, token).json()["code"] == "RECOVERY_INVALID"
        assert browser.get("/api/v1/auth/me").status_code == 401
        assert api.get("/api/v1/auth/me", headers=bearer).status_code == 401
        assert _log_in(browser, new_name, old_password).status_code == 401
        current = _log_in(browser, new_name, new_password)
        assert current.status_code == 200, current.text
        assert current.json()["id"] == user["id"]
        assert current.json()["role"] == user["role"]
    with db_runtime.session_factory() as db:
        stored = db.scalar(select(RecoveryCredential).where(
            RecoveryCredential.digest == account_recovery.sm3_digest(token.encode("ascii"))
        ))
        assert stored is not None and stored.used_at is not None
        audit = db.scalars(select(AuditLog).where(AuditLog.entity_id == user["id"])).all()
        assert any(item.action == "auth.recovery_redeem" for item in audit)
        dump = json.dumps([item.detail for item in audit])
    assert all(secret not in (caplog.text + dump) for secret in (original, token, old_password, new_password))


def test_rc1_recovery_bad_expired_revoked_and_concurrent(rc1_admin):
    user, _ = _create(rc1_admin)
    expired = _issue(rc1_admin, user)
    with db_runtime.session_factory.begin() as db:
        row = db.scalar(select(RecoveryCredential).where(
            RecoveryCredential.digest == account_recovery.sm3_digest(expired.encode("ascii"))
        ))
        assert row is not None
        row.expires_at = utcnow() - timedelta(seconds=1)
    with _client() as anonymous:
        responses = [_redeem(anonymous, token) for token in (expired, secrets.token_urlsafe(32))]
        assert [(item.status_code, item.json()["code"]) for item in responses] == [(400, "RECOVERY_INVALID")] * 2
        pending = _issue(rc1_admin, user)
        assert rc1_admin.delete(f"/api/v1/admin/users/{user['id']}/recovery").status_code == 204
        assert _redeem(anonymous, pending).json()["code"] == "RECOVERY_INVALID"
    token = _issue(rc1_admin, user)

    def submit_once(_):
        with _client() as browser:
            result = _redeem(browser, token)
            return result.status_code, result.json()

    with ThreadPoolExecutor(max_workers=2) as executor:
        attempts = list(executor.map(submit_once, range(2)))
    assert sorted(status for status, _ in attempts) == [200, 400]
    assert {"username": user["username"]} in [body for _, body in attempts]


def test_rc1_credentials_refuse_noop_and_incorrect_current_password_without_logout(rc1_admin):
    user, password = _create(rc1_admin)
    with _client() as browser:
        assert _log_in(browser, user["username"], password).status_code == 200
        cases = [
            ({"current_password": "incorrect-test-password"}, 403, "CURRENT_PASSWORD_INVALID"),
            ({"current_password": password, "username": user["username"]}, 422, "CREDENTIALS_UNCHANGED"),
            ({"current_password": password, "new_password": password}, 422, "PASSWORD_UNCHANGED"),
        ]
        for body, status, code in cases:
            response = browser.patch("/api/v1/auth/credentials", json=body)
            assert (response.status_code, response.json()["code"]) == (status, code)
            assert response.headers["cache-control"] == "no-store"
            assert browser.get("/api/v1/auth/me").json()["id"] == user["id"]


def test_rc1_recovery_revalidates_role_inside_redeem_transaction(rc1_admin):
    user, _ = _create(rc1_admin)
    credential = _issue(rc1_admin, user)
    route = f"/api/v1/admin/users/{user['id']}/recovery"
    with db_runtime.session_factory.begin() as db:
        db.get(User, user["id"]).role = UserRole.ADMIN
    with _client() as browser:
        for answer in (
            rc1_admin.post(route), rc1_admin.delete(route),
            _redeem(browser, credential), _redeem(browser, "invalid-format"),
        ):
            assert (answer.status_code, answer.json()["code"]) == (400, "RECOVERY_INVALID")
        with db_runtime.session_factory.begin() as db:
            row = db.scalar(select(RecoveryCredential).where(
                RecoveryCredential.digest == account_recovery.sm3_digest(credential.encode("ascii"))
            ))
            assert row and row.used_at is None and row.revoked_at is None
            db.get(User, user["id"]).role = UserRole.STAFF
        assert _redeem(browser, credential).json() == {"username": user["username"]}


def test_rc1_recovery_refuses_client_mode_before_starting_write(monkeypatch):
    settings = get_settings().model_copy(update={"mode": "client"})
    monkeypatch.setattr(account_recovery, "get_settings", lambda: settings)
    with _client() as browser:
        response = _redeem(browser, "invalid-format")
    assert (response.status_code, response.json()["code"]) == (403, "RECOVERY_HOST_REQUIRED")


def test_rc1_recovery_conflict_and_audit_failure_do_not_consume_credential(rc1_admin, monkeypatch):
    target, _ = _create(rc1_admin)
    other, _ = _create(rc1_admin)
    token = _issue(rc1_admin, target)
    new_name = f"fixed_{uuid4().hex[:8]}"
    with _client(raise_server_exceptions=False) as browser:
        taken = _redeem(browser, token, username=other["username"].upper())
        assert taken.status_code == 409
        assert taken.json()["code"] == "USERNAME_EXISTS"
        with monkeypatch.context() as patch:
            def fail_audit(*args, **kwargs):
                raise RuntimeError("合成审计故障")

            patch.setattr(account_recovery, "write_audit", fail_audit)
            failure = _redeem(browser, token, username=new_name)
            assert failure.status_code == 500
        with db_runtime.session_factory() as db:
            assert db.get(User, target["id"]).username == target["username"]
        recovered = _redeem(browser, token, username=new_name)
        assert recovered.status_code == 200, recovered.text
        assert recovered.json()["username"] == new_name


def test_rc1_cookies_and_bearer_are_independent_and_csrf_is_required(rc1_admin, monkeypatch):
    user, password = _create(rc1_admin)
    with _client(base_url="http://127.0.0.1") as browser, _client(base_url="http://127.0.0.1") as api:
        monkeypatch.setattr(get_settings(), "environment", "production")
        same_origin = {"Origin": "http://127.0.0.1"}
        assert _log_in(browser, user["username"], password, headers=same_origin).status_code == 200
        bearer = {"Authorization": f"Bearer {browser.cookies[SESSION_COOKIE]}"}
        assert browser.get("/api/v1/auth/me", headers=bearer).json()["code"] == "AUTH_SCHEME_CONFLICT"
        body = {"current_password": password, "username": f"changed_{uuid4().hex[:9]}"}
        assert browser.patch("/api/v1/auth/credentials", headers=same_origin, json=body).json()["code"] == (
            "SESSION_CSRF_INVALID"
        )
        csrf = {"X-PartyOps-CSRF": browser.cookies[CSRF_COOKIE], **same_origin}
        altered = browser.patch("/api/v1/auth/credentials", headers=csrf, json=body)
        assert altered.status_code == 200, altered.text
        assert SESSION_COOKIE not in browser.cookies
        assert altered.json()["id"] == user["id"]
        assert api.get("/api/v1/auth/me", headers=bearer).status_code == 401


def test_rc1_admin_offline_and_local_maintenance_are_independently_consumable(rc1_admin):
    user, password = _create(rc1_admin, role="admin")
    with _client() as admin, _client() as anonymous:
        assert _log_in(admin, user["username"], password).status_code == 200
        first = admin.post("/api/v1/auth/recovery/offline", json={"current_password": password})
        second = admin.post("/api/v1/auth/recovery/offline", json={"current_password": password})
        assert first.status_code == second.status_code == 201
        assert second.json()["expires_at"] is None
        assert _redeem(anonymous, first.json()["credential"]).status_code == 400
        assert _redeem(anonymous, second.json()["credential"]).status_code == 200
    with db_runtime.session_factory.begin() as db:
        proof = recovery_cli.authorize_local_maintenance(db, user["username"], password)
        previous_hash = db.get(User, user["id"]).password_hash
    with db_runtime.session_factory.begin() as db:
        token = recovery_cli.issue_local_admin_recovery(db, proof)
        assert db.get(User, user["id"]).password_hash == previous_hash
    with pytest.raises(ValueError, match="已使用"), db_runtime.session_factory.begin() as db:
        recovery_cli.issue_local_admin_recovery(db, proof)
    with _client() as anonymous:
        assert _redeem(anonymous, token).json() == {"username": user["username"]}


def test_rc1_recovery_after_disable_or_admin_reset_requires_a_new_token(rc1_admin):
    user, _ = _create(rc1_admin)
    path = f"/api/v1/admin/users/{user['id']}"
    token = _issue(rc1_admin, user)
    updated = rc1_admin.patch(path, headers={"If-Match": str(user["version"])}, json={"active": False})
    assert updated.status_code == 200, updated.text
    revived = rc1_admin.patch(path, headers={"If-Match": str(updated.json()["version"])}, json={"active": True})
    assert revived.status_code == 200, revived.text
    with _client() as anonymous:
        assert _redeem(anonymous, token).json()["code"] == "RECOVERY_INVALID"
        new_token = _issue(rc1_admin, user)
        archived = rc1_admin.delete(path)
        assert archived.status_code == 200, archived.text
        restored = rc1_admin.post(f"{path}/restore")
        assert restored.status_code == 200, restored.text
        assert _redeem(anonymous, new_token).json()["code"] == "RECOVERY_INVALID"
        replacement = _issue(rc1_admin, user)
        reset = rc1_admin.post(f"{path}/reset-password", json={"password": f"Synthetic!{uuid4().hex}"})
        assert reset.status_code == 200, reset.text
        assert _redeem(anonymous, replacement).json()["code"] == "RECOVERY_INVALID"


def test_rc1_recovery_applies_rate_limit_across_random_unrecognized_credentials(rc1_admin, monkeypatch):
    monkeypatch.setattr(get_settings(), "login_ip_failure_limit", 2)
    with _client() as browser:
        assert _redeem(browser, secrets.token_urlsafe(32)).status_code == 400
        limited = _redeem(browser, secrets.token_urlsafe(32))
        assert limited.status_code == 429
        assert limited.json()["code"] == "RECOVERY_THROTTLED"
        assert int(limited.headers["retry-after"]) > 0


def test_rc1_host_tool_refuses_unprivileged_console(monkeypatch, tmp_path, capsys):
    monkeypatch.setattr(recovery_cli, "is_os_administrator", lambda: False)
    assert recovery_cli.main(["issue", "--data-dir", str(tmp_path)]) == 2
    assert "提升权限" in capsys.readouterr().err


@pytest.fixture
def isolated_crypto_store(monkeypatch, tmp_path):
    settings = get_settings().model_copy(update={"data_dir": tmp_path})
    runtime = DatabaseRuntime(settings)
    Base.metadata.create_all(runtime.engine)
    monkeypatch.setattr(local_secrets, "get_settings", lambda: settings)
    yield runtime
    runtime.engine.dispose()


def test_rc1_sm4_nonce_authentication_legacy_and_missing_key(isolated_crypto_store):
    value = {"device_id": "synthetic-device", "purpose": "合成记录"}
    with isolated_crypto_store.session_factory.begin() as db:
        first = local_secrets.encrypt_local_json(value, device_id=value["device_id"], db=db)
        # 生产每次成功入网提交一次；同一事务手动 flush 后可模拟第二次写入。
        db.flush()
        second = local_secrets.encrypt_local_json(value, device_id=value["device_id"], db=db)
    assert first.startswith("v1.sm4-gcm.")
    assert first.split(".")[3] != second.split(".")[3]
    assert local_secrets.decrypt_local_json(first, device_id=value["device_id"]) == value
    with pytest.raises(local_secrets.LocalRecoveryError, match="验证上下文"):
        local_secrets.decrypt_local_json(first, device_id="other-device")
    for index in (3, 4, 5):
        damaged = first.split(".")
        damaged[index] = ("A" if damaged[index][0] != "A" else "B") + damaged[index][1:]
        with pytest.raises(local_secrets.LocalRecoveryError):
            local_secrets.decrypt_local_json(".".join(damaged), device_id=value["device_id"])
    key_path = local_secrets._key_path()
    legacy_key = Fernet.generate_key()
    key_path.write_bytes(legacy_key)
    key_path.chmod(0o600)
    ciphertext = Fernet(legacy_key).encrypt(json.dumps(value).encode()).decode()
    assert local_secrets.decrypt_local_json(ciphertext, device_id=value["device_id"]) == value
    with pytest.raises(local_secrets.LocalRecoveryError, match="设备身份"):
        local_secrets.decrypt_local_json(ciphertext, device_id="other-device")
    key_path.unlink()
    with pytest.raises(local_secrets.LocalRecoveryError, match="旧版入网恢复密钥不可读"):
        local_secrets.decrypt_local_json(ciphertext, device_id=value["device_id"])
    assert not key_path.exists()
    local_secrets._sm4_key_path(first.split(".")[2]).unlink()
    with pytest.raises(local_secrets.LocalRecoveryError, match="入网恢复密钥不可读"):
        local_secrets.decrypt_local_json(first, device_id=value["device_id"])


def test_rc1_sm_runtime_known_answers_and_disabled_provider(monkeypatch):
    assert verify_sm_runtime().keys() == {"cryptography", "openssl"}

    def unavailable(*args, **kwargs):
        raise UnsupportedAlgorithm("合成 provider 不可用")

    monkeypatch.setattr(local_secrets, "Cipher", unavailable)
    with pytest.raises(local_secrets.LocalRecoveryError) as failure:
        local_secrets._encryption_context(bytes(16), bytes(12))
    assert failure.value.code == "ENROLLMENT_CRYPTO_UNAVAILABLE"
    with pytest.raises(local_secrets.LocalRecoveryError) as failure:
        local_secrets._decryption_context(bytes(16), bytes(12), bytes(16))
    assert failure.value.code == "ENROLLMENT_CRYPTO_UNAVAILABLE"


def test_rc1_sm4_rotates_at_write_limit_and_reads_previous_envelope(isolated_crypto_store):
    runtime = isolated_crypto_store
    payload = {"device_id": "rotation-device"}
    with runtime.session_factory.begin() as db:
        first = local_secrets.encrypt_local_json(payload, device_id="rotation-device", db=db)
    with runtime.session_factory.begin() as db:
        setting = db.get(SystemSetting, local_secrets.ACTIVE_KEY_SETTING)
        assert setting is not None
        setting.value = {"id": first.split(".")[2], "writes": local_secrets.MAX_KEY_WRITES}

    with runtime.session_factory.begin() as db:
        second = local_secrets.encrypt_local_json(payload, device_id="rotation-device", db=db)
        setting = db.get(SystemSetting, local_secrets.ACTIVE_KEY_SETTING)
        assert setting and setting.value == {"id": second.split(".")[2], "writes": 1}
    assert first.split(".")[2] != second.split(".")[2]
    assert local_secrets.decrypt_local_json(first, device_id="rotation-device") == payload
    assert local_secrets.decrypt_local_json(second, device_id="rotation-device") == payload


@pytest.mark.parametrize("config", [True, {"id": False, "writes": 1}, {"id": "a" * 32, "writes": 0},
                                     {"id": "invalid-key-id", "writes": 1}])
def test_rc1_invalid_sm4_key_metadata_does_not_create_new_key(isolated_crypto_store, config):
    runtime = isolated_crypto_store
    with runtime.session_factory.begin() as db:
        db.add(SystemSetting(key=local_secrets.ACTIVE_KEY_SETTING, value=config))
    with runtime.session_factory.begin() as db:
        with pytest.raises(local_secrets.LocalRecoveryError) as failure:
            local_secrets.encrypt_local_json({"device_id": "fixture"}, device_id="fixture", db=db)
        assert failure.value.code == "ENROLLMENT_KEY_INVALID"
    assert not (local_secrets._key_path().parent / "enrollment-keys").exists()


def test_rc1_enrollment_rejects_invalid_legacy_key_and_envelope_structure(isolated_crypto_store):
    key_path = local_secrets._key_path()
    key_path.write_bytes(b"malformed-legacy-key")
    key_path.chmod(0o600)
    with pytest.raises(local_secrets.LocalRecoveryError) as failure:
        local_secrets.decrypt_local_json("legacy-record", device_id="fixture")
    assert failure.value.code == "ENROLLMENT_KEY_INVALID"

    legacy_key = Fernet.generate_key()
    key_path.write_bytes(legacy_key)
    non_object = Fernet(legacy_key).encrypt(b"[]").decode()
    for envelope in (non_object, "v9.sm4-gcm.key.AAAA.AAAA.AAAA", "v1.sm4-gcm.key.AAAA.AAAA.AAAA"):
        with pytest.raises(local_secrets.LocalRecoveryError) as failure:
            local_secrets.decrypt_local_json(envelope, device_id="fixture")
        assert failure.value.code == "ENROLLMENT_RECOVERY_INVALID"


def test_rc1_partial_ca_is_not_rebuilt(tmp_path):
    settings = get_settings().model_copy(update={"data_dir": tmp_path, "environment": "test"})
    material = ensure_tls_material(settings)
    root = material["ca_path"].parent
    before = material["ca_path"].read_bytes()
    (root / "ca.key").unlink()
    with pytest.raises(ValueError, match="材料缺失"):
        ensure_tls_material(settings)
    assert material["ca_path"].read_bytes() == before
    assert not (root / "ca.key").exists()


@pytest.mark.skipif(os.name == "nt", reason="POSIX 密钥文件读权限")
def test_rc1_legacy_key_permissions_are_checked_without_replacing_existing_keys(isolated_crypto_store):
    key_path = local_secrets._key_path()
    original = Fernet.generate_key()
    key_path.write_bytes(original)
    key_path.chmod(0o644)
    with pytest.raises(local_secrets.LocalRecoveryError) as error:
        local_secrets._fernet()
    assert error.value.code == "ENROLLMENT_KEY_PERMISSION_INVALID"
    assert key_path.read_bytes() == original
    settings = isolated_crypto_store.settings
    material = ensure_tls_material(settings)
    ca_file = material["ca_path"].with_name("ca.key")
    ca_file.chmod(0o644)
    original_ca = ca_file.read_bytes()
    with pytest.raises(ValueError, match="完整性校验失败"):
        ensure_tls_material(settings)
    assert ca_file.read_bytes() == original_ca


def test_rc1_legacy_issued_devices_prevent_ca_regeneration_after_entire_directory_loss(tmp_path: Path):
    settings = get_settings().model_copy(update={"data_dir": tmp_path, "environment": "test"})
    with closing(sqlite3.connect(settings.database_path)) as conn:
        conn.executescript("CREATE TABLE devices(certificate_fingerprint TEXT); INSERT INTO devices VALUES('issued');")
    with pytest.raises(ValueError, match="材料缺失"):
        ensure_tls_material(settings)
    assert not (settings.secrets_dir / "pki").exists()


def test_rc1_device_tls_checks_real_listener_state_and_bound_identity(isolated_crypto_store, monkeypatch):
    settings = get_settings()
    monkeypatch.setattr(settings, "tls_enabled", True)
    token = secrets.token_urlsafe(32)
    registered = secrets.token_hex(32)
    with isolated_crypto_store.session_factory.begin() as db:
        owner = User(username="synthetic-owner", display_name="合成测试所有者", password_hash="synthetic-hash")
        db.add(owner)
        db.flush()
        db.add(Device(
            name=f"synthetic-{uuid4().hex}", certificate_fingerprint=registered,
            agent_token_hash=hash_token(token), created_by=owner.id,
        ))
    with isolated_crypto_store.session_factory() as db:
        scope = {"type": "http", "server": ("127.0.0.1", settings.agent_port), "state": {
            "partyops_peer_cert_sha256": registered,
        }}
        check_device_tls(Request(scope), db, token)
        scope["state"]["partyops_peer_cert_sha256"] = secrets.token_hex(32)
        with pytest.raises(ProblemException) as error:
            check_device_tls(Request(scope), db, token)
        assert error.value.code == "DEVICE_CERTIFICATE_MISMATCH"
        scope["server"] = ("127.0.0.1", settings.port)
        with pytest.raises(ProblemException) as error:
            check_device_tls(Request(scope), db, token)
        assert error.value.code == "AGENT_MTLS_REQUIRED"


def test_rc1_agent_protocol_reads_distinct_certificates_from_real_local_tls_connections(tmp_path: Path):
    settings = get_settings().model_copy(update={"data_dir": tmp_path, "environment": "test"})
    material = ensure_tls_material(settings)
    contexts = []
    for suffix in ("one", "two"):
        key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
        csr = x509.CertificateSigningRequestBuilder().subject_name(x509.Name([
            x509.NameAttribute(NameOID.COMMON_NAME, f"synthetic-{suffix}"),
        ])).sign(key, hashes.SHA256())
        bundle = issue_device_certificate(settings, f"synthetic-{suffix}",
                                          csr.public_bytes(serialization.Encoding.PEM).decode())
        pem = tmp_path / f"synthetic-{suffix}.pem"
        pem.write_bytes(bundle["certificate_pem"].encode("ascii") + key.private_bytes(
            serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption(),
        ))
        context = ssl.create_default_context(cafile=str(material["ca_path"]))
        context.load_cert_chain(pem)
        contexts.append((context, bundle["certificate_fingerprint"]))

    async def probe(scope, receive, send):
        value = scope["state"]["partyops_peer_cert_sha256"].encode("ascii")
        await send({"type": "http.response.start", "status": 200, "headers": []})
        await send({"type": "http.response.body", "body": value})

    config = uvicorn.Config(
        probe, http=ClientCertificateH11Protocol, interface="asgi3", ws="none", lifespan="off",
        ssl_certfile=str(settings.tls_cert_file), ssl_keyfile=str(settings.tls_key_file),
        ssl_ca_certs=str(material["ca_path"]), ssl_cert_reqs=ssl.CERT_REQUIRED,
    )
    server = uvicorn.Server(config)
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.bind(("127.0.0.1", 0))
        port = listener.getsockname()[1]
        worker = threading.Thread(target=server.run, kwargs={"sockets": [listener]}, daemon=True)
        worker.start()
        try:
            deadline = time.monotonic() + 5
            while not server.started and worker.is_alive() and time.monotonic() < deadline:
                time.sleep(0.01)
            assert server.started
            for context, expected in contexts:
                with closing(HTTPSConnection("127.0.0.1", port, context=context, timeout=5)) as connection:
                    connection.request("GET", "/")
                    response = connection.getresponse()
                    assert response.status == 200
                    assert response.read().decode("ascii") == expected
        finally:
            server.should_exit = True
            worker.join(timeout=6)
        assert not worker.is_alive()
