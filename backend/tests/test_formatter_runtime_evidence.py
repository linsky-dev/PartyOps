"""目标平台真实 WPS 排版证据冻结门禁。"""

from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
from types import ModuleType

import pytest

ROOT = Path(__file__).resolve().parents[2]


def _module() -> ModuleType:
    spec = importlib.util.spec_from_file_location(
        "partyops_formatter_runtime_evidence",
        ROOT / "scripts" / "verify-formatter-runtime-evidence.py",
    )
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _fixture(tmp_path: Path) -> tuple[Path, Path, Path, Path, Path]:
    root = tmp_path / "repo"
    fixtures = root / "backend/tests/fixtures/document-formatter-source"
    fixtures.mkdir(parents=True)
    source = fixtures / "input-manual-break.docx"
    golden = fixtures / "expected-source-formatted.docx"
    source.write_bytes(b"source")
    golden.write_bytes(b"golden")

    runtime = tmp_path / "runtime"
    runtime.mkdir()
    host = runtime / "partyops-document-formatter-host"
    host.write_bytes(b"native-wps-host")
    (runtime / "word-vtable-map.json").write_text("{}", encoding="utf-8")
    (runtime / "LICENSE-WPS-SDK.txt").write_text("BSD-3-Clause", encoding="utf-8")
    (runtime / "LICENSE-MONO-RUNTIME.txt").write_text("MIT", encoding="utf-8")
    bridge_root = runtime / "wps-bridge"
    (bridge_root / "plugin").mkdir(parents=True)
    (bridge_root / "plugin" / "index.html").write_text("<!doctype html>", encoding="utf-8")
    (bridge_root / "plugin" / "main.js").write_text("// bridge", encoding="utf-8")
    (bridge_root / "LICENSE-WPS-SDK.txt").write_text("BSD-3-Clause", encoding="utf-8")
    (runtime / "source-host.json").write_text(
        json.dumps(
            {
                "host_sha256": _sha256(host),
                "adapter": "wps-native-source-adapter",
                "source_snapshot_sha256": "15c21b886f6a958fb61a3b106266b446a2b959b0085510015eeb790efaa770d3",
                "source_snapshot_files": 898,
                "word_vtable_map_sha256": _sha256(runtime / "word-vtable-map.json"),
            }
        ),
        encoding="utf-8",
    )

    common = {
        "schema": 2,
        "status": "passed",
        "verified_at": "2026-09-01T10:00:00+08:00",
        "timezone": "Asia/Shanghai",
        "platform": "linux",
        "architecture": "amd64",
        "provider": "wps",
        "host_sha256": _sha256(host),
    }
    parity = tmp_path / "parity.json"
    parity.write_text(
        json.dumps(
            {
                **common,
                "source_sha256": _sha256(source),
                "golden_sha256": _sha256(golden),
                "semantic_signature_sha256": "1" * 64,
                "paragraph_count": 8,
                "rendered_pages": [
                    {"pixel_sha256": str(index) * 64} for index in range(1, 4)
                ],
                "visual_comparison": {
                    "passed": True,
                    "pages": [
                        {
                            "page": index,
                            "passed": True,
                            "mean_abs_channel_delta": 0.02,
                            "same_channel_ratio": 0.9995,
                        }
                        for index in range(1, 4)
                    ],
                },
            }
        ),
        encoding="utf-8",
    )
    features = tmp_path / "features.json"
    case_names = [
        "format",
        "replace",
        "redheader",
        "rename",
        "convert-docx",
        "convert-pdf",
        "convert-txt",
        "convert-png-pages",
        "convert-jpg-long",
        "pdf-to-word",
    ]
    features.write_text(
        json.dumps(
            {
                **common,
                "case_count": len(case_names),
                "cases": [
                    {
                        "case": name,
                        "status": "passed",
                        "configuration_restored": True,
                        "host": "WPS Office Linux" if name != "pdf-to-word" else "",
                        "outputs": [{"name": f"{name}.docx"}],
                    }
                    for name in case_names
                ],
            }
        ),
        encoding="utf-8",
    )
    bridge = tmp_path / "bridge.json"
    bridge.write_text(
        json.dumps(
            {
                "schema": 1,
                "passed": True,
                "timezone": "Asia/Shanghai",
                "verified_at": "2026-09-01T10:01:00+08:00",
                "platform": "linux",
                "architecture": "amd64",
                "provider": "wps",
                "adapter": "wps-jsapi-source-bridge",
                "silent": True,
                "character_unit_first_line_indent": 2,
                "input_sha256": _sha256(source),
                "output_sha256": "9" * 64,
            }
        ),
        encoding="utf-8",
    )
    return root, runtime, parity, features, bridge


