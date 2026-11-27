"""认证与首次配置安全边界回归测试。"""

from __future__ import annotations

from types import SimpleNamespace

import pytest
from fastapi import Request

from app import config, security
from app.login_throttle import LoginThrottle, login_throttle
from app.problems import ProblemException
from app.routers import auth


def _request_from(address: str, headers: list[tuple[bytes, bytes]] | None = None) -> Request:
    return Request(
        {
            "type": "http",
            "method": "POST",
            "path": "/api/v1/bootstrap/host",
            "headers": headers or [],
            "client": (address, 12345),
            "server": ("partyops.local", 18765),
            "scheme": "https",
            "query_string": b"",
        }
    )


def test_login_throttle_locks_account_without_storing_plain_identity(tmp_path) -> None:
    settings = config.Settings(
        data_dir=tmp_path,
        login_account_failure_limit=3,
        login_ip_failure_limit=20,
        login_window_seconds=60,
        login_lock_seconds=120,
    )
    throttle = LoginThrottle()

    assert throttle.record_failure("Admin", "192.0.2.10", now=10, settings=settings) == 0
    assert throttle.record_failure("Admin", "192.0.2.10", now=11, settings=settings) == 0
    assert throttle.record_failure("Admin", "192.0.2.10", now=12, settings=settings) == 120
    assert throttle.retry_after("admin", "192.0.2.10", now=13, settings=settings) == 119
    assert all("admin" not in key and "192.0.2.10" not in key for key in throttle._states)

    throttle.record_success("ADMIN")
    assert throttle.retry_after("admin", "192.0.2.10", now=13, settings=settings) == 0


def test_login_throttle_state_is_capacity_bounded(tmp_path) -> None:
    settings = config.Settings(
        data_dir=tmp_path,
        login_account_failure_limit=20,
        login_ip_failure_limit=200,
        login_throttle_max_entries=128,
    )
    throttle = LoginThrottle()

    for index in range(300):
        throttle.record_failure(
            f"generated-user-{index}",
            f"192.0.2.{index % 250}",
            now=float(index),
            settings=settings,
        )

    assert len(throttle._states) <= settings.login_throttle_max_entries


def test_login_endpoint_throttles_repeated_failures(client, admin) -> None:
    settings = config.get_settings()
    original_limit = settings.login_account_failure_limit
    original_ip_limit = settings.login_ip_failure_limit
    login_throttle.reset()
    settings.login_account_failure_limit = 3
    settings.login_ip_failure_limit = 20
    try:
        for _ in range(2):
            response = client.post(
                "/api/v1/auth/login",
                json={"username": "admin", "password": "incorrect-password"},
            )
            assert response.status_code == 401
            assert response.json()["code"] == "LOGIN_FAILED"

        locked = client.post(
            "/api/v1/auth/login",
            json={"username": "admin", "password": "incorrect-password"},
        )
        assert locked.status_code == 429
        assert locked.json()["code"] == "LOGIN_THROTTLED"
        assert locked.json()["retry_after_seconds"] > 0
        assert int(locked.headers["retry-after"]) > 0

        valid_but_locked = client.post(
            "/api/v1/auth/login",
            json={"username": "admin", "password": "PartyOps@2026"},
        )
        assert valid_but_locked.status_code == 429
    finally:
        settings.login_account_failure_limit = original_limit
        settings.login_ip_failure_limit = original_ip_limit
        login_throttle.reset()


def test_https_login_cookie_is_secure(client, admin) -> None:
    settings = config.get_settings()
    original_tls = settings.tls_enabled
    login_throttle.reset()
    settings.tls_enabled = True
    try:
        response = client.post(
            "/api/v1/auth/login",
            json={"username": "admin", "password": "PartyOps@2026"},
        )
        assert response.status_code == 200
        cookie = response.headers["set-cookie"].lower()
        assert "secure" in cookie
        assert "httponly" in cookie
        assert "samesite=lax" in cookie
    finally:
        login_throttle.reset()
        # TestClient 使用 http://testserver；Secure Cookie 不会在后续请求发送。
        # 重新签发一个仅用于测试进程的非 Secure 会话，避免污染共享客户端。
        settings.tls_enabled = False
        restored = client.post(
            "/api/v1/auth/login",
            json={"username": "admin", "password": "PartyOps@2026"},
        )
        assert restored.status_code == 200
        settings.tls_enabled = original_tls


