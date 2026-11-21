"""恢复、保留策略、SSE 补发和运行时辅助分支测试。"""

from __future__ import annotations

import asyncio
import json
import zipfile
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest
from fastapi import Request
from sqlalchemy import select

from app.backups import (
    _safe_zip_members,
    apply_retention,
    restore_backup,
    verify_backup,
)
from app.config import get_settings
from app.database import db_runtime
from app.models import BackupRun, EventOutbox, LoginSession, User, utcnow
from app.problems import ProblemException
from app.routers.events import event_stream
from app.security import SESSION_COOKIE, issue_session


def sse_request(token: str, scheme: str = "cookie") -> Request:
    headers = [
        (b"cookie", f"{SESSION_COOKIE}={token}".encode("ascii"))
        if scheme == "cookie" else (b"authorization", f"Bearer {token}".encode("ascii"))
    ]
    return Request({"type": "http", "method": "GET", "headers": headers})


@pytest.mark.asyncio
async def test_sse_replays_event_and_heartbeat(admin, monkeypatch) -> None:
    with db_runtime.session_factory.begin() as db:
        user = db.get(User, admin["id"])
        token, _, _ = issue_session(db, user)
        fixture = EventOutbox(event_type="test.changed", payload={"test": "合成数据"})
        db.add(fixture)
        db.flush()
        event_id = fixture.id
    request = sse_request(token)
    response = await event_stream(request, str(event_id - 1), user)
    event = await anext(response.body_iterator)
    assert event["id"] == str(event_id)
    assert json.loads(event["data"]) == {}
    await response.body_iterator.aclose()

    async def no_sleep(_seconds: float) -> None:
        return None

    monkeypatch.setattr(asyncio, "sleep", no_sleep)
    heartbeat_response = await event_stream(request, str(event_id + 10000), user)
    heartbeat = await anext(heartbeat_response.body_iterator)
    assert heartbeat["event"] == "heartbeat"
    await heartbeat_response.body_iterator.aclose()


@pytest.mark.asyncio
@pytest.mark.parametrize("scheme", ["cookie", "bearer"])
async def test_sse_closes_active_stream_after_session_revocation(admin, monkeypatch, scheme) -> None:
    with db_runtime.session_factory.begin() as db:
        user = db.get(User, admin["id"])
        token, _, session = issue_session(db, user)
        session_id = session.id

    async def no_sleep(_seconds: float) -> None:
        return None

    monkeypatch.setattr(asyncio, "sleep", no_sleep)
    response = await event_stream(sse_request(token, scheme), "1000000000", user)
    iterator = response.body_iterator
    assert (await anext(iterator))["event"] == "heartbeat"
    with db_runtime.session_factory.begin() as db:
        db.get(LoginSession, session_id).revoked_at = utcnow()
    with pytest.raises(StopAsyncIteration):
        await anext(iterator)


def test_backup_safe_members_and_manifest_errors(tmp_path: Path) -> None:
    valid = tmp_path / "valid.zip"
    with zipfile.ZipFile(valid, "w") as archive:
        archive.writestr("folder/file.txt", "ok")
    destination = tmp_path / "extract"
    destination.mkdir()
    with zipfile.ZipFile(valid) as archive:
        _safe_zip_members(archive, destination)
    assert (destination / "folder" / "file.txt").read_text() == "ok"

    traversal = tmp_path / "traversal.zip"
    with zipfile.ZipFile(traversal, "w") as archive:
        archive.writestr("../outside.txt", "bad")
    with zipfile.ZipFile(traversal) as archive:
        with pytest.raises(ProblemException) as error:
            _safe_zip_members(archive, destination)
    assert error.value.code == "BACKUP_PATH_INVALID"

    bad_manifest = tmp_path / "bad-manifest.partyops-backup"
    with zipfile.ZipFile(bad_manifest, "w") as archive:
        archive.writestr("manifest.json", "{")
    with pytest.raises(ProblemException) as error:
        verify_backup(bad_manifest)
    assert error.value.code == "BACKUP_MANIFEST_INVALID"

    wrong_format = tmp_path / "wrong-format.partyops-backup"
    with zipfile.ZipFile(wrong_format, "w") as archive:
        archive.writestr(
            "manifest.json",
            json.dumps({"format": "other", "format_version": 1, "files": []}),
        )
    with pytest.raises(ProblemException) as error:
        verify_backup(wrong_format)
    assert error.value.code == "BACKUP_FORMAT_INVALID"

    too_new = tmp_path / "too-new.partyops-backup"
    with zipfile.ZipFile(too_new, "w") as archive:
        archive.writestr(
            "manifest.json",
            json.dumps(
                {"format": "partyops-backup", "format_version": 999, "files": []}
            ),
        )
    with pytest.raises(ProblemException) as error:
        verify_backup(too_new)
    assert error.value.code == "BACKUP_TOO_NEW"


def test_retention_keeps_daily_weekly_and_manual(client, admin, monkeypatch) -> None:
    settings = get_settings()
    monkeypatch.setattr(settings, "backup_daily_keep", 1)
    monkeypatch.setattr(settings, "backup_weekly_keep", 1)
    now = datetime.now(timezone.utc)
    with db_runtime.session_factory() as db:
        records = []
        for index, days in enumerate((1, 8, 15)):
            filename = f"retention-{index}.partyops-backup"
            (settings.backups_dir / filename).write_bytes(b"backup")
            record = BackupRun(
                filename=filename,
                kind="automatic",
                status="completed",
                created_by=admin["id"],
                created_at=now - timedelta(days=days),
                completed_at=now - timedelta(days=days),
            )
            db.add(record)
            records.append(record)
        db.commit()
        apply_retention(db)
        remaining = db.scalars(
            select(BackupRun).where(
                BackupRun.filename.like("retention-%"),
            )
        ).all()
    assert 1 <= len(remaining) <= 2


def test_restore_success_preserves_valid_database(client, admin) -> None:
    created = client.post("/api/v1/backups")
    assert created.status_code == 201
    path = get_settings().backups_dir / created.json()["filename"]
    restore_backup(path, admin["id"])
    db_runtime.create_schema()
    with db_runtime.session_factory() as db:
        assert db.get(User, admin["id"]) is not None
