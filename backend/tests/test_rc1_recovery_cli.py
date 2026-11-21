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
