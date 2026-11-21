"""发展党员实际进度、参考计划与历史保留的回归测试。"""

from fastapi.testclient import TestClient


def _date(value: str) -> str:
    return value[:10]


def _milestone(case: dict, key: str) -> dict:
    return next(row for row in case["milestones"] if row["milestone_type"] == key)


def test_actual_entry_retains_reference_plan_and_recalculates_future_nodes(
    client: TestClient, admin: dict
) -> None:
    base = "/api/v1/party-development"
    created = client.post(
        f"{base}/cases",
        json={
            "name": "合成进度人员",
            "party_committee": "合成党委",
            "party_branch": "合成支部",
            "application_date": "2025-01-01",
        },
    )
    assert created.status_code == 201, created.text
    case = created.json()
    case_url = f"{base}/cases/{case['id']}"
    stored_reference = _milestone(case, "activist_reference")
    initial_future = _milestone(case, "development_object_reference")

    recorded = client.post(
        f"{case_url}/progress-events",
        headers={"If-Match": str(case["version"])},
        json={
            "milestone_type": "activist_date",
            "actual_date": "2025-10-01",
            "evidence_note": "合成会议纪要甲",
        },
    )
    assert recorded.status_code == 201, recorded.text
    payload = recorded.json()
    actual = next(row for row in payload["timeline"] if row["milestone_type"] == "activist_date")
    assert _date(actual["actual_at"]) == "2025-10-01"
    assert actual["planned_at"] == stored_reference["planned_at"]
    assert stored_reference["id"] in actual["related_plan_ids"]
    future = _milestone(payload["case"], "development_object_reference")
    assert future["planned_at"] != initial_future["planned_at"]
    assert _date(future["planned_at"]) == "2026-10-01"
    assert future["actual_at"] is None

    fact = actual["progress_event"]
    corrected = client.post(
        f"{base}/progress-events/{fact['id']}/correct",
        headers={"If-Match": str(fact["version"])},
        json={"actual_date": "2025-11-01", "evidence_note": "合成会议纪要乙"},
    )
    assert corrected.status_code == 200, corrected.text
    payload = corrected.json()
    history = {row["status"]: row for row in payload["progress_history"] if row["milestone_type"] == "activist_date"}
    assert history["superseded"]["id"] == fact["id"]
    assert history["confirmed"]["supersedes_event_id"] == fact["id"]
    assert _milestone(payload["case"], "activist_reference")["planned_at"] == stored_reference["planned_at"]
    assert _date(_milestone(payload["case"], "development_object_reference")["planned_at"]) == "2026-11-01"
    assert all(row["legal_deadline_at"] for row in payload["timeline"] if row["visual_state"] == "overdue")

    latest = next(row["progress_event"] for row in payload["timeline"] if row["milestone_type"] == "activist_date")
    voided = client.post(
        f"{base}/progress-events/{latest['id']}/void",
        headers={"If-Match": str(latest["version"])},
        json={"reason": "按合成纪要甲恢复"},
    )
    assert voided.status_code == 200, voided.text
    history = {row["status"]: row for row in voided.json()["progress_history"] if row["milestone_type"] == "activist_date"}
    assert history["confirmed"]["id"] == fact["id"]
    assert history["voided"]["id"] == latest["id"]
    assert history["voided"]["voided_at"]
