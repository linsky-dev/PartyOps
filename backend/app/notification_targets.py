"""只在确认通知归属和当前业务权限后解析页面定位。"""

from __future__ import annotations

from typing import Literal

from fastapi import Request
from pydantic import BaseModel
from sqlalchemy.orm import Session

from .enums import (
    FileIndexStatus,
    TaskStatus,
    TransferDirection,
    TransferStatus,
    UserRole,
)
from .models import (
    Notification,
    PartyDevelopmentCase,
    PartyDevelopmentMilestone,
    Task,
    TaskComment,
    Transfer,
    User,
    WorkspaceFile,
    WorkspaceRoot,
)
from .problems import ProblemException
from .task_service import can_view_task
from .workspace_access import workspace_root_permissions

TASK_SECTIONS = {
    "comment": "comments",
    "mention": "comments",
    "review": "review",
    "feedback": "review",
    "materials": "materials",
}
DEVELOPMENT_DATE_KINDS = {
    "reference": "参考计划日期",
    "legal_deadline": "法规截止日期",
}


class NotificationTarget(BaseModel):
    kind: Literal["task", "development", "transfer", "root_approval", "workspace"]
    id: str
    section: str
    child_id: str | None = None
    archived: bool = False


class NotificationDestination(BaseModel):
    status: Literal["ready", "unavailable", "archived", "revoked"]
    target: NotificationTarget | None = None


def notification_target(
    entity_type: str,
    entity_id: str | None,
    section: str,
    child_id: str | None = None,
) -> dict | None:
    if entity_id is None:
        return None
    return {"kind": entity_type, "id": entity_id, "section": section, "child_id": child_id}


def _stored_id(item: Notification, key: str) -> str | None:
    """历史 JSON 仅提供定位线索，要求主对象一致且字段为有界字符串。"""

    stored = item.target
    if not isinstance(stored, dict) or stored.get("kind") != item.entity_type or stored.get("id") != item.entity_id:
        return None
    value = stored.get(key)
    return value if isinstance(value, str) and 0 < len(value) <= 36 else None


def _child_id(item: Notification) -> str | None:
    child = _stored_id(item, "child_id")
    if child:
        return child
    # 旧版通知去重键已携带评论或节点 ID。解析后仍要验证实际父子关系。
    parts = item.dedupe_key.split(":")
    if item.notification_type in {"comment", "mention"} and len(parts) == 3:
        if parts[0] == "comment" and parts[2] == item.user_id:
            return parts[1]
    if item.notification_type == "party_development":
        if len(parts) in (7, 8) and parts[-2] == "history":
            parts = parts[:-2]
        legacy = len(parts) == 5
        timed = len(parts) == 6 and parts[4] in DEVELOPMENT_DATE_KINDS
        if (legacy or timed) and parts[:3] == [item.user_id, "party-development", item.entity_id]:
            return parts[3]
    return None


def resolve_notification_target(
    db: Session, item: Notification, user: User, request: Request
) -> NotificationDestination:
    from .routers.workspace import current_device_id, get_file

    target: NotificationTarget | None = None
    status: Literal["ready", "unavailable", "archived", "revoked"] = "unavailable"
    entity_id = item.entity_id
    if not entity_id:
        return NotificationDestination(status="revoked" if item.revoked_at else status)
    if item.entity_type == "task":
        task = db.get(Task, entity_id)
        if task and can_view_task(db, task, user):
            target = NotificationTarget(
                kind="task", id=task.id, section=TASK_SECTIONS.get(item.notification_type, "overview")
            )
            status = "ready"
            if target.section == "comments":
                child = _child_id(item)
                comment = db.get(TaskComment, child) if child else None
                if comment and comment.task_id == task.id:
                    target.child_id = comment.id
                else:
                    status = "unavailable"
            if task.status == TaskStatus.ARCHIVED:
                status = "archived"
    elif item.entity_type == "party_development_case":
        case = db.get(PartyDevelopmentCase, entity_id)
        if case:
            archived = case.status == "archived"
            target = NotificationTarget(
                kind="development", id=case.id, section="timeline", archived=archived
            )
            child = _child_id(item)
            milestone = db.get(PartyDevelopmentMilestone, child) if child else None
            if milestone and milestone.case_id == case.id:
                target.child_id = milestone.id
                status = "ready"
            if archived:
                status = "archived"
    elif item.entity_type == "transfer":
        transfer = db.get(Transfer, entity_id)
        if transfer and (user.role == UserRole.ADMIN or transfer.requested_by == user.id):
            section = "inbox" if transfer.direction == TransferDirection.DEVICE_TO_HOST else "transfers"
            target = NotificationTarget(kind="transfer", id=transfer.id, section=section)
            status = "revoked" if transfer.status in {TransferStatus.CANCELLED, TransferStatus.EXPIRED} else "ready"
    elif item.entity_type == "workspace_root":
        root = db.get(WorkspaceRoot, entity_id)
        if root:
            if root.enabled and item.notification_type == "root_approval" and user.role == UserRole.ADMIN:
                target = NotificationTarget(kind="root_approval", id=root.id, section="grants")
                status = "ready" if root.approval_status == "pending" else "unavailable"
            elif workspace_root_permissions(db, root, user, current_device_id(request, db))["browse"]:
                target = NotificationTarget(kind="workspace", id=root.id, section="files")
                status = "ready"
    elif item.entity_type == "workspace_file":
        device_id = current_device_id(request, db)
        try:
            file, root = get_file(db, entity_id, user, device_id)
        except ProblemException as error:
            if error.status not in {403, 404, 410}:
                raise
            indexed_file = db.get(WorkspaceFile, entity_id)
            parent = indexed_file.root_id if indexed_file else _stored_id(item, "parent_id")
            root = db.get(WorkspaceRoot, parent) if parent else None
            if root and workspace_root_permissions(db, root, user, device_id)["browse"]:
                target = NotificationTarget(kind="workspace", id=root.id, section="files")
        else:
            target = NotificationTarget(kind="workspace", id=root.id, section="files")
            if file.status != FileIndexStatus.MISSING:
                target.child_id = file.id
                status = "ready"
    if item.revoked_at:
        status = "revoked"
    return NotificationDestination(status=status, target=target)
