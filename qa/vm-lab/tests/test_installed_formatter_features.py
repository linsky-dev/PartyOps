"""实际功能作业的内容/输入失败不能被记为通过。"""
import hashlib
import importlib.util
import json
from pathlib import Path

import pytest


@pytest.mark.parametrize("scenario", ["input-changed", "failed-job", "wrong-content", "empty-output"])
def test_feature_gate_rejects_failed_actual_outputs(tmp_path, scenario):
    path = Path(__file__).resolve().parents[1] / "guest/installed-formatter-features.py"
    spec = importlib.util.spec_from_file_location("installed_features", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    source = tmp_path / "input.docx"
    source.write_bytes(b"controlled input")
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    manifest = tmp_path / "manifest.json"
    manifest.write_text(json.dumps({"cases": [{"id": "convert-txt", "feature_id": "convert", "source": source.name,
        "sha256": digest, "options": {}, "extension": ".txt", "contains": ["正文内容"]}]}), encoding="utf-8")
    if scenario == "input-changed":
        source.write_bytes(b"changed input")
    output = tmp_path / "output"
    output.mkdir()

    def request(base, route, method="GET", *args):
        if route.endswith("/documents"):
            return {"document_id": "document"}
        if route.endswith("/jobs"):
            return {"id": "job", "state": "failed" if scenario == "failed-job" else "completed",
                    "outputs": [{"id": "output", "filename": "result.txt"}]}
        return b"" if scenario == "empty-output" else "缺少预期业务正文".encode()

    with pytest.raises(RuntimeError):
        module.exercise(request, "http://127.0.0.1", "/session", manifest, output)
    evidence = output / "installed-features-evidence.json"
    if evidence.exists():
        assert json.loads(evidence.read_text())["status"] != "passed"
