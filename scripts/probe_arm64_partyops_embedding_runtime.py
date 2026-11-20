"""Run unchanged PartyOps EmbeddingRuntime methods on native Windows ARM64.

AST-isolated actual source class; dependencies injected only into the test
namespace, not the production module. Uses a synthetic tiny ONNX graph, not BGE.
No WPS, production lock, real model pack, or customer data is touched.
"""

from __future__ import annotations

import ast
import hashlib
import json
import os
import platform
import sys
import threading
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace


class SyntheticProblem(Exception):
    def __init__(self, status: int, code: str, title: str, detail: str):
        super().__init__(detail)
        self.status = status
        self.code = code


def _load_unchanged_runtime(source: Path, components: Path):
    source_text = source.read_text(encoding="utf-8")
    module = ast.parse(source_text)
    classes = [item for item in module.body if isinstance(item, ast.ClassDef) and item.name == "EmbeddingRuntime"]
    if len(classes) != 1:
        raise RuntimeError("Unexpected EmbeddingRuntime class definition")
    cls = classes[0]
    required = {"encode", "unload", "loaded_for"}
    if not required.issubset({item.name for item in cls.body if isinstance(item, ast.FunctionDef)}):
        raise RuntimeError("PartyOps runtime method contract changed")
    program = ast.fix_missing_locations(
        ast.Module(
            body=[ast.ImportFrom(module="__future__", names=[ast.alias(name="annotations")], level=0), cls],
            type_ignores=[],
        )
    )
    namespace = {
        "threading": threading,
        "ProblemException": SyntheticProblem,
        "get_settings": lambda: SimpleNamespace(local_ai_max_threads=2),
        "_component_file": lambda pack, capability, role: components / pack.manifest["components"][capability][role],
    }
    exec(compile(program, str(source), "exec"), namespace)
    return namespace["EmbeddingRuntime"], hashlib.sha256(source.read_bytes()).hexdigest()


def _create_synthetic_components(directory: Path) -> None:
    import onnx
    from onnx import TensorProto, helper
    from tokenizers import Tokenizer, models, pre_tokenizers

    tokenizer = Tokenizer(
        models.WordLevel({"[UNK]": 0, "hello": 1, "world": 2, "prefix": 3}, unk_token="[UNK]")
    )
    tokenizer.pre_tokenizer = pre_tokenizers.Whitespace()
    tokenizer.save(str(directory / "tokenizer.json"))
    graph = helper.make_graph(
        [
            helper.make_node("Cast", ["input_ids"], ["float_ids"], to=TensorProto.FLOAT),
            helper.make_node("Cast", ["attention_mask"], ["float_mask"], to=TensorProto.FLOAT),
            helper.make_node("Unsqueeze", ["float_ids", "axes"], ["expanded_ids"]),
            helper.make_node("Unsqueeze", ["float_mask", "axes"], ["expanded_mask"]),
            helper.make_node("Concat", ["expanded_ids", "expanded_mask"], ["synthetic_hidden"], axis=2),
        ],
        "partyops-arm64-embedding-runtime-source-contract",
        [
            helper.make_tensor_value_info("input_ids", TensorProto.INT64, ["batch", "sequence"]),
            helper.make_tensor_value_info("attention_mask", TensorProto.INT64, ["batch", "sequence"]),
            helper.make_tensor_value_info("token_type_ids", TensorProto.INT64, ["batch", "sequence"]),
        ],
        [helper.make_tensor_value_info("synthetic_hidden", TensorProto.FLOAT, ["batch", "sequence", 2])],
        initializer=[helper.make_tensor("axes", TensorProto.INT64, [1], [2])],
    )
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 13)])
    model.ir_version = 9
    onnx.checker.check_model(model)
    (directory / "model.onnx").write_bytes(model.SerializeToString())


