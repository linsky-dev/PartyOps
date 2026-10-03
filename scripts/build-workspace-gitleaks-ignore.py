#!/usr/bin/env python3
"""仅为已核实且内容未变化的工作区误报生成一次性 gitleaks 例外。"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path

SHA256 = re.compile(r"[0-9a-f]{64}\Z")
PYTEST_PREFIX = ".test-temp/pytest-of-Administrator/pytest-0/test_"
RECEIPT_PATH = "qa/vm-lab/release-preparation/pytest-temp-cleanup-20260923.json"
RECEIPT_SHA256 = "8c6c58c14129c3399f62d4aecf9f226732b35bed596573245c85b380bad9b856"
PYTEST_FINDINGS_SHA256 = "10a00fd5ee20fe3e21c38a1eab450f3c86a4057ff90505c291cb440a00124125"
EXTRA_FINDINGS = {
    ("qa/vm-lab/release-preparation/loong64/libreoffice-signature-20260923.json", "generic-api-key", 8),
    ("qa/vm-lab/release-preparation/loong64/mono-bundle-patched-tool-resources-comparison.json", "generic-api-key", 3),
    ("qa/vm-lab/release-preparation/loong64/mono-bundle-patched-tool-resources-comparison.json", "generic-api-key", 10),
    ("qa/vm-lab/release-preparation/loong64/libreoffice-build-inputs/download.lst", "generic-api-key", 350),
}


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _safe_relative(root: Path, name: str, *, optional: bool = False) -> Path | None:
    if (not isinstance(name, str) or "\\" in name or name.startswith("/")
            or any(part in {"", ".", ".."} for part in name.split("/"))):
        raise ValueError("GITLEAKS_EXCEPTION_PATH_INVALID")
    path = root / name
    if path.is_symlink() or root not in path.resolve().parents:
        raise ValueError(f"GITLEAKS_EXCEPTION_FILE_MISSING_OR_LINKED:{name}")
    if not path.exists() and optional:
        return None
    if not path.is_file():
        raise ValueError(f"GITLEAKS_EXCEPTION_FILE_MISSING_OR_LINKED:{name}")
    return path


def _finding(value: object) -> tuple[str, str, int]:
    if not isinstance(value, dict):
        raise TypeError("GITLEAKS_EXCEPTION_FINDING_INVALID")
    path, rule, line = value.get("path"), value.get("rule"), value.get("line")
    if (not isinstance(path, str) or not isinstance(rule, str)
            or type(line) is not int or line < 1):
        raise ValueError("GITLEAKS_EXCEPTION_FINDING_INVALID")
    return path, rule, line


def validate(root: Path, manifest_path: Path) -> list[str]:
    root = root.resolve()
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if not isinstance(manifest, dict) or manifest.get("schema") != 1:
        raise ValueError("GITLEAKS_EXCEPTION_MANIFEST_INVALID")
    if manifest.get("receipt_sha256") != RECEIPT_SHA256:
        raise ValueError("GITLEAKS_EXCEPTION_RECEIPT_CHANGED")
    pytest_raw = manifest.get("pytest_findings")
    if not isinstance(pytest_raw, list) or len(pytest_raw) != 68:
        raise ValueError("GITLEAKS_EXCEPTION_PYTEST_SCOPE_INVALID")
    pytest_findings = [_finding(item) for item in pytest_raw]
    encoded = json.dumps(pytest_findings, ensure_ascii=False, sort_keys=True,
                         separators=(",", ":")).encode("utf-8")
    if (any(not name.startswith(PYTEST_PREFIX) or rule not in {"private-key", "generic-api-key"}
            for name, rule, _ in pytest_findings)
            or len(set(pytest_findings)) != 68
            or hashlib.sha256(encoded).hexdigest() != PYTEST_FINDINGS_SHA256):
        raise ValueError("GITLEAKS_EXCEPTION_PYTEST_SCOPE_INVALID")
    receipt = _safe_relative(root, RECEIPT_PATH, optional=True)
    if receipt is not None and _sha256(receipt) != RECEIPT_SHA256:
        raise ValueError("GITLEAKS_EXCEPTION_RECEIPT_CHANGED")
    extra_raw = manifest.get("extra_findings")
    if not isinstance(extra_raw, list) or len(extra_raw) != 4:
        raise ValueError("GITLEAKS_EXCEPTION_EXTRA_COUNT_INVALID")
    extra = [_finding(item) for item in extra_raw]
    if set(extra) != EXTRA_FINDINGS:
        raise ValueError("GITLEAKS_EXCEPTION_EXTRA_SCOPE_INVALID")
    findings = pytest_findings + extra
    hashes = manifest.get("file_sha256")
    if not isinstance(hashes, dict) or set(hashes) != {name for name, _, _ in findings}:
        raise ValueError("GITLEAKS_EXCEPTION_HASH_SET_INVALID")
    present: set[str] = set()
    for name, record in hashes.items():
        if not isinstance(record, dict) or set(record) != {"digest"}:
            raise ValueError(f"GITLEAKS_EXCEPTION_HASH_INVALID:{name}")
        expected = record["digest"]
        if not isinstance(expected, str) or not SHA256.fullmatch(expected):
            raise ValueError(f"GITLEAKS_EXCEPTION_HASH_INVALID:{name}")
        path = _safe_relative(root, name, optional=True)
        if path is None:
            continue
        if _sha256(path) != expected:
            raise ValueError(f"GITLEAKS_EXCEPTION_CONTENT_CHANGED:{name}")
        present.add(name)
    return [f"{(root / name).as_posix()}:{rule}:{line}"
            for name, rule, line in findings if name in present]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        fingerprints = validate(args.root, args.manifest)
        if args.output:
            if args.output.exists():
                raise ValueError("GITLEAKS_EXCEPTION_OUTPUT_EXISTS")
            # 例外文件只在本次目录扫描期间存在，Git 历史扫描从不读取。
            args.output.write_text("\n".join(fingerprints) + "\n", encoding="utf-8")
    except (OSError, TypeError, ValueError, KeyError) as exc:
        parser.exit(2, f"工作区凭据例外校验失败：{exc}\n")
    print(f"已核对 {len(fingerprints)} 项固定工作区误报及其文件哈希。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
