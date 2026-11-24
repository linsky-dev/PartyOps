"""Native Windows ARM64 PyMuPDF Lite candidate probe against PartyOps PDF API calls.

Separate experimental runner, no real user files and no changes to WPS engine,
production lock or RC1 package. A successful synthetic PDF API contract is not a
complete PartyOps PDF or installed-package acceptance.
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
    if os.name != "nt" or platform.machine().lower() not in {"arm64", "aarch64"}:
        raise RuntimeError("Requires native Windows ARM64 Python; emulated x64 is unacceptable")

    # PartyOps intake and official_format_features currently import legacy fitz.
    import fitz

    version = importlib.metadata.version("pymupdf-lite")
    if version != "2.1":
        raise RuntimeError(f"unexpected native PDF candidate version: {version}")

    with TemporaryDirectory() as scratch:
        location = Path(scratch) / "synthetic.pdf"
        with fitz.open() as document:
            page = document.new_page(width=595, height=842)
            page.insert_text((72, 100), "PartyOps synthetic PDF 2026", fontsize=12)
            page.insert_text((72, 134), "Local parser compatibility check", fontsize=12)
            document.save(str(location))
            stream = document.tobytes()
        assert stream.startswith(b"%PDF-")

        with fitz.open(stream=stream, filetype="pdf") as document:
            assert document.page_count == 1
            assert not document.needs_pass
            page = document[0]
            text = page.get_text("text").strip()
            assert "PartyOps synthetic PDF 2026" in text
            assert "Local parser compatibility check" in text
            sorted_text = page.get_text("text", sort=True)
            assert "PartyOps synthetic PDF" in sorted_text
            pixmap = page.get_pixmap(matrix=fitz.Matrix(1.8, 1.8), alpha=False)
            assert pixmap.width > 500 and pixmap.height > 900
            png = pixmap.tobytes("png")
            assert png.startswith(bytes.fromhex("89504e470d0a1a0a"))
            # Shared QA uses fitz.Pixmap to decode rendered image output.
            image_path = Path(scratch) / "rendered.png"
            image_path.write_bytes(png)
            decoded = fitz.Pixmap(str(image_path))
            assert decoded.width == pixmap.width and decoded.height == pixmap.height

        with fitz.open(location) as by_path:
            assert by_path.page_count == 1
            assert "Local parser" in by_path[0].get_text("text", sort=True)

        # Account for business logic which refuses encrypted intake PDFs.
        protected = Path(scratch) / "protected.pdf"
        with fitz.open(stream=stream, filetype="pdf") as document:
            document.save(
                str(protected),
                encryption=fitz.PDF_ENCRYPT_AES_256,
                owner_pw="partyops-test-owner",
                user_pw="partyops-test-user",
            )
        with fitz.open(protected) as document:
            assert document.needs_pass

        # Invalid document input must fail rather than be interpreted as a PDF.
        try:
            with fitz.open(stream=b"not-a-pdf-file", filetype="pdf"):
                pass
        except (ValueError, RuntimeError):
            invalid_rejected = True
        else:
            invalid_rejected = False
        if not invalid_rejected:
            raise RuntimeError("invalid PDF content was not rejected")

    result = {
        "schema_version": 1,
        "status": "PASS",
        "scope": "SYNTHETIC_PDF_API_COMPATIBILITY_ONLY",
        "version": version,
        "distribution": "pymupdf-lite",
        "source_commit": os.environ.get("GITHUB_SHA", ""),
        "python": sys.version.split()[0],
        "architecture": platform.machine(),
        "api_checks": [
            "legacy_fitz_import",
            "in_memory_pdf_open",
            "pdf_page_count",
            "plain_text_extraction",
            "sorted_text_extraction",
            "pdf_page_pixmap_render",
            "png_decode_via_fitz_pixmap",
            "pdf_path_open",
            "encrypted_pdf_requires_password",
            "invalid_pdf_rejected",
        ],
        "synthetic_pdf_sha256": hashlib.sha256(stream).hexdigest(),
        "full_layout_engine_tested": False,
        "tesseract_ocr_tested": False,
        "real_partyops_pdf_files_tested": False,
        "partyops_installed": False,
        "wps_modified": False,
        "production_dependency_changed": False,
        "release_ready": False,
    }
    out = Path("artifacts/arm64-pdf-lite/probe.json")
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("SYNTHETIC_PDF_API_COMPATIBILITY_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
