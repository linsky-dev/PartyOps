"""主机恢复命令入口的合成目录、控制台与事务回归。"""

from __future__ import annotations

import ctypes
import os
import secrets
import stat
import sys
import warnings
from datetime import timedelta, timezone
from pathlib import Path
from types import SimpleNamespace

import pytest
from sqlalchemy import select

from app import account_recovery, audit, database, recovery_cli, setup_wizard
from app.config import get_settings
from app.database import DatabaseRuntime
from app.enums import UserRole
from app.main import DataDirectoryInstanceLock
from app.models import AuditLog, RecoveryCredential, SystemSetting, User, utcnow
from app.security import hash_password

ADMIN_NAME = "synthetic-cli-admin"
ADMIN_PASSWORD = f"Synthetic-{secrets.token_urlsafe(18)}"


def _console(monkeypatch, *, elevated=True, stdin=True, stdout=True):
    class Console:
        @property
        def stderr(self):
            return sys.stderr

    streams = Console()
    streams.stdin = SimpleNamespace(isatty=lambda: stdin)
    streams.stdout = SimpleNamespace(isatty=lambda: stdout)
    monkeypatch.setattr(recovery_cli, "is_os_administrator", lambda: elevated)
    monkeypatch.setattr(recovery_cli, "sys", streams)


def _invoke(monkeypatch, path: Path, action: str, proof: str) -> int:
    monkeypatch.setattr(recovery_cli, "input", lambda _prompt: " SYNTHETIC-CLI-ADMIN ", raising=False)
    monkeypatch.setattr(recovery_cli.getpass, "getpass", lambda _prompt: proof)
    return recovery_cli.main([action, "--data-dir", str(path)])


@pytest.fixture
def cli_store(tmp_path, monkeypatch):
    path = tmp_path.resolve() / "synthetic-host"
    runtime = DatabaseRuntime(get_settings().model_copy(update={"data_dir": path, "mode": "host"}))
    runtime.create_schema()
    with runtime.session_factory.begin() as db:
        user = User(
            username=ADMIN_NAME, display_name="恢复命令测试账号", role=UserRole.ADMIN,
            password_hash=hash_password(ADMIN_PASSWORD),
        )
        db.add(user)
        db.flush()
        admin_id = user.id

    # 入口模块按启动参数解析数据库；合成测试把进程依赖绑定到同一个隔离库。
    monkeypatch.setattr(database, "db_runtime", runtime)
    monkeypatch.setattr(account_recovery, "get_settings", lambda: runtime.settings)
    cli_environment: dict[str, str] = {}
    monkeypatch.setattr(recovery_cli, "os", SimpleNamespace(name=os.name, environ=cli_environment))

    def check_candidate_acl(data_dir: Path, *, verify_target: bool) -> None:
        assert data_dir == path
        assert verify_target is True

    monkeypatch.setattr(setup_wizard, "assert_windows_service_data_path_security", check_candidate_acl)
    _console(monkeypatch)
    yield SimpleNamespace(
        runtime=runtime, path=path, admin_id=admin_id, password_hash=user.password_hash,
        env=cli_environment, setting_key=f"{account_recovery.HOST_RECOVERY_PREFIX}{admin_id}",
    )
    runtime.engine.dispose()


def test_trusted_data_directory_requires_absolute_initialized_local_path(tmp_path):
    root = tmp_path.resolve()
    path = root / "synthetic-host"
    path.mkdir()
    with pytest.raises(ValueError, match="绝对数据目录"):
        recovery_cli.trusted_data_directory(Path("synthetic-host"))
    with pytest.raises(ValueError, match="规范的绝对路径"):
        recovery_cli.trusted_data_directory(root / ".." / root.name / path.name)
    with pytest.raises(ValueError, match="没有已初始化"):
        recovery_cli.trusted_data_directory(path)
    (path / "partyops.db").touch(mode=0o600)
    assert recovery_cli.trusted_data_directory(path) == path


