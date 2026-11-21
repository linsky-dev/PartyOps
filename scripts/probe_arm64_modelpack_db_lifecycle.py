"""Native ARM64 original PartyOps model-pack install/activate/verify lifecycle.

TEST_ONLY; uses actual app.model_packs implementation, actual SQLAlchemy models,
an isolated in-memory SQLite schema and ephemeral signed synthetic ONNX assets.
This is not the HTTP upload API, RC6 signed pack or production installation.
"""

from __future__ import annotations

import hashlib
import json
import os
import platform
import shutil
import sys
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace

from probe_arm64_signed_embedding_chain import build_test_signed_pack


def _assert_denial(action, code: str) -> None:
    from app.problems import ProblemException

    try:
        action()
    except ProblemException as error:
        if error.code != code:
            raise AssertionError(f"expected {code}, got {error.code}") from error
    else:
        raise AssertionError(f"expected fail-closed {code}")


def main() -> int:
    if os.name != "nt" or platform.machine().lower() not in {"arm64", "aarch64"}:
        raise RuntimeError("NATIVE_WINDOWS_ARM64_REQUIRED")

    import numpy as np
    import onnxruntime as ort
    import sqlalchemy
    from cryptography import __version__ as crypto_version
    from sqlalchemy import create_engine, select
    from sqlalchemy.orm import Session

    if crypto_version != "50.0.0" or np.__version__ != "2.3.5" or ort.__version__ != "1.24.2":
        raise RuntimeError("ISOLATED_RUNTIME_VERSION_MISMATCH")

    root = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(root / "backend"))
    from app import model_packs
    from app.database import Base
    from app.enums import ModelPackStatus, UserRole
    from app.models import AIModelActivation, AIModelPack, User

    with TemporaryDirectory(prefix="partyops-arm64-sqlite-lifecycle-") as folder:
        scratch = Path(folder)
        storage = scratch / "managed-models"
        storage.mkdir()
        package, public_trust = build_test_signed_pack(
            root, scratch, root / "packaging" / "uos" / "build-model-pack.py"
        )
        replay = scratch / "duplicate-copy.partyops-modelpack"
        shutil.copyfile(package, replay)
        initial_sha = hashlib.sha256(package.read_bytes()).hexdigest()

        # Patch only this isolated module's configuration function; the real
        # source file, production trust root and system settings remain untouched.
        test_settings = SimpleNamespace(
            models_dir=storage,
            model_pack_public_key=public_trust,
            update_public_key="",
            app_version="1.4.6-rc.1",
        )
        model_packs.get_settings = lambda: test_settings

        engine = create_engine("sqlite+pysqlite:///:memory:")
        Base.metadata.create_all(
            bind=engine,
            tables=[User.__table__, AIModelPack.__table__, AIModelActivation.__table__],
        )
        with Session(engine, expire_on_commit=False) as db:
            admin = User(
                username="isolated-ci-admin",
                display_name="Native ARM64 test admin",
                password_hash="synthetic-not-used",
                role=UserRole.ADMIN,
            )
            db.add(admin)
            db.flush()

            # This calls the unchanged PartyOps source, not the prior hand-
            # copied extraction loop. It writes only to ephemeral test paths.
            pack = model_packs.install_model_pack(
                package, "signed-synthetic.partyops-modelpack", admin, db
            )
            db.commit()
            assert pack.signature_valid
            assert pack.status == ModelPackStatus.INSTALLED
            assert pack.sha256 == initial_sha
            assert not package.exists(), "import must move signed source to managed storage"
            unpacked_root = model_packs.model_pack_root(pack)
            assert unpacked_root.is_dir()
            assert model_packs.verify_installed_pack(pack)
            stored_package = storage / "packages" / pack.filename
            assert stored_package.is_file()
            assert hashlib.sha256(stored_package.read_bytes()).hexdigest() == initial_sha

            _assert_denial(
                lambda: model_packs.install_model_pack(
                    replay, "duplicate.partyops-modelpack", admin, db
                ),
                "MODEL_PACK_ALREADY_INSTALLED",
            )
            assert replay.is_file(), "rejected duplicate cannot consume original input"

            activated = model_packs.activate_model_pack(db, pack, "embedding", admin.id)
            assert activated.id == pack.id
            assert activated.status == ModelPackStatus.ACTIVE
            db.commit()
            activation = db.scalar(
                select(AIModelActivation).where(AIModelActivation.capability == "embedding")
            )
            assert activation is not None and activation.model_pack_id == pack.id
            assert model_packs.active_model_pack(db, "embedding").id == pack.id

            # Execute the original embedding class on the files installed by
            # the real lifecycle functions, not files copied by the test.
            from probe_arm64_partyops_embedding_runtime import _load_unchanged_runtime

            Runtime, runtime_sha = _load_unchanged_runtime(
                root / "backend" / "app" / "local_ai.py", unpacked_root
            )
            runtime = Runtime()
            values = runtime.encode(pack, ["hello world", "hello world"])
            assert len(values) == 2
            expected = np.asarray([1, 1], dtype=np.float32) / np.sqrt(2)
            for item in values:
                np.testing.assert_allclose(
                    np.frombuffer(item, dtype=np.float32), expected, rtol=1e-6
                )
            assert runtime.loaded_for(pack.id)
            runtime.unload()

            # Filesystem corruption must stop re-activation of the same record.
            model_file = unpacked_root.joinpath(
                *pack.manifest["components"]["embedding"]["model_file"].split("/")
            )
            model_file.write_bytes(b"tampered-installed-model")
            assert not model_packs.verify_installed_pack(pack)
            _assert_denial(
                lambda: model_packs.activate_model_pack(db, pack, "embedding", admin.id),
                "MODEL_PACK_CORRUPT",
            )
            assert pack.status == ModelPackStatus.CORRUPT
            db.rollback()

            source_sha = hashlib.sha256((root / "backend" / "app" / "model_packs.py").read_bytes()).hexdigest()
            evidence = {
                "schema_version": 1,
                "status": "PASS",
                "scope": "NATIVE_ARM64_ORIGINAL_PACK_INSTALL_ACTIVATE_VERIFY_SQLITE_SYNTHETIC",
                "source_commit": os.environ.get("GITHUB_SHA", ""),
                "native_architecture": platform.machine(),
                "python": sys.version.split()[0],
                "sqlalchemy": sqlalchemy.__version__,
                "cryptography": crypto_version,
                "onnxruntime": ort.__version__,
                "model_packs_original_source_sha256": source_sha,
                "embedding_original_source_sha256": runtime_sha,
                "test_signed_package_sha256": initial_sha,
                "checks": [
                    "real_partyops_install_model_pack",
                    "external_ephemeral_key_signature_accepted",
                    "signed_archive_moved_to_isolated_managed_storage",
                    "installed_files_verified",
                    "duplicate_install_rejected_before_input_consumed",
                    "real_partyops_activate_model_pack",
                    "sqlite_activation_record_committed",
                    "real_partyops_embedding_runtime_infers_installed_synthetic_model",
                    "corrupt_installed_file_rejected_before_reactivation",
                ],
                "sqlite_database": "EPHEMERAL_IN_MEMORY_ONLY",
                "real_user_accounts_used": False,
                "production_model_pack_tested": False,
                "http_upload_api_tested": False,
                "full_app_dependency_lock_installed": False,
                "real_bge_weights_in_this_test": False,
                "production_trust_root_used": False,
                "official_windows_arm64_package_built": False,
                "wps_modified": False,
                "release_ready": False,
            }

        engine.dispose()

    # The temporary DB, ephemeral test keys, signed package and extracted
    # model are gone before evidence is written.
    outfile = root / "artifacts" / "arm64-crypto-probe" / "modelpack-db-lifecycle.json"
    outfile.parent.mkdir(parents=True, exist_ok=True)
    outfile.write_text(
        json.dumps(evidence, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print("PARTYOPS_ARM64_REAL_MODEL_PACK_DB_LIFECYCLE_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
