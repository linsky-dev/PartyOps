"""通知定位对历史字段、消失的文件和当前共享范围保持兼容。"""

from types import SimpleNamespace

import pytest
from starlette.requests import Request

from app.enums import FileIndexStatus, Sensitivity, TaskStatus, UserRole
from app.models import Notification, Task, TaskComment, WorkspaceFile, WorkspaceRoot
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