@pytest.mark.parametrize("filename", ["partyops.db", ".partyops-instance.lock"])
def test_trusted_data_directory_rejects_key_path_reparse_points(tmp_path, monkeypatch, filename):
    root = tmp_path.resolve()
    (root / "partyops.db").touch(mode=0o600)
    (root / ".partyops-instance.lock").touch(mode=0o600)
    suspicious = root / filename
    actual_lstat = Path.lstat

    def synthetic_lstat(path):
        if path == suspicious:
            return SimpleNamespace(st_mode=stat.S_IFREG | 0o600, st_file_attributes=stat.FILE_ATTRIBUTE_REPARSE_POINT)
        return actual_lstat(path)

    monkeypatch.setattr(Path, "lstat", synthetic_lstat)
    with pytest.raises(ValueError, match="链接路径"):
        recovery_cli.trusted_data_directory(root)


@pytest.mark.skipif(os.name == "nt", reason="仅 POSIX 使用文件权限位")
def test_trusted_data_directory_refuses_another_users_write_permission(tmp_path):
    root = tmp_path.resolve()
    database_path = root / "partyops.db"
    database_path.touch(mode=0o600)
    database_path.chmod(0o620)
    with pytest.raises(ValueError, match="其他账号写入"):
        recovery_cli.trusted_data_directory(root)


def test_windows_trust_rejects_remote_or_removable_volume_before_opening_db(tmp_path, monkeypatch):
    monkeypatch.setattr(recovery_cli, "os", SimpleNamespace(name="nt"))
    monkeypatch.setattr(ctypes, "windll", SimpleNamespace(
        kernel32=SimpleNamespace(GetDriveTypeW=lambda _anchor: 4),
    ), raising=False)
    with pytest.raises(ValueError, match="本机固定磁盘"):
        recovery_cli.trusted_data_directory(tmp_path.resolve())


@pytest.mark.parametrize(
    ("elevated", "stdin", "stdout"),
    [(False, True, True), (True, False, True), (True, True, False)],
)
def test_cli_rejects_unelevated_and_redirected_console_before_reading_secret(
    tmp_path, monkeypatch, capsys, elevated, stdin, stdout,
):
    _console(monkeypatch, elevated=elevated, stdin=stdin, stdout=stdout)

    def no_secret_read(*_args):
        pytest.fail("权限与交互检查未通过时读取了恢复凭据")

    monkeypatch.setattr(recovery_cli.getpass, "getpass", no_secret_read)
    assert recovery_cli.main(["issue", "--data-dir", str(tmp_path)]) == 2
    captured = capsys.readouterr()
    assert captured.out == ""
    assert "提升权限" in captured.err and "交互终端" in captured.err


@pytest.mark.parametrize("platform, allowed", [("nt", False), ("nt", True), ("posix", False), ("posix", True)])
def test_cli_checks_platform_administrator_identity(monkeypatch, platform, allowed):
    monkeypatch.setattr(recovery_cli, "os", SimpleNamespace(
        name=platform, geteuid=lambda: 0 if allowed else 1001,
    ))
    monkeypatch.setattr(ctypes, "windll", SimpleNamespace(
        shell32=SimpleNamespace(IsUserAnAdmin=lambda: int(allowed)),
    ), raising=False)
    assert recovery_cli.is_os_administrator() is allowed


def _authorize(cli_store, monkeypatch, capsys) -> str:
    assert _invoke(monkeypatch, cli_store.path, "authorize", ADMIN_PASSWORD) == 0
    captured = capsys.readouterr()
    assert captured.err == ""
    assert captured.out.count("本机维护授权码（仅显示一次）") == 1
    assert ADMIN_PASSWORD not in captured.out
    proof = captured.out.strip().split("：", 1)[1]
    assert len(proof) == 43
    with cli_store.runtime.session_factory() as db:
        stored = db.get(SystemSetting, cli_store.setting_key)
        assert stored.value == account_recovery.sm3_digest(proof.encode("ascii"))
        assert stored.value != proof
        assert db.scalar(select(AuditLog).where(AuditLog.action == "auth.host_recovery_authorize")) is not None
    assert cli_store.env["PARTYOPS_DATA_DIR"] == str(cli_store.path)
    assert cli_store.env["PARTYOPS_ENVIRONMENT"] == "production"
    return proof


