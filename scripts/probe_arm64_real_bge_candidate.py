"""Native Windows ARM64 real BGE-zh ONNX candidate smoke, no application changes.

Model weights: ONNX Community export of upstream BAAI BGE Small Chinese v1.5
at an immutable commit, explicitly NOT the PartyOps signed production model pack.
Tokenizer: official BAAI upstream at a separately pinned commit and digest.
Only the unchanged EmbeddingRuntime class from PartyOps is executed in a
test-only namespace. No original AI/WPS/installer code is edited or invoked.
"""

from __future__ import annotations

import hashlib
import json
import math
import os
import platform
import sys
import urllib.request
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace

ONNX_REPOSITORY = "onnx-community/bge-small-zh-v1.5-ONNX"
ONNX_COMMIT = "18e2da99700c49ed48c0a0e3683da39348fbbb36"
TOKENIZER_REPOSITORY = "BAAI/bge-small-zh-v1.5"
TOKENIZER_COMMIT = "7999e1d3359715c523056ef9478215996d62a620"
# SHA-256 values were published for the immutable ONNX export and captured
# independently in the prior PartyOps real-model acceptance records.
ASSETS = (
    (
        ONNX_REPOSITORY, ONNX_COMMIT, "onnx/model.onnx",
        "69b353bb2aa2d09ab606ddbbc35437b03c843615a6bff28216a37fee7309c2aa", 2_000_000,
    ),
    (
        ONNX_REPOSITORY, ONNX_COMMIT, "onnx/model.onnx_data",
        "e72da961b03613124aa11317470c995ca197651a9d7f6be2b0e90aad92f71df0", 125_000_000,
    ),
    (
        TOKENIZER_REPOSITORY, TOKENIZER_COMMIT, "tokenizer.json",
        "258135af5b19e1c7f25ea95432e699e275053b381b04c5915054467cca8abab3", 1_000_000,
    ),
)


def _download_verified(directory: Path, repository: str, commit: str, filename: str,
                       expected_hash: str, max_bytes: int) -> dict:
    from urllib.error import HTTPError, URLError

    url = f"https://huggingface.co/{repository}/resolve/{commit}/{filename}"
    target = directory / filename
    target.parent.mkdir(parents=True, exist_ok=True)
    digest = hashlib.sha256()
    size = 0
    request = urllib.request.Request(
        url, headers={"User-Agent": "PartyOps-RC1-CI-ReadOnly-ModelValidation/1.0"}
    )
    try:
        with urllib.request.urlopen(request, timeout=90) as source, target.open("xb") as destination:
            while chunk := source.read(1_048_576):
                size += len(chunk)
                if size > max_bytes:
                    raise ValueError(f"BGE_ASSET_EXCEEDS_SIZE_CAP:{filename}")
                digest.update(chunk)
                destination.write(chunk)
    except (OSError, URLError, HTTPError, ValueError):
        target.unlink(missing_ok=True)
        raise
    actual_hash = digest.hexdigest()
    if actual_hash != expected_hash or not size:
        target.unlink(missing_ok=True)
        raise ValueError(f"BGE_ASSET_DIGEST_INVALID:{filename}")
    return {
        "source_repository": repository,
        "source_commit": commit,
        "filename": filename,
        "size_bytes": size,
        "sha256": actual_hash,
    }


