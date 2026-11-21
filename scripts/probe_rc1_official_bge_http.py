"""Verify a fixed published BGE archive and run the real FastAPI TestClient gate.

Source-only BUILD_ONLY: no production listener, model republishing or WPS.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

from probe_arm64_official_bge_signature import (
    ASSET_NAME,
    OFFICIAL_SHA256,
    download_exact_official_package,
)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--python", required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    interpreter = args.python.resolve(strict=True)
    evidence_dir = root / "artifacts" / "official-bge-http"
    evidence_dir.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(prefix="partyops-official-bge-http-") as tempdir:
        source = Path(tempdir) / ASSET_NAME
        provenance = download_exact_official_package(source)
        if provenance["archive_sha256"] != OFFICIAL_SHA256:
            raise RuntimeError("OFFICIAL_BGE_ARCHIVE_PROVENANCE_INVALID")
        env = os.environ.copy()
        env["PARTYOPS_RC1_OFFICIAL_BGE_HTTP_PACK"] = str(source)
        tested = subprocess.run(
            [
                str(interpreter), "-m", "pytest", "-q", "-rs",
                "tests/test_rc1_official_bge_http.py",
                "--junitxml=../artifacts/official-bge-http/junit.xml",
            ],
            cwd=root / "backend",
            env=env,
            capture_output=True,
            text=True,
            check=False,
            timeout=600,
        )
        print(tested.stdout[-4000:])
        print(tested.stderr[-1600:], file=sys.stderr)
        status = "PASS" if tested.returncode == 0 else "FAIL"
        evidence = {
            "schema_version": 1,
            "status": status,
            "scope": "WIN_X64_SIGNED_OFFICIAL_RC2_BGE_FASTAPI_TESTCLIENT",
            "source_commit": os.environ.get("GITHUB_SHA", ""),
            "original_release": provenance,
            "official_external_public_key": "packaging/uos/update-public-key.txt",
            "private_key_used": False,
            "official_archive_modified": False,
            "customer_database_touched": False,
            "fastapi_testclient_used": True,
            "deployed_http_server_tested": False,
            "arm64_http_api_tested": False,
            "wps_modified": False,
            "installer_or_upgrade_tested": False,
            "release_ready": False,
        }
        (evidence_dir / "evidence.json").write_text(
            json.dumps(evidence, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        if tested.returncode:
            raise RuntimeError("OFFICIAL_BGE_FASTAPI_TESTCLIENT_FAILED")
    print("PARTYOPS_OFFICIAL_SIGNED_BGE_FASTAPI_HTTP_TESTCLIENT_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
