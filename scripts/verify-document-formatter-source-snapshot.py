#!/usr/bin/env python3
"""校验内嵌排版源码与用户指定的上游功能源码完全一致。

校验范围只包含构建与功能所需的源码、测试、锁定依赖和资源。构建产物、日志、
证书及开发机输出不属于源码快照。文本统一换行为 LF 后计算摘要，避免 Git 在
Windows 上转换换行符造成误报；任何业务字符变化、文件增删或二进制变化都会失败。
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

EXPECTED_FILE_COUNT = 898
EXPECTED_AGGREGATE_SHA256 = "15c21b886f6a958fb61a3b106266b446a2b959b0085510015eeb790efaa770d3"
INCLUDED_ROOTS = frozenset({"src", "assets", "lib", "tests", "tools"})
ROOT_SUFFIXES = frozenset({".sln", ".props", ".targets", ".config"})
EXCLUDED_PARTS = frozenset({"bin", "obj", ".vs", "输出", "packages"})
TEXT_SUFFIXES = frozenset(
    {
        ".cs",
        ".csproj",
        ".sln",
        ".props",
        ".targets",
        ".config",
        ".xml",
        ".json",
        ".md",
        ".txt",
        ".ps1",
    }
)


def _included(relative: Path) -> bool:
    return (
        relative.parts
        and not any(part in EXCLUDED_PARTS for part in relative.parts)
        and (relative.parts[0] in INCLUDED_ROOTS or (len(relative.parts) == 1 and relative.suffix.lower() in ROOT_SUFFIXES))
    )


def _content_sha256(path: Path) -> str:
    content = path.read_bytes()
    if path.suffix.lower() in TEXT_SUFFIXES:
        text = content.decode("utf-8").replace("\r\n", "\n").replace("\r", "\n")
        content = text.encode("utf-8")
    return hashlib.sha256(content).hexdigest()


def fingerprint(source: Path) -> tuple[int, str]:
    source = source.resolve()
    if not source.is_dir():
        raise RuntimeError(f"[FORMATTER_SOURCE_MISSING] 排版源码快照不存在：{source}")
    records: list[tuple[str, str]] = []
    for path in source.rglob("*"):
        if not path.is_file():
            continue
        relative = path.relative_to(source)
        if _included(relative):
            records.append((relative.as_posix(), _content_sha256(path)))
    records.sort()
    joined = "".join(f"{name}\0{digest}\n" for name, digest in records).encode("utf-8")
    return len(records), hashlib.sha256(joined).hexdigest()


def verify(source: Path) -> dict[str, object]:
    file_count, aggregate = fingerprint(source)
    if file_count != EXPECTED_FILE_COUNT or aggregate != EXPECTED_AGGREGATE_SHA256:
        raise RuntimeError(
            "[FORMATTER_SOURCE_DIVERGED] 内嵌排版源码已偏离用户指定上游；"
            f"文件数={file_count}/{EXPECTED_FILE_COUNT}，"
            f"聚合摘要={aggregate}/{EXPECTED_AGGREGATE_SHA256}。"
        )
    return {
        "schema": 1,
        "passed": True,
        "source": "PartyOps.DocumentFormatter.Source",
        "file_count": file_count,
        "aggregate_sha256": aggregate,
        "text_line_endings_normalized": True,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = verify(args.source)
    except (OSError, UnicodeError, RuntimeError) as exc:
        print(str(exc))
        return 2
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