def test_formatter_runtime_evidence_freezes_real_wps_contract(tmp_path: Path) -> None:
    module = _module()
    root, runtime, parity, features, _bridge = _fixture(tmp_path)
    output = tmp_path / "runtime-evidence.json"
    payload = module.verify(
        root=root,
        runtime=runtime,
        platform_name="linux",
        architecture="amd64",
        parity_path=parity,
        features_path=features,
        bridge_path=None,
        output=output,
    )

    assert payload["status"] == "passed"
    assert payload["provider"] == "wps"
    assert payload["feature_cases"] == 10
    assert payload["rendered_pages"] == 3
    assert set(payload["native_adapter_files_sha256"]) == {
        "word-vtable-map.json",
        "LICENSE-WPS-SDK.txt",
        "LICENSE-MONO-RUNTIME.txt",
    }
    assert json.loads(output.read_text(encoding="utf-8")) == payload


def test_formatter_runtime_evidence_rejects_cross_platform_render_drift(
    tmp_path: Path,
) -> None:
    """跨平台仅允许极小抗锯齿噪声，不能用阈值掩盖真实版式偏差。"""

    module = _module()
    root, runtime, parity, features, _bridge = _fixture(tmp_path)
    payload = json.loads(parity.read_text(encoding="utf-8"))
    payload["visual_comparison"]["pages"][0]["mean_abs_channel_delta"] = 0.11
    parity.write_text(json.dumps(payload), encoding="utf-8")

    with pytest.raises(RuntimeError, match="跨平台页面差异超过抗锯齿噪声阈值"):
        module.verify(
            root=root,
            runtime=runtime,
            platform_name="linux",
            architecture="amd64",
            parity_path=parity,
            features_path=features,
            bridge_path=None,
            output=tmp_path / "render-drift.json",
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("mean_abs_channel_delta", float("nan")),
        ("mean_abs_channel_delta", float("-inf")),
        ("mean_abs_channel_delta", -0.01),
        ("mean_abs_channel_delta", "0.02"),
        ("mean_abs_channel_delta", False),
        ("mean_abs_channel_delta", None),
        ("same_channel_ratio", float("nan")),
        ("same_channel_ratio", float("inf")),
        ("same_channel_ratio", 1.01),
        ("same_channel_ratio", "0.9995"),
        ("same_channel_ratio", True),
        ("same_channel_ratio", None),
    ],
)
def test_formatter_evidence_rejects_invalid_visual_numbers(
    tmp_path: Path, field: str, value: object,
) -> None:
    """异常浮点、越界值和类型混淆不能成为页面通过证据。"""
    module = _module()
    root, runtime, parity, features, _bridge = _fixture(tmp_path)
    payload = json.loads(parity.read_text(encoding="utf-8"))
    payload["visual_comparison"]["pages"][0][field] = value
    parity.write_text(json.dumps(payload), encoding="utf-8")
    output = tmp_path / "invalid-number.json"
    with pytest.raises(RuntimeError, match="FORMATTER_PARITY_VISUAL_COMPARISON_INVALID"):
        module.verify(
            root=root, runtime=runtime, platform_name="linux", architecture="amd64",
            parity_path=parity, features_path=features, bridge_path=None, output=output,
        )
    assert not output.exists()


@pytest.mark.parametrize("raw_equal", [False, True])
def test_formatter_evidence_requires_windows_exact_pixels(
    tmp_path: Path, raw_equal: bool,
) -> None:
    """Windows 既要逐像素一致标志，也要零差异数值，不能信任总 passed。"""
    module = _module()
    root, runtime, parity, features, _bridge = _fixture(tmp_path)
    (runtime / "partyops-document-formatter-host").rename(
        runtime / "PartyOps.DocumentFormatter.Host.exe"
    )
    for path in (parity, features):
        payload = json.loads(path.read_text(encoding="utf-8"))
        payload.update(platform="windows", architecture="x64")
        if path == parity:
            for page in payload["visual_comparison"]["pages"]:
                page["raw_equal"] = raw_equal
        path.write_text(json.dumps(payload), encoding="utf-8")
    with pytest.raises(RuntimeError, match="FORMATTER_PARITY_VISUAL_COMPARISON_INVALID"):
        module.verify(
            root=root, runtime=runtime, platform_name="windows", architecture="x64",
            parity_path=parity, features_path=features, bridge_path=None,
            output=tmp_path / "windows-not-exact.json",
        )


