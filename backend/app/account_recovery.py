"""一次性账号恢复与登录信息事务。"""

from __future__ import annotations

import re
import secrets
from collections.abc import Iterator
from contextlib import contextmanager
from datetime import datetime, timedelta
from typing import Literal

from cryptography.exceptions import UnsupportedAlgorithm
from cryptography.hazmat.primitives import hashes
from pydantic import BaseModel, Field, SecretStr
from sqlalchemy import delete, or_, select, update
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from .audit import write_audit
from .config import get_settings
from .database import db_runtime
from .enums import UserRole
from .login_throttle import LoginThrottle
from .models import LoginSession, RecoveryCredential, SystemSetting, User, utcnow
from .problems import ProblemException
from .security import hash_password, verify_password

Purpose = Literal["staff", "admin_offline", "admin_host"]
USERNAME_PATTERN = r"^[A-Za-z0-9_.-]+$"
TOKEN_PATTERN = re.compile(r"[A-Za-z0-9_-]{43}\Z", re.ASCII)
RECOVERY_TTL = timedelta(minutes=30)
NO_STORE = {"Cache-Control": "no-store"}
HOST_RECOVERY_PREFIX = "host_recovery:"
recovery_throttle = LoginThrottle(log_failures=False)
credential_change_throttle = LoginThrottle(log_failures=False)


class CredentialChanges(BaseModel):
    username: str | None = Field(default=None, min_length=3, max_length=64, pattern=USERNAME_PATTERN)
    new_password: SecretStr | None = Field(default=None, min_length=8, max_length=128)

    @property
    def has_changes(self) -> bool:
        return self.username is not None or self.new_password is not None


class CurrentPassword(BaseModel):
    current_password: SecretStr = Field(min_length=1, max_length=128)


class UpdateCredentials(CredentialChanges, CurrentPassword):
    """已登录用户提交当前密码和新登录信息。"""


class RedeemCredential(CredentialChanges):
    credential: SecretStr = Field(min_length=1, max_length=128)


class IssuedCredential(BaseModel):
    credential: str = Field(repr=False)
    expires_at: datetime | None


class RecoveredUsername(BaseModel):
    username: str


@contextmanager
def credential_transaction(db: Session) -> Iterator[None]:
    """在处理线程回滚失败写入并释放 SQLite 锁。"""
    with db_runtime.write_lock:
        try:
            yield
            db.commit()
        except IntegrityError:
            db.rollback()
            raise ProblemException(
                409, "CREDENTIALS_CONFLICT", "登录信息发生冲突",
                "请确认用户名可用并重试。", headers=NO_STORE,
            ) from None
        except BaseException:
            db.rollback()
            raise


def ensure_authoritative_host() -> None:
    if get_settings().mode == "client":
        raise ProblemException(403, "RECOVERY_HOST_REQUIRED", "请使用权威主机", "请在主机登录页面继续。")


def sm3_digest(value: bytes) -> str:
    """调用运行时密码库计算 SM3，运行时缺失时安全失败。"""
    try:
        digest = hashes.Hash(hashes.SM3())
        digest.update(value)
        return digest.finalize().hex()
    except UnsupportedAlgorithm:
        raise ProblemException(
            503, "RECOVERY_CRYPTO_UNAVAILABLE", "恢复功能暂不可用",
            "当前运行时不支持 SM3，请由维护人员检查安装制品。",
            headers=NO_STORE,
        ) from None


def recovery_error() -> ProblemException:
    return ProblemException(
        400, "RECOVERY_INVALID", "恢复凭据不可用", "请检查凭据或联系管理员重新签发。",
        headers=NO_STORE,
    )


def verify_current_password(db: Session, user: User, supplied: SecretStr, address: str) -> None:
    # 鉴权依赖可能早于当前写临界区读到用户；在锁内刷新后复核当前密码。
    db.refresh(user)
    throttle_key = user.id
    _check_throttle(credential_change_throttle, throttle_key, address)
    if not user.active or user.archived_at is not None or not verify_password(
        supplied.get_secret_value(), user.password_hash
    ):
        _check_throttle(credential_change_throttle, throttle_key, address, failed=True)
        raise ProblemException(
            403, "CURRENT_PASSWORD_INVALID", "当前密码不正确", "请核对当前密码后重试。",
            headers=NO_STORE,
        )
    credential_change_throttle.record_success(throttle_key)


def _check_throttle(throttle: LoginThrottle, key: str, address: str, *, failed: bool = False) -> None:
    retry_after = (
        throttle.record_failure(key, address) if failed else throttle.retry_after(key, address)
    )
    if retry_after:
        raise ProblemException(
            429, "RECOVERY_THROTTLED", "操作过于频繁", "请稍后重试。",
            extra={"retry_after_seconds": retry_after},
            headers={**NO_STORE, "Retry-After": str(retry_after)},
        )


def _purpose_matches_user(user: User, purpose: str) -> bool:
    if purpose == "staff":
        return user.role != UserRole.ADMIN
    return purpose in {"admin_offline", "admin_host"} and user.role == UserRole.ADMIN


