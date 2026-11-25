"""RC1 regression: duplicate notification creation must not abort the transaction."""
from concurrent.futures import ThreadPoolExecutor
from threading import Barrier
from uuid import uuid4

from sqlalchemy import select

from app.database import db_runtime
from app.models import EventOutbox, Notification, utcnow
from app.notifications import add_notification


def test_notification_dedupe_and_history_key_reuse(client, admin):
    key = f"rc1-atomic-{uuid4()}"
    args = dict(
        user_id=admin["id"],
        notification_type="deadline",
        title="RC1 synthetic deadline",
        body="Regression",
        entity_type="task",
        entity_id=None,
        dedupe_key=key,
    )
    with db_runtime.session_factory() as db:
        assert add_notification(db, **args)
        assert not add_notification(db, **args)
        first = db.scalar(select(Notification).where(Notification.dedupe_key == key))
        assert first is not None
        first_id = first.id
        first.revoked_at = utcnow()
        first.dedupe_key = f"{key}:history:{first_id}"
        # No explicit caller flush: session autoflush is disabled.
        assert add_notification(db, **args)
        assert not add_notification(db, **args)
        db.commit()
        current = db.scalar(select(Notification).where(Notification.dedupe_key == key))
        assert current is not None and current.id != first_id
        assert current.revoked_at is None
        assert db.get(Notification, first_id).revoked_at is not None


def test_notification_concurrent_independent_sessions(client, admin):
    """Two independent sessions race for the same key; only one emits an event."""
    key = f"rc1-concurrent-{uuid4()}"
    args = dict(
        user_id=admin["id"], notification_type="deadline",
        title="RC1 concurrent synthetic deadline", body="Regression",
        entity_type="task", entity_id=None, dedupe_key=key,
    )
    barrier = Barrier(2, timeout=15)

    def create():
        with db_runtime.session_factory() as db:
            barrier.wait()
            result = add_notification(db, **args)
            db.commit()
            return result

    with ThreadPoolExecutor(max_workers=2) as pool:
        futures = [pool.submit(create) for _ in range(2)]
        results = [future.result(timeout=30) for future in futures]
    assert sorted(results) == [False, True]
    with db_runtime.session_factory() as db:
        rows = db.scalars(select(Notification).where(Notification.dedupe_key == key)).all()
        assert len(rows) == 1
        events = db.scalars(select(EventOutbox).where(
            EventOutbox.event_type == "notification.created",
            EventOutbox.entity_id == rows[0].id,
        )).all()
        assert len(events) == 1