def test_cli_authorization_and_issue_commit_with_a_real_instance_lock(cli_store, monkeypatch, capsys):
    wrong_password = "synthetic-invalid-admin-input"
    assert _invoke(monkeypatch, cli_store.path, "authorize", wrong_password) == 2
    wrong = capsys.readouterr()
    assert wrong.out == "" and wrong_password not in wrong.err
    with cli_store.runtime.session_factory() as db:
        assert db.get(SystemSetting, cli_store.setting_key) is None

    proof = _authorize(cli_store, monkeypatch, capsys)
    lock = DataDirectoryInstanceLock(cli_store.path)
    lock.acquire()
    try:
        assert _invoke(monkeypatch, cli_store.path, "issue", proof) == 2
        rejected = capsys.readouterr()
        assert rejected.out == "" and proof not in rejected.err
        with cli_store.runtime.session_factory() as db:
            assert db.get(SystemSetting, cli_store.setting_key) is not None
            assert db.scalar(select(RecoveryCredential)) is None
    finally:
        lock.release()

    started = utcnow()
    assert _invoke(monkeypatch, cli_store.path, "issue", proof) == 0
    issued = capsys.readouterr()
    assert issued.err == ""
    assert issued.out.count("一次性管理员恢复凭据（仅显示一次）") == 1
    token = issued.out.strip().split("：", 1)[1]
    assert len(token) == 43 and token != proof
    with cli_store.runtime.session_factory() as db:
        assert db.get(SystemSetting, cli_store.setting_key) is None
        saved = db.scalar(select(RecoveryCredential))
        assert saved.user_id == cli_store.admin_id and saved.purpose == "admin_host"
        assert saved.digest == account_recovery.sm3_digest(token.encode("ascii"))
        expiry = saved.expires_at.replace(tzinfo=timezone.utc)
        assert started + timedelta(minutes=30) <= expiry <= utcnow() + timedelta(minutes=30)
        assert db.get(User, cli_store.admin_id).password_hash == cli_store.password_hash
        assert db.scalar(select(AuditLog).where(
            AuditLog.action == "auth.host_recovery_authorization_consumed",
        )) is not None

    assert _invoke(monkeypatch, cli_store.path, "issue", proof) == 2
    replay = capsys.readouterr()
    assert replay.out == "" and proof not in replay.err and token not in replay.err


def test_cli_issue_rolls_back_flushed_changes_and_hides_credentials_on_error(cli_store, monkeypatch, capsys):
    proof = _authorize(cli_store, monkeypatch, capsys)
    captured_tokens = []
    real_issue = account_recovery.issue_credential

    def issue_and_record(*args, **kwargs):
        issued = real_issue(*args, **kwargs)
        captured_tokens.append(issued.credential)
        return issued

    def fail_before_commit(db, *_args, **_kwargs):
        db.flush()
        raise RuntimeError(f"synthetic commit failure {proof} {captured_tokens[-1]}")

    with monkeypatch.context() as fault:
        fault.setattr(account_recovery, "issue_credential", issue_and_record)
        fault.setattr(audit, "write_audit", fail_before_commit)
        assert _invoke(fault, cli_store.path, "issue", proof) == 2
    failure = capsys.readouterr()
    assert captured_tokens
    assert failure.out == ""
    assert "主机恢复未完成" in failure.err
    assert all(value not in failure.err for value in (proof, captured_tokens[0]))
    with cli_store.runtime.session_factory() as db:
        assert db.get(SystemSetting, cli_store.setting_key) is not None
        assert db.scalar(select(RecoveryCredential)) is None
        assert db.scalar(select(AuditLog).where(AuditLog.action == "auth.recovery_issue")) is None

    # 底层事务报错后必须已释放实际锁，原维护授权才可在同一路径重试。
    assert _invoke(monkeypatch, cli_store.path, "issue", proof) == 0
    assert "一次性管理员恢复凭据" in capsys.readouterr().out


