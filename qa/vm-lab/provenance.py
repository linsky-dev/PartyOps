"""控制器登记的构建回执；待验收报告不能自行声明安装包对应当前源码。"""
from __future__ import annotations

import json
from pathlib import Path

from evidence import safe_child, sha256


def bind_package(lab, package: dict, source: str) -> dict:
    """每次续跑/汇总重新读取回执、构建日志和包，拒绝旧包追认。"""
    path = lab.root / "state/builds" / (package["sha256"] + ".json")
    if not path.is_file():
        raise RuntimeError("PACKAGE_BUILD_RECEIPT_MISSING")
    record = json.loads(path.read_text(encoding="utf-8"))
    for field in ("id", "version", "sha256"):
        if record.get("package", {}).get(field) != package.get(field):
            raise RuntimeError("PACKAGE_BUILD_RECEIPT_MISMATCH")
    if record.get("source_before") != source or record.get("source_after") != source:
        raise RuntimeError("PACKAGE_SOURCE_FINGERPRINT_MISMATCH")
    if record.get("exit_code") != 0 or record.get("output_created_during_build") is not True:
        raise RuntimeError("PACKAGE_BUILD_NOT_PROVEN")
    try:
        log = safe_child(lab.root / "reports", Path(record["log"]["path"]))
        if not log.is_file() or not log.stat().st_size or sha256(log) != record["log"]["sha256"]:
            raise RuntimeError("PACKAGE_BUILD_LOG_CHANGED")
    except (KeyError, ValueError) as exc:
        raise RuntimeError("PACKAGE_BUILD_LOG_INVALID") from exc
    if sha256(Path(package["path"])) != package["sha256"]:
        raise RuntimeError("PACKAGE_SHA256_CHANGED")
    return {**package, "source_fingerprint": source, "provenance_status": "verified",
            "build_receipt": str(path), "build_receipt_sha256": sha256(path)}
