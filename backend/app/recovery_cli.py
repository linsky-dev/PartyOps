"""已安装主机上需提升权限的管理员账号恢复工具。"""

from __future__ import annotations

import argparse
import getpass
import json
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



def _windows_personal_scope(path: Path, *, repair: bool = False) -> str:
    """Validate saved personal identity, distinguish repairable inherited ACLs.

    Never adopt a foreign-owned or shared host database. An OS-owned legacy
    data directory is accepted only with an explicit current-user write grant
    and a private personal-only ownership marker. Broad ACL repair is limited
    to objects already owned by the current user, with no foreign explicit
    writers and no elevation. No ownership transfer or database edits.
    """
    if os.name != "nt":
        return "denied"
    import json
    import shlex


    # Do not import setup_wizard here: its transitive imports may initialize
    # app.database before PARTYOPS_DATA_DIR and PARTYOPS_MODE are bound.
    try:
        import win32api  # type: ignore[import-untyped]
        import win32security  # type: ignore[import-untyped]

        root = Path(os.getenv("LOCALAPPDATA", Path.home() / "AppData" / "Local")) / "PartyOps"
        mode_path = root / "mode.json"
        config_path = root / "personal.env"
        for entry, limit in ((root, None), (mode_path, 8192), (config_path, 65536),
                             (path, None), (path / "partyops.db", None)):
            info = entry.lstat()
            if stat.S_ISLNK(info.st_mode) or (
                getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
            ):
                return "denied"
            if limit is not None and info.st_size > limit:
                return "denied"
        mode = json.loads(mode_path.read_text(encoding="utf-8"))
        if not isinstance(mode, dict) or mode.get("mode") != "personal":
            return "denied"
        configured_path = Path(str(mode.get("config_path") or config_path))
        if not configured_path.is_absolute() or configured_path.resolve(strict=True) != config_path.resolve(strict=True):
            return "denied"
        environment: dict[str, str] = {}
        # Parse only the two needed keys as data; never execute saved content.
        for line in config_path.read_text(encoding="utf-8").splitlines():
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            key, separator, raw_value = line.partition("=")
            if not separator or key not in {"PARTYOPS_MODE", "PARTYOPS_DATA_DIR"}:
                continue
            values = shlex.split(raw_value)
            if len(values) != 1 or key in environment:
                return "denied"
            environment[key] = values[0]
        saved_path = environment.get("PARTYOPS_DATA_DIR", "").strip()
        if environment.get("PARTYOPS_MODE") != "personal" or not saved_path:
            return "denied"
        declared = Path(saved_path)
        if not declared.is_absolute() or declared.resolve(strict=True) != path.resolve(strict=True):
            return "denied"

        token = win32security.OpenProcessToken(
            win32api.GetCurrentProcess(), win32security.TOKEN_QUERY
        )
        try:
            user_sid = win32security.ConvertSidToStringSid(
                win32security.GetTokenInformation(token, win32security.TokenUser)[0]
            )
        finally:
            token.Close()
        allowed_writers = {
            user_sid, "S-1-5-18", "S-1-5-32-544",
            "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464",
        }
        write_mask = (
            0x00040000 | 0x00080000 | 0x10000000 | 0x40000000 |
            0x00000002 | 0x00000004 | 0x00000010 | 0x00000040 | 0x00000100
        )
        # The profile must remain private. Legacy data ACLs may inherit
        # common local-disk groups; only repair our own data, never a foreign
        # owner's files or any configured host database.
        broad_groups = {"S-1-1-0", "S-1-5-11", "S-1-5-32-545"}
        os_owners = {"S-1-5-18", "S-1-5-32-544"}
        marker = path / ".partyops-data-root.json"
        profile_entries = (root, mode_path, config_path)
        data_entries = [path, path / "partyops.db"]
        for name in ("partyops.db-wal", "partyops.db-shm", "partyops.db-journal", ".partyops-instance.lock"):
            item = path / name
            if item.exists() or item.is_symlink():
                info = item.lstat()
                if not stat.S_ISREG(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
                    return "denied"
                data_entries.append(item)

        def acl_state(entry: Path):
            descriptor = win32security.GetFileSecurity(
                str(entry), win32security.OWNER_SECURITY_INFORMATION |
                win32security.DACL_SECURITY_INFORMATION,
            )
            owner = win32security.ConvertSidToStringSid(descriptor.GetSecurityDescriptorOwner())
            dacl = descriptor.GetSecurityDescriptorDacl()
            if dacl is None or owner not in {user_sid, *os_owners}:
                return None
            direct = False
            broad = False
            for index in range(dacl.GetAceCount()):
                ace = dacl.GetAce(index)
                ace_type, ace_flags = ace[0]
                if ace_flags & 0x08:
                    continue
                if ace_type not in {
                    win32security.ACCESS_ALLOWED_ACE_TYPE,
                    getattr(win32security, "ACCESS_ALLOWED_OBJECT_ACE_TYPE", 5),
                }:
                    # Unknown allow/callback ACEs cannot be interpreted safely.
                    if ace_type not in {1, 2, 3, 6, 7, 8}:
                        return None
                    continue
                sid = win32security.ConvertSidToStringSid(ace[-1])
                if not int(ace[1]) & write_mask:
                    continue
                if sid == user_sid:
                    direct = True
                elif sid in broad_groups:
                    broad = True
                elif sid not in allowed_writers:
                    return None
            if owner != user_sid and not direct:
                return None
            return owner, broad

        profile_states = [acl_state(entry) for entry in profile_entries]
        if any(value is None or value[1] for value in profile_states):
            return "denied"
        data_states = [acl_state(entry) for entry in data_entries]
        if any(value is None for value in data_states):
            return "denied"
        needs_marker = any(value[0] != user_sid or value[1] for value in data_states)
        if needs_marker:
            # Marker must be a regular current-user-owned personal-only
            # record. Existing host/client/mixed scope is never adopted.
            info = marker.lstat()
            if not stat.S_ISREG(info.st_mode) or info.st_size > 8192 or getattr(info, "st_file_attributes", 0) & 0x400:
                return "denied"
            mark = json.loads(marker.read_text(encoding="utf-8"))
            if not isinstance(mark, dict) or mark.get("product") != "PartyOps" or mark.get("app_id") != "1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A":
                return "denied"
            if not ((mark.get("format_version") == 2 and mark.get("scopes") == ["personal"]) or
                    (mark.get("format_version") == 1 and mark.get("scope") == "personal")):
                return "denied"
            marker_state = acl_state(marker)
            if marker_state is None or marker_state[0] != user_sid:
                return "denied"
            data_entries.append(marker)
            data_states.append(marker_state)
            # Do not reinterpret an active host configuration as personal.
            host_config = Path(os.getenv("PROGRAMDATA", "C:/ProgramData")) / "PartyOps" / "partyops.env"
            if host_config.is_file():
                for line in host_config.read_text(encoding="utf-8-sig").splitlines():
                    key, sep, raw = line.partition("=")
                    if sep and key == "PARTYOPS_DATA_DIR":
                        values = shlex.split(raw)
                        if len(values) != 1 or Path(values[0]).resolve() == path.resolve():
                            return "denied"
        if not any(value[1] for value in data_states):
            return "verified"
        # Never repair ACLs on a SYSTEM/admin/foreign-owned directory or file;
        # no takeover and no elevated helper. Only current user's old
        # personal files with ordinary inherited broad group permissions.
        if any(value[0] != user_sid for value in data_states):
            return "denied"
        if not repair:
            return "repairable"
        # Tighten only the database security boundary, not attachments,
        # unrelated files or the parent volume. Protect DACL from inheritance.
        for entry, state in zip(data_entries, data_states):
            if not state[1]:
                continue
            acl = win32security.ACL()
            flags = 0x03 if entry == path else 0  # object/container inherit
            for sid in (user_sid, "S-1-5-18", "S-1-5-32-544"):
                acl.AddAccessAllowedAceEx(
                    win32security.ACL_REVISION_DS, flags, 0x001F01FF,
                    win32security.ConvertStringSidToSid(sid),
                )
            win32security.SetNamedSecurityInfo(
                str(entry), win32security.SE_FILE_OBJECT,
                win32security.DACL_SECURITY_INFORMATION |
                win32security.PROTECTED_DACL_SECURITY_INFORMATION,
                None, None, acl, None,
            )
        # No reset may proceed unless actual ACL readback now passes.
        return _windows_personal_scope(path)
    except Exception:  # OS/ACL/parse errors are fail-closed, never expose secrets.
        return "denied"


def windows_personal_owner_scope(path: Path) -> bool:
    return _windows_personal_scope(path) == "verified"


def windows_personal_recovery_status(path: Path) -> str:
    return _windows_personal_scope(path)


def repair_windows_personal_recovery_acl(path: Path) -> bool:
    return _windows_personal_scope(path, repair=True) == "verified"


def personal_owner_scope(path: Path) -> bool:
    """Bind recovery to the current OS account and its saved personal configuration."""
    if os.name == "nt":
        return windows_personal_owner_scope(path)
    import shlex

    try:
        root = (
            Path.home() / "Library" / "Application Support" / "PartyOps" / "Config"
            if sys.platform == "darwin"
            else Path(os.getenv("XDG_CONFIG_HOME", Path.home() / ".config")) / "partyops"
        )
        mode_path, config = root / "mode.json", root / "personal.env"
        for item in (root, mode_path, config, path, path / "partyops.db"):
            info = item.lstat()
            if stat.S_ISLNK(info.st_mode) or info.st_uid != getattr(os, "geteuid", lambda: -1)() or info.st_mode & 0o022:
                return False
        if mode_path.stat().st_size > 8192 or config.stat().st_size > 65536:
            return False
        mode = json.loads(mode_path.read_text(encoding="utf-8"))
        if mode.get("mode") != "personal" or Path(mode.get("config_path") or config).resolve() != config.resolve():
            return False
        values = {}
        for line in config.read_text(encoding="utf-8").splitlines():
            key, separator, raw = line.partition("=")
            if separator and key in {"PARTYOPS_MODE", "PARTYOPS_DATA_DIR"}:
                parts = shlex.split(raw)
                if len(parts) != 1 or key in values:
                    return False
                values[key] = parts[0]
        declared = Path(values.get("PARTYOPS_DATA_DIR", ""))
        return (values.get("PARTYOPS_MODE") == "personal" and declared.is_absolute()
                and declared.resolve(strict=True) == path.resolve(strict=True))
    except (OSError, ValueError, TypeError, AttributeError):
        return False


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



def issue_single_admin_emergency(
    db: Session, confirmation: str, *, method: str = "elevated-local-single-admin"
) -> str:
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

    if not secrets.compare_digest(confirmation.encode("utf-8"), "本机应急恢复".encode("utf-8")):
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
        {"method": method, "credential_ttl_minutes": 30},
        "local-emergency-console",
    )
    return issued.credential


