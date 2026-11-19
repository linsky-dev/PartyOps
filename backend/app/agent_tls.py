"""从真实 Agent TLS 连接取得证书并核对设备身份。"""

from __future__ import annotations

import asyncio
import hashlib
import secrets

from fastapi import Request
from sqlalchemy import select
from sqlalchemy.orm import Session
from uvicorn.protocols.http.h11_impl import H11Protocol

from .config import get_settings
from .models import Device
from .problems import ProblemException
from .security import hash_token


class ClientCertificateH11Protocol(H11Protocol):
    """将传输层证书指纹保留在当前连接的 ASGI 状态中。"""

    def connection_made(self, transport: asyncio.Transport) -> None:
        super().connection_made(transport)
        tls = transport.get_extra_info("ssl_object")
        cert = tls.getpeercert(binary_form=True) if tls is not None else None
        if cert:
            # Uvicorn 按请求复制 app_state。先脱离服务共享字典，避免跨连接串用证书。
            self.app_state = {
                **self.app_state,
                "partyops_peer_cert_sha256": hashlib.sha256(cert).hexdigest(),
            }


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
    device = db.scalar(select(Device).where(Device.agent_token_hash == hash_token(token)))
    if device is None or not device.active:
        raise ProblemException(401, "DEVICE_TOKEN_INVALID", "设备令牌无效", "请重新入网或联系管理员。")
    if not device.certificate_fingerprint or not secrets.compare_digest(
        device.certificate_fingerprint, cert_fingerprint
    ):
        raise ProblemException(
            403, "DEVICE_CERTIFICATE_MISMATCH", "设备证书与设备身份不符",
            "请检查当前设备的入网状态与证书。",
        )
