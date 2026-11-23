"""Previous Windows WPS x64 receipts may inform BUILD_ONLY, never current acceptance."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "rc1_previous_wps", ROOT / "scripts/rc1_windows_historical_wps_reference.py"
)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def _sample(tmp_path: Path) -> Path:
    root = tmp_path / "root"
    fixture = root / "backend/tests/fixtures/document-formatter-source"
    fixture.mkdir(parents=True)
    (fixture / "input-manual-break.docx").write_bytes(b"source")
    (fixture / "expected-source-formatted.docx").write_bytes(b"golden")
    evidence = root / ".release-gates"
    evidence.mkdir()
    common = {
        "schema": 2, "status": "passed", "platform": "windows",
        "architecture": "x64", "provider": "wps",
        "timezone": "Asia/Shanghai",
        "verified_at": "2026-10-18T16:54:02+08:00",
        "host_sha256": "c" * 64,
    }
    parity = {
        **common,
        "source_sha256": module.digest(fixture / "input-manual-break.docx"),
        "golden_sha256": module.digest(fixture / "expected-source-formatted.docx"),
        "paragraph_count": 8,
        "rendered_pages": [
            {"page": i, "pixel_sha256": str(i) * 64}
            for i in range(1, 4)
        ],
        "visual_comparison": {
            "passed": True,
            "pages": [
                {"page": i, "passed": True, "raw_equal": True,
                 "mean_abs_channel_delta": 0.0, "same_channel_ratio": 1.0}
                for i in range(1, 4)
            ],
        },
    }
    cases = [
        {"case": name, "status": "passed", "configuration_restored": True,
         "host": "partyops-source-host+wps",
         "outputs": [{"sha256": "d" * 64, "bytes": 500}]}
        for name in module.CASES
    ]
    features = {**common, "case_count": 10, "cases": cases}
    (evidence / "formatter-parity-x64.json").write_text(json.dumps(parity), encoding="utf-8")
    (evidence / "formatter-features-e2e-x64.json").write_text(json.dumps(features), encoding="utf-8")
    return root


def _run(root: Path) -> dict:
    return module.review(root, output=root / "artifacts/previous-wps-reference.json")


def test_historical_reference_never_approves_current_native_wps(tmp_path: Path) -> None:
    root = _sample(tmp_path)
    result = _run(root)
    assert result["status"] == "HISTORICAL_WPS_REFERENCE_ONLY"
    assert result["previous_feature_cases"] == 10
    for flag in ("current_build_host_verified", "current_version_native_wps_tested",
                 "full_function_gate_passed", "signed", "release_ready"):
        assert result[flag] is False
    assert json.loads((root / "artifacts/previous-wps-reference.json").read_text()) == result


@pytest.mark.parametrize("mutation,code", [
    ("host", "WPS_HISTORY_PROVENANCE_INVALID"),
    ("golden", "WPS_HISTORY_GOLDEN_DRIFT"),
    ("pixel", "WPS_HISTORY_GOLDEN_PIXEL_DRIFT"),
    ("case", "WPS_HISTORY_CASE_NOT_PROVEN"),
    ("provider", "WPS_HISTORY_PROVIDER_NOT_WPS"),
])
def test_historical_reference_rejects_untrusted_changes(
    tmp_path: Path, mutation: str, code: str
) -> None:
    root = _sample(tmp_path)
    parity_file = root / ".release-gates/formatter-parity-x64.json"
    features_file = root / ".release-gates/formatter-features-e2e-x64.json"
    parity = json.loads(parity_file.read_text(encoding="utf-8"))
    features = json.loads(features_file.read_text(encoding="utf-8"))
    if mutation == "host":
        features["host_sha256"] = "e" * 64
    elif mutation == "golden":
        (root / "backend/tests/fixtures/document-formatter-source/expected-source-formatted.docx").write_bytes(b"bad")
    elif mutation == "pixel":
        parity["visual_comparison"]["pages"][0]["same_channel_ratio"] = 0.5
    elif mutation == "case":
        features["cases"][0]["status"] = "failed"
    elif mutation == "provider":
        features["cases"][0]["host"] = "LibreOffice"
    parity_file.write_text(json.dumps(parity), encoding="utf-8")
    features_file.write_text(json.dumps(features), encoding="utf-8")
    with pytest.raises(ValueError, match=code):
        _run(root)
    assert not (root / "artifacts/previous-wps-reference.json").exists()


def test_historical_reference_does_not_read_existing_fake_output(tmp_path: Path) -> None:
    root = _sample(tmp_path)
    dest = root / "artifacts/previous-wps-reference.json"
    dest.parent.mkdir()
    dest.write_text('{"release_ready":true}', encoding="utf-8")
    result = _run(root)
    assert result["release_ready"] is False
