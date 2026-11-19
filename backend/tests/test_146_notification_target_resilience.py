"""通知定位对历史字段、消失的文件和当前共享范围保持兼容。"""

from types import SimpleNamespace

import pytest
from starlette.requests import Request

from app.enums import (
    FileIndexStatus,
    Sensitivity,
    TaskStatus,
    TransferDirection,
    TransferStatus,
    UserRole,
)
from app.models import (
    Notification,
    PartyDevelopmentCase,
    PartyDevelopmentMilestone,
    Task,
    TaskComment,
    Transfer,
    WorkspaceFile,
    WorkspaceRoot,
)
from app.notification_targets import resolve_notification_target

USER = SimpleNamespace(id="notice-user", role=UserRole.STAFF)
ROOT = SimpleNamespace(id="file-root", enabled=True, source=SimpleNamespace(value="host"))


def _request(peer="127.0.0.1"):
    return Request({"type": "http", "client": (peer, 1234), "headers": []})


class _Objects:
    def __init__(self, *entries):
        self.rows = {(model, row.id): row for model, row in entries}

    def get(self, model, key):
        return self.rows.get((model, key))


def _file_notification(target):
    return Notification(
        user_id=USER.id,
        notification_type="archive_suggestion",
        entity_type="workspace_file",
        entity_id="sample-file",
        target=target,
        dedupe_key="sample-notice",
    )


@pytest.mark.parametrize("stored_target", ["历史文本", ["旧列表"], 42, {"kind": "task", "id": "sample-file", "parent_id": ROOT.id}])
def test_absent_file_ignores_invalid_target_metadata(stored_target):
    db = _Objects((WorkspaceRoot, ROOT))
    destination = resolve_notification_target(db, _file_notification(stored_target), USER, _request())
    assert destination.status == "unavailable"
    assert destination.target is None


def test_known_missing_file_only_returns_accessible_parent():
    file = SimpleNamespace(id="sample-file", root_id=ROOT.id, in_scope=True, status=FileIndexStatus.MISSING)
    db = _Objects((WorkspaceRoot, ROOT), (WorkspaceFile, file))
    destination = resolve_notification_target(db, _file_notification(None), USER, _request())
    assert destination.status == "unavailable"
    assert destination.target and destination.target.id == ROOT.id
    assert destination.target.child_id is None


def test_removed_file_falls_back_only_under_current_share_scope():
    db = _Objects((WorkspaceRoot, ROOT))
    item = _file_notification({"kind": "workspace_file", "id": "sample-file", "parent_id": ROOT.id})
    local = resolve_notification_target(db, item, USER, _request())
    assert local.target and local.target.id == ROOT.id
    remote = resolve_notification_target(db, item, USER, _request("192.0.2.5"))
    assert remote.status == "unavailable"
    assert remote.target is None


def test_historical_comment_recovers_verified_child_even_with_malformed_json():
    task = SimpleNamespace(id="sample-task", deleted_at=None, sensitivity=Sensitivity.NORMAL, status=TaskStatus.IN_PROGRESS)
    comment = SimpleNamespace(id="sample-comment", task_id=task.id)
    db = _Objects((Task, task), (TaskComment, comment))
    item = Notification(
        user_id=USER.id,
        notification_type="comment",
        entity_type="task",
        entity_id=task.id,
        target=["历史列表"],
        dedupe_key=f"comment:{comment.id}:{USER.id}",
    )
    destination = resolve_notification_target(db, item, USER, _request())
    assert destination.status == "ready"
    assert destination.target and destination.target.child_id == comment.id


@pytest.mark.parametrize(("notice_type", "section"), [
    ("deadline", "overview"),
    ("overdue", "overview"),
    ("assignment", "overview"),
    ("automation", "overview"),
    ("review", "review"),
    ("feedback", "review"),
    ("materials", "materials"),
    ("comment", "comments"),
    ("mention", "comments"),
])
def test_task_notice_type_resolves_current_task_and_section(notice_type, section):
    task = SimpleNamespace(id="sample-task", deleted_at=None, sensitivity=Sensitivity.NORMAL, status=TaskStatus.IN_PROGRESS)
    comment = SimpleNamespace(id="sample-comment", task_id=task.id)
    child = comment.id if section == "comments" else None
    db = _Objects((Task, task), (TaskComment, comment))
    item = Notification(
        user_id=USER.id, notification_type=notice_type, entity_type="task", entity_id=task.id,
        target={"kind": "task", "id": task.id, "child_id": child}, dedupe_key=f"notice-{notice_type}",
    )
    destination = resolve_notification_target(db, item, USER, _request())
    assert destination.status == "ready"
    assert destination.target and destination.target.id == task.id
    assert destination.target.section == section
    assert destination.target.child_id == child


@pytest.mark.parametrize(
    ("notice_type", "entity_type", "kind", "section"),
    [
        ("party_development", "party_development_case", "development", "timeline"),
        ("root_approval", "workspace_root", "root_approval", "grants"),
        ("transfer", "transfer", "transfer", "inbox"),
        ("archive_suggestion", "workspace_file", "workspace", "files"),
    ],
)
def test_notices_resolve_real_non_task_relationships(notice_type, entity_type, kind, section):
    case = SimpleNamespace(id="case", status="active")
    milestone = SimpleNamespace(id="node", case_id=case.id)
    root = SimpleNamespace(id="root", enabled=True, approval_status="pending", source=SimpleNamespace(value="host"))
    file = SimpleNamespace(id="file", root_id=root.id, in_scope=True, status=FileIndexStatus.INDEXED)
    transfer = SimpleNamespace(
        id="transfer", direction=TransferDirection.DEVICE_TO_HOST,
        status=TransferStatus.COMPLETED, requested_by=USER.id,
    )
    db = _Objects(
        (PartyDevelopmentCase, case), (PartyDevelopmentMilestone, milestone),
        (WorkspaceRoot, root), (WorkspaceFile, file), (Transfer, transfer),
    )
    entity = {"party_development_case": case, "workspace_root": root, "workspace_file": file, "transfer": transfer}[entity_type]
    user = SimpleNamespace(id=USER.id, role=UserRole.ADMIN) if kind == "root_approval" else USER
    item = Notification(
        user_id=user.id, notification_type=notice_type, entity_type=entity_type, entity_id=entity.id,
        target={"kind": entity_type, "id": entity.id, "child_id": milestone.id}, dedupe_key=f"notice-{notice_type}",
    )
    destination = resolve_notification_target(db, item, user, _request())
    assert destination.status == "ready"
    assert destination.target and destination.target.kind == kind
    assert destination.target.section == section
    assert destination.target.id == (root.id if kind == "workspace" else entity.id)