def _run(
    path: Path, action: str, identity: str, proof: str, *, personal_scope: bool = False,
    new_password: str | None = None,
) -> str:
    if personal_scope:
        owner_ok = windows_personal_owner_scope(path) if action == "emergency" else personal_owner_scope(path)
        if action not in {"emergency", "desktop-reset"} or not owner_ok:
            raise RecoveryCLIError("个人模式目录归属或权限校验未通过。")
    os.environ["PARTYOPS_DATA_DIR"] = str(path)
    os.environ["PARTYOPS_ENVIRONMENT"] = "production"
    if personal_scope:
        os.environ["PARTYOPS_MODE"] = "personal"
        from .config import get_settings

        get_settings.cache_clear()

    from sqlalchemy import inspect

    if action == "desktop-reset" and not personal_scope:
        if not is_os_administrator():
            raise RecoveryCLIError("团队主机需要操作系统管理员授权。")
        os.environ["PARTYOPS_MODE"] = "host"
        from .config import get_settings

        get_settings.cache_clear()

    from .account_recovery import ensure_authoritative_host
    from .database import db_runtime
    from .main import DataDirectoryInstanceLock

    ensure_authoritative_host()
    if os.name == "nt" and not personal_scope:
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
            if action == "desktop-reset":
                from pydantic import SecretStr

                from .account_recovery import RedeemCredential, redeem_credential

                if new_password is None or not 8 <= len(new_password) <= 128:
                    raise RecoveryCLIError("新密码长度必须为 8—128 个字符。")
                token = issue_single_admin_emergency(
                    db, proof, method="desktop-personal-owner" if personal_scope else "desktop-elevated-host",
                )
                db.flush()
                user = redeem_credential(db, RedeemCredential(
                    credential=SecretStr(token), new_password=SecretStr(new_password),
                ), "local-desktop-recovery")
                return user.username
            if action == "emergency":
                return issue_single_admin_emergency(
                    db, proof, method=(
                        "windows-personal-owner-single-admin"
                        if personal_scope else "elevated-local-single-admin"
                    )
                )
            return issue_local_admin_recovery(db, proof)
    finally:
        lock.release()


