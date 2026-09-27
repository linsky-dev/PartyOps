"""本机安装前绑定当前构建封存载荷；只读验证不会采用安装后自报哈希。"""
from __future__ import annotations

import json
from pathlib import Path

from evidence import checked_id, now, safe_child, sha256
from windows_build_payload import expected_payload

TARGET = "win11-x64-native"


def run_directory(lab, path: Path) -> Path:
    run = safe_child(lab.root / "reports" / TARGET, path)
    checked_id(run.name)
    if run.parent != (lab.root / "reports" / TARGET).resolve():
        raise RuntimeError("NATIVE_RUN_MUST_BE_DIRECT_CHILD")
    return run


def write_new(path: Path, value: dict) -> None:
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write("\n")


def prepare(lab, path: Path, package: dict, source: str) -> dict:
    run = run_directory(lab, path)
    for name in ("candidate.json", "install-binding.json", "install-start.json", "install-result.json", "install.log"):
        if (run / name).exists():
            raise RuntimeError("NATIVE_RUN_ALREADY_BOUND_USE_NEW_RUN")
    protection_path = safe_child(run, run / "protection.json")
    protection = json.loads(protection_path.read_text(encoding="utf-8"))
    if protection.get("target") != TARGET or protection.get("environment_type") != "native-host":
        raise RuntimeError("NATIVE_PROTECTION_CONTEXT_INVALID")
    if package.get("id") != "windows_amd64":
        raise RuntimeError("NATIVE_PACKAGE_PLATFORM_MISMATCH")
    bound, payload, _ = expected_payload(lab, package, source)
    binding = {"schema_version": 2, "target": TARGET, "environment_type": "native-host",
               "created_at": now(), "run_directory": str(run), "source_fingerprint": source,
               "package": bound, "windows_payload": payload,
               "protection_sha256": sha256(protection_path), "runtime_environment_passed": False}
    write_new(run / "candidate.json", bound)
    write_new(run / "install-binding.json", binding)
    return binding


def verify(lab, path: Path, source: str) -> dict:
    run = run_directory(lab, path)
    binding = json.loads(safe_child(run, run / "install-binding.json").read_text(encoding="utf-8"))
    if (binding.get("schema_version") != 2 or binding.get("target") != TARGET
            or binding.get("environment_type") != "native-host" or binding.get("run_directory") != str(run)
            or binding.get("source_fingerprint") != source or binding.get("package", {}).get("id") != "windows_amd64"):
        raise RuntimeError("NATIVE_INSTALL_BINDING_MISMATCH")
    protection = safe_child(run, run / "protection.json")
    if sha256(protection) != binding.get("protection_sha256"):
        raise RuntimeError("NATIVE_PROTECTION_CHANGED")
    package, payload, _ = expected_payload(lab, binding["package"], source)
    candidate = json.loads(safe_child(run, run / "candidate.json").read_text(encoding="utf-8"))
    if package != binding["package"] or candidate != package or payload != binding.get("windows_payload"):
        raise RuntimeError("NATIVE_BUILD_OR_CANDIDATE_BINDING_CHANGED")
    return binding
