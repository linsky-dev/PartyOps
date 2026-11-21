"""Guard that native ARM64 PDF experiment cannot become a silent production upgrade."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def test_native_pdf_api_probe_keeps_pymupdf_lite_isolated() -> None:
    script = (ROOT / "scripts/probe_arm64_pdf_lite.py").read_text(encoding="utf-8")
    workflow = (ROOT / ".github/workflows/cloud-arm64-pdf-lite-candidate.yml").read_text(encoding="utf-8")
    assert '"arm64", "aarch64"' in script or '"arm64", "aarch64"' in workflow or '{"arm64", "aarch64"}' in script
    assert "import fitz" in script
    assert "get_text(\"text\"" in script
    assert 'sort=True' in script
    assert 'PDF_ENCRYPT_AES_256' in script
    assert 'get_pixmap(' in script
    assert 'fitz.Pixmap(' in script
    assert 'invalid_rejected' in script
    assert '"real_partyops_pdf_files_tested": False' in script
    assert '"release_ready": False' in script
    assert '"wps_modified": False' in script
    assert "windows-11-arm" in workflow
    assert 'pymupdf-lite==2.1' in workflow
    assert '--only-binary=:all:' in workflow
    assert "BUILD_ONLY" in workflow
    assert "contents: read" in workflow


def test_production_lock_and_existing_formatter_not_changed_by_experiment() -> None:
    lock = (ROOT / "backend" / "uv.lock").read_text(encoding="utf-8")
    assert 'name = "pymupdf"' in lock
    assert 'version = "1.26.0"' in lock
    # A candidate experiment must not silently replace the tested dependency.
    assert 'name = "pymupdf-lite"' not in lock


def test_isolated_arm64_pdf_experiment_calls_unchanged_partyops_intake() -> None:
    script = (ROOT / "scripts/probe_arm64_partyops_intake.py").read_text(encoding="utf-8")
    workflow = (ROOT / ".github/workflows/cloud-arm64-pdf-lite-candidate.yml").read_text(encoding="utf-8")
    assert "from app.intake import MAX_PDF_PAGES, _extract_pdf" in script
    assert "_extract_pdf(document_bytes)" in script
    assert "_extract_pdf(protected)" in script
    assert "_extract_pdf(too_many_pages)" in script
    assert '"INTAKE_PDF_ENCRYPTED"' in script
    assert '"INTAKE_PDF_PAGE_LIMIT"' in script
    assert '"original_intake_code_modified": False' in script
    assert '"release_ready": False' in script
    assert "scripts/probe_arm64_partyops_intake.py" in workflow
    assert "pymupdf-lite==2.1" in workflow
    assert "app/intake.py" not in workflow
