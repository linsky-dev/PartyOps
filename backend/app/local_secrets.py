"""仅为设备入网重试保留受保护的短期响应。"""

from __future__ import annotations

import base64
import json
import os
import re
import secrets
import stat
from functools import lru_cache
from pathlib import Path
from typing import Any

from cryptography.exceptions import InvalidTag, UnsupportedAlgorithm
from cryptography.fernet import Fernet, InvalidToken
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives.ciphers.base import (
    AEADCipherContext,
    AEADEncryptionContext,
)
from sqlalchemy import select
from sqlalchemy.orm import Session

from .config import get_settings
from .models import Device, SystemSetting

VERSION = "v1"
ALGORITHM = "sm4-gcm"
PURPOSE = "device-enrollment-recovery"
ACTIVE_KEY_SETTING = "device_enrollment_sm4_key"
MAX_KEY_WRITES = 1 << 20
KEY_ID = re.compile(r"[0-9a-f]{32}\Z")
NONCE_BYTES = 12
TAG_BYTES = 16


class LocalRecoveryError(ValueError):
    """仅含可审计错误代码及固定说明，避免披露密钥或密文。"""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


def _key_path() -> Path:
    return get_settings().secrets_dir / "local-recovery-fernet.key"


def _fernet() -> Fernet:
    """既有 Fernet 记录只读，缺钥时保留现场。"""
    try:
        key = _key_path().read_bytes().strip()
    except OSError:
        raise LocalRecoveryError("ENROLLMENT_KEY_MISSING", "旧版入网恢复密钥不可读，请检查主机备份。") from None
    try:
        return Fernet(key)
    except ValueError:
        raise LocalRecoveryError("ENROLLMENT_KEY_INVALID", "旧版入网恢复密钥格式无效。") from None


def _verify_key_path(path: Path) -> None:
    info = path.lstat()
    if stat.S_ISLNK(info.st_mode) or (
        getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
    ):
        raise PermissionError("入网密钥目录和文件必须使用普通本机路径")
    if os.name != "nt" and (info.st_uid != os.geteuid() or stat.S_IMODE(info.st_mode) & 0o077):
        raise PermissionError("入网密钥目录和文件的权限必须仅授予服务账号")


def _sm4_key_path(key_id: str) -> Path:
    if not KEY_ID.fullmatch(key_id):
        raise LocalRecoveryError("ENROLLMENT_KEY_INVALID", "入网恢复密钥标识无效。")
    return get_settings().secrets_dir / "enrollment-keys" / f"{key_id}.key"


def _load_sm4_key(key_id: str) -> bytes:
    path = _sm4_key_path(key_id)
    try:
        _verify_key_path(path.parent)
        _verify_key_path(path)
        envelope = path.read_bytes()
    except OSError:
        raise LocalRecoveryError("ENROLLMENT_KEY_MISSING", "入网恢复密钥不可读，请检查主机备份。") from None
    if os.name == "nt":
        import win32crypt  # type: ignore[import-untyped]

        try:
            _, envelope = win32crypt.CryptUnprotectData(envelope, None, None, None, 1)
        except Exception:
            raise LocalRecoveryError("ENROLLMENT_KEY_INVALID", "当前主机服务账号无法解封入网恢复密钥。") from None
    if len(envelope) != 16:
        raise LocalRecoveryError("ENROLLMENT_KEY_INVALID", "入网恢复密钥长度无效。")
    return envelope


def _create_sm4_key() -> tuple[str, bytes]:
    key_id = secrets.token_hex(16)
    key = secrets.token_bytes(16)
    path = _sm4_key_path(key_id)
    if os.name == "nt":
        import win32crypt  # type: ignore[import-untyped]

        try:
            sealed = win32crypt.CryptProtectData(key, "PartyOps device enrollment", None, None, None, 1)
        except Exception:
            raise LocalRecoveryError("ENROLLMENT_KEY_WRITE_FAILED", "无法保护入网恢复密钥。") from None
    else:
        sealed = key
    try:
        path.parent.mkdir(mode=0o700, exist_ok=True)
        _verify_key_path(path.parent)
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(sealed)
            handle.flush()
            os.fsync(handle.fileno())
        if os.name != "nt":
            for directory in (path.parent, path.parent.parent):
                descriptor = os.open(directory, os.O_RDONLY | os.O_DIRECTORY)
                try:
                    os.fsync(descriptor)
                finally:
                    os.close(descriptor)
    except OSError:
        raise LocalRecoveryError("ENROLLMENT_KEY_WRITE_FAILED", "无法写入受保护的入网恢复密钥。") from None
    return key_id, key