def test_cli_aborts_if_terminal_cannot_hide_input(cli_store, monkeypatch, capsys):
    def unreliable_console(_prompt):
        warnings.warn("synthetic echo fallback", recovery_cli.getpass.GetPassWarning, stacklevel=1)
        pytest.fail("无法关闭回显时读到了授权码")

    monkeypatch.setattr(recovery_cli.getpass, "getpass", unreliable_console)
    assert recovery_cli.main(["issue", "--data-dir", str(cli_store.path)]) == 2
    failure = capsys.readouterr()
    assert failure.out == "" and "主机恢复未完成" in failure.err
    with cli_store.runtime.session_factory() as db:
        assert db.scalar(select(RecoveryCredential)) is None


def test_cli_refuses_unmigrated_database_and_releases_lock(cli_store, tmp_path, monkeypatch, capsys):
    legacy_path = tmp_path.resolve() / "unmigrated-host"
    settings = cli_store.runtime.settings.model_copy(update={"data_dir": legacy_path})
    legacy = DatabaseRuntime(settings)
    with legacy.engine.begin() as conn:
        conn.exec_driver_sql("CREATE TABLE legacy_fixture(id INTEGER PRIMARY KEY)")
    monkeypatch.setattr(database, "db_runtime", legacy)
    monkeypatch.setattr(setup_wizard, "assert_windows_service_data_path_security", lambda *_a, **_k: None)
    passphrase = "synthetic-unmigrated-input"
    try:
        assert _invoke(monkeypatch, legacy_path, "authorize", passphrase) == 2
        failure = capsys.readouterr()
        assert failure.out == ""
        assert "账号恢复迁移" in failure.err and passphrase not in failure.err
        lock = DataDirectoryInstanceLock(legacy_path)
        lock.acquire()
        lock.release()
    finally:
        legacy.engine.dispose()


@pytest.mark.skipif(os.name != "nt", reason="仅 Windows 服务目录使用 DACL 校验")
def test_cli_refuses_windows_service_acl_before_locking_or_committing(cli_store, monkeypatch, capsys):
    passphrase = "synthetic-acl-denial-input"

    def deny_acl(data_dir, *, verify_target):
        assert data_dir == cli_store.path and verify_target is True
        raise PermissionError(passphrase)

    monkeypatch.setattr(setup_wizard, "assert_windows_service_data_path_security", deny_acl)
    assert _invoke(monkeypatch, cli_store.path, "authorize", passphrase) == 2
    failure = capsys.readouterr()
    assert failure.out == "" and passphrase not in failure.err
    assert not (cli_store.path / ".partyops-instance.lock").exists()
    with cli_store.runtime.session_factory() as db:
        assert db.get(SystemSetting, cli_store.setting_key) is None



def test_single_admin_emergency_without_password_or_previous_proof(cli_store, monkeypatch, capsys):
    """A locked-out sole admin can recover without a previously issued code."""
    monkeypatch.setattr(recovery_cli, "input", lambda _prompt: "本机应急恢复", raising=False)
    assert recovery_cli.main(["emergency", "--data-dir", str(cli_store.path)]) == 0
    result = capsys.readouterr()
    assert result.err == ""
    assert result.out.count("一次性管理员恢复凭据（仅显示一次）") == 1
    token = result.out.strip().split("一次性管理员恢复凭据（仅显示一次）：", 1)[1]
    assert len(token) == 43
    assert ADMIN_PASSWORD not in result.out
    with cli_store.runtime.session_factory() as db:
        user = db.get(User, cli_store.admin_id)
        assert user.password_hash == cli_store.password_hash
        credential = db.scalar(select(RecoveryCredential).where(
            RecoveryCredential.digest == account_recovery.sm3_digest(token.encode("ascii"))
        ))
        assert credential is not None and credential.user_id == cli_store.admin_id
        assert credential.purpose == "admin_emergency" and credential.used_at is None
        assert db.scalar(select(AuditLog).where(
            AuditLog.action == "auth.host_emergency_recovery_issue"
        )) is not None
    # Emergency credentials must not silently be spent on username-only lookup.
    from app.problems import ProblemException
    with pytest.raises(ProblemException) as denied:
        with cli_store.runtime.session_factory.begin() as db:
            account_recovery.redeem_credential(
                db, account_recovery.RedeemCredential(credential=token), "127.0.0.1"
            )
    assert denied.value.code == "RECOVERY_PASSWORD_REQUIRED"
    with cli_store.runtime.session_factory() as db:
        assert db.scalar(select(RecoveryCredential).where(
            RecoveryCredential.digest == account_recovery.sm3_digest(token.encode("ascii"))
        )).used_at is None
    # The same authenticated redeem transaction must still enforce one-time use.
    with cli_store.runtime.session_factory.begin() as db:
        account_recovery.redeem_credential(
            db, account_recovery.RedeemCredential(
                credential=token, new_password="Synthetic-New-Strong-Passphrase!"
            ), "127.0.0.1"
        )
    with cli_store.runtime.session_factory() as db:
        user = db.get(User, cli_store.admin_id)
        from app.security import verify_password
        assert verify_password("Synthetic-New-Strong-Passphrase!", user.password_hash)
        assert not verify_password(ADMIN_PASSWORD, user.password_hash)
        assert user.role == UserRole.ADMIN
        assert db.scalar(select(RecoveryCredential).where(
            RecoveryCredential.digest == account_recovery.sm3_digest(token.encode("ascii"))
        )).used_at is not None


