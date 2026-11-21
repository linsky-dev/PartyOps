"""Real PartyOps PDF functions with CVE-remediated PyMuPDF 2.1, isolated x64.

Synthetic local documents only; no WPS, installation, real data, release or
change to backend/uv.lock. The 2.x compatibility result must be reviewed before
any production dependency upgrade, including table/layout behavior and AGPL.
"""

from __future__ import annotations

import hashlib
import importlib.metadata
import json
import os
import platform
import sys
from pathlib import Path
from tempfile import TemporaryDirectory


def main() -> int:
    if os.name != "nt" or platform.machine().lower() not in {"amd64", "x86_64"}:
        raise RuntimeError("this candidate must use native Windows x64")
    if importlib.metadata.version("pymupdf") != "2.1":
        raise RuntimeError("unexpected PyMuPDF version")
    import fitz

    root = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(root / "backend"))
    from docx import Document
    from app.intake import MAX_PDF_PAGES, _extract_pdf
    from app.official_format_features import (
        _extract_pdf_text,
        _pdf_to_docx,
        _render_pdf_images,
    )
    from app.problems import ProblemException

    with TemporaryDirectory() as tmp:
        folder = Path(tmp)
        pdf_path = folder / "synthetic.pdf"
        with fitz.open() as doc:
            page = doc.new_page(width=595, height=842)
            page.insert_text((70, 90), "PartyOps secure PyMuPDF 2.1", fontsize=12)
            page.insert_text((70, 120), "Synthetic PDF document", fontsize=12)
            blob = doc.tobytes()
        pdf_path.write_bytes(blob)

        text, warnings = _extract_pdf(blob)
        assert "PartyOps secure PyMuPDF 2.1" in text
        assert "Synthetic PDF document" in text
        assert not warnings, warnings
        formatted_text = _extract_pdf_text(pdf_path)
        assert "PartyOps secure PyMuPDF 2.1" in formatted_text

        with fitz.open(stream=blob, filetype="pdf") as doc:
            assert doc.page_count == 1
            assert hasattr(doc[0], "find_tables"), "full table reconstruction API is missing"
            result = doc[0].find_tables()
            assert hasattr(result, "tables")
            encrypted = doc.tobytes(
                encryption=fitz.PDF_ENCRYPT_AES_256,
                owner_pw="synthetic-owner",
                user_pw="synthetic-user",
            )
        try:
            _extract_pdf(encrypted)
        except ProblemException as exc:
            assert exc.status == 422 and exc.code == "INTAKE_PDF_ENCRYPTED"
        else:
            raise AssertionError("original PartyOps intake accepted encrypted PDF")

        with fitz.open() as excessive:
            for _ in range(MAX_PDF_PAGES + 1):
                excessive.new_page(width=72, height=72)
            overflow = excessive.tobytes()
        try:
            _extract_pdf(overflow)
        except ProblemException as exc:
            assert exc.status == 422 and exc.code == "INTAKE_PDF_PAGE_LIMIT"
        else:
            raise AssertionError("original PartyOps PDF page limit was bypassed")

        # PyMuPDF 2.x changes find_tables() defaults. The unchanged PartyOps
        # converter calls page.find_tables() without options; a successful
        # Python import is not sufficient to establish table parity.
        table_pdf = folder / "ruled-table.pdf"
        with fitz.open() as table_document:
            table_page = table_document.new_page(width=595, height=842)
            for row_index in range(4):
                y = 120 + 74 * row_index
                table_page.draw_line((72, y), (452, y), color=(0, 0, 0), width=1)
            for col_index in range(3):
                x = 72 + 190 * col_index
                table_page.draw_line((x, 120), (x, 342), color=(0, 0, 0), width=1)
            for row_index in range(3):
                for col_index in range(2):
                    table_page.insert_text(
                        (84 + 190 * col_index, 151 + 74 * row_index),
                        f"R{row_index + 1}C{col_index + 1}", fontsize=11,
                    )
            table_document.save(table_pdf)
        with fitz.open(table_pdf) as document:
            ordinary = document[0].find_tables()
            legacy_compatible = document[0].find_tables(use_layout=False)
            assert legacy_compatible.tables, "synthetic ruled table fixture not detected"
            assert ordinary.tables, "PyMuPDF 2.1 default table detection regressed"
            assert len(ordinary.tables) >= len(legacy_compatible.tables)

        progress = lambda _percent, _message: None
        cancelled = lambda: False
        rendered = _render_pdf_images(
            pdf_path, folder, "sample", {"dpi": 72, "target_format": "png"},
            progress, cancelled,
        )
        assert len(rendered) == 1 and rendered[0].path.read_bytes().startswith(
            bytes.fromhex("89504e470d0a1a0a")
        )
        docx_path = folder / "converted.docx"
        _pdf_to_docx(
            pdf_path, docx_path, {"reconstruct_tables": False}, progress, cancelled
        )
        converted = Document(str(docx_path))
        assert any("PartyOps secure PyMuPDF 2.1" in p.text for p in converted.paragraphs)
        table_output = folder / "converted-table.docx"
        _pdf_to_docx(
            table_pdf, table_output, {"reconstruct_tables": True},
            progress, cancelled,
        )
        assert Document(str(table_output)).tables, (
            "PartyOps unchanged PDF->DOCX table reconstruction was lost"
        )

    evidence = {
        "schema_version": 1,
        "status": "PASS",
        "scope": "WINDOWS_X64_PYMUPDF_2_1_ORIGINAL_PARTYOPS_PDF_SYNTHETIC_ONLY",
        "source_commit": os.environ.get("GITHUB_SHA", ""),
        "wheel_sha256": "e1122bbc3a1212396f0d70d19f78e084991b59ebdfc9d846c28ca8b509de01ad",
        "dependency_version": importlib.metadata.version("pymupdf"),
        "platform": platform.platform(),
        "python": sys.version.split()[0],
        "synthetic_pdf_sha256": hashlib.sha256(blob).hexdigest(),
        "tests": [
            "original_partyops_intake_text",
            "original_partyops_official_format_text",
            "page_find_tables_api_present_and_runs",
            "original_partyops_encrypted_pdf_rejection",
            "original_partyops_pdf_page_limit_rejection",
            "original_partyops_page_rasterization",
            "original_partyops_pdf_to_docx_conversion",
            "ruled_table_default_vs_legacy_detection",
            "original_partyops_pdf_to_docx_table_reconstruction",
        ],
        "real_document_tested": False,
        "synthetic_ruled_table_parity_verified": True,
        "real_table_extraction_parity_verified": False,
        "wps_or_libreoffice_tested": False,
        "native_windows_arm64_tested": False,
        "license_review_complete": False,
        "production_dependency_changed": False,
        "release_ready": False,
    }
    out = Path("artifacts/pymupdf-21-x64/compatibility.json")
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("PARTYOPS_PYMUPDF_21_ORIGINAL_PDF_X64_COMPATIBILITY_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
