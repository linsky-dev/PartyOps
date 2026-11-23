"""已安装主机上需提升权限的管理员账号恢复工具。"""

from __future__ import annotations

import argparse
import getpass
import os
import secrets
import stat
import sys
import warnings
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from sqlalchemy.orm import Session


class RecoveryCLIError(ValueError):
    """可以安全显示给本机操作员的固定提示。"""


def is_os_administrator() -> bool:
    if os.name == "nt":
        from ctypes import windll

        return bool(windll.shell32.IsUserAnAdmin())
    geteuid = getattr(os, "geteuid", None)
    return geteuid is not None and geteuid() == 0


def trusted_data_directory(path: Path) -> Path:
    """在连接现有数据库前确认实际本机路径，拒绝重解析点与过宽权限。"""
    if not path.is_absolute():
        raise RecoveryCLIError("请提供主机已配置的绝对数据目录。")
    if os.name == "nt":
        from ctypes import windll

        if windll.kernel32.GetDriveTypeW(path.anchor) != 3:
            raise RecoveryCLIError("主机恢复只能使用本机固定磁盘上的数据目录。")
    if ".." in path.parts:
        raise RecoveryCLIError("恢复目录必须使用规范的绝对路径。")
    database = path / "partyops.db"
    if not database.is_file() or not path.is_dir():
        raise RecoveryCLIError("指定目录没有已初始化的 PartyOps 主机数据库。")
    for component in (database, path / ".partyops-instance.lock", path, *path.parents):
        try:
            info = component.lstat()
        except FileNotFoundError:
            if component == path / ".partyops-instance.lock":
                continue
            raise
        if stat.S_ISLNK(info.st_mode) or (
            getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
        ):
            raise RecoveryCLIError("恢复目录或数据库包含不受支持的链接路径。")
        if os.name != "nt" and component in {path, database} and info.st_mode & 0o022:
            raise RecoveryCLIError("数据目录或数据库允许其他账号写入，请先核对主机权限。")
    return path


def authorize_local_maintenance(db: Session, username: str, password: str) -> str:
    """在仍能验证现有管理员密码时，登记独立的本机维护授权。"""
    from sqlalchemy import select

    from .account_recovery import (
        HOST_RECOVERY_PREFIX,
        revoke_recovery_credentials,
        sm3_digest,
    )
    from .audit import write_audit
    from .enums import UserRole
    from .models import SystemSetting, User
    from .security import verify_password

    admin = db.scalar(select(User).where(
        User.username == username.strip().lower(),
        User.role == UserRole.ADMIN,
        User.active.is_(True),
        User.archived_at.is_(None),
    ))
    if admin is None or not verify_password(password, admin.password_hash):
        raise RecoveryCLIError("管理员授权验证未通过。")
    proof = secrets.token_urlsafe(32)
    setting_key = f"{HOST_RECOVERY_PREFIX}{admin.id}"
    db.merge(SystemSetting(key=setting_key, value=sm3_digest(proof.encode("ascii"))))
    revoke_recovery_credentials(db, admin.id, "admin_host")
    write_audit(db, admin, "auth.host_recovery_authorize", "user", admin.id)
    return proof


def issue_local_admin_recovery(db: Session, proof: str) -> str:
    """消耗本机维护授权并签发 30 分钟凭据，不接触密码或角色。"""
    from sqlalchemy import select

    from .account_recovery import (
        HOST_RECOVERY_PREFIX,
        TOKEN_PATTERN,
        issue_credential,
        sm3_digest,
    )
    from .audit import write_audit
    from .enums import UserRole
    from .models import SystemSetting, User

    proof = proof.strip()
    if TOKEN_PATTERN.fullmatch(proof):
        digest = sm3_digest(proof.encode("ascii"))
        for record in db.scalars(select(SystemSetting).where(SystemSetting.key.startswith(HOST_RECOVERY_PREFIX))):
            if isinstance(record.value, str) and secrets.compare_digest(record.value, digest):
                admin = db.get(User, record.key[len(HOST_RECOVERY_PREFIX):])
                if admin and admin.active and admin.archived_at is None and admin.role == UserRole.ADMIN:
                    db.delete(record)
                    issued = issue_credential(db, admin, "admin_host", None, "local-admin-console")
                    write_audit(db, None, "auth.host_recovery_authorization_consumed", "user", admin.id)
                    return issued.credential
                break
    raise RecoveryCLIError("本机维护授权无效、已使用或已撤销。")



