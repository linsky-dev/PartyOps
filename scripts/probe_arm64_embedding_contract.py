"""Isolated Windows ARM64 test for PartyOps-like tokenizer + ONNX input contract.

Synthetic inputs only; NOT a BGE model, production acceptance or dependency update.
"""

from __future__ import annotations

import json
import os
import platform
import sys
from pathlib import Path
from tempfile import TemporaryDirectory


def main() -> int:
    import numpy as np
    import onnx
    import onnxruntime as ort
    import tokenizers
    from onnx import TensorProto, helper
    from tokenizers import Tokenizer, models, pre_tokenizers

    if os.name != "nt" or platform.machine().lower() not in ("arm64", "aarch64"):
        raise RuntimeError("Native Windows ARM64 Python is required")
    if tokenizers.__version__ != "0.23.3":
        raise RuntimeError(f"unexpected tokenizer version: {tokenizers.__version__}")

    with TemporaryDirectory() as directory:
        tokenizer_file = Path(directory) / "tokenizer.json"
        writer = Tokenizer(models.WordLevel({"[UNK]": 0, "hello": 1, "world": 2}, unk_token="[UNK]"))
        writer.pre_tokenizer = pre_tokenizers.Whitespace()
        writer.save(str(tokenizer_file))
        tokenizer = Tokenizer.from_file(str(tokenizer_file))
        rows = tokenizer.encode_batch(["hello world"])
        assert len(rows) == 1 and rows[0].ids == [1, 2]
        assert rows[0].attention_mask == [1, 1]
        assert rows[0].type_ids == [0, 0]

    inputs = [
        helper.make_tensor_value_info(name, TensorProto.INT64, [1, 2])
        for name in ("input_ids", "attention_mask", "token_type_ids")
    ]
    output_info = helper.make_tensor_value_info("synthetic_embedding", TensorProto.FLOAT, [1, 2])
    graph = helper.make_graph(
        [helper.make_node("Cast", inputs=["input_ids"], outputs=["synthetic_embedding"], to=TensorProto.FLOAT)],
        "partyops-tokenizer-contract",
        inputs,
        [output_info],
    )
    model = helper.make_model(
        graph,
        producer_name="PartyOps test-only ARM64 embedding contract",
        opset_imports=[helper.make_opsetid("", 13)],
    )
    model.ir_version = 9
    onnx.checker.check_model(model)
    session = ort.InferenceSession(model.SerializeToString(), providers=["CPUExecutionProvider"])
    possible_inputs = {item.name for item in session.get_inputs()}
    tensors = {
        "input_ids": np.asarray([rows[0].ids], dtype=np.int64),
        "attention_mask": np.asarray([rows[0].attention_mask], dtype=np.int64),
        "token_type_ids": np.asarray([rows[0].type_ids], dtype=np.int64),
    }
    result = session.run(None, {key: value for key, value in tensors.items() if key in possible_inputs})[0]
    assert result.shape == (1, 2)
    np.testing.assert_array_equal(result, np.asarray([[1.0, 2.0]], dtype=np.float32))
    embedding = result / np.maximum(np.linalg.norm(result, axis=1, keepdims=True), 1e-12)
    np.testing.assert_allclose(np.linalg.norm(embedding, axis=1), np.ones(1), rtol=1e-6)

    evidence = {
        "schema_version": 1,
        "status": "PASS",
        "scope": "SYNTHETIC_EMBEDDING_INPUT_CONTRACT_ONLY",
        "result": "SYNTHETIC_TOKENIZER_AND_EMBEDDING_CONTRACT_PASS",
        "source_commit": os.environ.get("GITHUB_SHA", ""),
        "python": sys.version.split()[0],
        "architecture": platform.machine(),
        "versions": {
            "onnxruntime": ort.__version__,
            "numpy": np.__version__,
            "onnx": onnx.__version__,
            "tokenizers": tokenizers.__version__,
        },
        "real_bge_model_tested": False,
        "real_partyops_inference_verified": False,
        "partyops_app_installed": False,
        "release_ready": False,
    }
    path = Path("artifacts/arm64-onnx-candidate/embedding-contract.json")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(evidence["result"])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
