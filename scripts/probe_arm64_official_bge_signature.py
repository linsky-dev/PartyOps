"""Audit an authentic publicly released PartyOps BGE signed model pack on ARM64.

The RC6-accepted 94,965,776-byte model pack is published under v1.4.5-rc.2.
Download only from its immutable release path; verify exact archival SHA-256,
then run original PartyOps manifest/Ed25519/payload verifier with the repository
public trust root. Never modify or re-sign the official model pack. This is
BUILD_ONLY verification, not authorization to install an AMD64-only model on
ARM64. No model file is uploaded into GitHub Actions artifacts.
"""

from __future__ import annotations

import hashlib
import json
import os
import platform
import sys
import urllib.request
import zipfile
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace

RELEASE_TAG = "v1.4.5-rc.2"
ASSET_NAME = "PartyOps_BGE_Small_ZH_1.5.0.partyops-modelpack"
ASSET_URL = (
    "https://github.com/linsky-dev/PartyOps/releases/download/"
    + RELEASE_TAG + "/" + ASSET_NAME
)
OFFICIAL_SHA256 = "57df3669e368497ad6d428dab309fb7665229a57edfe3a4fe1f2afd6a0f1a155"
OFFICIAL_BYTES = 94_965_776


def download_exact_official_package(out: Path) -> dict:
    digest = hashlib.sha256()
    received = 0
    req = urllib.request.Request(
        ASSET_URL, headers={"User-Agent": "PartyOps-rc1-verified-model-audit/1.0"}
    )
    try:
        with urllib.request.urlopen(req, timeout=120) as stream, out.open("xb") as destination:
            while chunk := stream.read(1024 * 1024):
                received += len(chunk)
                if received > OFFICIAL_BYTES:
                    raise ValueError("OFFICIAL_BGE_SIZE_LIMIT")
                digest.update(chunk)
                destination.write(chunk)
    except Exception:
        out.unlink(missing_ok=True)
        raise
    actual = digest.hexdigest()
    if received != OFFICIAL_BYTES or actual != OFFICIAL_SHA256:
        out.unlink(missing_ok=True)
        raise ValueError(f"OFFICIAL_BGE_PROVENANCE_MISMATCH:bytes={received}:sha256={actual}")
    return {
        "release_tag": RELEASE_TAG,
        "archive_sha256": actual,
        "archive_bytes": received,
    }


def main() -> int:
    if os.name != "nt" or platform.machine().lower() not in ("arm64", "aarch64"):
        raise RuntimeError("OFFICIAL_BGE_NATIVE_ARM64_REQUIRED")
    from cryptography import __version__ as cryptography_version
    if cryptography_version != "50.0.0":
        raise RuntimeError("OFFICIAL_BGE_PINNED_CRYPTO_REQUIRED")
    repo = Path(__file__).resolve().parents[1]
    trust_path = repo / "packaging" / "uos" / "update-public-key.txt"
    public = trust_path.read_text(encoding="ascii").strip()
    if not public:
        raise RuntimeError("OFFICIAL_BGE_EXTERNAL_TRUST_ROOT_MISSING")
    sys.path.insert(0, str(repo / "backend"))
    from app import model_packs

    # Invoke unchanged app.model_packs verifier with the external release key,
    # not a key embedded inside any untrusted archive.
    model_packs.get_settings = lambda: SimpleNamespace(
        model_pack_public_key=public,
        update_public_key=public,
    )
    with TemporaryDirectory(prefix="partyops-rc2-signed-bge-arm64-") as folder:
        path = Path(folder) / ASSET_NAME
        provenance = download_exact_official_package(path)
        with zipfile.ZipFile(path) as archive:
            manifests = [
                info for info in archive.infolist()
                if info.filename == "manifest.json"
            ]
            if len(manifests) != 1 or manifests[0].file_size > 1024 * 1024:
                raise ValueError("OFFICIAL_BGE_MANIFEST_FORMAT_INVALID")
            manifest = json.loads(archive.read("manifest.json").decode("utf-8"))
            if not isinstance(manifest, dict):
                raise ValueError("OFFICIAL_BGE_MANIFEST_OBJECT_REQUIRED")
            files, signature_valid = model_packs._validate_manifest(manifest, archive)
            if not signature_valid:
                raise RuntimeError("OFFICIAL_BGE_SIGNATURE_INVALID")
            assert len(files) > 0

            architecture = manifest.get("architecture")
            architectures = manifest.get("architectures")
            declared = (
                architectures if isinstance(architectures, list) and architectures
                else [architecture if isinstance(architecture, str) else "unspecified"]
            )
            platforms = manifest.get("platforms")
            declared_platforms = (
                platforms if isinstance(platforms, list) and platforms
                else ["unspecified"]
            )
            # A published AMD64-only package cannot be promoted to ARM64
            # merely because third-party equivalent weights execute there.
            arm64_declared = "arm64" in declared or "universal" in declared
            windows_declared = "windows" in declared_platforms
            conclusion = (
                "ARM64_DECLARED_BUT_NATIVE_IMPORT_STILL_NOT_RUN"
                if arm64_declared and windows_declared
                else "ARM64_NOT_DECLARED_BY_OFFICIAL_SIGNED_PACK"
            )
            evidence = {
                "schema_version": 1,
                "status": "PASS",
                "scope": "OFFICIAL_RELEASE_ARCHIVE_HASH_SIGNATURE_AND_PAYLOAD_VALIDATION",
                "source_commit": os.environ.get("GITHUB_SHA", ""),
                "native_architecture": platform.machine(),
                "python": sys.version.split()[0],
                "cryptography_version": cryptography_version,
                "original_verifier_sha256": hashlib.sha256(
                    (repo / "backend" / "app" / "model_packs.py").read_bytes()
                ).hexdigest(),
                "external_public_trust_root_sha256": hashlib.sha256(
                    trust_path.read_bytes()
                ).hexdigest(),
                "release_provenance": provenance,
                "format_version": manifest.get("format_version"),
                "declared_architectures": declared,
                "declared_platforms": declared_platforms,
                "model_id": manifest.get("model_id"),
                "verified_payload_count": len(files),
                "verified_payload_bytes": sum(int(item["size"]) for item in files.values()),
                "authentic_published_release_asset": True,
                "original_ed25519_signature_valid": True,
                "all_original_payload_hashes_valid": True,
                "arm64_platform_claim_accepted": bool(arm64_declared and windows_declared),
                "arm64_runtime_conclusion": conclusion,
                "native_partyops_model_import": "NOT_RUN",
                "native_partyops_model_activation": "NOT_RUN",
                "native_real_bge_from_official_archive": "NOT_RUN",
                "legacy_release_model_resigned_or_modified": False,
                "production_private_key_used": False,
                "downloaded_archive_uploaded": False,
                "wps_modified": False,
                "release_ready": False,
            }
    out = repo / "artifacts" / "arm64-crypto-probe" / "official-bge-signature-audit.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("PARTYOPS_ARM64_OFFICIAL_BGE_RELEASE_SIGNATURE_AND_HASH_PASS")
    print("OFFICIAL_BGE_ARM64_DECLARATION=" + conclusion)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
