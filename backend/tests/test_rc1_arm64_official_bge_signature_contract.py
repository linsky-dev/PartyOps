"""RC6 historically accepted BGE package may only pass with exact SHA and trust."""

from __future__ import annotations

import io
import runpy
import urllib.request
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/probe_arm64_official_bge_signature.py"
WORKFLOW = ROOT / ".github/workflows/cloud-runner-smoke.yml"


def _scope():
    return runpy.run_path(str(SCRIPT))


def test_authentic_release_asset_identity_and_trust_source_are_frozen() -> None:
    scope = _scope()
    assert scope["RELEASE_TAG"] == "v1.4.5-rc.2"
    assert scope["OFFICIAL_BYTES"] == 94_965_776
    assert scope["OFFICIAL_SHA256"] == "57df3669e368497ad6d428dab309fb7665229a57edfe3a4fe1f2afd6a0f1a155"
    assert scope["ASSET_URL"].startswith(
        "https://github.com/linsky-dev/PartyOps/releases/download/v1.4.5-rc.2/"
    )
    script = SCRIPT.read_text(encoding="utf-8")
    assert 'repo / "packaging" / "uos" / "update-public-key.txt"' in script
    assert "model_packs._validate_manifest(manifest, archive)" in script
    assert 'model_pack_public_key=public' in script
    assert "ARM64_NOT_DECLARED_BY_OFFICIAL_SIGNED_PACK" in script
    assert '"native_partyops_model_import": "NOT_RUN"' in script
    assert '"native_real_bge_from_official_archive": "NOT_RUN"' in script
    assert '"legacy_release_model_resigned_or_modified": False' in script
    assert '"release_ready": False' in script


def test_mismatched_official_bytes_are_deleted_and_never_accepted(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        urllib.request, "urlopen",
        lambda *_args, **_kwargs: io.BytesIO(b"incorrect model asset"),
    )
    output = tmp_path / "test-model.partyops-modelpack"
    with pytest.raises(ValueError, match="OFFICIAL_BGE_PROVENANCE_MISMATCH"):
        _scope()["download_exact_official_package"](output)
    assert not output.exists()


def test_oversized_download_rejected_before_signature_check(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
) -> None:
    scope = _scope()
    scope["download_exact_official_package"].__globals__["OFFICIAL_BYTES"] = 8
    monkeypatch.setattr(
        urllib.request, "urlopen",
        lambda *_args, **_kwargs: io.BytesIO(b"0123456789"),
    )
    output = tmp_path / "oversize.partyops-modelpack"
    with pytest.raises(ValueError, match="OFFICIAL_BGE_SIZE_LIMIT"):
        scope["download_exact_official_package"](output)
    assert not output.exists()


def test_workflow_never_uploads_official_model_or_private_key() -> None:
    workflow = WORKFLOW.read_text(encoding="utf-8")
    script = SCRIPT.read_text(encoding="utf-8")
    assert "scripts/probe_arm64_official_bge_signature.py" in workflow
    assert "official-bge-signature-audit.json" in workflow
    assert ".partyops-modelpack" not in workflow
    assert "private-key.pem" not in workflow
    assert "with TemporaryDirectory(" in script
    assert "downloaded_archive_uploaded" in script
    assert "release_ready" in script
