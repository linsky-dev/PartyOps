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
import shutil
import stat
from pathlib import Path

EXPECTED_FILE_COUNT = 898
EXPECTED_AGGREGATE_SHA256 = "7ae0eb67a0cb6a2d4a332cde74adf8977d93ae73541f864f01df214d39fefdf2"
MAC_QUOTE_AGGREGATE_SHA256 = "ac8466edf2513ea8e3fe9e61d3b86fb8d7a72ceb6cce366f2d19b59d9c9be171"
INCLUDED_ROOTS = frozenset({"src", "assets", "lib", "tests", "tools"})
ROOT_SUFFIXES = frozenset({".sln", ".props", ".targets", ".config"})
EXCLUDED_PARTS = frozenset({"bin", "obj", ".vs", "输出", "packages"})
PUNCTUATION_SOURCE = Path(
    "src/PartyOps.DocumentFormatter.AddIn/DocumentRepository/Services/Formatting/EastAsianPunctuationFontService.cs"
)
UPSTREAM_PUNCTUATION_SOURCE = (
    Path(__file__).resolve().parents[1] / "packaging" / "formatter-host" / "upstream" / PUNCTUATION_SOURCE.name
)
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
        ".proj",
        ".py",
        ".resx",
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


def verify(source: Path, profile: str = "upstream") -> dict[str, object]:
    # 仅显式 Mac 构建使用用户授权的四引号字体修订；旧平台默认仍锁上游。
    if profile not in {"upstream", "mac-quotes-tnr"}:
        raise RuntimeError("[FORMATTER_SOURCE_PROFILE_INVALID] 未知源码范围。")
    expected = MAC_QUOTE_AGGREGATE_SHA256 if profile == "mac-quotes-tnr" else EXPECTED_AGGREGATE_SHA256
    file_count, aggregate = fingerprint(source)
    if file_count != EXPECTED_FILE_COUNT or aggregate != expected:
        raise RuntimeError(
            "[FORMATTER_SOURCE_DIVERGED] 内嵌排版源码已偏离用户指定上游；"
            f"文件数={file_count}/{EXPECTED_FILE_COUNT}，"
            f"聚合摘要={aggregate}/{expected}。"
        )
    return {
        "schema": 1,
        "passed": True,
        "source": "PartyOps.DocumentFormatter.Source",
        "file_count": file_count,
        "aggregate_sha256": aggregate,
        "text_line_endings_normalized": True,
    }


def _ignore_build_products(directory: str, names: list[str]) -> set[str]:
    ignored = (EXCLUDED_PARTS | {".git"}).intersection(names)
    for name in set(names) - ignored:
        path = Path(directory) / name
        entry = path.lstat()
        # 避免复制时经符号链接或 Windows 目录连接读取其他工作树。
        attributes = getattr(entry, "st_file_attributes", 0)
        if stat.S_ISLNK(entry.st_mode) or attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise RuntimeError(f"[FORMATTER_SOURCE_LINK] 源码暂存不接受链接：{path}")
    return ignored


def stage_upstream(source: Path, destination: Path) -> dict[str, object]:
    source = source.absolute()
    destination = destination.absolute()
    if source.resolve() != source or destination.resolve() != destination:
        raise RuntimeError("[FORMATTER_SOURCE_LINK] 源码和暂存目录必须使用实际路径。")
    if destination.exists() or destination.is_symlink() or destination.is_relative_to(source):
        raise RuntimeError(f"[FORMATTER_SOURCE_STAGE_INVALID] 上游源码需要独立的全新暂存目录：{destination}")
    shutil.copytree(source, destination, ignore=_ignore_build_products)
    verify(destination, "mac-quotes-tnr")
    (destination / PUNCTUATION_SOURCE).write_bytes(UPSTREAM_PUNCTUATION_SOURCE.read_bytes())
    return verify(destination)


def verify_managed_runtime(runtime: Path, snapshot: dict[str, object]) -> None:
    manifest = json.loads((runtime / "source-host.json").read_text(encoding="utf-8"))
    expected = {
        "schema": 2,
        "platform": "windows",
        "architecture": "AnyCPU",
        "adapter": "dotnet-framework-com-source",
        "source_snapshot_sha256": snapshot["aggregate_sha256"],
        "source_snapshot_files": snapshot["file_count"],
    }
    if not isinstance(manifest, dict) or any(manifest.get(key) != value for key, value in expected.items()):
        raise RuntimeError("[FORMATTER_MANAGED_SOURCE_DIVERGED] Linux 需要锁定上游构建的 Windows AnyCPU 载荷。")
    for key, name in (
        ("host_sha256", "PartyOps.DocumentFormatter.Host.exe"),
        ("rules_sha256", "PartyOps.DocumentFormatter.AddIn.dll"),
    ):
        if manifest.get(key) != _content_sha256(runtime / name):
            raise RuntimeError(f"[FORMATTER_MANAGED_HASH_DIVERGED] AnyCPU 文件与构建清单不一致：{name}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--profile", choices=("upstream", "mac-quotes-tnr"), default="upstream")
    parser.add_argument("--stage-upstream", type=Path, help="从内嵌 Mac 快照恢复到全新的上游源码目录")
    parser.add_argument("--managed-runtime", type=Path, help="核对 Linux 所需的 Windows AnyCPU 载荷及构建清单")
    args = parser.parse_args()
    if args.profile != "upstream" and (args.stage_upstream or args.managed_runtime):
        parser.error("上游暂存和 Linux 托管载荷校验只适用于 upstream。")
    try:
        result = stage_upstream(args.source, args.stage_upstream) if args.stage_upstream else verify(args.source, args.profile)
        if args.managed_runtime:
            verify_managed_runtime(args.managed_runtime, result)
    except (OSError, UnicodeError, RuntimeError, json.JSONDecodeError) as exc:
        print(str(exc))
        return 2
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
