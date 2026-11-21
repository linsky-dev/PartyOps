"""Fail closed on known vulnerable PyMuPDF locks during RC1 release review.

A major-version CVE floor does not replace a full vulnerability scan, binary
supply-chain validation, PDF behavior parity, or authorized release approval.
"""

from __future__ import annotations

import re
import tomllib
from pathlib import Path


def assert_safe_pymupdf_lock(lock_path: Path) -> str:
    """Block PyMuPDF <2.0 pending CVE-2026-120752 and 82035 review.

    Earlier 1.x versions not covered by the named CVE also remain unsupported
    as a conservative floor. Signed backports require a separate audited path.
    """
    with lock_path.open("rb") as source:
        lock = tomllib.load(source)
    packages = lock.get("package")
    if not isinstance(packages, list):
        raise ValueError("Python uv.lock lacks a package list")
    matched = [
        item for item in packages
        if isinstance(item, dict) and isinstance(item.get("name"), str)
        and item["name"].lower() == "pymupdf"
    ]
    if len(matched) != 1:
        raise ValueError("exactly one locked PyMuPDF distribution is required")
    version = matched[0].get("version")
    if not isinstance(version, str):
        raise ValueError("PyMuPDF lock version missing")
    parsed = re.fullmatch(r"([0-9]+)\.([0-9]+)\.([0-9]+)", version)
    if parsed is None:
        raise ValueError("unrecognized PyMuPDF version; review required")
    numeric = tuple(int(number) for number in parsed.groups())
    if numeric < (2, 0, 0):
        raise ValueError(
            f"PyMuPDF {version} violates security floor >=2.0; "
            "CVE-2026-120752 / CVE-2026-82035 require remediation"
        )
    return version
