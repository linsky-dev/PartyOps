"""独立校验党员发展节点的法规与参考提醒窗口。"""

from __future__ import annotations

from datetime import datetime, timedelta, timezone

import pytest
from fastapi import Request
from sqlalchemy import select

from app.config import get_settings
from app.database import DatabaseRuntime
from app.models import (
    Notification,
    PartyDevelopmentCase,
    PartyDevelopmentMilestone,
    User,
)
from app.notification_targets import resolve_notification_target
from app.notifications import refresh_party_development_notifications


@pytest.mark.parametrize("legal_days", [1, 7])
def test_development_reminders_track_each_date_kind_and_keep_legacy_location(tmp_path, legal_days):
    runtime = DatabaseRuntime(get_settings().model_copy(update={"data_dir": tmp_path}))
    runtime.create_schema()
    now = datetime(2026, 11, 20, 1, 45, tzinfo=timezone.utc)
    request = Request({"type": "http", "method": "GET", "headers": []})
    try:
        with runtime.session_factory() as db:
            user = User(username="dual-date-example", display_name="合成测试账号", password_hash="fixture")
            db.add(user)
            db.flush()
            case = PartyDevelopmentCase(
                name="合成发展人员", party_committee="合成党委", party_branch="合成支部",
                application_at=now - timedelta(days=30), created_by=user.id,
            )
            db.add(case)
            db.flush()
            node = PartyDevelopmentMilestone(
                case_id=case.id, milestone_type="conversation_deadline",
                planned_at=now + timedelta(days=7),
                legal_deadline_at=now + timedelta(days=legal_days), reminder_days=[7, 1, 0],
            )
            db.add(node)
            db.flush()
            prefix = f"{user.id}:party-development:{case.id}:{node.id}:"
            reference_key = f"{prefix}reference:7"
            legal_key = f"{prefix}legal_deadline:{legal_days}"
            legacy = Notification(
                user_id=user.id, notification_type="party_development", entity_type="party_development_case",
                entity_id=case.id, title="党员发展参考计划日期还有 7 天：合成发展人员",
                body="已读的旧版合成提醒", dedupe_key=f"{prefix}7", read_at=now - timedelta(hours=1), target=None,
            )
            db.add(legacy)
            db.commit()

            def active():
                return {
                    row.dedupe_key: row
                    for row in db.scalars(select(Notification).where(Notification.revoked_at.is_(None)))
                }

            assert refresh_party_development_notifications(db, now=now) == 2
            db.commit()
            initial = active()
            assert set(initial) == {reference_key, legal_key}
            assert initial[reference_key].id == legacy.id
            assert initial[reference_key].read_at is not None
            assert initial[reference_key].body == "已读的旧版合成提醒"
            legal = initial[legal_key]
            assert f"法规截止日期还有 {legal_days} 天" in legal.title
            assert legal.target["child_id"] == node.id
            resolved = resolve_notification_target(db, legacy, user, request)
            assert resolved.status == "ready"
            assert resolved.target and resolved.target.child_id == node.id
            assert refresh_party_development_notifications(db, now=now) == 0

            node.adjusted_at = now + timedelta(days=2)
            db.commit()
            assert refresh_party_development_notifications(db, now=now) == 1
            assert refresh_party_development_notifications(db, now=now) == 0
            db.commit()
            assert set(active()) == {legal_key}
            assert active()[legal_key].id == legal.id

            node.adjusted_at = now + timedelta(days=7)
            db.commit()
            assert refresh_party_development_notifications(db, now=now) == 1
            db.commit()
            replacement = active()[reference_key]
            assert replacement.id != legacy.id
            assert legacy.dedupe_key == f"{reference_key}:history:{legacy.id}"
            historical = resolve_notification_target(db, legacy, user, request)
            assert historical.status == "revoked"
            assert historical.target and historical.target.child_id == node.id
            assert active()[legal_key].id == legal.id

            node.actual_at = now
            db.commit()
            assert refresh_party_development_notifications(db, now=now) == 2
            db.commit()
            assert active() == {}
            assert refresh_party_development_notifications(db, now=now) == 0
    finally:
        runtime.engine.dispose()
