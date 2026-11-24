"""Native ARM64 execute unchanged PartyOps PDF intake on synthetic fixtures.

Requires isolated pymupdf-lite 2.1 and only the intake module's portable
dependencies. Never invokes WPS, OCR or real user files.
"""

from __future__ import annotations

import json
import os
import platform
import sys
from pathlib import Path


def main() -> int:
    if os.name != "nt" or platform.machine().lower() not in {"arm64", "aarch64"}:
        raise RuntimeError("native Windows ARM64 is required")

    root = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(root / "backend"))

    import fitz
    from app.intake import MAX_PDF_PAGES, _extract_pdf
    from app.problems import ProblemException

    with fitz.open() as pdf:
        page = pdf.new_page()
        page.insert_text((72, 90), "PartyOps real intake module", fontsize=13)
        page.insert_text((72, 120), "Synthetic local document", fontsize=13)
        document_bytes = pdf.tobytes()

    text, warnings = _extract_pdf(document_bytes)
    assert "PartyOps real intake module" in text
    assert "Synthetic local document" in text
    assert warnings == [], warnings

    with fitz.open(stream=document_bytes, filetype="pdf") as document:
        protected = document.tobytes(
            encryption=fitz.PDF_ENCRYPT_AES_256,
            owner_pw="owner-synthetic-only",
            user_pw="user-synthetic-only",
        )
    try:
        _extract_pdf(protected)
    except ProblemException as exc:
        assert exc.status == 422 and exc.code == "INTAKE_PDF_ENCRYPTED"
    else:
        raise AssertionError("PartyOps intake accepted encrypted PDF")

    with fitz.open() as oversized:
        for _ in range(MAX_PDF_PAGES + 1):
            oversized.new_page(width=72, height=72)
        too_many_pages = oversized.tobytes()
    try:
        _extract_pdf(too_many_pages)
    except ProblemException as exc:
        assert exc.status == 422 and exc.code == "INTAKE_PDF_PAGE_LIMIT"
    else:
        raise AssertionError("PartyOps intake failed to reject excessive PDF pages")

    evidence = {
        "schema_version": 1,
        "status": "PASS",
        "scope": "REAL_PARTYOPS_INTAKE_WITH_SYNTHETIC_PDF_ONLY",
        "module": "app.intake._extract_pdf",
        "source_commit": os.environ.get("GITHUB_SHA", ""),
        "python": sys.version.split()[0],
        "architecture": platform.machine(),
        "cases": [
            "extract_real_business_function",
            "encrypted_pdf_rejected_with_business_code",
            "pdf_page_limit_rejected_with_business_code",
        ],
        "real_user_data_tested": False,
        "scan_ocr_tested": False,
        "whole_app_dependencies_installed": False,
        "original_intake_code_modified": False,
        "wps_modified": False,
        "release_ready": False,
    }
    output = Path("artifacts/arm64-pdf-lite/intake-module.json")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("PARTYOPS_ARM64_SYNTHETIC_REAL_INTAKE_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