def main() -> int:
    if os.name != "nt" or platform.machine().lower() not in {"arm64", "aarch64"}:
        raise RuntimeError("BGE_NATIVE_WINDOWS_ARM64_REQUIRED")
    import numpy as np
    import onnxruntime as ort
    from tokenizers import __version__ as tokenizer_version
    from probe_arm64_partyops_embedding_runtime import _load_unchanged_runtime

    expected_versions = {"onnxruntime": "1.24.2", "numpy": "2.3.5", "tokenizers": "0.23.3"}
    versions = {"onnxruntime": ort.__version__, "numpy": np.__version__, "tokenizers": tokenizer_version}
    if versions != expected_versions:
        raise RuntimeError(f"BGE_CANDIDATE_RUNTIME_VERSION_MISMATCH:{versions}")

    partyops_root = Path(__file__).resolve().parents[1]
    actual_source = partyops_root / "backend" / "app" / "local_ai.py"
    with TemporaryDirectory(prefix="partyops-bge-arm64-") as folder:
        root = Path(folder)
        sources = [_download_verified(root, *asset) for asset in ASSETS]
        Runtime, source_digest = _load_unchanged_runtime(actual_source, root)

        manifest = {
            "components": {
                "embedding": {
                    "model_file": "onnx/model.onnx",
                    "tokenizer_file": "tokenizer.json",
                    "dimension": 512,
                    "max_length": 128,
                    "pooling": "cls",
                    "query_prefix": "为这个句子生成表示以用于检索相关文章：",
                }
            }
        }
        pack = SimpleNamespace(id="bge-real-third-party-export-ci-only", manifest=manifest)
        engine = Runtime()
        phrases = ["支部党员大会会议记录", "支部党员大会会议记录", "本周食堂菜单"]
        raw = engine.encode(pack, phrases)
        if len(raw) != len(phrases):
            raise AssertionError("BGE_OUTPUT_BATCH_COUNT")
        vectors = [np.frombuffer(item, dtype=np.float32) for item in raw]
        if any(vec.shape != (512,) or not np.all(np.isfinite(vec)) for vec in vectors):
            raise AssertionError("BGE_OUTPUT_SHAPE_OR_FINITE")
        norms = [float(np.linalg.norm(vec)) for vec in vectors]
        if any(not math.isfinite(value) or abs(value - 1.0) > 1e-4 for value in norms):
            raise AssertionError("BGE_OUTPUT_NOT_NORMALIZED")
        same = float(np.dot(vectors[0], vectors[1]))
        different = float(np.dot(vectors[0], vectors[2]))
        if same < 0.9999 or different >= 0.999:
            raise AssertionError(f"BGE_RETRIEVAL_SANITY_CHECK:{same:.6f}/{different:.6f}")
        query = np.frombuffer(engine.encode(pack, ["支部党员大会会议记录"], is_query=True)[0], dtype=np.float32)
        if query.shape != (512,) or not np.all(np.isfinite(query)):
            raise AssertionError("BGE_QUERY_OUTPUT_INVALID")
        if not np.isclose(np.linalg.norm(query), 1.0, atol=1e-4):
            raise AssertionError("BGE_QUERY_NOT_NORMALIZED")
        engine.unload()
        if engine.loaded_for(pack.id):
            raise AssertionError("BGE_UNLOAD_FAILED")

        evidence = {
            "schema_version": 1,
            "status": "PASS",
            "scope": "REAL_BGE_ONNX_THIRD_PARTY_EXPORT_AST_ISOLATED_PARTYOPS_EMBEDDING",
            "source_commit": os.environ.get("GITHUB_SHA", ""),
            "actual_partyops_source_sha256": source_digest,
            "model_repository_commit": ONNX_COMMIT,
            "upstream_tokenizer_commit": TOKENIZER_COMMIT,
            "files": sources,
            "runtime_versions": versions,
            "native_architecture": platform.machine(),
            "embedding_dimensions": 512,
            "same_text_cosine": same,
            "different_text_cosine": different,
            "query_prefix_checked": True,
            "real_model_weights_executed": True,
            "production_signed_modelpack_tested": False,
            "equivalent_to_signed_rc6_model": False,
            "application_imported": False,
            "partyops_app_installed": False,
            "release_ready": False,
            "wps_modified": False,
            "production_lock_modified": False,
        }

    output = Path("artifacts/arm64-real-bge/evidence.json")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("ARM64_REAL_BGE_THIRD_PARTY_EXPORT_SOURCE_RUNTIME_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
