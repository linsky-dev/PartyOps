"""矩阵制品验收门禁：缺失、错架构、过期或部分结果一律不能标绿。"""
from __future__ import annotations

import hashlib
import json
import re
from datetime import datetime, timedelta, timezone
from pathlib import Path

import jsonschema

BEIJING = timezone(timedelta(hours=8))
ENVIRONMENTS = {"native-host", "hardware-virtualized", "full-system-emulated",
                "experimental-runtime", "hosted-runner"}


def now() -> str:
    return datetime.now(BEIJING).isoformat(timespec="seconds")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def safe_child(root: Path, candidate: Path) -> Path:
    """拒绝根本身、路径穿越和链接绕过；可用于读取证据及限定清理范围。"""
    resolved = candidate.resolve()
    parent = root.resolve()
    if resolved == parent or not resolved.is_relative_to(parent):
        raise ValueError(f"PATH_OUTSIDE_LAB: {candidate}")
    cursor = candidate.absolute()
    while cursor != parent and cursor != cursor.parent:
        if cursor.is_symlink() or (hasattr(cursor, "is_junction") and cursor.is_junction()):
            raise ValueError(f"LINK_NOT_ALLOWED: {candidate}")
        # Python 3.11 没有 is_junction，Windows 目录联接仍须拦截。
        if cursor.exists() and getattr(cursor.lstat(), "st_file_attributes", 0) & 0x400:
            raise ValueError(f"REPARSE_POINT_NOT_ALLOWED: {candidate}")
        cursor = cursor.parent
    return resolved


def checked_id(value: str) -> str:
    if not re.fullmatch(r"[a-z0-9][a-z0-9_-]{0,79}", value):
        raise ValueError("INVALID_IDENTIFIER")
    return value


