#!/usr/bin/env python3
"""冻结目标平台真实 WPS 排版证据，供安装包自检与发布门禁使用。"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any

FEATURES = ["format", "replace", "redheader", "rename", "convert", "pdf-to-word"]
EXPECTED_CASES = [
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
SOURCE_SNAPSHOT_SHA256 = "15c21b886f6a958fb61a3b106266b446a2b959b0085510015eeb790efaa770d3"
SOURCE_SNAPSHOT_FILES = 898


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_json(path: Path, label: str) -> dict[str, Any]:
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise RuntimeError(f"[{label}_INVALID] {path}") from exc
    if not isinstance(payload, dict):
        raise TypeError(f"[{label}_INVALID] {path}")
    return payload


def _require_wps_cases(cases: object) -> list[dict[str, Any]]:
    if not isinstance(cases, list) or len(cases) != len(EXPECTED_CASES):
        raise RuntimeError("[FORMATTER_FEATURE_EVIDENCE_INVALID] 六类功能场景数量不完整。")
    values = [item for item in cases if isinstance(item, dict)]
    if len(values) != len(cases) or [item.get("case") for item in values] != EXPECTED_CASES:
        raise RuntimeError("[FORMATTER_FEATURE_EVIDENCE_INVALID] 六类功能场景顺序或名称不一致。")
    for item in values:
        if (
            item.get("status") != "passed"
            or item.get("configuration_restored") is not True
            or not isinstance(item.get("outputs"), list)
            or not item["outputs"]
        ):
            raise RuntimeError(
                f"[FORMATTER_FEATURE_EVIDENCE_INVALID] 场景未完整通过：{item.get('case')}。"
            )
        if item.get("case") != "pdf-to-word":
            provider = str(item.get("host", "")).lower()
            if not any(token in provider for token in ("wps", "kingsoft", "金山")):
                raise RuntimeError(
                    "[FORMATTER_FEATURE_PROVIDER_INVALID] "
                    f"场景未证明使用真实 WPS：{item.get('case')}。"
                )
    return values


def verify(
    *,
    root: Path,
    runtime: Path,
    platform_name: str,
    architecture: str,
    parity_path: Path,
    features_path: Path,
    bridge_path: Path | None,
    output: Path,
) -> dict[str, Any]:
    host = runtime / (
        "PartyOps.DocumentFormatter.Host.exe"
        if platform_name == "windows"
        else "partyops-document-formatter-host"
    )
    source_record = read_json(runtime / "source-host.json", "FORMATTER_SOURCE_RECORD")
    parity = read_json(parity_path, "FORMATTER_PARITY_EVIDENCE")
    features = read_json(features_path, "FORMATTER_FEATURE_EVIDENCE")
    source = root / "backend/tests/fixtures/document-formatter-source/input-manual-break.docx"
    golden = root / "backend/tests/fixtures/document-formatter-source/expected-source-formatted.docx"
    if not host.is_file() or not source.is_file() or not golden.is_file():
        raise RuntimeError("[FORMATTER_EVIDENCE_INPUT_MISSING] 宿主或用户金样缺失。")
    host_hash = sha256(host)
    source_hash = sha256(source)
    golden_hash = sha256(golden)
    if source_record.get("host_sha256") != host_hash:
        raise RuntimeError("[FORMATTER_EVIDENCE_HOST_MISMATCH] 来源清单与实测宿主哈希不一致。")
    if (
        source_record.get("source_snapshot_sha256") != SOURCE_SNAPSHOT_SHA256
        or source_record.get("source_snapshot_files") != SOURCE_SNAPSHOT_FILES
    ):
        raise RuntimeError("[FORMATTER_EVIDENCE_SOURCE_MISMATCH] 来源清单未绑定锁定的原排版源码。")

    common = {
        "status": "passed",
        "timezone": "Asia/Shanghai",
        "platform": platform_name,
        "architecture": architecture,
        "provider": "wps",
        "host_sha256": host_hash,
    }
    for label, payload in (("PARITY", parity), ("FEATURE", features)):
        mismatches = [key for key, value in common.items() if payload.get(key) != value]
        if payload.get("schema") != 2 or mismatches:
            fields = (["schema"] if payload.get("schema") != 2 else []) + mismatches
            raise RuntimeError(
                f"[FORMATTER_{label}_EVIDENCE_MISMATCH] 字段不匹配："
                + ", ".join(fields)
            )
    if parity.get("source_sha256") != source_hash or parity.get("golden_sha256") != golden_hash:
        raise RuntimeError("[FORMATTER_PARITY_FIXTURE_MISMATCH] 实测输入或正确金样已变化。")
    pages = parity.get("rendered_pages")
    if (
        parity.get("paragraph_count") != 8
        or not isinstance(parity.get("semantic_signature_sha256"), str)
        or len(parity["semantic_signature_sha256"]) != 64
        or not isinstance(pages, list)
        or len(pages) != 3
        or any(not isinstance(page, dict) or len(str(page.get("pixel_sha256", ""))) != 64 for page in pages)
    ):
        raise RuntimeError("[FORMATTER_PARITY_EVIDENCE_INVALID] 结构或三页像素金样证据不完整。")
    visual = parity.get("visual_comparison")
    if not isinstance(visual, dict) or visual.get("passed") is not True:
        raise RuntimeError("[FORMATTER_PARITY_VISUAL_COMPARISON_INVALID] 渲染页面比较未通过。")
    visual_pages = visual.get("pages")
    if not isinstance(visual_pages, list) or len(visual_pages) != len(pages):
        raise RuntimeError("[FORMATTER_PARITY_VISUAL_COMPARISON_INVALID] 渲染页面明细不完整。")
    for visual_page in visual_pages:
        if not isinstance(visual_page, dict) or visual_page.get("passed") is not True:
            raise RuntimeError("[FORMATTER_PARITY_VISUAL_COMPARISON_INVALID] 页面比较未通过。")
        delta = visual_page.get("mean_abs_channel_delta")
        ratio = visual_page.get("same_channel_ratio")
        # 不强制转换类型：bool/字符串不是测量值；闭区间同时拒绝 NaN、无穷
        # 和负数，避免 IEEE 浮点的单边比较将损坏证据误判为通过。
        if (
            type(delta) not in (int, float)
            or type(ratio) not in (int, float)
            or not 0 <= delta <= 0.10
            or not 0.999 <= ratio <= 1
        ):
            raise RuntimeError(
                "[FORMATTER_PARITY_VISUAL_COMPARISON_INVALID] "
                "跨平台页面差异超过抗锯齿噪声阈值，或测量值无效。"
            )
        if platform_name == "windows" and (
            visual_page.get("raw_equal") is not True or delta != 0 or ratio != 1
        ):
            raise RuntimeError("[FORMATTER_PARITY_VISUAL_COMPARISON_INVALID] Windows 页面必须逐像素一致。")
    cases = _require_wps_cases(features.get("cases"))
    if features.get("case_count") != len(cases):
        raise RuntimeError("[FORMATTER_FEATURE_EVIDENCE_INVALID] 场景计数不一致。")

    bridge: dict[str, Any] | None = None
    native_adapter_files_sha256: dict[str, str] | None = None
    if platform_name != "windows":
        native_files = {
            "word-vtable-map.json": runtime / "word-vtable-map.json",
            "LICENSE-WPS-SDK.txt": runtime / "LICENSE-WPS-SDK.txt",
            "LICENSE-MONO-RUNTIME.txt": runtime / "LICENSE-MONO-RUNTIME.txt",
        }
        if any(not path.is_file() or path.stat().st_size <= 0 for path in native_files.values()):
            raise RuntimeError(
                "[FORMATTER_NATIVE_ADAPTER_FILES_MISSING] WPS 原生适配运行时不完整。"
            )
        native_adapter_files_sha256 = {
            name: sha256(path) for name, path in native_files.items()
        }
        if source_record.get("word_vtable_map_sha256") != native_adapter_files_sha256["word-vtable-map.json"]:
            raise RuntimeError("[FORMATTER_NATIVE_ADAPTER_MAP_MISMATCH] WPS 槽位表与来源清单不一致。")

        # JSAPI 探针是开发诊断，不再是正式运行依赖；若调用方显式提供，仍
        # 校验并记录，避免旧证据被静默接受为真实 RPC 功能测试。
        if bridge_path is not None:
            bridge = read_json(bridge_path, "FORMATTER_BRIDGE_EVIDENCE")
            if (
                bridge.get("schema") != 1
                or bridge.get("passed") is not True
                or bridge.get("platform") != platform_name
                or bridge.get("architecture") != architecture
                or bridge.get("adapter") != "wps-jsapi-source-bridge"
                or bridge.get("character_unit_first_line_indent") != 2
            ):
                raise RuntimeError(
                    "[FORMATTER_BRIDGE_EVIDENCE_INVALID] 可选 WPS JSAPI 诊断证据无效。"
                )

    payload = {
        "schema": 1,
        "status": "passed",
        "timezone": "Asia/Shanghai",
        "verified_at": max(
            str(parity.get("verified_at", "")),
            str(features.get("verified_at", "")),
            str(bridge.get("verified_at", "")) if bridge else "",
        ),
        "platform": platform_name,
        "architecture": architecture,
        "provider": "wps",
        "adapter": source_record.get("adapter"),
        "host_sha256": host_hash,
        "source_sha256": source_hash,
        "golden_sha256": golden_hash,
        "semantic_signature_sha256": parity["semantic_signature_sha256"],
        "rendered_pages": len(pages),
        "feature_cases": len(cases),
        "features": FEATURES,
        "capabilities": 25,
        "parity_evidence_sha256": sha256(parity_path),
        "feature_evidence_sha256": sha256(features_path),
    }
    if bridge is not None and bridge_path is not None:
        payload["bridge_probe_evidence_sha256"] = sha256(bridge_path)
    if native_adapter_files_sha256 is not None:
        payload["native_adapter_files_sha256"] = native_adapter_files_sha256
    output.parent.mkdir(parents=True, exist_ok=True)
    # Python 3.8 的 Path.write_text 尚不支持 newline 参数；Win7 构建链
    # 固定使用 Python 3.8，因此通过 Path.open 显式统一为 LF。
    with output.open("w", encoding="utf-8", newline="\n") as stream:
        stream.write(json.dumps(payload, ensure_ascii=False, indent=2) + "\n")
    return payload


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--runtime", type=Path, required=True)
    parser.add_argument("--platform", choices=("windows", "linux", "macos"), required=True)
    parser.add_argument("--architecture", required=True)
    parser.add_argument("--parity-evidence", type=Path, required=True)
    parser.add_argument("--features-evidence", type=Path, required=True)
    parser.add_argument("--bridge-evidence", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        payload = verify(
            root=args.root.resolve(),
            runtime=args.runtime.resolve(),
            platform_name=args.platform,
            architecture=args.architecture,
            parity_path=args.parity_evidence.resolve(),
            features_path=args.features_evidence.resolve(),
            bridge_path=(args.bridge_evidence.resolve() if args.bridge_evidence else None),
            output=args.output.resolve(),
        )
    except (RuntimeError, TypeError) as exc:
        print(str(exc))
        return 2
    print(json.dumps(payload, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