def revoke_recovery_credentials(
    db: Session, user_id: str, purpose: Purpose | None = None
) -> None:
    query = update(RecoveryCredential).where(
        RecoveryCredential.user_id == user_id,
        RecoveryCredential.used_at.is_(None),
        RecoveryCredential.revoked_at.is_(None),
    )
    if purpose is not None:
        query = query.where(RecoveryCredential.purpose == purpose)
    else:
        db.execute(delete(SystemSetting).where(SystemSetting.key == f"{HOST_RECOVERY_PREFIX}{user_id}"))
    db.execute(query.values(revoked_at=utcnow()))


def issue_credential(
    db: Session, target: User, purpose: Purpose, actor: User | None, address: str
) -> IssuedCredential:
    """在调用方的写事务内替换凭据，只把原始凭据交给签发者一次。"""
    if not target.active or target.archived_at is not None or not _purpose_matches_user(target, purpose):
        raise recovery_error()
    credential = secrets.token_urlsafe(32)
    digest = sm3_digest(credential.encode("ascii"))
    now = utcnow()
    expires_at = None if purpose == "admin_offline" else now + RECOVERY_TTL
    revoke_recovery_credentials(db, target.id, purpose)
    db.add(RecoveryCredential(
        user_id=target.id, purpose=purpose, digest=digest, expires_at=expires_at,
    ))
    write_audit(db, actor, "auth.recovery_issue", "user", target.id, {"purpose": purpose}, address)
    return IssuedCredential(credential=credential, expires_at=expires_at)


def revoke_account_sessions(db: Session, user_id: str) -> None:
    db.execute(
        update(LoginSession).where(
            LoginSession.user_id == user_id, LoginSession.revoked_at.is_(None)
        ).values(revoked_at=utcnow())
    )


def apply_credential_changes(
    db: Session,
    user: User,
    changes: CredentialChanges,
    *,
    actor: User | None,
    address: str,
    action: str,
) -> None:
    username = changes.username.lower() if changes.username is not None else user.username
    if username != user.username and db.scalar(
        select(User.id).where(User.username == username, User.id != user.id)
    ):
        raise ProblemException(
            409, "USERNAME_EXISTS", "用户名已存在", "请使用其他用户名。", headers=NO_STORE
        )
    changed_username = username != user.username
    changed_password = changes.new_password is not None
    if not changed_username and not changed_password:
        raise ProblemException(
            422, "CREDENTIALS_UNCHANGED", "未修改登录信息", "请填写新的用户名或密码。",
            headers=NO_STORE,
        )
    user.username = username
    if changes.new_password is not None:
        new_password = changes.new_password.get_secret_value()
        if verify_password(new_password, user.password_hash):
            raise ProblemException(
                422, "PASSWORD_UNCHANGED", "新密码未改变", "请填写不同的新密码。", headers=NO_STORE,
            )
        user.password_hash = hash_password(new_password)
    user.version += 1
    revoke_account_sessions(db, user.id)
    revoke_recovery_credentials(db, user.id)
    write_audit(
        db, actor, action, "user", user.id,
        {"username_changed": changed_username, "password_changed": changed_password},
        address,
    )


def redeem_credential(db: Session, payload: RedeemCredential, address: str) -> User:
    """条件更新通过后才读用户；任一步失败由调用方回滚本次消费。"""
    token = payload.credential.get_secret_value().strip()
    key = sm3_digest(token.encode("utf-8"))
    _check_throttle(recovery_throttle, key, address)
    now = utcnow()
    result = None
    if TOKEN_PATTERN.fullmatch(token):
        result = db.execute(
            update(RecoveryCredential).where(
                RecoveryCredential.digest == key,
                RecoveryCredential.used_at.is_(None),
                RecoveryCredential.revoked_at.is_(None),
                or_(RecoveryCredential.expires_at.is_(None), RecoveryCredential.expires_at > now),
            ).values(used_at=now).returning(RecoveryCredential.user_id, RecoveryCredential.purpose)
        ).one_or_none()
    if result is None:
        _check_throttle(recovery_throttle, key, address, failed=True)
        raise recovery_error()
    user_id, purpose = result
    user = db.get(User, user_id, populate_existing=True)
    if not user or not user.active or user.archived_at is not None or not _purpose_matches_user(user, purpose):
        _check_throttle(recovery_throttle, key, address, failed=True)
        raise recovery_error()
    if payload.has_changes:
        apply_credential_changes(
            db, user, payload, actor=None, address=address, action="auth.recovery_credentials_changed"
        )
    else:
        # 仅找回用户名也属于一次性恢复凭据消费。撤销旧 Cookie/Bearer
        # 会话与消费记录、审计写入同事务，避免曾丢失的凭据被用于会话保留。
        revoke_account_sessions(db, user.id)
    write_audit(db, None, "auth.recovery_redeem", "user", user.id, {"purpose": purpose}, address)
    return user
