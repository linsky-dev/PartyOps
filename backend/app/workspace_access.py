"""原始文件、远端目录与跨设备传输的统一授权规则。"""

from __future__ import annotations

import ipaddress

from fastapi import Request
from sqlalchemy import or_, select
from sqlalchemy.orm import Session

from .config import get_settings
from .models import Device, DeviceGrant, User, WorkspaceRoot, WorkspaceRootMember

DEVICE_GRANT_CAPABILITIES = frozenset({"download", "share", "upload"})
UNIDENTIFIED_REMOTE_DEVICE = "unidentified-remote-device"


def workspace_request_device_id(request: Request, device: Device | None) -> str | None:
    """仅直连本机传输可使用主机本地上下文；远程请求须识别设备。"""

    if device is not None:
        return device.id
    if any(header in request.headers for header in ("Forwarded", "X-Forwarded-For", "X-Real-IP")):
        return UNIDENTIFIED_REMOTE_DEVICE
    peer = request.client.host.split("%", 1)[0] if request.client else ""
    try:
        address = ipaddress.ip_address(peer)
        if isinstance(address, ipaddress.IPv6Address) and address.ipv4_mapped is not None:
            address = address.ipv4_mapped
        if address.is_loopback:
            return None
    except ValueError:
        if peer == "testclient" and get_settings().environment == "test":
            return None
    return UNIDENTIFIED_REMOTE_DEVICE


def _active_device(db: Session, device_id: str) -> Device | None:
    device = db.get(Device, device_id)
    if (
        not device
        or not device.active
        or device.status in {"revoked", "quarantined"}
    ):
        return None
    return device


def grant_allows(
    db: Session,
    user: User,
    device_id: str,
    root_id: str | None,
    capability: str,
) -> bool:
    if capability not in DEVICE_GRANT_CAPABILITIES:
        return False
    device = _active_device(db, device_id)
    if not device:
        return False
    root = db.get(WorkspaceRoot, root_id) if root_id else None
    if root_id:
        if not root or not root.enabled:
            return False
        if (
            root.source.value == "device"
            and (root.device_id != device.id or root.approval_status != "approved")
        ):
            return False
    if user.role.value == "admin":
        return True
    if root and root.source.value == "device":
        # 1.4.1 发布目录先按目录级共享范围判断；旧 DeviceGrant 继续作为兼容兜底。
        if root.published_by_user_id == user.id and capability in {"download", "share"}:
            return True
        if root.share_scope == "team" and capability in {"download", "share"}:
            return True
        member = db.scalar(
            select(WorkspaceRootMember).where(
                WorkspaceRootMember.root_id == root.id,
                WorkspaceRootMember.user_id == user.id,
                WorkspaceRootMember.active.is_(True),
            )
        )
        if member:
            member_permissions = {
                "download": member.can_download,
                "share": member.can_send,
                "upload": False,
            }
            if member_permissions.get(capability, False):
                return True
    grants = db.scalars(
        select(DeviceGrant).where(
            DeviceGrant.device_id == device.id,
            DeviceGrant.active.is_(True),
            or_(DeviceGrant.user_id.is_(None), DeviceGrant.user_id == user.id),
            or_(DeviceGrant.root_id.is_(None), DeviceGrant.root_id == root_id),
        )
    ).all()
    return any(
        capability in grant.capabilities or "*" in grant.capabilities
        for grant in grants
    )


def workspace_root_permissions(
    db: Session,
    root: WorkspaceRoot,
    user: User,
    current_device_id: str | None = None,
) -> dict[str, bool]:
    denied = {
        "browse": False,
        "download": False,
        "send": False,
        "receive": False,
        "manage_root": False,
        # 兼容 1.4.0 客户端的旧能力名。
        "share": False,
        "upload": False,
    }
    if not root.enabled:
        return denied
    if root.source.value == "host":
        if current_device_id and user.role.value != "admin":
            if current_device_id == UNIDENTIFIED_REMOTE_DEVICE:
                return denied
            current = _active_device(db, current_device_id)
            if not current or not current.allow_host_access:
                return denied
        return {
            "browse": True,
            "download": True,
            "send": True,
            "receive": False,
            "manage_root": user.role.value == "admin",
            "share": True,
            "upload": False,
        }
    if root.approval_status != "approved" or not root.device_id:
        return denied
    device = _active_device(db, root.device_id)
    if not device:
        return denied
    member = db.scalar(
        select(WorkspaceRootMember).where(
            WorkspaceRootMember.root_id == root.id,
            WorkspaceRootMember.user_id == user.id,
            WorkspaceRootMember.active.is_(True),
        )
    )
    is_admin = user.role.value == "admin"
    is_publisher = root.published_by_user_id == user.id
    can_browse = (
        is_admin
        or is_publisher
        or root.share_scope == "team"
        or bool(member and member.can_browse)
    )
    can_download = grant_allows(db, user, root.device_id, root.id, "download")
    can_send = grant_allows(db, user, root.device_id, root.id, "share")
    can_upload = grant_allows(db, user, root.device_id, root.id, "upload")
    return {
        "browse": can_browse or can_download or can_send,
        "download": can_download,
        "send": can_send,
        "receive": can_upload,
        "manage_root": is_admin or (is_publisher and current_device_id == root.device_id),
        "share": can_send,
        "upload": can_upload,
    }
