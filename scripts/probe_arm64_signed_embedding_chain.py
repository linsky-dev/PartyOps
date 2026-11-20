"""Native Windows ARM64 signed synthetic model-pack -> verified ONNX inference.

Runs unchanged PartyOps model-pack *builder*, original verifier functions and
original EmbeddingRuntime source in an isolated test namespace. This is a
source-path integration test, not full API installation, production model-pack
import/activation or real BGE verification. Nothing is redistributed.
"""

from __future__ import annotations

import base64
import hashlib
import json
import os
import platform
import subprocess
import sys
import zipfile
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace

from cryptography import __version__ as crypto_version
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey

from probe_arm64_partyops_embedding_runtime import (
    _create_synthetic_components,
    _load_unchanged_runtime,
)
from probe_arm64_signed_modelpack import (
    TestProblemException,
    change_archive_member,
    expect_denied,
    load_unchanged_modelpack_verifier,
)


def build_test_signed_pack(repo: Path, scratch: Path, builder: Path) -> tuple[Path, str]:
    _create_synthetic_components(scratch)
    notice = scratch / "TEST-NOTICE.txt"
    notice.write_text("Synthesized test assets only, no real model weights.\n", encoding="utf-8")
    ephemeral_key = Ed25519PrivateKey.generate()
    private = scratch / "one-time-private-key.pem"
    private.write_bytes(ephemeral_key.private_bytes(
        serialization.Encoding.PEM,
        serialization.PrivateFormat.PKCS8,
        serialization.NoEncryption(),
    ))
    private.chmod(0o600)
    trusted = base64.b64encode(ephemeral_key.public_key().public_bytes(
        serialization.Encoding.Raw,
        serialization.PublicFormat.Raw,
    )).decode("ascii")
    trust_file = scratch / "one-time-public-key.txt"
    trust_file.write_text(trusted + "\n", encoding="ascii")
    bundle = scratch / "synthetic-inference.partyops-modelpack"
    args = [
        sys.executable, str(builder),
        "--embedding", str(scratch / "model.onnx"),
        "--tokenizer", str(scratch / "tokenizer.json"),
        "--license", str(notice),
        "--private-key", str(private),
        "--public-key", str(trust_file),
        "--architecture", "arm64",
        "--platform", "windows",
        "--pooling", "cls",
        "--query-prefix", "prefix ",
        "--max-length", "32",
        "--dimension", "2",
        "--model-source", "synthetic-local-test",
        "--license-name", "TEST-ONLY",
        "--model-id", "synthetic-arm64-signed-inference",
        "--name", "test-only synthetic ARM64 inference",
        "--version", "0.0.0-test",
        "--output", str(bundle),
    ]
    result = subprocess.run(args, capture_output=True, text=True, timeout=30, check=False)
    if result.returncode != 0:
        raise RuntimeError(f"SYNTHETIC_PACK_BUILD_FAILED:{result.stderr[:300]}")
    return bundle, trusted