def issue_single_admin_emergency(db: Session, confirmation: str) -> str:
    """Last-admin break-glass: elevated *local* operator, protected DB and exclusive lock.

    This action is NEVER exposed through HTTP. It is intentionally scoped to
    a single active administrator: when several admins exist, use another
    authenticated administrator's normal reset workflow instead.
    """
    from sqlalchemy import select

    from .account_recovery import issue_credential
    from .audit import write_audit
    from .enums import UserRole
    from .models import User

    if not secrets.compare_digest(confirmation, "本机应急恢复"):
        raise RecoveryCLIError("本机应急恢复确认未通过。")
    admins = db.scalars(select(User).where(
        User.role == UserRole.ADMIN,
        User.active.is_(True),
        User.archived_at.is_(None),
    )).all()
    if len(admins) != 1:
        raise RecoveryCLIError(
            "仅当主机恰有一名有效管理员时可使用本机应急恢复；"
            "多管理员单位请由其他管理员核验身份后重置。"
        )
    admin = admins[0]
    issued = issue_credential(db, admin, "admin_emergency", None, "local-emergency-console")
    write_audit(
        db, None, "auth.host_emergency_recovery_issue", "user", admin.id,
        {"method": "elevated-local-single-admin", "credential_ttl_minutes": 30},
        "local-emergency-console",
    )
    return issued.credential


def _run(path: Path, action: str, identity: str, proof: str) -> str:
    os.environ["PARTYOPS_DATA_DIR"] = str(path)
    os.environ["PARTYOPS_ENVIRONMENT"] = "production"

    from sqlalchemy import inspect

    from .account_recovery import ensure_authoritative_host
    from .database import db_runtime
    from .main import DataDirectoryInstanceLock

    ensure_authoritative_host()
    if os.name == "nt":
        from .setup_wizard import assert_windows_service_data_path_security

        assert_windows_service_data_path_security(path, verify_target=True)
    lock = DataDirectoryInstanceLock(path)
    lock.acquire()
    try:
        if not inspect(db_runtime.engine).has_table("recovery_credentials"):
            raise RecoveryCLIError("主机尚未完成 1.4.6 账号恢复迁移。")
        with db_runtime.session_factory.begin() as db:
            if action == "authorize":
                return authorize_local_maintenance(db, identity, proof)
            if action == "emergency":
                return issue_single_admin_emergency(db, proof)
            return issue_local_admin_recovery(db, proof)
    finally:
        lock.release()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="PartyOps 主机管理员账号恢复")
    parser.add_argument("action", choices=("authorize", "issue", "emergency"))
    parser.add_argument("--data-dir", required=True, type=Path)
    args = parser.parse_args(argv)
    if not is_os_administrator() or not sys.stdin.isatty() or not sys.stdout.isatty():
        print("此工具需在主机提升权限的本机交互终端中运行。", file=sys.stderr)
        return 2
    try:
        path = trusted_data_directory(args.data_dir)
        identity = input("管理员用户名：").strip() if args.action == "authorize" else ""
        if args.action == "emergency":
            print(
                "危险操作：仅适用于唯一管理员失去全部登录与离线恢复凭据的已安装主机。"
                "应确认业务数据已备份、主机服务已停止；此操作会留下安全审计记录。"
            )
            proof = input("如确需恢复，请输入“本机应急恢复”以继续：").strip()
        else:
            prompt = "管理员当前密码：" if args.action == "authorize" else "本机维护授权码："
            with warnings.catch_warnings():
                warnings.simplefilter("error", getpass.GetPassWarning)
                proof = getpass.getpass(prompt)
        result = _run(path, args.action, identity, proof)
    except RecoveryCLIError as exc:
        print(str(exc), file=sys.stderr)
        return 2
    except Exception:
        # 底层诊断可能含上下文参数，终端只输出固定错误。数据库未提交时自动回滚。
        print("主机恢复未完成，请核对主机状态和运行时。", file=sys.stderr)
        return 2
    title = "本机维护授权码" if args.action == "authorize" else "一次性管理员恢复凭据"
    print(f"{title}（仅显示一次）：{result}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
