"""通知定位对历史字段、消失的文件和当前共享范围保持兼容。"""

from datetime import datetime, timezone
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
from app.notification_targets import notification_target, resolve_notification_target
from app.problems import ProblemException
from app.routers import workspace

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


def test_development_history_keeps_verified_child_after_reissuing_notice():
    case = SimpleNamespace(id="sample-case", status="active")
    milestone = SimpleNamespace(id="sample-milestone", case_id=case.id)
    db = _Objects((PartyDevelopmentCase, case), (PartyDevelopmentMilestone, milestone))
    item = Notification(
        user_id=USER.id,
        notification_type="party_development",
        entity_type="party_development_case",
        entity_id=case.id,
        revoked_at=datetime(2026, 8, 25, tzinfo=timezone.utc),
        dedupe_key=f"{USER.id}:party-development:{case.id}:{milestone.id}:1:history:old-id",
    )
    destination = resolve_notification_target(db, item, USER, _request())
    assert destination.status == "revoked"
    assert destination.target and destination.target.child_id == milestone.id


@pytest.mark.parametrize(("revoked", "status"), [(False, "unavailable"), (True, "revoked")])
def test_notice_without_entity_returns_no_navigation(revoked, status):
    item = Notification(
        user_id=USER.id, notification_type="assignment", entity_type="task",
        entity_id=None, dedupe_key="detached-task",
        revoked_at=datetime(2026, 8, 25, tzinfo=timezone.utc) if revoked else None,
    )
    result = resolve_notification_target(_Objects(), item, USER, _request())
    assert result.status == status
    assert result.target is None
    assert notification_target("task", None, "overview") is None


@pytest.mark.parametrize(("notice_type", "entity_type"), [
    ("mention", "task"),
    ("party_development", "party_development_case"),
    ("root_approval", "workspace_root"),
    ("historical_notice", "unknown_object"),
])
def test_deleted_or_unknown_objects_do_not_get_a_fallback_from_old_json(notice_type, entity_type):
    item = Notification(
        user_id=USER.id, notification_type=notice_type, entity_type=entity_type,
        entity_id="missing-object", dedupe_key=f"missing-{notice_type}",
        target={"kind": "workspace_root", "id": ROOT.id, "child_id": ROOT.id},
    )
    result = resolve_notification_target(_Objects((WorkspaceRoot, ROOT)), item, USER, _request())
    assert result.status == "unavailable"
    assert result.target is None


@pytest.mark.parametrize(("date_kind", "window"), [("legal_deadline", "7"), ("unknown", "7")])
def test_missing_development_target_recovers_only_recognized_date_kind(date_kind, window):
    case = SimpleNamespace(id="old-case", status="active")
    node = SimpleNamespace(id="old-node", case_id=case.id)
    item = Notification(
        user_id=USER.id, notification_type="party_development", entity_type="party_development_case",
        entity_id=case.id, dedupe_key=f"{USER.id}:party-development:{case.id}:{node.id}:{date_kind}:{window}",
    )
    result = resolve_notification_target(
        _Objects((PartyDevelopmentCase, case), (PartyDevelopmentMilestone, node)),
        item, USER, _request(),
    )
    assert result.target
    if date_kind == "legal_deadline":
        assert result.status == "ready" and result.target.child_id == node.id
    else:
        assert result.status == "unavailable" and result.target.child_id is None


@pytest.mark.parametrize(("archived", "status"), [(False, "unavailable"), (True, "archived")])
def test_comment_outside_target_task_never_supplies_deep_link(archived, status):
    task = SimpleNamespace(
        id="current-task", deleted_at=None, sensitivity=Sensitivity.NORMAL,
        status=TaskStatus.ARCHIVED if archived else TaskStatus.IN_PROGRESS,
    )
    comment = SimpleNamespace(id="foreign-comment", task_id="other-task")
    item = Notification(
        user_id=USER.id, notification_type="comment", entity_type="task", entity_id=task.id,
        dedupe_key=f"comment:{comment.id}:{USER.id}",
    )
    result = resolve_notification_target(_Objects((Task, task), (TaskComment, comment)), item, USER, _request())
    assert result.status == status
    assert result.target and result.target.id == task.id
    assert result.target.child_id is None