def main() -> int:
    if os.name != "nt" or platform.machine().lower() not in ("arm64", "aarch64"):
        raise RuntimeError("NATIVE_ARM64_ONLY")
    if crypto_version != "50.0.0":
        raise RuntimeError(f"PINNED_CRYPTO_MISSING:{crypto_version}")

    import numpy as np
    import onnxruntime as ort
    from tokenizers import __version__ as tokenizer_version

    assert np.__version__ == "2.3.5"
    assert ort.__version__ == "1.24.2"
    assert tokenizer_version == "0.23.3"

    repo = Path(__file__).resolve().parents[1]
    builder = repo / "packaging" / "uos" / "build-model-pack.py"
    verifier_source = repo / "backend" / "app" / "model_packs.py"
    embedding_source = repo / "backend" / "app" / "local_ai.py"
    with TemporaryDirectory(prefix="partyops-arm64-signed-chain-") as temp:
        scratch = Path(temp)
        bundle, public_trust = build_test_signed_pack(repo, scratch, builder)
        validate, _settings, verifier_sha = load_unchanged_modelpack_verifier(
            verifier_source, public_trust
        )
        root = scratch / "verified"
        with zipfile.ZipFile(bundle, "r") as archive:
            manifest = json.loads(archive.read("manifest.json"))
            records, signed = validate(manifest, archive)
            assert signed and len(records) == 3
            assert manifest["format_version"] == 2
            assert manifest["architectures"] == ["arm64"]
            assert manifest["platforms"] == ["windows"]
            assert manifest["components"]["embedding"]["dimension"] == 2
            # Extraction is allowed only after verified signature and all file hashes.
            for name in records:
                dst = root.joinpath(*name.split("/"))
                dst.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(name) as source, dst.open("xb") as sink:
                    while chunk := source.read(1024 * 1024):
                        sink.write(chunk)
                assert hashlib.sha256(dst.read_bytes()).hexdigest() == records[name]["sha256"]

        runtime_class, local_ai_sha = _load_unchanged_runtime(embedding_source, root)
        pack = SimpleNamespace(id="synthetic-arm64-signed-inference", manifest=manifest)
        runtime = runtime_class()
        texts = ["hello world", "hello", "hello world"]
        output = runtime.encode(pack, texts)
        assert len(output) == len(texts)
        vectors = [np.frombuffer(row, dtype=np.float32) for row in output]
        assert all(row.shape == (2,) and np.all(np.isfinite(row)) for row in vectors)
        expected = np.asarray([1, 1], dtype=np.float32) / np.sqrt(2)
        np.testing.assert_allclose(vectors[0], expected, rtol=1e-6)
        np.testing.assert_allclose(vectors[0], vectors[2], rtol=1e-6)
        assert all(np.isclose(np.linalg.norm(row), 1.0, atol=1e-6) for row in vectors)
        query = np.frombuffer(runtime.encode(pack, ["hello world"], is_query=True)[0], dtype=np.float32)
        np.testing.assert_allclose(query, np.array([3, 1], np.float32) / np.sqrt(10), rtol=1e-6)
        runtime.unload()
        assert not runtime.loaded_for(pack.id)

        # No tampered archive is loaded into the runtime; original validator
        # must reject it before any extraction or model session is possible.
        corrupted = scratch / "tampered-inference.partyops-modelpack"
        model_name = manifest["components"]["embedding"]["model_file"]
        change_archive_member(bundle, corrupted, model_name, b"attacker bytes")
        expect_denied(validate, corrupted, "MODEL_PACK_HASH_MISMATCH")

        provenance = {
            "schema_version": 1,
            "status": "PASS",
            "scope": "TEST_ONLY_SIGNED_MODEL_PACK_VERIFIED_THEN_SYNTHETIC_INFERENCE",
            "source_sha": os.environ.get("GITHUB_SHA", ""),
            "architecture": platform.machine(),
            "python": sys.version.split()[0],
            "cryptography": crypto_version,
            "numpy": np.__version__,
            "onnxruntime": ort.__version__,
            "tokenizers": tokenizer_version,
            "original_builder_sha256": hashlib.sha256(builder.read_bytes()).hexdigest(),
            "original_verifier_sha256": verifier_sha,
            "original_embedding_runtime_sha256": local_ai_sha,
            "temporary_signed_bundle_sha256": hashlib.sha256(bundle.read_bytes()).hexdigest(),
            "test_only_ephemeral_ed25519_key_used": True,
            "official_trust_root_used": False,
            "signature_and_file_hash_verified_before_extraction": True,
            "verified_contents_inferred_with_original_source": True,
            "synthetic_embedding_batch_and_query_pass": True,
            "tampered_bundle_rejected_before_inference": True,
            "full_app_import_and_activation": "NOT_RUN",
            "signed_rc6_model_pack_tested": False,
            "real_bge_weights_tested_here": False,
            "installed_partyops_arm64": False,
            "wps_modified": False,
            "production_lock_modified": False,
            "usable_for_release": False,
        }

    # Keys, plaintext files and both signed archives disappear with the
    # TemporaryDirectory; only non-sensitive provenance is retained.
    output_path = repo / "artifacts/arm64-crypto-probe/signed-embedding-chain.json"
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(provenance, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("PARTYOPS_ARM64_SIGNED_SYNTHETIC_EMBEDDING_CHAIN_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