def test_emergency_refuses_multiple_or_no_active_administrators(cli_store, monkeypatch, capsys):
    monkeypatch.setattr(recovery_cli, "input", lambda _prompt: "本机应急恢复", raising=False)
    with cli_store.runtime.session_factory.begin() as db:
        db.add(User(username="second-admin", display_name="另一名管理员",
                    role=UserRole.ADMIN, password_hash=hash_password("SyntheticOtherAdmin!23")))
    assert recovery_cli.main(["emergency", "--data-dir", str(cli_store.path)]) == 2
    result = capsys.readouterr()
    assert "多管理员" in result.err
    assert "一次性管理员恢复凭据" not in result.out
    with cli_store.runtime.session_factory() as db:
        assert db.scalar(select(RecoveryCredential)) is None
    with cli_store.runtime.session_factory.begin() as db:
        for user in db.scalars(select(User).where(User.role == UserRole.ADMIN)):
            user.active = False
    assert recovery_cli.main(["emergency", "--data-dir", str(cli_store.path)]) == 2
    assert "仅当主机恰有一名有效管理员" in capsys.readouterr().err


def test_emergency_refuses_missing_confirmation_and_locked_instance(cli_store, monkeypatch, capsys):
    monkeypatch.setattr(recovery_cli, "input", lambda _prompt: "incorrect confirmation", raising=False)
    assert recovery_cli.main(["emergency", "--data-dir", str(cli_store.path)]) == 2
    assert "确认未通过" in capsys.readouterr().err
    with cli_store.runtime.session_factory() as db:
        assert db.scalar(select(RecoveryCredential)) is None
    monkeypatch.setattr(recovery_cli, "input", lambda _prompt: "本机应急恢复", raising=False)
    lock = DataDirectoryInstanceLock(cli_store.path)
    lock.acquire()
    try:
        assert recovery_cli.main(["emergency", "--data-dir", str(cli_store.path)]) == 2
        assert "一次性管理员恢复凭据" not in capsys.readouterr().out
    finally:
        lock.release()
    with cli_store.runtime.session_factory() as db:
        assert db.scalar(select(RecoveryCredential)) is None


def test_emergency_refuses_non_admin_console_before_input(cli_store, monkeypatch, capsys):
    _console(monkeypatch, elevated=False)
    monkeypatch.setattr(
        recovery_cli, "input",
        lambda _prompt: pytest.fail("不得在未提权的会话请求本机恢复确认"),
        raising=False,
    )
    assert recovery_cli.main(["emergency", "--data-dir", str(cli_store.path)]) == 2
    assert "提升权限" in capsys.readouterr().err
    with cli_store.runtime.session_factory() as db:
        assert db.scalar(select(RecoveryCredential)) is None