def _configure_recovery_stdio() -> None:
    """Frozen Windows may ignore PYTHONUTF8; use UTF-8 for redirected recovery streams.

    Keep native interactive terminal handling and never change other application entrypoints.
    """
    for stream in (sys.stdin, sys.stdout, sys.stderr):
        if stream is None or stream.isatty():
            continue
        reconfigure = getattr(stream, "reconfigure", None)
        if callable(reconfigure):
            reconfigure(encoding="utf-8", errors="strict")


def main(argv: list[str] | None = None) -> int:
    _configure_recovery_stdio()
    parser = argparse.ArgumentParser(description="PartyOps 主机管理员账号恢复")
    parser.add_argument("action", choices=("authorize", "issue", "emergency", "desktop-reset"))
    parser.add_argument("--data-dir", required=True, type=Path)
    args = parser.parse_args(argv)
    if args.action == "desktop-reset":
        # This is a local executable entry, never an HTTP route. The desktop
        # wizard sends the password on a private pipe, not argv, URL or log.
        try:
            path = trusted_data_directory(args.data_dir)
            personal = personal_owner_scope(path)
            if not personal:
                if not is_os_administrator() or os.name != "nt":
                    raise RecoveryCLIError("仅原个人系统账号或受支持的本机主机管理员可以恢复。")
                import shlex

                config = Path(os.getenv("PROGRAMDATA", "C:/ProgramData")) / "PartyOps" / "partyops.env"
                if config.is_symlink() or not config.is_file() or config.stat().st_size > 65536:
                    raise RecoveryCLIError("主机配置不可验证。")
                values = {}
                for line in config.read_text(encoding="utf-8").splitlines():
                    key, sep, raw = line.partition("=")
                    if sep and key in {"PARTYOPS_MODE", "PARTYOPS_DATA_DIR"}:
                        parts = shlex.split(raw)
                        if len(parts) != 1 or key in values:
                            raise RecoveryCLIError("主机配置不可验证。")
                        values[key] = parts[0]
                declared = Path(values.get("PARTYOPS_DATA_DIR", ""))
                if values.get("PARTYOPS_MODE") != "host" or not declared.is_absolute() or declared.resolve(strict=True) != path.resolve(strict=True):
                    raise RecoveryCLIError("恢复目录不是当前已配置的主机业务目录。")
            payload = json.loads(sys.stdin.read(8193))
            if (not isinstance(payload, dict) or set(payload) != {"password", "confirmation"}
                    or not isinstance(payload["password"], str)
                    or payload["confirmation"] != "本机应急恢复"):
                raise RecoveryCLIError("恢复请求无效。")
            username = _run(path, args.action, "", payload["confirmation"],
                            personal_scope=personal, new_password=payload["password"])
            print(json.dumps({"username": username}, ensure_ascii=True))
            return 0
        except RecoveryCLIError as exc:
            print(str(exc), file=sys.stderr)
            return 2
        except Exception:
            print("账号恢复未完成，请确认唯一管理员、数据目录权限及原进程已退出。", file=sys.stderr)
            return 2
    if not sys.stdin.isatty() or not sys.stdout.isatty():
        print("此工具需在主机提升权限或个人目录所属账号的本机交互终端中运行。", file=sys.stderr)
        return 2
    if not is_os_administrator() and (os.name != "nt" or args.action != "emergency"):
        print("此工具需在主机提升权限的本机交互终端中运行。", file=sys.stderr)
        return 2
    try:
        if os.name != "nt" and not is_os_administrator():
            raise RecoveryCLIError("此工具需在主机提升权限的本机交互终端中运行。")
        path = trusted_data_directory(args.data_dir)
        personal_scope = (
            args.action == "emergency" and os.name == "nt"
            and windows_personal_owner_scope(path)
        )
        if not is_os_administrator() and not personal_scope:
            raise RecoveryCLIError("此工具需在主机提升权限的本机交互终端中运行。")
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
        result = _run(path, args.action, identity, proof, personal_scope=personal_scope)
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