def evaluate(result: dict, target: dict, package: dict, cases: list[str],
             directory: Path, schema: dict, fingerprint: str) -> list[str]:
    errors: list[str] = []
    try:
        jsonschema.validate(result, schema)
    except jsonschema.ValidationError as exc:
        return [f"INVALID_RESULT_SCHEMA: {exc.json_path}"]
    for key in ("sha256", "version", "id"):
        if result["package"].get(key) != package.get(key):
            errors.append(f"PACKAGE_{key.upper()}_MISMATCH")
    if result["source_fingerprint"] != fingerprint:
        errors.append("STALE_SOURCE_FINGERPRINT")
    # package 来自控制器清单及构建回执，不能读取 result.package 的自报字段。
    if package.get("provenance_status") != "verified" or package.get("source_fingerprint") != fingerprint:
        errors.append("PACKAGE_SOURCE_BINDING_NOT_PROVEN")
    system = result["system"]
    if target["os"] == "windows":
        from windows_execution import evidence_errors
        errors.extend(evidence_errors(result, target, package, directory))
    if system["os"] != target["os"] or system["arch"] != target["arch"]:
        errors.append("GUEST_OS_ISA_MISMATCH")
    if target.get("os_release") and system.get("os_release") != target["os_release"]:
        errors.append("GUEST_OS_RELEASE_MISMATCH")
    for field in ("os_build", "edition", "distribution_id"):
        if target.get(field) and system.get(field) != target[field]:
            errors.append(f"GUEST_{field.upper()}_MISMATCH")
    matches_distribution = (
        not target.get("distribution")
        or system.get("distribution", "").casefold() == target["distribution"].casefold()
    )
    if not matches_distribution:
        errors.append("GUEST_DISTRIBUTION_MISMATCH")
    if target.get("local_only") and system["environment_type"] == "hosted-runner":
        errors.append("REMOTE_ENVIRONMENT_NOT_ALLOWED")
    # 原生目标从本机实时采集；VM 从登记身份与基础快照取得，均不信任报告自报绑定。
    binding = target.get("runtime_binding")
    native = target.get("backend") == "native-host"
    if native != (system["environment_type"] == "native-host"):
        errors.append("RUNTIME_BACKEND_MISMATCH")
    if not binding:
        errors.append("RUNTIME_BINDING_NOT_AVAILABLE")
    else:
        fields = ("backend", "host_id", "identity_sha256") if native else ("vm_uuid", "media_sha256", "baseline_id", "identity_sha256")
        if target.get("firmware"):
            from firmware import FIELDS
            fields += FIELDS
        for field in fields:
            if not binding.get(field) or result.get("environment", {}).get(field) != binding[field]:
                errors.append(f"RUNTIME_BINDING_MISMATCH: {field}")
        if native:
            from native_windows import identity_digest, validate_identity
            try:
                validate_identity(system, target)
                if identity_digest(system) != binding["identity_sha256"]:
                    errors.append("NATIVE_REPORTED_SYSTEM_IDENTITY_MISMATCH")
            except (RuntimeError, KeyError) as exc:
                errors.append(str(exc))
            # 当前原生重启必须可实时核对，不接受虚拟机启动标识或随意生成的进程标识。
            if not binding.get("current_boot_id") or result["restart"]["boot_id_after"] != binding["current_boot_id"]:
                errors.append("NATIVE_REBOOT_DESTINATION_NOT_CURRENT")
            if result["restart"]["boot_id_before"] not in binding.get("observed_boot_ids", []):
                errors.append("NATIVE_REBOOT_SOURCE_NOT_OBSERVED")
    if not system["boot_id"] or system["environment_type"] not in ENVIRONMENTS:
        errors.append("GUEST_RUNTIME_NOT_PROVEN")
    if result["status"] != "passed":
        errors.append("RESULT_NOT_PASSED")
    by_id: dict = {}
    for case in result["cases"]:
        if case["id"] in by_id:
            errors.append("DUPLICATE_CASE")
        by_id[case["id"]] = case
        if case["status"] != "passed":
            errors.append(f"CASE_NOT_PASSED: {case['id']}")
        if not case["evidence"]:
            errors.append(f"MISSING_EVIDENCE: {case['id']}")
        for entry in case["evidence"]:
            try:
                path = safe_child(directory, directory / entry["path"])
                if not path.is_file() or path.stat().st_size == 0 or sha256(path) != entry["sha256"]:
                    errors.append(f"EVIDENCE_HASH_MISMATCH: {case['id']}")
            except (ValueError, OSError):
                errors.append(f"UNSAFE_OR_MISSING_EVIDENCE: {case['id']}")
    for required in cases:
        if required not in by_id:
            errors.append(f"MISSING_CASE: {required}")
    restart = result["restart"]
    if restart["actual"]:
        if not restart["boot_id_after"] or restart["boot_id_before"] == restart["boot_id_after"]:
            errors.append("REBOOT_NOT_PROVEN")
        if restart["boot_id_before"] != system["boot_id"]:
            errors.append("REBOOT_SOURCE_MISMATCH")
    elif not (
        not target.get("local_only") and target.get("hosted_fallback") and system["environment_type"] == "hosted-runner"
        and restart.get("method") == "fresh-job-persisted-state"
        and restart.get("job_before") and restart.get("job_after")
        and restart["job_before"] != restart["job_after"]
        and re.fullmatch(r"[0-9a-f]{64}", restart.get("persisted_state_sha256", ""))
        and restart.get("restored_without_reinstall") is True
    ):
        errors.append("RESTART_POLICY_NOT_SATISFIED")
    if result.get("distribution_match") is not matches_distribution:
        errors.append("DISTRIBUTION_MATCH_MISREPORTED")
    return errors


def aggregate(matrix: dict, packages: dict, reports: dict) -> dict:
    rows = []
    for package_id, spec in matrix["packages"].items():
        targets = spec["required_targets"]
        passed = bool(targets) and package_id in packages and all(
            reports.get(target, {}).get("runtime_environment_passed") is True
            and reports[target].get("package_sha256") == packages[package_id]["sha256"]
            for target in targets
        )
        rows.append({"id": package_id, "required_targets": targets,
                     "runtime_environment_passed": passed, "real_environment_passed": passed,
                     "package_sha256": packages.get(package_id, {}).get("sha256"),
                     "targets": {target: reports.get(target, {"status": "not_run"}) for target in targets}})
    count = sum(row["runtime_environment_passed"] for row in rows)
    return {"schema_version": 1, "generated_at": now(), "version": matrix["version"],
            "required_packages": len(rows), "passed_packages": count,
            "all_packages_runtime_gate": "passed" if rows and count == len(rows) else "blocked",
            "packages": rows}
