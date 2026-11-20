"""主机共享范围使用明确的本机或已验证设备上下文。"""

import json
import zipfile
from types import SimpleNamespace

import pytest
from starlette.requests import Request

from app.config import get_settings
from app.database import db_runtime
from app.enums import UserRole
from app.models import Task, User, WorkspaceFile, WorkspaceLink, WorkspaceRoot, utcnow
from app.routers import productivity, today
from app.workspace_access import (
    UNIDENTIFIED_REMOTE_DEVICE,
    workspace_request_device_id,
    workspace_root_permissions,
)


def _request(host: str, *, forwarded: bool = False) -> Request:
    headers = [(b"x-forwarded-for", b"192.0.2.5")] if forwarded else []
    return Request({"type": "http", "client": (host, 1234), "headers": headers})


class _Devices:
    def __init__(self, device=None):
        self.device = device

    def get(self, _model, key):
        return self.device if self.device and key == self.device.id else None


@pytest.mark.parametrize(
    ("host", "forwarded"),
    [("192.0.2.5", False), ("127.0.0.1", True), ("", False)],
)
def test_host_share_requires_local_transport_or_a_verified_device(host, forwarded):
    root = SimpleNamespace(enabled=True, source=SimpleNamespace(value="host"))
    member = SimpleNamespace(role=UserRole.STAFF)
    scope = workspace_request_device_id(_request(host, forwarded=forwarded), None)
    assert scope == UNIDENTIFIED_REMOTE_DEVICE
    permissions = workspace_root_permissions(_Devices(), root, member, scope)
    assert permissions["browse"] is False
    assert permissions["download"] is False
    assert permissions["send"] is False


@pytest.mark.parametrize("host", ["127.0.0.1", "::1", "::ffff:127.0.0.1"])
def test_direct_loopback_preserves_member_access_to_host_share(host):
    root = SimpleNamespace(enabled=True, source=SimpleNamespace(value="host"))
    member = SimpleNamespace(role=UserRole.STAFF)
    scope = workspace_request_device_id(_request(host), None)
    assert scope is None
    assert workspace_root_permissions(_Devices(), root, member, scope)["browse"] is True


def test_verified_remote_device_uses_its_host_access_setting():
    root = SimpleNamespace(enabled=True, source=SimpleNamespace(value="host"))
    member = SimpleNamespace(role=UserRole.STAFF)
    device = SimpleNamespace(id="verified-device", active=True, status="online", allow_host_access=False)
    scope = workspace_request_device_id(_request("192.0.2.5"), device)
    assert scope == device.id
    assert workspace_root_permissions(_Devices(device), root, member, scope)["download"] is False
    device.allow_host_access = True
    assert workspace_root_permissions(_Devices(device), root, member, scope)["download"] is True


def test_global_search_obeys_context_for_host_files(client, admin, staff, tmp_path, monkeypatch):
    with db_runtime.session_factory() as db:
        user = db.get(User, staff["id"])
        root = WorkspaceRoot(name="合成主机文件夹", absolute_path=str(tmp_path), created_by=admin["id"])
        db.add(root)
        db.flush()
        file = WorkspaceFile(
            root_id=root.id,
            relative_path="synthetic-only.txt",
            name="synthetic-only.txt",
            last_seen_at=utcnow(),
        )
        db.add(file)
        db.flush()
        task = Task(title="合成交接事项", owner_id=user.id, created_by=admin["id"], updated_by=admin["id"])
        db.add(task)
        db.flush()
        db.add(WorkspaceLink(file_id=file.id, entity_type="task", entity_id=task.id, created_by=admin["id"]))
        db.commit()
        monkeypatch.setattr(productivity, "search_workspace_files", lambda *_a, **_k: [file])
        monkeypatch.setattr(productivity, "request_device", lambda *_a, **_k: None)
        monkeypatch.setattr(today, "request_device", lambda *_a, **_k: None)
        for host, visible in (("127.0.0.1", True), ("192.0.2.5", False)):
            request = _request(host)
            result = productivity.global_search(request, "synthetic-only", 10, user, db)
            assert any(item["id"] == file.id for item in result["items"]) is visible
            for dashboard in (productivity.workbench(request, user, db), today.today(request, user, db)):
                assert any(item["id"] == file.id for item in dashboard["recent_files"]) is visible
            handover = productivity.create_handover(request, user, db)
            with zipfile.ZipFile(get_settings().exports_dir / handover["filename"]) as archive:
                result = json.loads(archive.read("manifest.json"))
            exported_task = next(item for item in result["tasks"] if item["id"] == task.id)
            assert any(item["name"] == file.name for item in exported_task["linked_files"]) is visible