def test_bootstrap_accepts_only_host_machine(monkeypatch) -> None:
    monkeypatch.setattr(auth, "discover_lan_addresses", lambda: ["192.168.10.5"])

    assert auth.bootstrap_request_is_local(_request_from("127.0.0.1"))
    assert not auth.bootstrap_request_is_local(_request_from("192.168.10.5"))
    assert not auth.bootstrap_request_is_local(_request_from("203.0.113.20"))


def test_production_bootstrap_requires_same_origin_or_protected_token(monkeypatch) -> None:
    settings = SimpleNamespace(environment="production", bootstrap_token="t" * 43)
    monkeypatch.setattr(auth, "get_settings", lambda: settings)

    assert not auth.bootstrap_request_is_trusted(_request_from("127.0.0.1"))
    assert not auth.bootstrap_request_is_trusted(
        _request_from("127.0.0.1", [(b"origin", b"https://attacker.invalid")])
    )
    assert auth.bootstrap_request_is_trusted(
        _request_from("127.0.0.1", [(b"origin", b"https://partyops.local:18765")])
    )
    assert auth.bootstrap_request_is_trusted(
        _request_from(
            "127.0.0.1",
            [(b"x-partyops-bootstrap-token", b"t" * 43)],
        )
    )
    assert not auth.bootstrap_request_is_trusted(
        _request_from(
            "192.168.10.5",
            [(b"x-partyops-bootstrap-token", b"t" * 43)],
        )
    )


def test_demo_seed_is_opt_in_by_default() -> None:
    assert config.Settings.model_fields["seed_demo"].default is False


def test_session_csrf_uses_double_submit_token_and_rejects_legacy_write() -> None:
    token = "session-bound-csrf-token"
    valid = _request_from(
        "127.0.0.1",
        [
            (b"cookie", f"partyops_csrf={token}".encode("ascii")),
            (b"x-partyops-csrf", token.encode("ascii")),
        ],
    )
    record = SimpleNamespace(csrf_token_hash=security.hash_token(token))
    security.validate_session_csrf(valid, record)

    forged = _request_from(
        "127.0.0.1",
        [
            (b"cookie", f"partyops_csrf={token}".encode("ascii")),
            (b"x-partyops-csrf", b"forged"),
        ],
    )
    with pytest.raises(ProblemException) as captured:
        security.validate_session_csrf(forged, record)
    assert captured.value.code == "SESSION_CSRF_INVALID"

    with pytest.raises(ProblemException) as captured:
        security.validate_session_csrf(valid, SimpleNamespace(csrf_token_hash=None))
    assert captured.value.code == "SESSION_CSRF_INVALID"



def test_optional_cookie_auth_requires_csrf_for_production_writes(monkeypatch):
    from datetime import timedelta
    from app.models import utcnow

    record = SimpleNamespace(revoked_at=None, expires_at=utcnow() + timedelta(hours=1),
                             user_id="u", csrf_token_hash=security.hash_token("csrf"))
    user = SimpleNamespace(active=True)
    db = SimpleNamespace(scalar=lambda _: record, get=lambda *_: user)
    monkeypatch.setattr(security, "get_settings", lambda: SimpleNamespace(environment="production"))
    req = _request_from("127.0.0.1", [(b"cookie", b"partyops_session=token")])
    with pytest.raises(ProblemException) as captured:
        security.get_current_user_optional(req, db)
    assert captured.value.code == "SESSION_CSRF_INVALID"
    valid = _request_from("127.0.0.1", [
        (b"cookie", b"partyops_session=token; partyops_csrf=csrf"),
        (b"x-partyops-csrf", b"csrf"),
    ])
    assert security.get_current_user_optional(valid, db) is user


@pytest.mark.parametrize("header,value,allowed", [
    (b"referer", b"https://partyops.local:18765/a?tab=1#x", True),
    (b"origin", b"https://partyops.local:18765/?tab=1", False),
    (b"origin", b"https://partyops.local:18765/#x", False),
    (b"referer", b"https://other.invalid/a?tab=1", False),
])
def test_referer_checks_origin_not_page_parameters(monkeypatch, header, value, allowed):
    from app import main
    monkeypatch.setattr(main.settings, "environment", "production")
    assert main._origin_allowed(_request_from("127.0.0.1", [(header, value)])) is allowed



