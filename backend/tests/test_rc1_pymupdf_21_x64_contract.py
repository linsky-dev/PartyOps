"""Static guardrails for isolated CVE-remediated PyMuPDF 2.1 x64 smoke."""

from __future__ import annotations

import ast
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def test_secure_pymupdf_candidate_uses_fixed_upstream_wheel_without_lock_edit():
    workflow = (ROOT / ".github/workflows/cloud-pymupdf-21-x64-candidate.yml").read_text(
        encoding="utf-8"
    )
    assert "runs-on: windows-2025" in workflow
    assert "architecture: x64" in workflow
    assert "'pymupdf==2.1'" in workflow
    assert "pymupdf-2.1-cp311-abi3-win_amd64.whl" in workflow
    assert "e1122bbc3a1212396f0d70d19f78e084991b59ebdfc9d846c28ca8b509de01ad" in workflow
    assert "--only-binary=:all:" in workflow
    assert "--no-index --no-deps" in workflow
    assert "scripts/probe_pymupdf_21_x64.py" in workflow
    assert "persist-credentials: false" in workflow
    assert "BUILD_ONLY" in workflow


def test_secure_pymupdf_probe_invokes_original_functions_and_remains_isolated():
    text = (ROOT / "scripts/probe_pymupdf_21_x64.py").read_text(encoding="utf-8")
    ast.parse(text)
    for reference in (
        "from app.intake import MAX_PDF_PAGES, _extract_pdf",
        "from app.official_format_features import (",
        "_render_pdf_images(",
        "_pdf_to_docx(",
        'assert hasattr(doc[0], "find_tables")',
        '"license_review_complete": False',
        '"production_dependency_changed": False',
        '"release_ready": False',
    ):
        assert reference in text