def test_archived_development_case_without_node_preserves_timeline_link():
    case = SimpleNamespace(id="archived-case", status="archived")
    item = Notification(
        user_id=USER.id, notification_type="party_development", entity_type="party_development_case",
        entity_id=case.id, dedupe_key=f"{USER.id}:party-development:{case.id}:missing:legal_deadline:7",
    )
    result = resolve_notification_target(_Objects((PartyDevelopmentCase, case)), item, USER, _request())
    assert result.status == "archived"
    assert result.target and result.target.archived and result.target.section == "timeline"
    assert result.target.child_id is None


def test_expired_outbound_transfer_only_links_for_requester_and_admin():
    transfer = SimpleNamespace(
        id="expired-transfer", direction=TransferDirection.HOST_TO_DEVICE,
        status=TransferStatus.EXPIRED, requested_by="requester",
    )
    item = Notification(
        user_id=USER.id, notification_type="transfer", entity_type="transfer", entity_id=transfer.id,
        dedupe_key="expired-transfer",
    )
    db = _Objects((Transfer, transfer))
    denied = resolve_notification_target(db, item, USER, _request())
    assert denied.status == "unavailable" and denied.target is None
    admin = SimpleNamespace(id=USER.id, role=UserRole.ADMIN)
    result = resolve_notification_target(db, item, admin, _request())
    assert result.status == "revoked"
    assert result.target and result.target.section == "transfers"


def test_approved_root_uses_current_workspace_scope():
    root = SimpleNamespace(
        id="approved-root", enabled=True, approval_status="approved", source=SimpleNamespace(value="host"),
    )
    item = Notification(
        user_id=USER.id, notification_type="root_approval", entity_type="workspace_root",
        entity_id=root.id, dedupe_key="old-approval",
    )
    db = _Objects((WorkspaceRoot, root))
    admin = SimpleNamespace(id=USER.id, role=UserRole.ADMIN)
    settled = resolve_notification_target(db, item, admin, _request())
    assert settled.status == "unavailable" and settled.target and settled.target.section == "grants"
    local = resolve_notification_target(db, item, USER, _request())
    assert local.status == "ready" and local.target and local.target.kind == "workspace"
    remote = resolve_notification_target(db, item, USER, _request("192.0.2.5"))
    assert remote.status == "unavailable" and remote.target is None


def test_unexpected_workspace_failure_is_returned_instead_of_silencing(monkeypatch):
    def unexpected(*_args):
        raise ProblemException(503, "SYNTHETIC_UNAVAILABLE", "文件服务不可用", "合成故障")

    monkeypatch.setattr(workspace, "get_file", unexpected)
    with pytest.raises(ProblemException) as failure:
        resolve_notification_target(_Objects(), _file_notification(None), USER, _request())
    assert failure.value.code == "SYNTHETIC_UNAVAILABLE"


@pytest.mark.parametrize("revoked", [False, True])
@pytest.mark.parametrize("entity_type", ["task", "workspace_file", "party_development_case"])
def test_foreign_recipient_cannot_resolve_any_deep_link(entity_type, revoked):
    """收到别人的通知对象时，在查业务对象前拒绝，避免泄露定位信息。"""
    class RejectLookup:
        def get(self, *_args):
            raise AssertionError("foreign notification reached business lookup")

    item = Notification(
        user_id="other-recipient",
        notification_type="archive_suggestion" if entity_type == "workspace_file" else "comment",
        entity_type=entity_type,
        entity_id="foreign-object",
        target={"kind": entity_type, "id": "foreign-object", "child_id": "foreign-child"},
        dedupe_key=f"other-recipient:{entity_type}",
        revoked_at=datetime(2026, 8, 25, tzinfo=timezone.utc) if revoked else None,
    )
    result = resolve_notification_target(RejectLookup(), item, USER, _request())
    assert result.status == "unavailable"
    assert result.target is None