def test_emergency_audit_failure_rolls_back_unissued_token(cli_store, monkeypatch, capsys):
    monkeypatch.setattr(recovery_cli, "input", lambda _prompt: "本机应急恢复", raising=False)
    with monkeypatch.context() as bad:
        def fail_audit(*_args, **_kwargs):
            raise RuntimeError("synthetic audit failure")
        bad.setattr(audit, "write_audit", fail_audit)
        assert recovery_cli.main(["emergency", "--data-dir", str(cli_store.path)]) == 2
    result = capsys.readouterr()
    assert "一次性管理员恢复凭据" not in result.out
    assert "主机恢复未完成" in result.err
    with cli_store.runtime.session_factory() as db:
        assert db.scalar(select(RecoveryCredential)) is None
    assert recovery_cli.main(["emergency", "--data-dir", str(cli_store.path)]) == 0
    assert "一次性管理员恢复凭据" in capsys.readouterr().out


def _synthetic_windows_personal_owner_scope(tmp_path, monkeypatch):
    """Mock only Windows SID/ACL APIs; use real local files and saved config parsing."""
    import json

    user_sid = "S-1-5-21-1111-2222-3333-1001"
    data_dir = tmp_path / "owned-personal-data"
    data_dir.mkdir()
    (data_dir / "partyops.db").write_bytes(b"synthetic-sqlite-placeholder")
    root = tmp_path / "PartyOps"
    root.mkdir()
    personal_env = root / "personal.env"
    personal_env.write_text(
        "PARTYOPS_MODE=personal\nPARTYOPS_DATA_DIR=" + repr(str(data_dir)) + "\n",
        encoding="utf-8",
    )
    mode_file = root / "mode.json"
    mode_file.write_text(
        json.dumps({"mode": "personal", "config_path": str(personal_env)}),
        encoding="utf-8",
    )
    # Simulate LOCALAPPDATA without importing setup_wizard (which can load DB).
    monkeypatch.setattr(
        recovery_cli, "os",
        SimpleNamespace(
            name="nt",
            getenv=lambda name, default=None: str(tmp_path)
            if name == "LOCALAPPDATA" else default,
        ),
    )
    state = {"owner": user_sid, "writer": user_sid}
    class Handle:
        def Close(self):
            pass
    class DACL:
        def GetAceCount(self):
            return 1
        def GetAce(self, _index):
            return ((0, 0), 0x40000000, state["writer"])
    class Descriptor:
        def GetSecurityDescriptorOwner(self):
            return state["owner"]
        def GetSecurityDescriptorDacl(self):
            return DACL()
    fake_security = SimpleNamespace(
        TOKEN_QUERY=8,
        TokenUser=1,
        OWNER_SECURITY_INFORMATION=1,
        DACL_SECURITY_INFORMATION=4,
        ACCESS_ALLOWED_ACE_TYPE=0,
        ACCESS_ALLOWED_OBJECT_ACE_TYPE=5,
        OpenProcessToken=lambda *_args: Handle(),
        GetTokenInformation=lambda *_args: (user_sid, 0),
        ConvertSidToStringSid=lambda sid: sid,
        GetFileSecurity=lambda *_args: Descriptor(),
    )
    monkeypatch.setitem(sys.modules, "win32api", SimpleNamespace(GetCurrentProcess=lambda: object()))
    monkeypatch.setitem(sys.modules, "win32security", fake_security)
    return SimpleNamespace(
        root=root, path=data_dir, env=personal_env, mode=mode_file, state=state,
    )


