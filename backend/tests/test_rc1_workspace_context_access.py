"""使用合成账号、根目录和签名设备上下文验证文件权限。"""

from __future__ import annotations

from types import SimpleNamespace

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from app.config import get_settings
from app.database import DatabaseRuntime, get_session
from app.device_versions import DEVICE_CONTEXT_COOKIE, issue_device_context_token
from app.enums import UserRole, WorkspaceRootSource
from app.models import (
    AIPolicy,
    Device,
    KnowledgeEntry,
    ObjectLink,
    User,
    WorkspaceFile,
    WorkspaceRoot,
    WorkspaceRootMember,
)
from app.problems import install_problem_handlers
from app.routers import ai, relations
from app.security import get_current_user


def _file(db, root, label, *, in_scope=True):
    record = WorkspaceFile(
        root_id=root.id, relative_path=f"{label}.txt", name=f"{label}.txt",
        extension=".txt", extracted_text=f"{label}的合成内容", in_scope=in_scope,
    )
    db.add(record)
    db.flush()
    return record


@pytest.fixture
def scopes(tmp_path, monkeypatch):
    runtime = DatabaseRuntime(get_settings().model_copy(update={"data_dir": tmp_path}))
    runtime.create_schema()
    with runtime.session_factory.begin() as db:
        owner = User(username="scope-owner", display_name="目录发布人", password_hash="test-only", role=UserRole.ADMIN)
        user = User(username="scope-member", display_name="合成组员", password_hash="test-only")
        db.add_all([owner, user])
        db.flush()
        device = Device(name="合成设备", created_by=owner.id, allow_host_access=False)
        db.add(device)
        db.flush()
        cookie, _ = issue_device_context_token(db, device)
        host = WorkspaceRoot(name="主机目录", absolute_path=str(tmp_path / "synthetic-host"), created_by=owner.id)
        shared = WorkspaceRoot(
            name="成员共享目录", absolute_path=str(tmp_path / "synthetic-device"), created_by=owner.id,
            device_id=device.id, source=WorkspaceRootSource.DEVICE, remote_key="scope",
            published_by_user_id=owner.id, share_scope="selected",
        )
        db.add_all([host, shared])
        db.flush()
        host_file = _file(db, host, "合成主机资料")
        outside = _file(db, host, "合成越界资料", in_scope=False)
        shared_file = _file(db, shared, "合成仅浏览资料")
        membership = WorkspaceRootMember(
            root_id=shared.id, user_id=user.id, created_by=owner.id,
            can_browse=True, can_download=False, can_send=False,
        )
        knowledge = KnowledgeEntry(title="合成制度", body="合成来源", updated_by=owner.id)
        policy = AIPolicy(allowed_root_ids=[host.id, shared.id], allowed_file_types=[".txt"], created_by=owner.id)
        db.add_all([membership, knowledge, policy])
        db.flush()

    model_calls = []

    def complete_synthetic(_db, _instruction, excerpts):
        model_calls.extend(excerpts)
        return "合成模型输出"

    monkeypatch.setattr(ai, "complete_locally", complete_synthetic)
    app = FastAPI()
    install_problem_handlers(app)
    app.include_router(ai.router)
    app.include_router(relations.router)

    def session():
        with runtime.session_factory() as db:
            yield db

    app.dependency_overrides[get_session] = session
    app.dependency_overrides[get_current_user] = lambda: user
    yield SimpleNamespace(
        app=app, runtime=runtime, model_calls=model_calls, host_file=host_file, outside=outside,
        shared_file=shared_file, membership=membership, knowledge=knowledge, cookie=cookie, device=device,
    )
    runtime.engine.dispose()


def _ask(client, file=None, *, instruction="摘要"):
    return client.post("/ai/query", json={
        "capability": "summarize",
        "instruction": instruction,
        "file_ids": [file.id] if file else [],
    })


