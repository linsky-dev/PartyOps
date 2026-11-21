"""Native Windows ARM64 official signed BGE: real PartyOps import, activation and inference.

This test downloads the immutable historical public release model pack, verifies its
exact archive digest and publisher signature, then exercises unchanged PartyOps
model management with in-memory SQLite and temporary storage. No user database,
production package, signing key, public trust root, or WPS file is modified.
This is BUILD_ONLY; it is not HTTP upload, an installer, or release approval.
"""

from __future__ import annotations

import hashlib
import json
import os
import platform
import sys
import zipfile
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace

from probe_arm64_official_bge_signature import (
    ASSET_NAME,
    OFFICIAL_SHA256,
    download_exact_official_package,
)
from probe_arm64_modelpack_db_lifecycle import _assert_denial
from probe_arm64_partyops_embedding_runtime import _load_unchanged_runtime


def main() -> int:
    if os.name != "nt" or platform.machine().lower() not in {"arm64", "aarch64"}:
        raise RuntimeError("NATIVE_WINDOWS_ARM64_REQUIRED")

    import numpy as np
    import onnxruntime as ort
    import sqlalchemy
    from cryptography import __version__ as crypto_version
    from sqlalchemy import create_engine, select
    from sqlalchemy.orm import Session
    from tokenizers import __version__ as tokenizer_version

    if (
        crypto_version != "50.0.0"
        or np.__version__ != "2.3.5"
        or ort.__version__ != "1.24.2"
        or tokenizer_version != "0.23.3"
    ):
        raise RuntimeError("ISOLATED_ARM64_RUNTIME_VERSION_MISMATCH")

    repo = Path(__file__).resolve().parents[1]
    trust_file = repo / "packaging" / "uos" / "update-public-key.txt"
    public_key = trust_file.read_text(encoding="ascii").strip()
    if not public_key:
        raise RuntimeError("OFFICIAL_EXTERNAL_PUBLIC_KEY_MISSING")
    sys.path.insert(0, str(repo / "backend"))

    from app import model_packs
    from app.database import Base
    from app.enums import ModelPackStatus, UserRole
    from app.models import AIModelActivation, AIModelPack, User

    with TemporaryDirectory(prefix="partyops-official-bge-arm64-lifecycle-") as directory:
        scratch = Path(directory)
        storage = scratch / "managed-models"
        storage.mkdir()
        source = scratch / ASSET_NAME
        provenance = download_exact_official_package(source)

        # Unlike a synthetic model, the source and signing key are the original
        # immutable release asset and the existing external official public key.
        test_settings = SimpleNamespace(
            models_dir=storage,
            model_pack_public_key=public_key,
            update_public_key=public_key,
            app_version="1.4.6-rc.1",
        )
        model_packs.get_settings = lambda: test_settings
        with zipfile.ZipFile(source) as archive:
            manifest = json.loads(archive.read("manifest.json").decode("utf-8"))
            files, signed = model_packs._validate_manifest(manifest, archive)
            if not signed or not files:
                raise RuntimeError("OFFICIAL_SIGNED_PAYLOAD_INVALID")
            if not (
                "arm64" in manifest.get("architectures", [])
                and "windows" in manifest.get("platforms", [])
                and isinstance(manifest.get("components", {}).get("embedding"), dict)
            ):
                raise RuntimeError("OFFICIAL_BGE_ARM64_EMBEDDING_NOT_DECLARED")

        engine = create_engine("sqlite+pysqlite:///:memory:")
        Base.metadata.create_all(
            bind=engine,
            tables=[User.__table__, AIModelPack.__table__, AIModelActivation.__table__],
        )
        try:
            with Session(engine, expire_on_commit=False) as db:
                admin = User(
                    username="isolated-official-bge-ci",
                    display_name="Ephemeral official BGE validation",
                    password_hash="not-a-real-user-password",
                    role=UserRole.ADMIN,
                )
                db.add(admin)
                db.flush()

                pack = model_packs.install_model_pack(source, ASSET_NAME, admin, db)
                db.commit()
                if not pack.signature_valid or pack.status != ModelPackStatus.INSTALLED:
                    raise AssertionError("OFFICIAL_BGE_REAL_IMPORT_FAILED")
                if source.exists() or pack.sha256 != OFFICIAL_SHA256:
                    raise AssertionError("OFFICIAL_BGE_PROVENANCE_LOST_AFTER_IMPORT")
                stored = storage / "packages" / pack.filename
                if model_packs.sha256_path(stored) != OFFICIAL_SHA256:
                    raise AssertionError("OFFICIAL_BGE_STORED_ARCHIVE_MISMATCH")
                if not model_packs.verify_installed_pack(pack):
                    raise AssertionError("OFFICIAL_BGE_EXTRACTED_PAYLOAD_INVALID")

                # Same digest must be refused before moving the already managed
                # source package. A rejection must not delete the trusted archive.
                _assert_denial(
                    lambda: model_packs.install_model_pack(
                        stored, ASSET_NAME, admin, db
                    ),
                    "MODEL_PACK_ALREADY_INSTALLED",
                )
                if not stored.is_file():
                    raise AssertionError("DUPLICATE_REFUSAL_CONSUMED_OFFICIAL_PACK")

                model_packs.activate_model_pack(db, pack, "embedding", admin.id)
                db.commit()
                activation = db.scalar(
                    select(AIModelActivation).where(
                        AIModelActivation.capability == "embedding"
                    )
                )
                if (
                    activation is None
                    or activation.model_pack_id != pack.id
                    or model_packs.active_model_pack(db, "embedding").id != pack.id
                ):
                    raise AssertionError("OFFICIAL_BGE_ACTIVATION_NOT_PERSISTED")

                installed = model_packs.model_pack_root(pack)
                Runtime, runtime_hash = _load_unchanged_runtime(
                    repo / "backend" / "app" / "local_ai.py", installed
                )
                runtime = Runtime()
                phrases = [
                    "支部党员大会会议记录",
                    "支部党员大会会议记录",
                    "本周食堂菜单",
                ]
                rows = runtime.encode(pack, phrases)
                dimension = int(pack.manifest["components"]["embedding"]["dimension"])
                if len(rows) != len(phrases):
                    raise AssertionError("OFFICIAL_BGE_INFERENCE_BATCH_INVALID")
                vectors = [np.frombuffer(row, dtype=np.float32) for row in rows]
                if any(v.shape != (dimension,) or not np.all(np.isfinite(v)) for v in vectors):
                    raise AssertionError("OFFICIAL_BGE_VECTOR_DIMENSION_INVALID")
                if any(not np.isclose(np.linalg.norm(v), 1.0, atol=1e-4) for v in vectors):
                    raise AssertionError("OFFICIAL_BGE_VECTOR_NORMALIZATION_INVALID")
                if float(np.dot(vectors[0], vectors[1])) < 0.9999:
                    raise AssertionError("OFFICIAL_BGE_REPEATABILITY_INVALID")
                query_rows = runtime.encode(
                    pack, ["支部党员大会会议记录"], is_query=True
                )
                query = np.frombuffer(query_rows[0], dtype=np.float32)
                if query.shape != (dimension,) or not np.all(np.isfinite(query)):
                    raise AssertionError("OFFICIAL_BGE_QUERY_VECTOR_INVALID")
                runtime.unload()
                if runtime.loaded_for(pack.id):
                    raise AssertionError("OFFICIAL_BGE_UNLOAD_FAILED")

                evidence = {
                    "schema_version": 1,
                    "status": "PASS",
                    "scope": "OFFICIAL_SIGNED_RC2_BGE_NATIVE_ARM64_SQLITE_INSTALL_ACTIVATE_INFERENCE",
                    "source_commit": os.environ.get("GITHUB_SHA", ""),
                    "native_architecture": platform.machine(),
                    "python": sys.version.split()[0],
                    "cryptography": crypto_version,
                    "sqlalchemy": sqlalchemy.__version__,
                    "onnxruntime": ort.__version__,
                    "numpy": np.__version__,
                    "tokenizers": tokenizer_version,
                    "source_archive": provenance,
                    "official_external_public_key_sha256": hashlib.sha256(
                        trust_file.read_bytes()
                    ).hexdigest(),
                    "original_model_manager_sha256": hashlib.sha256(
                        (repo / "backend" / "app" / "model_packs.py").read_bytes()
                    ).hexdigest(),
                    "original_embedding_runtime_sha256": runtime_hash,
                    "verified_payload_count": len(files),
                    "embedding_dimension": dimension,
                    "checks": [
                        "exact_official_archive_sha256",
                        "external_original_ed25519_signature_and_payload_hashes",
                        "original_partyops_install_model_pack",
                        "duplicate_import_denied_without_source_consumption",
                        "original_partyops_verify_installed_pack",
                        "sqlite_activation_committed",
                        "official_real_bge_cpu_inference_and_query",
                        "runtime_unload",
                    ],
                    "official_published_archive_modified": False,
                    "public_trust_root_modified": False,
                    "real_user_database_touched": False,
                    "http_upload_api_tested": False,
                    "full_application_installed": False,
                    "full_production_dependency_lock_installed": False,
                    "wps_modified": False,
                    "signed_release_installer_ready": False,
                    "release_ready": False,
                }
        finally:
            engine.dispose()

    # No model, SQLite DB, or raw public archive is retained as a CI artifact.
    output = repo / "artifacts" / "arm64-crypto-probe" / "official-bge-lifecycle.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("PARTYOPS_ARM64_OFFICIAL_SIGNED_BGE_IMPORT_ACTIVATION_INFERENCE_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