def test_windows_personal_owner_scope_requires_saved_mode_acl_and_identity(tmp_path, monkeypatch):
    files = _synthetic_windows_personal_owner_scope(tmp_path, monkeypatch)
    assert recovery_cli.windows_personal_owner_scope(files.path)
    files.mode.write_text('{"mode":"host"}', encoding="utf-8")
    assert not recovery_cli.windows_personal_owner_scope(files.path)
    files.mode.write_text(
        '{"mode":"personal","config_path":' + __import__("json").dumps(str(files.env)) + '}',
        encoding="utf-8",
    )
    files.env.write_text("PARTYOPS_MODE=personal\nPARTYOPS_DATA_DIR=/unrelated-db\n", encoding="utf-8")
    assert not recovery_cli.windows_personal_owner_scope(files.path)
    files.env.write_text(
        "PARTYOPS_MODE=personal\nPARTYOPS_DATA_DIR=" + repr(str(files.path)) + "\n",
        encoding="utf-8",
    )
    files.state["owner"] = "S-1-5-21-foreign-user"
    assert not recovery_cli.windows_personal_owner_scope(files.path)
    files.state["owner"] = "S-1-5-21-1111-2222-3333-1001"
    files.state["writer"] = "S-1-5-11"  # Authenticated Users: Generic Write
    assert not recovery_cli.windows_personal_owner_scope(files.path)
    files.state["writer"] = "S-1-5-21-1111-2222-3333-1001"
    assert recovery_cli.windows_personal_owner_scope(files.path)


def test_windows_personal_emergency_allows_verified_owner_without_administrator(
    tmp_path, monkeypatch, capsys,
):
    """Personal-only fallback must not require a SYSTEM service ACL or OS admin."""
    data_dir = tmp_path.resolve() / "synthetic-personal-data"
    _console(monkeypatch, elevated=False)
    monkeypatch.setattr(recovery_cli, "os", SimpleNamespace(name="nt"))
    monkeypatch.setattr(recovery_cli, "trusted_data_directory", lambda _path: data_dir)
    monkeypatch.setattr(recovery_cli, "windows_personal_owner_scope", lambda path: path == data_dir)
    monkeypatch.setattr(recovery_cli, "input", lambda _prompt: "本机应急恢复", raising=False)
    calls = []

    def capture_run(path, action, identity, proof, *, personal_scope=False):
        calls.append((path, action, identity, proof, personal_scope))
        return "T" * 43

    monkeypatch.setattr(recovery_cli, "_run", capture_run)
    assert recovery_cli.main(["emergency", "--data-dir", str(data_dir)]) == 0
    assert calls == [(data_dir, "emergency", "", "本机应急恢复", True)]
    assert "一次性管理员恢复凭据" in capsys.readouterr().out


def test_windows_personal_emergency_rechecks_saved_owner_before_lock(
    tmp_path, monkeypatch,
):
    """No stale preflight may allow an unowned DB to skip service ACL enforcement."""
    data_dir = tmp_path.resolve() / "synthetic-personal"
    monkeypatch.setattr(recovery_cli, "windows_personal_owner_scope", lambda _path: False)
    with pytest.raises(recovery_cli.RecoveryCLIError, match="个人模式目录归属"):
        recovery_cli._run(data_dir, "emergency", "", "本机应急恢复", personal_scope=True)


def test_personal_scope_issue_skips_system_acl_but_preserves_lock_and_audit(
    cli_store, monkeypatch,
):
    """Personal-owner recovery uses its own verified DACL, not host SYSTEM DACL."""
    monkeypatch.setattr(
        recovery_cli, "windows_personal_owner_scope",
        lambda path: path == cli_store.path,
    )
    monkeypatch.setattr(
        setup_wizard, "assert_windows_service_data_path_security",
        lambda *_a, **_k: pytest.fail("个人数据库不应该应用 SYSTEM 服务 ACL"),
    )
    monkeypatch.setattr(
        recovery_cli, "os",
        SimpleNamespace(name="nt", environ=cli_store.env),
    )
    token = recovery_cli._run(
        cli_store.path, "emergency", "", "本机应急恢复", personal_scope=True
    )
    assert len(token) == 43
    with cli_store.runtime.session_factory() as db:
        credential = db.scalar(select(RecoveryCredential))
        assert credential is not None and credential.purpose == "admin_emergency"
        assert credential.user_id == cli_store.admin_id
        audit_entry = db.scalar(
            select(AuditLog).where(
                AuditLog.action == "auth.host_emergency_recovery_issue"
            )
        )
        assert audit_entry is not None
        assert audit_entry.detail.get("method") == "windows-personal-owner-single-admin"
    assert cli_store.env["PARTYOPS_MODE"] == "personal"
    lock = DataDirectoryInstanceLock(cli_store.path)
    lock.acquire()
    lock.release()