@pytest.mark.parametrize("platform_name", ["linux", "macos", "windows"])
def test_formatter_evidence_accepts_valid_pixel_boundaries(
    tmp_path: Path, platform_name: str,
) -> None:
    """收紧无效输入不能误拒合法边界或 Windows 完全一致的结果。"""
    module = _module()
    root, runtime, parity, features, _bridge = _fixture(tmp_path)
    if platform_name == "windows":
        (runtime / "partyops-document-formatter-host").rename(
            runtime / "PartyOps.DocumentFormatter.Host.exe"
        )
    for path in (parity, features):
        payload = json.loads(path.read_text(encoding="utf-8"))
        payload["platform"] = platform_name
        if path == parity:
            for page in payload["visual_comparison"]["pages"]:
                page.update(
                    raw_equal=platform_name == "windows",
                    mean_abs_channel_delta=0 if platform_name == "windows" else 0.10,
                    same_channel_ratio=1 if platform_name == "windows" else 0.999,
                )
        path.write_text(json.dumps(payload), encoding="utf-8")
    result = module.verify(
        root=root, runtime=runtime, platform_name=platform_name, architecture="amd64",
        parity_path=parity, features_path=features, bridge_path=None,
        output=tmp_path / "boundary.json",
    )
    assert result["status"] == "passed"


@pytest.mark.parametrize("page", [None, {"passed": False}])
def test_formatter_evidence_rejects_missing_or_failed_page(
    tmp_path: Path, page: object,
) -> None:
    module = _module()
    root, runtime, parity, features, _bridge = _fixture(tmp_path)
    payload = json.loads(parity.read_text(encoding="utf-8"))
    payload["visual_comparison"]["pages"][0] = page
    parity.write_text(json.dumps(payload), encoding="utf-8")
    with pytest.raises(RuntimeError, match="FORMATTER_PARITY_VISUAL_COMPARISON_INVALID"):
        module.verify(
            root=root, runtime=runtime, platform_name="linux", architecture="amd64",
            parity_path=parity, features_path=features, bridge_path=None,
            output=tmp_path / "invalid-page.json",
        )


def test_formatter_runtime_evidence_rejects_fake_provider_and_host_drift(
    tmp_path: Path,
) -> None:
    module = _module()
    root, runtime, parity, features, bridge = _fixture(tmp_path)
    feature_payload = json.loads(features.read_text(encoding="utf-8"))
    feature_payload["cases"][0]["host"] = "LibreOffice"
    features.write_text(json.dumps(feature_payload), encoding="utf-8")
    with pytest.raises(RuntimeError, match="未证明使用真实 WPS"):
        module.verify(
            root=root,
            runtime=runtime,
            platform_name="linux",
            architecture="amd64",
            parity_path=parity,
            features_path=features,
            bridge_path=bridge,
            output=tmp_path / "rejected.json",
        )

    root, runtime, parity, features, bridge = _fixture(tmp_path / "drift")
    (runtime / "partyops-document-formatter-host").write_bytes(b"changed")
    with pytest.raises(RuntimeError, match="来源清单与实测宿主哈希不一致"):
        module.verify(
            root=root,
            runtime=runtime,
            platform_name="linux",
            architecture="amd64",
            parity_path=parity,
            features_path=features,
            bridge_path=bridge,
            output=tmp_path / "drift-rejected.json",
        )


def test_formatter_runtime_evidence_rejects_missing_native_files_or_invalid_optional_bridge(
    tmp_path: Path,
) -> None:
    module = _module()
    root, runtime, parity, features, bridge = _fixture(tmp_path)
    (runtime / "word-vtable-map.json").unlink()
    with pytest.raises(RuntimeError, match="FORMATTER_NATIVE_ADAPTER_FILES_MISSING"):
        module.verify(
            root=root,
            runtime=runtime,
            platform_name="linux",
            architecture="amd64",
            parity_path=parity,
            features_path=features,
            bridge_path=None,
            output=tmp_path / "missing.json",
        )
    root, runtime, parity, features, bridge = _fixture(tmp_path / "invalid-bridge")
    payload = json.loads(bridge.read_text(encoding="utf-8"))
    payload["adapter"] = "fake-bridge"
    bridge.write_text(json.dumps(payload), encoding="utf-8")
    with pytest.raises(RuntimeError, match="FORMATTER_BRIDGE_EVIDENCE_INVALID"):
        module.verify(
            root=root,
            runtime=runtime,
            platform_name="linux",
            architecture="amd64",
            parity_path=parity,
            features_path=features,
            bridge_path=bridge,
            output=tmp_path / "visible.json",
        )
