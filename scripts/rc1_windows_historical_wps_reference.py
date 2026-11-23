"""Validate genuine prior Windows WPS receipts as HISTORICAL reference only.

Never issue current native acceptance or a full-function release receipt.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from datetime import datetime, timedelta
from pathlib import Path

CASES = (
    "format", "replace", "redheader", "rename", "convert-docx",
    "convert-pdf", "convert-txt", "convert-png-pages",
    "convert-jpg-long", "pdf-to-word",
)
HEX64 = re.compile(r"[0-9a-f]{64}")


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _read(path: Path) -> dict:
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError("WPS_HISTORY_RECORD_INVALID")
    return data


def review(root: Path, *, output: Path) -> dict:
    root = root.resolve(strict=True)
    evidence = root / ".release-gates"
    parity_path = evidence / "formatter-parity-x64.json"
    features_path = evidence / "formatter-features-e2e-x64.json"
    parity = _read(parity_path)
    features = _read(features_path)
    host_hash = parity.get("host_sha256")
    if not isinstance(host_hash, str) or not HEX64.fullmatch(host_hash):
        raise ValueError("WPS_HISTORY_HOST_HASH_INVALID")
    for record in (parity, features):
        if any(record.get(k) != v for k, v in {
            "schema": 2,
            "status": "passed",
            "platform": "windows",
            "architecture": "x64",
            "provider": "wps",
            "timezone": "Asia/Shanghai",
            "host_sha256": host_hash,
        }.items()):
            raise ValueError("WPS_HISTORY_PROVENANCE_INVALID")
        try:
            if datetime.fromisoformat(record["verified_at"]).utcoffset() != timedelta(hours=8):
                raise ValueError("timezone")
        except (ValueError, TypeError, KeyError) as exc:
            raise ValueError("WPS_HISTORY_TIMESTAMP_INVALID") from exc
    fixture = root / "backend/tests/fixtures/document-formatter-source"
    if parity.get("source_sha256") != digest(fixture / "input-manual-break.docx"):
        raise ValueError("WPS_HISTORY_SOURCE_DRIFT")
    if parity.get("golden_sha256") != digest(fixture / "expected-source-formatted.docx"):
        raise ValueError("WPS_HISTORY_GOLDEN_DRIFT")
    visual = parity.get("visual_comparison", {})
    pages = visual.get("pages") if isinstance(visual, dict) else None
    render = parity.get("rendered_pages")
    if (visual.get("passed") is not True or not isinstance(pages, list)
            or len(pages) != 3 or not isinstance(render, list) or len(render) != 3
            or parity.get("paragraph_count") != 8):
        raise ValueError("WPS_HISTORY_GOLDEN_NOT_PROVEN")
    for n, (page, rendered) in enumerate(zip(pages, render), start=1):
        if (not isinstance(page, dict) or page.get("page") != n
                or page.get("passed") is not True or page.get("raw_equal") is not True
                or type(page.get("mean_abs_channel_delta")) not in (int, float)
                or page["mean_abs_channel_delta"] != 0
                or type(page.get("same_channel_ratio")) not in (int, float)
                or page["same_channel_ratio"] != 1
                or not isinstance(rendered, dict) or rendered.get("page") != n
                or not isinstance(rendered.get("pixel_sha256"), str)
                or not HEX64.fullmatch(rendered["pixel_sha256"])):
            raise ValueError("WPS_HISTORY_GOLDEN_PIXEL_DRIFT")
    cases = features.get("cases")
    if (not isinstance(cases, list) or len(cases) != len(CASES)
            or features.get("case_count") != len(CASES)):
        raise ValueError("WPS_HISTORY_CASES_INCOMPLETE")
    for expected, row in zip(CASES, cases):
        if (not isinstance(row, dict) or row.get("case") != expected
                or row.get("status") != "passed" or row.get("configuration_restored") is not True
                or not isinstance(row.get("outputs"), list) or not row["outputs"]
                or any(not isinstance(f, dict) or not isinstance(f.get("sha256"), str)
                       or not HEX64.fullmatch(f["sha256"]) or type(f.get("bytes")) is not int
                       or f["bytes"] <= 0 for f in row["outputs"])):
            raise ValueError("WPS_HISTORY_CASE_NOT_PROVEN:" + expected)
        if expected != "pdf-to-word" and "wps" not in str(row.get("host", "")).lower():
            raise ValueError("WPS_HISTORY_PROVIDER_NOT_WPS:" + expected)
    report = {
        "schema_version": 1,
        "status": "HISTORICAL_WPS_REFERENCE_ONLY",
        "platform": "windows",
        "architecture": "x64",
        "historical_host_sha256": host_hash,
        "historical_parity_verified_at": parity["verified_at"],
        "historical_features_verified_at": features["verified_at"],
        "parity_receipt_sha256": digest(parity_path),
        "features_receipt_sha256": digest(features_path),
        "source_fixture_sha256": parity["source_sha256"],
        "golden_fixture_sha256": parity["golden_sha256"],
        "previous_visual_pages": 3,
        "previous_feature_cases": 10,
        "current_build_host_verified": False,
        "current_version_native_wps_tested": False,
        "full_function_gate_passed": False,
        "signed": False,
        "release_ready": False,
    }
    output = output.resolve()
    if not output.is_relative_to(root / "artifacts") or output.is_symlink():
        raise ValueError("WPS_HISTORY_OUTPUT_OUTSIDE_ARTIFACTS")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        report = review(args.root, output=args.output)
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print("RC1_HISTORICAL_WPS_REFERENCE_BLOCKED: " + str(exc))
        return 2
    print("RC1_HISTORICAL_WPS_REFERENCE_VERIFIED_NON_RELEASE")
    print(json.dumps(report, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
