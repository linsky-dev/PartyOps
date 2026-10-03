"""下载成品金样校验器的拒绝路径；合成结果仅存在 pytest 临时目录。"""
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace

import pytest

SCRIPT = Path(__file__).resolve().parents[1] / "guest/verify-installed-formatter-output.py"


@pytest.mark.parametrize("scenario", ["passed", "platform", "status", "scope", "count", "hash", "path", "semantic", "visual"])
def test_download_golden_boundary(tmp_path, monkeypatch, scenario):
    spec = importlib.util.spec_from_file_location("download_probe", SCRIPT)
    probe = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(probe)
    monkeypatch.setattr(probe.sys, "platform", "win32" if scenario == "platform" else "linux")
    root = tmp_path / "api"
    root.mkdir()
    for name in ("golden", "result1", "result2"):
        (root / name).write_bytes(b"synthetic")
    outside = tmp_path / "outside"
    outside.write_bytes(b"synthetic")
    record = {"status": "failed" if scenario == "status" else "passed",
              "scope": "wrong" if scenario == "scope" else "installed-program-http-batch-formatter-only",
              "outputs": [{"filename": "../outside" if scenario == "path" else "result1", "sha256": "bad" if scenario == "hash" else "sha"},
                          {"filename": "result2", "sha256": "sha"}]}
    if scenario == "count":
        record["outputs"].pop()
    evidence = root / "evidence.json"
    evidence.write_text(json.dumps(record), encoding="utf-8")
    checks = []

    def signature(path):
        return {"paragraphs": ["different" if scenario == "semantic" and path.name == "result1" else "same"]}

    oracle = SimpleNamespace(
        _sha256=lambda path: "sha", _document_signature=signature,
        _semantic_equivalence_signature=lambda value: value,
        _require_contract=lambda value: checks.append(value),
        _render_page_hashes=lambda *args: ["synthetic-page"],
        _visual_page_comparison=lambda *args, **kwargs: {"passed": scenario != "visual"},
    )
    monkeypatch.setattr(probe.importlib.util, "module_from_spec", lambda spec: oracle)
    monkeypatch.setattr(probe.importlib.util, "spec_from_file_location", lambda *args: SimpleNamespace(loader=SimpleNamespace(exec_module=lambda module: None)))
    if scenario == "passed":
        result = probe.verify(evidence, root / "golden", root / "oracle.py", root / "soffice")
        assert result["status"] == "passed" and len(result["outputs"]) == 2
        assert result["runtime_environment_passed"] is False and len(checks) == 2
    else:
        with pytest.raises(RuntimeError):
            probe.verify(evidence, root / "golden", root / "oracle.py", root / "soffice")
    report = root / "download-golden-review/download-golden-evidence.json"
    if report.exists():
        assert json.loads(report.read_text(encoding="utf-8"))["status"] == ("passed" if scenario == "passed" else "failed")
    assert (root / "result1").read_bytes() == b"synthetic"


def test_cli_arguments(monkeypatch, capsys):
    spec = importlib.util.spec_from_file_location("download_cli", SCRIPT)
    probe = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(probe)
    monkeypatch.setattr(probe.sys, "argv", [str(SCRIPT), "--evidence", "e", "--golden", "g", "--oracle", "o", "--office", "f"])
    received = []
    monkeypatch.setattr(probe, "verify", lambda *args: received.append(args) or {"synthetic": True})
    probe.main()
    assert received == [(Path("e"), Path("g"), Path("o"), Path("f"))]
    assert json.loads(capsys.readouterr().out) == {"synthetic": True}