def test_missing_account_performs_password_verification_but_never_authenticates(client, admin, monkeypatch):
    calls = []
    def verify(password, encoded):
        calls.append(encoded)
        return True
    monkeypatch.setattr(auth, "verify_password", verify)
    login_throttle.reset()
    try:
        result = client.post("/api/v1/auth/login", json={
            "username": "nonexistent-regression-account", "password": "not-a-password"})
        assert result.status_code == 401
        assert result.json()["code"] == "LOGIN_FAILED"
        assert calls == [auth._DUMMY_PASSWORD_HASH]
    finally:
        login_throttle.reset()


@pytest.mark.parametrize("username,password", [("x" * 65, "password"), ("admin", "x" * 129)])
def test_login_input_bounds(client, username, password):
    response = client.post("/api/v1/auth/login", json={"username": username, "password": password})
    assert response.status_code == 422



def test_login_throttle_capacity_with_locked_entries_keeps_existing_locks(tmp_path):
    settings = config.Settings(data_dir=tmp_path, login_account_failure_limit=3,
        login_ip_failure_limit=5, login_throttle_max_entries=128,
        login_window_seconds=60, login_lock_seconds=120)
    throttle = LoginThrottle(log_failures=False)
    for index in range(64):
        for _ in range(5):
            throttle.record_failure(f"locked-{index}", f"192.0.2.{index}", now=10, settings=settings)
    assert len(throttle._states) == 128
    # The new keys may be pruned immediately. Never index a pruned key or
    # evict an existing active lock to retain low-frequency noise.
    assert throttle.record_failure("new-account", "198.51.100.1", now=11, settings=settings) == 0
    assert len(throttle._states) == 128
    assert throttle.retry_after("locked-0", "192.0.2.0", now=11, settings=settings) == 119


def test_login_throttle_failure_does_not_reset_active_lock_at_window_boundary(tmp_path):
    settings = config.Settings(data_dir=tmp_path, login_account_failure_limit=3,
        login_ip_failure_limit=20, login_window_seconds=60, login_lock_seconds=120)
    throttle = LoginThrottle(log_failures=False)
    for when in (0, 1, 2):
        throttle.record_failure("admin", "192.0.2.1", now=when, settings=settings)
    assert throttle.retry_after("admin", "192.0.2.1", now=61, settings=settings) == 61
    assert throttle.record_failure("admin", "192.0.2.1", now=61, settings=settings) >= 61


def test_login_rechecks_throttle_after_waiting_for_credential_transaction(client, admin, monkeypatch):
    checks = iter([0, 90])
    monkeypatch.setattr(login_throttle, "retry_after", lambda *_a, **_k: next(checks))
    def unexpected(*_a, **_k):
        pytest.fail("queued request verified password despite newly active lock")
    monkeypatch.setattr(auth, "verify_password", unexpected)
    response = client.post("/api/v1/auth/login", json={
        "username": admin["username"], "password": "PartyOps@2026"})
    assert response.status_code == 429, response.text
    assert response.json()["code"] == "LOGIN_THROTTLED"
    assert response.headers["retry-after"] == "90"



@pytest.mark.parametrize("age,refresh", [(0, False), (59, False), (60, True), (600, True), (None, True)])
def test_session_activity_metadata_is_throttled_without_changing_expiry(monkeypatch, age, refresh):
    from datetime import datetime, timedelta, timezone

    now = datetime(2026, 11, 27, 8, 0, tzinfo=timezone.utc)
    seen = (now - timedelta(seconds=age)).replace(tzinfo=None) if age is not None else None
    expiry = now + timedelta(hours=1)
    record = SimpleNamespace(revoked_at=None, expires_at=expiry, user_id="u", last_seen_at=seen)
    user = SimpleNamespace(active=True)
    db = SimpleNamespace(scalar=lambda _: record, get=lambda *_a, **_k: user)
    monkeypatch.setattr(security, "utcnow", lambda: now)
    monkeypatch.setattr(security, "get_settings", lambda: SimpleNamespace(environment="production"))
    req = _request_from("127.0.0.1", [(b"authorization", b"Bearer synthetic")])
    assert security.get_current_user(req, db) is user
    assert record.last_seen_at == (now if refresh else seen)
    assert record.expires_at == expiry


def test_csrf_non_ascii_header_is_rejected_as_controlled_error():
    req = _request_from("127.0.0.1", [
        (b"cookie", b"partyops_session=synthetic; partyops_csrf=valid"),
        (b"x-partyops-csrf", b"\xe9"),
    ])
    record = SimpleNamespace(csrf_token_hash=security.hash_token("valid"))
    with pytest.raises(ProblemException) as captured:
        security.validate_session_csrf(req, record)
    assert captured.value.code == "SESSION_CSRF_INVALID"
