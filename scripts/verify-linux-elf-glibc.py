#!/usr/bin/env python3
"""逐个核对冻结包 ELF 实际引用的最高 glibc 符号版本。"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
from pathlib import Path

NEEDS_SECTION = re.compile(r"^Version needs section\b")
VERSION_SECTION = re.compile(r"^Version \S+ section\b")
GLIBC_NEED = re.compile(r"\bName:\s*GLIBC_(\d+)\.(\d+)\b")


def required_glibc_versions(version_info: str) -> set[tuple[int, int]]:
    """只读取 .gnu.version_r 的需求；定义和符号表不代表外部 ABI 下限。"""
    needed = False
    versions: set[tuple[int, int]] = set()
    for line in version_info.splitlines():
        if VERSION_SECTION.match(line):
            needed = bool(NEEDS_SECTION.match(line))
            continue
        if needed:
            versions.update((int(major), int(minor)) for major, minor in GLIBC_NEED.findall(line))
    return versions


def verify(root: Path, maximum: tuple[int, int]) -> tuple[int, int, int]:
    if not root.is_dir():
        raise ValueError(f"运行时目录不存在：{root}")
    highest = (0, 0)
    count = 0
    for path in sorted(root.rglob("*")):
        if not path.is_file() or path.is_symlink():
            continue
        with path.open("rb") as stream:
            if stream.read(4) != b"\x7fELF":
                continue
        count += 1
        result = subprocess.run(
            ["readelf", "-W", "--version-info", str(path)],
            capture_output=True, text=True, check=False,
            env={**os.environ, "LC_ALL": "C"},
        )
        if result.returncode != 0:
            raise ValueError(f"无法读取 ELF 符号版本：{path}: {result.stderr[:300]}")
        symbols = required_glibc_versions(result.stdout)
        actual = max(symbols, default=(0, 0))
        if actual > maximum:
            raise ValueError(
                f"{path} 实际引用 GLIBC_{actual[0]}.{actual[1]}，"
                f"高于目标 GLIBC_{maximum[0]}.{maximum[1]}"
            )
        highest = max(highest, actual)
    if not count:
        raise ValueError("运行时没有 ELF，无法证明目标 ABI")
    return count, *highest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--max-glibc", default="2.38")
    args = parser.parse_args()
    if not re.fullmatch(r"\d+\.\d+", args.max_glibc):
        parser.error("--max-glibc 必须是 major.minor")
    try:
        count, major, minor = verify(args.root, tuple(map(int, args.max_glibc.split("."))))
    except (OSError, ValueError) as exc:
        parser.exit(2, f"[LOONG_ELF_GLIBC_INVALID] {exc}\n")
    print(f"ELF 数量={count}；实际最高 GLIBC_{major}.{minor}；目标上限={args.max_glibc}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
