"""BUILD_ONLY: reopen official signed BGE from a fresh native Windows ARM64 Python process.

The parent has already verified the immutable official archive's exact hash and
Ed25519 signature, imported it with PartyOps, and persisted its activation.
This independent interpreter reads only a temporary SQLite DB, installed
files and a 512-float expected vector. It never touches customer data,
production keys, installed PartyOps services, or WPS.
"""

from __future__ import annotations

import argparse
import hashlib
import os
import platform
import sys
from pathlib import Path
from types import SimpleNamespace


def main() -> int:
    parser = argparse.ArgumentParser(description="Isolated official BGE restart probe")
    parser.add_argument("--sqlite", type=Path, required=True)
    parser.add_argument("--models-dir", type=Path, required=True)
    parser.add_argument("--pack-id", required=True)
    parser.add_argument("--expected-vector", type=Path, required=True)
    parser.add_argument("--runtime-sha256", required=True)
    args = parser.parse_args()
    if os.name != "nt" or platform.machine().lower() not in {"arm64", "aarch64"}:
        raise RuntimeError("NATIVE_WINDOWS_ARM64_REQUIRED")
    if not all((args.sqlite.is_file(), args.models_dir.is_dir(), args.expected_vector.is_file())):
        raise RuntimeError("OFFICIAL_BGE_EPHEMERAL_PROCESS_INPUT_MISSING")

    import numpy as np
    from sqlalchemy import create_engine
    from sqlalchemy.orm import Session

    repo = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(repo / "scripts"))
    sys.path.insert(0, str(repo / "backend"))
    from app import model_packs
    from app.enums import ModelPackStatus
    from app.models import AIModelPack
    from probe_arm64_partyops_embedding_runtime import _load_unchanged_runtime

    public_key = (repo / "packaging" / "uos" / "update-public-key.txt").read_text(encoding="ascii").strip()
    if not public_key:
        raise RuntimeError("OFFICIAL_BGE_EXTERNAL_TRUST_ROOT_MISSING")
    model_packs.get_settings = lambda: SimpleNamespace(
        models_dir=args.models_dir,
        model_pack_public_key=public_key,
        update_public_key=public_key,
        app_version="1.4.6-rc.1",
    )
    engine = create_engine(f"sqlite+pysqlite:///{args.sqlite.as_posix()}")
    try:
        with Session(engine, expire_on_commit=False) as db:
            pack = db.get(AIModelPack, args.pack_id)
            active = model_packs.active_model_pack(db, "embedding")
            if (
                pack is None
                or active is None
                or active.id != pack.id
                or pack.status != ModelPackStatus.ACTIVE
                or not pack.signature_valid
            ):
                raise AssertionError("OFFICIAL_BGE_NEW_PROCESS_ACTIVATION_MISSING")
            # This process starts with a *fresh*, empty verifier cache.
            if not model_packs.verify_installed_pack(pack):
                raise AssertionError("OFFICIAL_BGE_NEW_PROCESS_PAYLOAD_HASH_INVALID")
            runtime_path = repo / "backend" / "app" / "local_ai.py"
            if hashlib.sha256(runtime_path.read_bytes()).hexdigest() != args.runtime_sha256:
                raise AssertionError("OFFICIAL_BGE_NEW_PROCESS_RUNTIME_CHANGED")
            Runtime, _ = _load_unchanged_runtime(
                runtime_path, model_packs.model_pack_root(pack)
            )
            expected = np.fromfile(args.expected_vector, dtype=np.float32)
            dimension = int(pack.manifest["components"]["embedding"]["dimension"])
            if expected.shape != (dimension,):
                raise AssertionError("OFFICIAL_BGE_NEW_PROCESS_EXPECTED_VECTOR_INVALID")
            runtime = Runtime()
            try:
                output = runtime.encode(pack, ["支部党员大会会议记录"])
                got = np.frombuffer(output[0], dtype=np.float32)
                if got.shape != (dimension,) or not np.all(np.isfinite(got)):
                    raise AssertionError("OFFICIAL_BGE_NEW_PROCESS_VECTOR_INVALID")
                if not np.allclose(got, expected, rtol=1e-5, atol=1e-5):
                    raise AssertionError("OFFICIAL_BGE_NEW_PROCESS_VECTOR_DRIFT")
                if not runtime.loaded_for(pack.id):
                    raise AssertionError("OFFICIAL_BGE_NEW_PROCESS_MODEL_NOT_LOADED")
            finally:
                runtime.unload()
    finally:
        engine.dispose()
    print("PARTYOPS_ARM64_OFFICIAL_BGE_FRESH_INTERPRETER_REOPEN_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
