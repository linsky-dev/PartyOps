"""构建结束时封存 Windows 载荷清单；安装前不读取已安装程序来推断期望哈希。"""
from __future__ import annotations

import json
import re
from pathlib import Path, PureWindowsPath

from evidence import now, safe_child, sha256
from provenance import bind_package

CONTRACTS = {
    "windows_amd64": ("windows-amd64", "windows", "amd64", "full"),
    "windows7_amd64": ("windows7-amd64", "windows7", "amd64", "legacy-smart"),
    "windows7_x86": ("windows7-x86", "windows7", "x86", "legacy-core"),
}


def manifest_entries(value: dict, package: dict) -> dict:
    _, platform, architecture, profile = CONTRACTS[package["id"]]
    if not isinstance(value, dict):
        raise TypeError("WINDOWS_PAYLOAD_MANIFEST_IDENTITY_MISMATCH")
    if type(value.get("schema_version")) is not int:
        raise RuntimeError("WINDOWS_PAYLOAD_MANIFEST_IDENTITY_MISMATCH")
    if any(value.get(key) != expected for key, expected in {
        "schema_version": 1, "product": "PartyOps", "version": package["version"],
        "platform": platform, "architecture": architecture, "runtime_profile": profile,
    }.items()):
        raise RuntimeError("WINDOWS_PAYLOAD_MANIFEST_IDENTITY_MISMATCH")
    if not isinstance(value.get("files"), list) or not value["files"]:
        raise RuntimeError("WINDOWS_PAYLOAD_MANIFEST_EMPTY")
    result = {}
    for item in value["files"]:
        if not isinstance(item, dict):
            raise TypeError("WINDOWS_PAYLOAD_MANIFEST_ENTRY_INVALID")
        name = str(item.get("path", ""))
        path = PureWindowsPath(name)
        if (not name or not path.parts or path.as_posix() != name or path.is_absolute() or path.drive or ".." in path.parts or ":" in name
                or "\\" in name or name.startswith("/") or name.casefold() in result
                or type(item.get("size")) is not int or item["size"] < 0
                or not re.fullmatch(r"[a-f0-9]{64}", str(item.get("sha256", "")))):
            raise RuntimeError("WINDOWS_PAYLOAD_MANIFEST_ENTRY_INVALID")
        result[name.casefold()] = item
    for name in ("partyops.exe", "partyopswizard.exe"):
        if name not in result or result[name]["size"] <= 0:
            raise RuntimeError("WINDOWS_PAYLOAD_REQUIRED_EXECUTABLE_MISSING")
    return result


def capture_payload(lab, artifacts: Path, package: dict, report: Path, started_ns: int) -> dict:
    """只能由真实构建回执控制器在成功命令结束、源码再次校验前调用。"""
    suffix = CONTRACTS[package["id"]][0]
    stage = (artifacts / f"PartyOps-{package['version']}-{suffix}").resolve()
    manifest = safe_child(stage, stage / "release-manifest.json")
    before = manifest.stat()
    if before.st_mtime_ns < started_ns:
        raise RuntimeError("WINDOWS_PAYLOAD_MANIFEST_NOT_FROM_THIS_BUILD")
    content = manifest.read_bytes()
    value = json.loads(content)
    entries = manifest_entries(value, package)
    executables = {}
    for name in ("PartyOps.exe", "PartyOpsWizard.exe"):
        path = safe_child(stage, stage / name)
        stat = path.stat()
        item = entries[name.casefold()]
        digest = sha256(path)
        if stat.st_mtime_ns < started_ns or stat.st_size != item["size"] or digest != item["sha256"]:
            raise RuntimeError("WINDOWS_PAYLOAD_EXECUTABLE_NOT_FROM_THIS_BUILD:" + name)
        if path.stat().st_mtime_ns != stat.st_mtime_ns:
            raise RuntimeError("WINDOWS_PAYLOAD_CHANGED_DURING_CAPTURE")
        executables[name] = {"sha256": digest, "bytes": stat.st_size}
    if manifest.read_bytes() != content or manifest.stat().st_mtime_ns != before.st_mtime_ns:
        raise RuntimeError("WINDOWS_PAYLOAD_CHANGED_DURING_CAPTURE")
    destination = safe_child(lab.root / "reports", report / ("payload-" + package["id"]) / "release-manifest.json")
    destination.parent.mkdir(parents=True, exist_ok=False)
    with destination.open("xb") as stream:
        stream.write(content)
    return {"schema_version": 1, "captured_at": now(), "capture": "controller-after-successful-build",
            "package_sha256": package["sha256"], "staging_directory": str(stage),
            "manifest": {"path": str(destination), "sha256": sha256(destination), "bytes": len(content)},
            "executables": executables, "manifest_file_count": len(entries)}


def expected_payload(lab, package: dict, source: str) -> tuple[dict, dict, dict]:
    """重读真实回执、日志和包；旧回执缺载荷封存时明确阻断，不能事后追认。"""
    bound = bind_package(lab, package, source)
    receipt = json.loads(Path(bound["build_receipt"]).read_text(encoding="utf-8"))
    payload = receipt.get("windows_payload")
    if (not isinstance(payload, dict) or type(payload.get("schema_version")) is not int or payload.get("schema_version") != 1
            or payload.get("capture") != "controller-after-successful-build"
            or payload.get("package_sha256") != bound["sha256"]):
        raise RuntimeError("WINDOWS_BUILD_PAYLOAD_RECEIPT_REQUIRED")
    try:
        manifest = safe_child(lab.root / "reports", Path(payload["manifest"]["path"]))
        if manifest.stat().st_size != payload["manifest"]["bytes"] or sha256(manifest) != payload["manifest"]["sha256"]:
            raise RuntimeError("WINDOWS_BUILD_PAYLOAD_MANIFEST_CHANGED")
        entries = manifest_entries(json.loads(manifest.read_text(encoding="utf-8")), bound)
        if len(entries) != payload["manifest_file_count"]:
            raise RuntimeError("WINDOWS_BUILD_PAYLOAD_MANIFEST_CHANGED")
        for name in ("PartyOps.exe", "PartyOpsWizard.exe"):
            if payload["executables"][name] != {"sha256": entries[name.casefold()]["sha256"], "bytes": entries[name.casefold()]["size"]}:
                raise RuntimeError("WINDOWS_BUILD_PAYLOAD_EXECUTABLE_BINDING_CHANGED")
    except (KeyError, TypeError, ValueError, OSError) as exc:
        raise RuntimeError("WINDOWS_BUILD_PAYLOAD_RECORD_INVALID") from exc
    return bound, payload, entries
