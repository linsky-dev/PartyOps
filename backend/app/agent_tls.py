"""从真实 Agent TLS 连接取得证书并核对设备身份。"""

from __future__ import annotations

import asyncio
import hashlib
import secrets
from datetime import datetime, timedelta
from typing import cast

from fastapi import Request
from sqlalchemy import select
from sqlalchemy.orm import Session
from uvicorn.protocols.http.h11_impl import H11Protocol

from .config import get_settings
from .database import db_runtime
from .models import Device, SystemSetting, utcnow
from .pki import DeviceCSRInvalid, issue_device_certificate
from .problems import ProblemException
from .security import hash_token


class ClientCertificateH11Protocol(H11Protocol):
    """将传输层证书指纹保留在当前连接的 ASGI 状态中。"""

    def connection_made(self, transport: asyncio.BaseTransport) -> None:
        super().connection_made(cast(asyncio.Transport, transport))
        tls = transport.get_extra_info("ssl_object")
        cert = tls.getpeercert(binary_form=True) if tls is not None else None
        if cert:
            # Uvicorn 按请求复制 app_state。先脱离服务共享字典，避免跨连接串用证书。
            self.app_state = {
                **self.app_state,
                "partyops_peer_cert_sha256": hashlib.sha256(cert).hexdigest(),
            }


def _pending_key(device: Device) -> str:
    return f"agent.certificate.pending.{device.id}"


def _pending_rotation(row: SystemSetting | None, device: Device) -> dict:
    if row is None or not isinstance(row.value, dict):
        return {}
    if (
        row.value.get("previous_fingerprint") != device.certificate_fingerprint
        or row.value.get("token_hash") != device.agent_token_hash
    ):
        return {}
    return row.value


def stage_device_certificate(db: Session, device: Device, csr: str) -> dict:
    """重复提交同一 CSR 时重发原证书，直到新连接证明持有私钥。"""
    key = _pending_key(device)
    row = db.get(SystemSetting, key)
    previous = _pending_rotation(row, device)
    csr_hash = hashlib.sha256(csr.encode("utf-8")).hexdigest()
    if previous.get("csr_hash") == csr_hash and datetime.fromisoformat(previous["expires_at"]) > utcnow():
        return previous
    try:
        bundle = issue_device_certificate(get_settings(), device.id, csr)
    except DeviceCSRInvalid:
        raise ProblemException(
            422, "DEVICE_CSR_INVALID", "设备证书请求无效", "请重新生成证书请求。"
        ) from None
    except (OSError, ValueError):
        raise ProblemException(
            503, "DEVICE_CA_UNAVAILABLE", "主机设备证书服务不可用",
            "请由主机管理员检查已经登记的 CA 与密钥材料。",
        ) from None
    pending = {
        **bundle,
        "previous_fingerprint": device.certificate_fingerprint,
        "token_hash": device.agent_token_hash,
        "csr_hash": csr_hash,
        "expires_at": (utcnow() + timedelta(days=365)).isoformat(),
    }
    if row is None:
        db.add(SystemSetting(key=key, value=pending))
    else:
        row.value = pending
    return pending


def check_device_tls(request: Request, db: Session, token: str) -> None:
    """设备令牌只在实际 Agent 监听器上与其登记的证书组合使用。"""
    settings = get_settings()
    server = request.scope.get("server")
    cert_fingerprint = getattr(request.state, "partyops_peer_cert_sha256", "")
    if (
        not settings.tls_enabled
        or not server
        or server[1] != settings.agent_port
        or not cert_fingerprint
    ):
        raise ProblemException(
            403, "AGENT_MTLS_REQUIRED", "需要设备双向 TLS 通道",
            "终端接口必须通过 Agent 双向 TLS 安全端口访问。",
        )
    with db_runtime.write_lock:
        device = db.scalar(select(Device).where(Device.agent_token_hash == hash_token(token)))
        if device is None or not device.active:
            raise ProblemException(401, "DEVICE_TOKEN_INVALID", "设备令牌无效", "请重新入网或联系管理员。")
        if secrets.compare_digest(device.certificate_fingerprint or "", cert_fingerprint):
            return
        row = db.get(SystemSetting, _pending_key(device))
        pending = _pending_rotation(row, device)
        if (
            row is not None
            and secrets.compare_digest(str(pending.get("certificate_fingerprint", "")), cert_fingerprint)
            and datetime.fromisoformat(pending["expires_at"]) > utcnow()
        ):
            device.certificate_fingerprint = cert_fingerprint
            device.certificate_expires_at = datetime.fromisoformat(pending["expires_at"])
            device.version += 1
            db.delete(row)
            db.commit()
            return
    raise ProblemException(
        403, "DEVICE_CERTIFICATE_MISMATCH", "设备证书与设备身份不符",
        "请检查当前设备的入网状态与证书。",
    )