def main() -> int:
    if os.name != "nt" or platform.machine().lower() not in ("arm64", "aarch64"):
        raise RuntimeError("Native Windows ARM64 Python is required")
    import numpy as np
    import onnxruntime as ort
    from tokenizers import __version__ as tokenizer_version

    source = Path(__file__).resolve().parents[1] / "backend" / "app" / "local_ai.py"
    with TemporaryDirectory(prefix="partyops-arm64-embed-") as directory:
        root = Path(directory)
        _create_synthetic_components(root)
        Runtime, source_hash = _load_unchanged_runtime(source, root)
        component = {
            "model_file": "model.onnx",
            "tokenizer_file": "tokenizer.json",
            "query_prefix": "prefix ",
            "pooling": "cls",
            "dimension": 2,
            "max_length": 32,
        }
        pack = SimpleNamespace(id="synthetic-cls", manifest={"components": {"embedding": component}})
        engine = Runtime()
        assert not engine.loaded_for(pack.id)
        vectors = engine.encode(pack, ["hello world", "hello"])
        assert len(vectors) == 2
        first = np.frombuffer(vectors[0], dtype=np.float32)
        assert first.shape == (2,)
        np.testing.assert_allclose(first, np.array([1, 1], np.float32) / np.sqrt(2), rtol=1e-6)
        assert engine.loaded_for(pack.id)
        prefix_result = engine.encode(pack, ["hello world"], is_query=True)
        prefix_vector = np.frombuffer(prefix_result[0], dtype=np.float32)
        np.testing.assert_allclose(
            prefix_vector, np.array([3, 1], np.float32) / np.sqrt(10), rtol=1e-6
        )
        # Same synthetic model, new pack identity and actual mean pooling path.
        component["pooling"] = "mean"
        pack.id = "synthetic-mean"
        mean = engine.encode(pack, ["hello world", "hello"])
        np.testing.assert_allclose(
            np.frombuffer(mean[0], dtype=np.float32),
            np.array([1.5, 1], np.float32) / np.sqrt(3.25),
            rtol=1e-6,
        )
        np.testing.assert_allclose(
            np.frombuffer(mean[1], dtype=np.float32),
            np.array([1, 1], np.float32) / np.sqrt(2),
            rtol=1e-6,
        )
        component["dimension"] = 3
        try:
            engine.encode(pack, ["hello world"])
        except SyntheticProblem as exc:
            assert exc.code == "MODEL_DIMENSION_MISMATCH"
        else:
            raise AssertionError("Unexpected dimension mismatch acceptance")
        component["dimension"] = 2
        component["pooling"] = "unknown"
        try:
            engine.encode(pack, ["hello world"])
        except SyntheticProblem as exc:
            assert exc.code == "MODEL_POOLING_INVALID"
        else:
            raise AssertionError("Unexpected invalid pooling acceptance")
        engine.unload()
        assert not engine.loaded_for(pack.id)

    evidence = {
        "schema_version": 1,
        "status": "PASS",
        "scope": "UNCHANGED_EMBEDDING_RUNTIME_CLASS_AST_ISOLATED_SYNTHETIC_ONNX",
        "source_commit": os.getenv("GITHUB_SHA", ""),
        "local_ai_source_sha256": source_hash,
        "native_architecture": platform.machine(),
        "python": sys.version.split()[0],
        "onnxruntime_version": ort.__version__,
        "tokenizers_version": tokenizer_version,
        "cases": [
            "actual_class_cls_pooling",
            "actual_class_mean_pooling_with_padding",
            "actual_class_query_prefix",
            "actual_class_pack_identity_reload",
            "actual_class_dimension_rejection",
            "actual_class_invalid_pooling_rejection",
            "actual_class_unload",
        ],
        "full_app_imported": False,
        "real_bge_model_tested": False,
        "real_partyops_modelpack_tested": False,
        "production_lock_modified": False,
        "wps_modified": False,
        "release_ready": False,
    }
    output = Path("artifacts/arm64-onnx-candidate/partyops-embedding-runtime.json")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(evidence, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("PARTYOPS_ARM64_SYNTHETIC_ACTUAL_EMBEDDING_RUNTIME_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
