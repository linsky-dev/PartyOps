"""Latest isolated ARM64 ONNX Runtime wheel against unchanged PartyOps AI."""

from __future__ import annotations

import importlib.metadata
import json
import os
import platform
import sys
from pathlib import Path

EXPECTED_ORT = "1.32.0"
EXPECTED_SHA256 = "64d394c69d561af3efd4d4f49dcac8f11d0ce135a20701c16d53b74056cd4cf8"


def main() -> int:
    if os.name != "nt" or platform.machine().lower() not in {"arm64", "aarch64"}:
        raise RuntimeError("native Windows ARM64 Python required")

    import numpy
    import onnx
    import onnxruntime
    import tokenizers

    actual = {
        "onnxruntime": onnxruntime.__version__,
        "numpy": numpy.__version__,
        "onnx": onnx.__version__,
        "tokenizers": tokenizers.__version__,
    }
    expected = {
        "onnxruntime": EXPECTED_ORT,
        "numpy": "2.3.5",
        "onnx": "1.23.2",
        "tokenizers": "0.23.3",
    }
    if actual != expected or importlib.metadata.version("onnxruntime") != EXPECTED_ORT:
        raise RuntimeError(f"candidate dependencies differ: {actual}")

    path = Path("artifacts/arm64-onnx-candidate")
    contract = json.loads((path / "embedding-contract.json").read_text(encoding="utf-8"))
    runtime = json.loads((path / "partyops-embedding-runtime.json").read_text(encoding="utf-8"))
    if (
        contract.get("status") != "PASS"
        or contract.get("result") != "SYNTHETIC_TOKENIZER_AND_EMBEDDING_CONTRACT_PASS"
        or contract.get("versions") != expected
        or runtime.get("status") != "PASS"
        or runtime.get("onnxruntime_version") != EXPECTED_ORT
        or len(runtime.get("cases", [])) != 7
    ):
        raise RuntimeError("original PartyOps embedding runtime or synthetic contract failed")

    result = {
        "schema_version": 1,
        "status": "PASS",
        "scope": "NATIVE_WINDOWS_ARM64_LATEST_ONNX_SYNTHETIC_ONLY",
        "wheel_sha256": EXPECTED_SHA256,
        "candidate_versions": actual,
        "source_commit": os.environ.get("GITHUB_SHA", ""),
        "python": sys.version.split()[0],
        "architecture": platform.machine(),
        "unchanged_partyops_embedding_runtime": runtime,
        "synthetic_tokenizer_contract": contract,
        "real_bge_weight_tested": False,
        "full_partyops_app_installed": False,
        "full_locked_dependency_closure_tested": False,
        "production_lock_modified": False,
        "wps_modified": False,
        "signed_or_released": False,
        "release_ready": False,
    }
    output = Path("artifacts/arm64-onnx-latest/latest-candidate.json")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("PARTYOPS_NATIVE_ARM64_ONNX_132_ORIGINAL_EMBEDDING_RUNTIME_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
