#!/usr/bin/env python3
"""验证 Linux/macOS 原源码排版适配宿主的来源清单与离线自检。"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import tempfile
from datetime import datetime, timedelta
from pathlib import Path, PurePosixPath
from typing import Any

FEATURES = ["format", "replace", "redheader", "rename", "convert", "pdf-to-word"]
SOURCE_SNAPSHOT_SHA256 = "15c21b886f6a958fb61a3b106266b446a2b959b0085510015eeb790efaa770d3"
SOURCE_SNAPSHOT_FILES = 898
WORD_VTABLE_MAP_SHA256 = "871fa605d294620b273f19eff20c587e742af6d46903a16216c69913675b5f41"
WPS_SDK_HEADER_SHA256 = "4d0529c076f8f36ce49301982e0c2bb46cdcc4087c3e9945a9b57d649fe26791"
BUNDLED_MSCORLIB_RECORD = re.compile(
    r"^Mono: Assembly Loader loaded assembly from bundle: '([^'\r\n]+)'\.$"
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_json(path: Path, label: str) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise RuntimeError(f"[{label}_INVALID] {path}") from exc
    if not isinstance(value, dict):
        raise TypeError(f"[{label}_INVALID] {path}")
    return value


def has_bundled_mscorlib(trace: str) -> bool:
    """逐行解析 Mono bundle 记录，只接受裸文件名或 Unix 绝对路径。"""
    for line in trace.splitlines():
        match = BUNDLED_MSCORLIB_RECORD.fullmatch(line)
        if match is None:
            continue
        assembly_path = match.group(1)
        if assembly_path == "mscorlib.dll" or (
            assembly_path.startswith("/")
            and PurePosixPath(assembly_path).name == "mscorlib.dll"
        ):
            return True
    return False


def validate(runtime: Path, platform_name: str, architecture: str) -> dict[str, Any]:
    runtime = runtime.resolve()
    host = runtime / "partyops-document-formatter-host"
    record_path = runtime / "source-host.json"
    vtable_map = runtime / "word-vtable-map.json"
    wps_license = runtime / "LICENSE-WPS-SDK.txt"
    mono_license = runtime / "LICENSE-MONO-RUNTIME.txt"
    if (
        not runtime.is_dir()
        or host.is_symlink()
        or not host.is_file()
        or host.stat().st_size <= 0
        or not record_path.is_file()
        or not vtable_map.is_file()
        or not wps_license.is_file()
        or not mono_license.is_file()
    ):
        raise RuntimeError("[FORMATTER_RUNTIME_INCOMPLETE] 本机 WPS 原源码宿主不完整。")

    record = read_json(record_path, "FORMATTER_SOURCE_RECORD")
    expected = {
        "schema": 2,
        "platform": platform_name,
        "architecture": architecture,
        "adapter": "wps-native-source-adapter",
        "source_project": "PartyOps.DocumentFormatter.AddIn",
        "source_snapshot_sha256": SOURCE_SNAPSHOT_SHA256,
        "source_snapshot_files": SOURCE_SNAPSHOT_FILES,
        "timezone": "Asia/Shanghai",
        "features": FEATURES,
        "capabilities": 25,
        "host_sha256": sha256(host),
        "word_vtable_map_sha256": WORD_VTABLE_MAP_SHA256,
        "wps_sdk_header_sha256": WPS_SDK_HEADER_SHA256,
        "wps_sdk_matched_methods": 528,
        "wps_sdk_mismatched_methods": 0,
    }
    mismatches = [
        key for key, expected_value in expected.items() if record.get(key) != expected_value
    ]
    if mismatches:
        raise RuntimeError(
            "[FORMATTER_SOURCE_RECORD_MISMATCH] 来源清单字段不匹配："
            + ", ".join(mismatches)
        )
    if sha256(vtable_map) != WORD_VTABLE_MAP_SHA256:
        raise RuntimeError("[FORMATTER_VTABLE_MAP_MISMATCH] WPS 槽位表已变化。")
    try:
        built_at = datetime.fromisoformat(str(record.get("built_at", "")))
    except ValueError as exc:
        raise RuntimeError("[FORMATTER_SOURCE_RECORD_TIME_INVALID] built_at 无效。") from exc
    if built_at.utcoffset() != timedelta(hours=8):
        raise RuntimeError("[FORMATTER_SOURCE_RECORD_TIME_INVALID] built_at 必须使用北京时间 +08:00。")
    runtime_label = record.get("native_bundle_runtime")
    if not isinstance(runtime_label, str) or "Mono JIT compiler version" not in runtime_label:
        raise RuntimeError("[FORMATTER_SOURCE_RECORD_RUNTIME_INVALID] 缺少 Mono 原生封装版本。")

    with tempfile.TemporaryDirectory(prefix="partyops-formatter-runtime-") as work:
        output = Path(work) / "self-test.json"
        # 开启程序集加载追踪，拒绝“依赖构建机 Mono 才通过”的假阳性。
        environment = {key: value for key, value in os.environ.items() if not key.startswith("MONO_")}
        environment.update(MONO_LOG_LEVEL="debug", MONO_LOG_MASK="asm")
        try:
            result = subprocess.run(
                [str(host), "--self-test", str(output)],
                check=False,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=30,
                env=environment,
            )
        except (OSError, subprocess.TimeoutExpired) as exc:
            raise RuntimeError("[FORMATTER_RUNTIME_START_FAILED] 宿主无法启动。") from exc
        if result.returncode != 0 or not output.is_file():
            detail = " ".join((result.stderr or result.stdout or "无输出").split())[:800]
            raise RuntimeError(
                f"[FORMATTER_RUNTIME_SELFTEST_FAILED] 退出码 {result.returncode}：{detail}"
            )
        selftest = read_json(output, "FORMATTER_RUNTIME_SELFTEST")
    if (
        selftest.get("schema") != 1
        or selftest.get("passed") is not True
        or selftest.get("engine") != "source-standalone-batch-processor"
        or selftest.get("source_project") != "PartyOps.DocumentFormatter.AddIn"
        or selftest.get("features") != FEATURES
    ):
        raise RuntimeError("[FORMATTER_RUNTIME_SELFTEST_INVALID] 宿主能力结果不完整。")
    trace = result.stdout + result.stderr
    if (
        not has_bundled_mscorlib(trace)
        or "Assembly Loader loaded assembly from location:" in trace
    ):
        raise RuntimeError(
            "[FORMATTER_RUNTIME_ASSEMBLY_NOT_BUNDLED] 基础程序集未由宿主内嵌加载，或仍借用了外部程序集。"
        )
    return {
        "passed": True,
        "platform": platform_name,
        "architecture": architecture,
        "adapter": record["adapter"],
        "host_sha256": expected["host_sha256"],
        "features": len(FEATURES),
        "capabilities": 25,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--runtime", type=Path, required=True)
    parser.add_argument("--platform", choices=("linux", "macos"), required=True)
    parser.add_argument("--architecture", required=True)
    args = parser.parse_args()
    try:
        result = validate(args.runtime, args.platform, args.architecture)
    except (RuntimeError, TypeError) as exc:
        print(str(exc))
        return 2
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
