"""Isolated native Windows ARM64 ONNX CPU experiment; never a production dependency update."""

from __future__ import annotations

import hashlib
import json
import os
import platform
import sys
from pathlib import Path


def main() -> int:
    import numpy as np
    import onnx
    import onnxruntime as ort
    from onnx import TensorProto, helper

    if os.name != "nt" or platform.machine().lower() not in ("arm64", "aarch64"):
        raise RuntimeError("Native Windows ARM64 is mandatory; x64 emulation is not accepted")
    expected = {"onnxruntime": "1.24.2", "numpy": "2.3.5", "onnx": "1.23.2"}
    actual = {"onnxruntime": ort.__version__, "numpy": np.__version__, "onnx": onnx.__version__}
    if actual != expected:
        raise RuntimeError(f"unexpected candidate versions: {actual}")

    x_info = helper.make_tensor_value_info("input", TensorProto.FLOAT, [1, 3])
    y_info = helper.make_tensor_value_info("output", TensorProto.FLOAT, [1, 3])
    graph = helper.make_graph(
        [helper.make_node("Identity", ["input"], ["output"])],
        "partyops-win-arm64-synthetic-identity",
        [x_info],
        [y_info],
    )
    model = helper.make_model(
        graph,
        producer_name="PartyOps RC1 ARM64 compatibility investigation",
        opset_imports=[helper.make_opsetid("", 13)],
    )
    model.ir_version = 9
    onnx.checker.check_model(model)
    payload = model.SerializeToString()
    session = ort.InferenceSession(payload, providers=["CPUExecutionProvider"])
    data = np.array([[1.125, -3.5, 0.0]], dtype=np.float32)
    out = session.run(None, {"input": data})[0]
    np.testing.assert_array_equal(out, data)
    if session.get_providers() != ["CPUExecutionProvider"]:
        raise RuntimeError(f"unexpected execution provider: {session.get_providers()}")

    evidence = {
        "schema_version": 1,
        "status": "PASS",
        "scope": "SYNTHETIC_MODEL_ONLY",
        "product": "PartyOps",
        "source_commit": os.environ.get("GITHUB_SHA", ""),
        "candidate_versions": actual,
        "platform": platform.platform(),
        "machine": platform.machine(),
        "python": sys.version.split()[0],
        "execution_providers": session.get_providers(),
        "onnx_ir": model.ir_version,
        "opset": 13,
        "model_sha256": hashlib.sha256(payload).hexdigest(),
        "result": "SYNTHETIC_IDENTITY_CPU_INFERENCE_PASS",
        "production_lock_modified": False,
        "partyops_models_verified": False,
        "partyops_app_installed": False,
        "release_ready": False,
    }
    location = Path("artifacts/arm64-onnx-candidate/result.json")
    location.parent.mkdir(parents=True, exist_ok=True)
    location.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"result": evidence["result"], "versions": actual}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