def _active_key(db: Session) -> tuple[str, bytes]:
    """活跃写密钥元数据和入网事务同步提交，按写入次数限制单把密钥。"""
    setting = db.get(SystemSetting, ACTIVE_KEY_SETTING)
    if setting is None:
        for metadata in db.scalars(select(Device.device_metadata)):
            if str((metadata or {}).get("enrollment_recovery", "")).startswith(f"{VERSION}."):
                raise LocalRecoveryError("ENROLLMENT_KEY_MISSING", "已有入网密文的写密钥登记缺失。")
        key_id, key = _create_sm4_key()
        db.add(SystemSetting(key=ACTIVE_KEY_SETTING, value={"id": key_id, "writes": 1}))
        return key_id, key

    config = setting.value
    if not isinstance(config, dict) or not isinstance(config.get("id"), str) or (
        type(config.get("writes")) is not int or not 0 < config["writes"] <= MAX_KEY_WRITES
    ):
        raise LocalRecoveryError("ENROLLMENT_KEY_INVALID", "入网恢复密钥登记格式无效。")
    key_id = config["id"]
    key = _load_sm4_key(key_id)
    if config["writes"] == MAX_KEY_WRITES:
        key_id, key = _create_sm4_key()
        setting.value = {"id": key_id, "writes": 1}
    else:
        setting.value = {"id": key_id, "writes": config["writes"] + 1}
    return key_id, key


def _encode(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).decode("ascii")


def _decode(value: str) -> bytes:
    return base64.b64decode(value, altchars=b"-_", validate=True)


def _aad(device_id: str, key_id: str) -> bytes:
    return "\0".join((VERSION, ALGORITHM, key_id, PURPOSE, device_id)).encode("utf-8")


def _encryption_context(key: bytes, nonce: bytes) -> AEADEncryptionContext:
    try:
        return Cipher(algorithms.SM4(key), modes.GCM(nonce)).encryptor()
    except UnsupportedAlgorithm:
        raise LocalRecoveryError("ENROLLMENT_CRYPTO_UNAVAILABLE", "当前安装运行时不支持 SM4-GCM。") from None


def _decryption_context(key: bytes, nonce: bytes, tag: bytes) -> AEADCipherContext:
    try:
        return Cipher(algorithms.SM4(key), modes.GCM(nonce, tag)).decryptor()
    except UnsupportedAlgorithm:
        raise LocalRecoveryError("ENROLLMENT_CRYPTO_UNAVAILABLE", "当前安装运行时不支持 SM4-GCM。") from None


@lru_cache(maxsize=1)
def _require_enrollment_crypto() -> None:
    # 在创建密钥文件之前验证 provider，可以避免不支持的包留下废弃密钥。
    _encryption_context(bytes(16), bytes(NONCE_BYTES))


def encrypt_local_json(value: dict[str, Any], *, device_id: str, db: Session) -> str:
    if not device_id or value.get("device_id") != device_id:
        raise LocalRecoveryError("ENROLLMENT_RECOVERY_CONTEXT_INVALID", "入网恢复设备身份不匹配。")
    _require_enrollment_crypto()
    key_id, key = _active_key(db)
    nonce = secrets.token_bytes(NONCE_BYTES)
    payload = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    context = _encryption_context(key, nonce)
    context.authenticate_additional_data(_aad(device_id, key_id))
    ciphertext = context.update(payload) + context.finalize()
    return ".".join((VERSION, ALGORITHM, key_id, _encode(nonce), _encode(ciphertext), _encode(context.tag)))


def _decrypt_sm4(value: str, device_id: str) -> bytes:
    version, algorithm, key_id, nonce_b64, ciphertext_b64, tag_b64 = value.split(".")
    if version != VERSION or algorithm != ALGORITHM:
        raise ValueError("未知的入网恢复密文版本")
    nonce, ciphertext, tag = _decode(nonce_b64), _decode(ciphertext_b64), _decode(tag_b64)
    if len(nonce) != NONCE_BYTES or len(tag) != TAG_BYTES or not ciphertext:
        raise ValueError("入网恢复密文结构无效")
    context = _decryption_context(_load_sm4_key(key_id), nonce, tag)
    context.authenticate_additional_data(_aad(device_id, key_id))
    return context.update(ciphertext) + context.finalize()


def decrypt_local_json(value: str, *, device_id: str = "") -> dict[str, Any]:
    try:
        if "." in value:
            payload = _decrypt_sm4(value, device_id)
        else:
            payload = _fernet().decrypt(value.encode("ascii"))
        decoded = json.loads(payload.decode("utf-8"))
    except LocalRecoveryError:
        raise
    except (InvalidTag, InvalidToken, ValueError):
        raise LocalRecoveryError(
            "ENROLLMENT_RECOVERY_INVALID", "本机恢复凭据无法解密，密文或验证上下文无效。"
        ) from None
    if not isinstance(decoded, dict):
        raise LocalRecoveryError("ENROLLMENT_RECOVERY_INVALID", "本机恢复凭据格式无效。")
    if not device_id or decoded.get("device_id") != device_id:
        raise LocalRecoveryError("ENROLLMENT_RECOVERY_CONTEXT_INVALID", "入网恢复设备身份不匹配。")
    return decoded