def test_ai_file_context_intersects_policy_scope_and_download_permissions(scopes):
    with TestClient(scopes.app) as browser:
        for item in (scopes.outside, scopes.shared_file):
            result = _ask(browser, item)
            assert result.status_code == 404
            assert result.json()["code"] == "WORKSPACE_FILE_NOT_FOUND"
            assert item.name not in result.text
        automatic = _ask(browser, instruction="合成仅浏览资料")
        assert automatic.status_code == 422 and automatic.json()["code"] == "AI_SOURCE_EMPTY"
        assert scopes.model_calls == []

        with scopes.runtime.session_factory.begin() as db:
            db.get(WorkspaceRootMember, scopes.membership.id).can_download = True

        result = _ask(browser, instruction="合成仅浏览资料")
        assert result.status_code == 201, result.text
        assert [source["id"] for source in result.json()["sources"]] == [scopes.shared_file.id]
        assert scopes.shared_file.extracted_text in scopes.model_calls[0]


def test_ai_host_files_follow_signed_device_host_access(scopes):
    with TestClient(scopes.app) as local, TestClient(scopes.app, client=("192.0.2.8", 50000)) as remote:
        assert _ask(local, scopes.host_file).status_code == 201
        scopes.model_calls.clear()
        unknown = _ask(remote, scopes.host_file)
        assert unknown.status_code == 404
        remote.cookies.set(DEVICE_CONTEXT_COOKIE, scopes.cookie)
        restricted = _ask(remote, scopes.host_file)
        assert restricted.status_code == 404
        assert not scopes.model_calls

        with scopes.runtime.session_factory.begin() as db:
            db.get(Device, scopes.device.id).allow_host_access = True

        permitted = _ask(remote, scopes.host_file)
        assert permitted.status_code == 201, permitted.text
        assert permitted.json()["sources"][0]["id"] == scopes.host_file.id


def test_relations_filter_target_metadata_and_edits_using_current_device(scopes):
    source = f"/objects/knowledge/{scopes.knowledge.id}"
    target = f"/objects/workspace_file/{scopes.host_file.id}"
    payload = {"target_type": "workspace_file", "target_id": scopes.host_file.id}
    with TestClient(scopes.app) as local, TestClient(scopes.app, client=("192.0.2.8", 50000)) as remote:
        created = local.post(f"{source}/links", json=payload, headers={"Idempotency-Key": "synthetic-relation"})
        assert created.status_code == 201, created.text
        link_id = created.json()["id"]
        replay = local.post(f"{source}/links", json=payload, headers={"Idempotency-Key": "synthetic-relation"})
        assert replay.json()["id"] == link_id
        assert local.get(f"{source}/activity").json()[0]["event_data"]["target_id"] == scopes.host_file.id
        remote.cookies.set(DEVICE_CONTEXT_COOKIE, scopes.cookie)

        assert remote.get(f"{target}/links").status_code == 404
        assert remote.get(f"{source}/links").json() == []
        assert remote.get(f"{source}/activity").json() == []
        assert remote.post(f"{source}/links", json=payload).status_code == 404
        assert remote.delete(f"{source}/links/{link_id}", headers={"If-Match": "1"}).status_code == 404
        with scopes.runtime.session_factory() as db:
            assert db.get(ObjectLink, link_id) is not None

        with scopes.runtime.session_factory.begin() as db:
            db.get(Device, scopes.device.id).allow_host_access = True

        assert remote.get(f"{source}/links").json()[0]["title"] == scopes.host_file.name
        assert remote.get(f"{source}/activity").json()[0]["event_data"]["target_id"] == scopes.host_file.id
        assert remote.delete(f"{source}/links/{link_id}", headers={"If-Match": "1"}).status_code == 200


def test_relations_keep_browse_only_metadata_after_ai_download_is_denied(scopes):
    source = f"/objects/knowledge/{scopes.knowledge.id}"
    payload = {"target_type": "workspace_file", "target_id": scopes.shared_file.id}
    with TestClient(scopes.app) as browser:
        assert _ask(browser, scopes.shared_file).status_code == 404
        result = browser.post(f"{source}/links", json=payload)
        assert result.status_code == 201, result.text
        assert result.json()["title"] == scopes.shared_file.name
