"""Fail-closed static contract for an isolated official BGE HTTP acceptance."""

from __future__ import annotations

import ast
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def test_official_bge_http_requires_opt_in_original_release_and_is_not_published() -> None:
    runner = (ROOT / "scripts/probe_rc1_official_bge_http.py").read_text(encoding="utf-8")
    case = (ROOT / "backend/tests/test_rc1_official_bge_http.py").read_text(encoding="utf-8")
    workflow = (ROOT / ".github/workflows/cloud-official-bge-http.yml").read_text(encoding="utf-8")
    ast.parse(runner)
    ast.parse(case)
    assert "download_exact_official_package(source)" in runner
    assert "OFFICIAL_SHA256" in runner
    assert "PARTYOPS_RC1_OFFICIAL_BGE_HTTP_PACK" in runner
    assert "pytest.skip(" in case
    assert "57df3669e368497ad6d428dab309fb7665229a57edfe3a4fe1f2afd6a0f1a155" in case
    assert "update-public-key.txt" in case
    assert '"/api/v1/admin/ai/model-packs"' in case
    assert "capability=embedding" in case
    assert "MODEL_PACK_ALREADY_INSTALLED" in case
    assert "MODEL_CAPABILITY_MISSING" in case
    assert "MODEL_PACK_CORRUPT" in case
    assert "AUTH_REQUIRED" in case
    assert "ADMIN_REQUIRED" in case
    assert "TestClient(client.app)" in case
    assert "extracted.stat().st_mtime_ns == original.st_mtime_ns" in case
    assert "runs-on: windows-2025" in workflow
    assert "persist-credentials: false" in workflow
    assert "uv sync --project backend --frozen --group dev" in workflow
    assert "workflow_dispatch:" in workflow
    assert '"deployed_http_server_tested": False' in runner
    assert '"release_ready": False' in runner
    assert "private-key.pem" not in runner
