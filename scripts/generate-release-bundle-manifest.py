"""为最终公开发布目录生成可审计的多平台制品清单。"""

from __future__ import annotations

import argparse
import base64
import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
from contextlib import contextmanager
from datetime import datetime
from pathlib import Path

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey

VERSION = "1.4.5-rc.6"
# 经确认的最终活动矩阵固定在发布工具源码中，和质量门禁一起冻结。
# 数量从完整目录派生；用户传入的报告或可编辑的实验室 YAML 不能删除必需目标。
RELEASE_PACKAGES = {
    "windows_amd64": {"filename": f"PartyOps_{VERSION}_windows_amd64.exe", "platform": "windows/amd64",
                      "targets": ("win11-x64-native", "win10-x64", "win11-arm64")},
    "windows7_amd64": {"filename": f"PartyOps_{VERSION}_windows7_amd64.exe", "platform": "windows7/amd64", "targets": ("win7-x64",)},
    "windows7_x86": {"filename": f"PartyOps_{VERSION}_windows7_x86.exe", "platform": "windows7/x86",
                     "targets": ("win7-x86", "win10-x86", "win10-arm64")},
    "linux_amd64": {"filename": f"PartyOps_{VERSION}_linux_amd64.deb", "platform": "linux-deb/amd64", "targets": ("uos-deb-x64",)},
    "linux_arm64": {"filename": f"PartyOps_{VERSION}_linux_arm64.deb", "platform": "linux-deb/arm64", "targets": ("uos-deb-arm64",)},
    "linux_loong64": {"filename": f"PartyOps_{VERSION}_linux_loong64.deb", "platform": "linux-deb/loong64", "targets": ("deepin-deb-loong64",)},
    "rpm_x86_64": {"filename": "PartyOps-1.4.5-0.rc.6.1.x86_64.rpm", "platform": "linux-rpm/amd64", "targets": ("openeuler-iso-x64",)},
    "rpm_aarch64": {"filename": "PartyOps-1.4.5-0.rc.6.1.aarch64.rpm", "platform": "linux-rpm/arm64", "targets": ("openeuler-iso-arm64",)},
    "macos_x86_64": {"filename": f"PartyOps_{VERSION}_macos_x86_64.pkg", "platform": "macos/amd64", "targets": ("macos-x64",)},
    "macos_arm64": {"filename": f"PartyOps_{VERSION}_macos_arm64.pkg", "platform": "macos/arm64", "targets": ("macos-arm64",)},
}
TARGET_REQUIREMENTS = {
    "win11-x64-native": {"os": "windows", "os_release": "11", "arch": "x86_64", "backend": "native-host"},
    "win10-x64": {"os": "windows", "os_release": "10", "arch": "x86_64", "backend": "qemu", "media": "win10-x64"},
    "win10-x86": {"os": "windows", "os_release": "10", "os_build": "19045", "arch": "i686", "backend": "qemu", "media": "win10-x86",
                  "windows_execution_contract": 1, "package_arch": "i686", "expected_runtime_profile": "legacy-core", "application_execution": "native"},
    "win10-arm64": {"os": "windows", "os_release": "10", "arch": "arm64", "backend": "qemu", "media": "win10-arm64",
                    "windows_execution_contract": 1, "package_arch": "i686", "expected_runtime_profile": "legacy-core",
                    "application_execution": "windows-x86-emulation", "requires_uefi_secureboot_tpm2": True,
                    "driver_media": "virtio-win-arm64", "hardware_scope": "qemu-virt"},
    "win11-arm64": {"os": "windows", "os_release": "11", "os_build": "26200", "arch": "arm64", "backend": "qemu", "media": "win11-arm64",
                    "windows_execution_contract": 1, "package_arch": "x86_64", "expected_runtime_profile": "full",
                    "application_execution": "windows-x64-emulation", "requires_uefi_secureboot_tpm2": True,
                    "driver_media": "virtio-win-arm64", "hardware_scope": "qemu-virt"},
    "win7-x64": {"os": "windows", "os_release": "7 SP1", "arch": "x86_64", "backend": "qemu", "media": "win7-x64"},
    "win7-x86": {"os": "windows", "os_release": "7 SP1", "arch": "i686", "backend": "qemu", "media": "win7-x86"},
    "uos-deb-x64": {"os": "linux", "arch": "x86_64", "backend": "qemu", "media": "uos-1070-hwe-x64", "distribution_id": "uos",
                    "distribution": "UOS", "os_release": "20", "os_build": "1070", "edition": "Professional"},
    "uos-deb-arm64": {"os": "linux", "arch": "aarch64", "backend": "qemu", "media": "uos-1070-arm64", "distribution_id": "uos",
                      "distribution": "UOS", "os_release": "20", "os_build": "1070", "edition": "Professional"},
    "deepin-deb-loong64": {"os": "linux", "arch": "loongarch64", "package_arch": "loong64", "backend": "qemu", "media": "deepin-25-loong64",
                          "distribution_id": "deepin", "distribution": "Deepin", "os_release": "25", "os_build": "25.2.0",
                          "firmware": {"code": "share/edk2-loongarch64-code.fd",
                                       "code_sha256": "edd5a67fe50f7597faecb2fe67c5733b9a31b0b345a2127c3c358a5737446ef7",
                                       "vars_template": "share/edk2-loongarch64-vars.fd",
                                       "vars_template_sha256": "adbfcb31d6470ef090220baeb53559e26323b6cfb5e7614f879e89608a3ed748"}},
    "openeuler-iso-x64": {"os": "linux", "arch": "x86_64", "backend": "qemu", "media": "openeuler-iso-x64", "distribution_id": "openeuler",
                         "distribution": "openEuler", "os_release": "24.03", "os_build": "LTS-SP2"},
    "openeuler-iso-arm64": {"os": "linux", "arch": "aarch64", "backend": "qemu", "media": "openeuler-iso-arm64", "distribution_id": "openeuler",
                           "distribution": "openEuler", "os_release": "24.03", "os_build": "LTS-SP2"},
    "macos-x64": {"os": "darwin", "arch": "x86_64", "backend": "vmware", "media": "macos-x64"},
    "macos-arm64": {"os": "darwin", "arch": "arm64", "backend": "macos-arm64-experimental", "media": "macos-arm64"},
}
REQUIRED_CASES = (
    "clean_install", "standard_user_launch", "first_configuration", "personal_and_collaboration", "health_and_selftest",
    "formatter_contract_and_visual_golden", "offline_models", "business_backup_restore", "restart_persistence",
    "upgrade_and_data_preservation", "uninstall_keep_data", "uninstall_remove_test_data", "historical_failure_regressions",
)
INSTALLERS = {item["filename"]: item["platform"] for item in RELEASE_PACKAGES.values()}
PACKAGE_IDS = tuple(RELEASE_PACKAGES)
UNAVAILABLE_INSTALLERS: dict[str, str] = {}
LOONG_SCOPE_STATUSES = {
    "install-and-background-verification": "passed_with_scope_limit",
    "configure-and-business": "passed", "actual-reboot-and-after-reboot": "passed",
    "ocr": "passed", "core-ai-capability-rejection": "passed_as_capability_rejection_only",
    "rules-intent-preview": "passed_as_rules_preview_only",
    "same-guest-collaboration": "passed_same_guest_only",
    "installed-wps-formatter-nine-features": "passed_9_cases",
    "wps-golden-three-pages": "passed_two_documents_three_pages_each",
    "firefox-gui": "passed", "uninstall-keep": "passed",
    "same-package-reinstall-and-recovery": "passed_same_package_recovery",
    "remove-registered-test-data": "passed_guarded_scope",
    "normal-guest-shutdown": "stopped_confirmed",
    "old-same-architecture-upgrade-candidate": "not_applicable_candidate",
}
SIX_PACKAGE_FREEZE_PATH = Path("qa/vm-lab/release-preparation/rc6-six-package-freeze-20260927.json")
WIN7_APPROVED_SCOPE = "安装、普通用户启动、health、自检、中文OCR、数据保留、WPS转换与补字体后排版、真实冷启动"


def _same_members(actual: object, expected: object) -> bool:
    return isinstance(actual, (list, tuple)) and all(isinstance(value, str) for value in actual) and len(actual) == len(set(actual)) and set(actual) == set(expected)


def _matrix_contract(matrix: dict) -> dict:
    """核验每个包、必需目标、ISA/执行层和十三场景；不得从自由输入派生较小门禁。"""
    if (matrix.get("version") != VERSION or matrix.get("local_only") is not True
            or set(matrix.get("packages", {})) != set(RELEASE_PACKAGES)
            or set(matrix.get("targets", {})) != set(TARGET_REQUIREMENTS)):
        raise ValueError("本地实验室与冻结的完整发布矩阵不一致，禁止删减安装包或必需环境")
    if not _same_members(matrix.get("required_cases"), REQUIRED_CASES):
        raise ValueError("本地实验室必须保留全部十三项必需场景")
    for package_id, expected in RELEASE_PACKAGES.items():
        configured = matrix["packages"][package_id]
        if not _same_members(configured.get("required_targets"), expected["targets"]):
            raise ValueError(f"安装包必需环境与冻结目录不一致：{package_id}")
    for target, expected in TARGET_REQUIREMENTS.items():
        configured = matrix["targets"][target]
        if (any(configured.get(field) != value for field, value in expected.items())
                or configured.get("hosted_fallback") is True or configured.get("local_only") is False):
            raise ValueError(f"必需环境系统或执行层合同被改变：{target}")
    contract = {"version": VERSION, "packages": RELEASE_PACKAGES, "targets": TARGET_REQUIREMENTS, "required_cases": REQUIRED_CASES}
    canonical = json.dumps(contract, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode()
    return {"required_packages": len(RELEASE_PACKAGES), "required_environments": len(TARGET_REQUIREMENTS),
            "required_cases": list(REQUIRED_CASES), "matrix_contract_sha256": hashlib.sha256(canonical).hexdigest()}


def _load_module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ValueError(f"无法载入本地发布门禁：{path.name}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _current_source(source_root: Path) -> tuple[str, str]:
    """从干净的产品源码与现有质量门禁读取身份，不接受手工填写提交/指纹。"""
    gate = _load_module(source_root / "scripts/verify-full-function-gate.py", "release_full_gate")
    if gate.verify(source_root, "package") != 0:
        raise ValueError("完整产品质量门禁尚未通过或已过期")
    roots = (*gate.PACKAGE_INCLUDE_ROOTS, *gate.PACKAGE_INCLUDE_FILES)

    def git(*arguments: str) -> str:
        return subprocess.run(
            ["git", "-C", str(source_root), *arguments], check=True,
            capture_output=True, encoding="utf-8",
        ).stdout

    changed = git("diff", "--name-only", "-z", "HEAD", "--", *roots).split("\0")
    untracked = git("ls-files", "--others", "--exclude-standard", "-z", "--", *roots).split("\0")
    if any(path and not gate.EXCLUDED_PARTS.intersection(Path(path).parts) for path in changed + untracked):
        raise ValueError("产品源码尚未冻结为干净提交；请提交后重建并重验对应制品")
    return git("rev-parse", "HEAD").strip(), gate.source_fingerprint(source_root, "package")[0]


@contextmanager
def _laboratory(source_root: Path, lab_root: Path):
    """复用原版环境门禁；只读取状态/证据与快照信息，不创建或启动虚拟机。"""
    directory = source_root / "qa/vm-lab"
    sys.path.insert(0, str(directory))
    try:
        module = _load_module(directory / "lab.py", "release_original_os_lab")
        matrix, media = module.load_configuration()
        yield module, module.QemuLab(matrix, lab_root, media)
    finally:
        sys.path.remove(str(directory))


def _acceptance(root: Path, report_path: Path, source_root: Path, lab_root: Path) -> dict:
    """逐项重新绑定冻结活动矩阵、构建回执及原始证据，拒绝汇总字段单独追认。"""
    report_bytes = report_path.read_bytes()
    report = json.loads(report_bytes)
    report_sha256 = hashlib.sha256(report_bytes).hexdigest()
    if (report.get("version") != VERSION or report.get("required_packages") != len(RELEASE_PACKAGES)
            or report.get("passed_packages") != len(RELEASE_PACKAGES) or report.get("all_packages_runtime_gate") != "passed"
            or report.get("package_errors")):
        raise ValueError(f"完整活动矩阵生命周期门禁未通过（{report.get('passed_packages', 0)}/{len(RELEASE_PACKAGES)}），禁止生成可发布清单")
    rows = report.get("packages", [])
    if not isinstance(rows, list) or len(rows) != len(RELEASE_PACKAGES) or {row.get("id") for row in rows} != set(PACKAGE_IDS):
        raise ValueError("最终报告的安装包矩阵不完整或含重复目标")
    commit, fingerprint = _current_source(source_root)
    if report.get("source_fingerprint") != fingerprint:
        raise ValueError("最终报告源码指纹已过期")
    row_by_id = {row["id"]: row for row in rows}
    inventory = report.get("package_inventory", {})
    if set(inventory) != set(PACKAGE_IDS):
        raise ValueError("最终报告的安装包清单不完整")
    environments, builds = [], []
    with _laboratory(source_root, lab_root) as (module, lab):
        matrix_contract = _matrix_contract(lab.matrix)
        for package_id, release in RELEASE_PACKAGES.items():
            filename, platform = release["filename"], release["platform"]
            candidate = root / filename
            package = {"id": package_id, "version": VERSION, "path": str(candidate),
                       "sha256": sha256(candidate), "bytes": candidate.stat().st_size}
            recorded = inventory[package_id]
            row = row_by_id[package_id]
            targets = release["targets"]
            if any(recorded.get(field) != package[field] for field in ("id", "version", "sha256", "bytes")):
                raise ValueError(f"制品与最终报告不一致：{package_id}")
            if (recorded.get("source_fingerprint") != fingerprint or recorded.get("provenance_status") != "verified"
                    or row.get("package_sha256") != package["sha256"]
                    or row.get("runtime_environment_passed") is not True
                    or not _same_members(row.get("required_targets"), targets) or set(row.get("targets", {})) != set(targets)):
                raise ValueError(f"制品的来源或必需环境门禁不完整：{package_id}")
            packages = {package_id: package}
            for target in targets:
                original = row["targets"][target]
                if (original.get("target") != target or original.get("status") != "passed"
                        or original.get("runtime_environment_passed") is not True or original.get("errors")
                        or original.get("package_sha256") != package["sha256"] or not original.get("evidence_path")):
                    raise ValueError(f"最终报告缺少完整环境证据：{target}")
                evidence_path = Path(original["evidence_path"])
                fresh = module.check_target(lab, target, packages, {}, evidence_path, fingerprint)
                if fresh.get("runtime_environment_passed") is not True or fresh.get("errors"):
                    raise ValueError(f"原始环境证据复核失败：{target}；{fresh.get('errors', [])}")
                environment = fresh.get("environment_type")
                if environment not in {"native-host", "hardware-virtualized", "full-system-emulated", "experimental-runtime"}:
                    raise ValueError(f"不允许远端或不明验收环境：{target}")
                spec = TARGET_REQUIREMENTS[target]
                environments.append({"target": target, "package_id": package_id, "platform": platform,
                                     "environment_type": environment, "os_isa": spec["arch"],
                                     "package_isa": spec.get("package_arch", spec["arch"]),
                                     "application_execution": spec.get("application_execution", "native"),
                                     "evidence_sha256": sha256(evidence_path)})
            bound = packages[package_id]
            if bound.get("build_receipt_sha256") != recorded.get("build_receipt_sha256"):
                raise ValueError(f"最终报告之后构建回执已改变：{package_id}")
            receipt_path = Path(bound["build_receipt"])
            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
            if receipt.get("status") != "completed" or receipt.get("errors"):
                raise ValueError(f"构建回执没有正常完成：{package_id}")
            builds.append({"platform": platform, "package_sha256": package["sha256"],
                           "build_receipt_sha256": bound["build_receipt_sha256"],
                           "build_log_sha256": receipt["log"]["sha256"]})
    if sha256(report_path) != report_sha256:
        raise ValueError("最终报告在复核期间发生变化")
    return {"source_commit": commit, "source_fingerprint": fingerprint,
            "qa_report_sha256": report_sha256, "qa_generated_at": report["generated_at"],
            **matrix_contract, "environments": environments, "builds": builds}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _verified_key(private_key_path: Path, public_key_path: Path) -> tuple[Ed25519PrivateKey, str, str]:
    """载入正式密钥并只返回公钥与指纹，私钥内容不进入清单或日志。"""

    if not private_key_path.is_file() or private_key_path.is_symlink():
        raise ValueError("正式发布私钥必须是本机普通文件")
    data = private_key_path.read_bytes()
    try:
        key = serialization.load_pem_private_key(data, password=None)
    except ValueError:
        key = Ed25519PrivateKey.from_private_bytes(base64.b64decode(data.strip(), validate=True))
    if not isinstance(key, Ed25519PrivateKey):
        raise TypeError("正式发布私钥必须使用 Ed25519")
    trusted = public_key_path.read_text(encoding="ascii").strip()
    actual = base64.b64encode(
        key.public_key().public_bytes(
            serialization.Encoding.Raw,
            serialization.PublicFormat.Raw,
        )
    ).decode("ascii")
    if actual != trusted:
        raise ValueError("正式发布私钥与客户端内置信任公钥不匹配")
    fingerprint = hashlib.sha256(base64.b64decode(actual)).hexdigest()
    return key, actual, fingerprint


def _sign_payload(key: Ed25519PrivateKey, payload: dict[str, object]) -> str:
    canonical = json.dumps(
        payload, ensure_ascii=False, sort_keys=True, separators=(",", ":")
    ).encode("utf-8")
    return base64.b64encode(key.sign(canonical)).decode("ascii")


def _checked_evidence(items: object, *, label: str) -> list[dict[str, str]]:
    """逐项核实原始证据，不把摘要报告中的 passed 当作证据本身。"""
    if not isinstance(items, list) or not items:
        raise ValueError(f"{label}缺少原始证据")
    checked = []
    for item in items:
        if not isinstance(item, dict) or not isinstance(item.get("path"), str) or not isinstance(item.get("sha256"), str):
            raise ValueError(f"{label}证据格式无效")
        path = Path(item["path"])
        if not path.is_file() or sha256(path) != item["sha256"]:
            raise ValueError(f"{label}原始证据已改变：{path}")
        checked.append({"path": str(path), "sha256": item["sha256"]})
    return checked


def _scoped_acceptance(root: Path, plan_path: Path, source_root: Path, lab_root: Path) -> dict:
    """显式限域验收：逐包绑定冻结计划、原验收回执、原证据和实际构建。"""
    plan_bytes = plan_path.read_bytes()
    plan = json.loads(plan_bytes)
    if (plan.get("schema_version") != 1 or plan.get("release_mode") != "scoped"
            or plan.get("version") != VERSION):
        raise ValueError("限域发布计划版本或模式无效")
    rows, deferred = plan.get("packages"), plan.get("deferred_packages")
    if not isinstance(rows, list) or not rows or not isinstance(deferred, list):
        raise ValueError("限域发布计划必须明确纳入包和延期包")
    ids = [row.get("id") for row in rows if isinstance(row, dict)]
    deferred_ids = [row.get("id") for row in deferred if isinstance(row, dict)]
    if (len(ids) != len(rows) or len(ids) != len(set(ids)) or len(deferred_ids) != len(deferred)
            or len(deferred_ids) != len(set(deferred_ids)) or set(ids) & set(deferred_ids)
            or set(ids) | set(deferred_ids) != set(PACKAGE_IDS)):
        raise ValueError("限域发布计划含重复、未知或未交代的安装包")
    if any(not isinstance(row.get("reason"), str) or not row["reason"].strip()
           or not _same_members(row.get("targets"), RELEASE_PACKAGES[row["id"]]["targets"])
           for row in deferred):
        raise ValueError("延期包必须列出原目标和具体原因")
    freeze_ref = plan.get("six_package_freeze")
    freeze_path = source_root / SIX_PACKAGE_FREEZE_PATH
    if (not isinstance(freeze_ref, dict) or Path(freeze_ref.get("path", "")).resolve() != freeze_path.resolve()
            or not freeze_path.is_file() or freeze_ref.get("sha256") != sha256(freeze_path)):
        raise ValueError("六包冻结快照路径或摘要不匹配")
    freeze = json.loads(freeze_path.read_bytes())
    freeze_rows = freeze.get("packages")
    if (freeze.get("schema_version") != 1 or not isinstance(freeze_rows, list)
            or {item.get("id") for item in freeze_rows} != {
                "windows_amd64", "windows7_amd64", "linux_amd64", "linux_arm64", "rpm_x86_64", "rpm_aarch64"}):
        raise ValueError("六包冻结快照结构不符")
    frozen_by_id = {item["id"]: item for item in freeze_rows}
    planned_names = {RELEASE_PACKAGES[package_id]["filename"] for package_id in ids}
    actual_installers = {path.name for path in root.iterdir() if path.is_file() and path.suffix.lower() in {".exe", ".deb", ".rpm", ".pkg"}}
    if actual_installers != planned_names:
        raise ValueError(f"发布目录安装包与限域计划不一致：缺少{sorted(planned_names - actual_installers)}，额外{sorted(actual_installers - planned_names)}")
    accepted, inputs = [], [(plan_path, hashlib.sha256(plan_bytes).hexdigest()), (freeze_path, freeze_ref["sha256"])]
    with _laboratory(source_root, lab_root) as (_, lab):
        from provenance import bind_package
        for row in rows:
            package_id = row["id"]
            spec = RELEASE_PACKAGES[package_id]
            path = root / spec["filename"]
            digest = sha256(path)
            size = path.stat().st_size
            if (row.get("filename") != path.name or row.get("size") != size or row.get("sha256") != digest
                    or row.get("platform") != spec["platform"]):
                raise ValueError(f"安装包与冻结计划不一致：{package_id}")
            targets = row.get("targets")
            if (not isinstance(targets, list) or not targets or len(targets) != len(set(targets))
                    or not set(targets) <= set(spec["targets"])
                    or not _same_members(row.get("untested_targets"), set(spec["targets"]) - set(targets))):
                raise ValueError(f"安装包验收目标不属于冻结矩阵：{package_id}")
            limitations, untested = row.get("limitations"), row.get("untested")
            if (not isinstance(limitations, list) or not limitations or not all(isinstance(x, str) and x.strip() for x in limitations)
                    or not isinstance(untested, list) or not untested
                    or not all(isinstance(x, str) and x.strip() for x in untested)):
                raise ValueError(f"安装包限制或未测项不完整：{package_id}")
            source = row.get("source_fingerprint")
            if not isinstance(source, str) or len(source) != 64 or any(c not in "0123456789abcdef" for c in source):
                raise ValueError(f"构建源码指纹无效：{package_id}")
            package = {"id": package_id, "version": VERSION, "path": str(path), "sha256": digest, "bytes": size}
            bound = bind_package(lab, package, source)
            build_ref = row.get("build_receipt")
            if (not isinstance(build_ref, dict) or Path(build_ref.get("path", "")) != Path(bound["build_receipt"])
                    or build_ref.get("sha256") != bound["build_receipt_sha256"]):
                raise ValueError(f"构建回执与冻结计划不一致：{package_id}")
            build_path = Path(bound["build_receipt"])
            build = json.loads(build_path.read_bytes())
            log_path = Path(build["log"]["path"])
            inputs.extend([(path, digest), (build_path, build_ref["sha256"]), (log_path, build["log"]["sha256"])])
            if package_id == "linux_loong64":
                if targets != ["deepin-deb-loong64"] or row.get("acceptance_receipts"):
                    raise ValueError("龙芯首发范围只能绑定原始首发审计")
                audit_ref = row.get("scope_audit")
                audit_path = Path(audit_ref.get("path", "")) if isinstance(audit_ref, dict) else Path("")
                if (not audit_path.is_file() or audit_path.name != "first-release-audit-20260927.json"
                        or audit_ref.get("sha256") != sha256(audit_path)
                        or audit_path.resolve() != (source_root / "qa/vm-lab/release-preparation/loong64/first-release-audit-20260927.json").resolve()):
                    raise ValueError("龙芯首发审计不是已冻结的原始证据")
                audit = json.loads(audit_path.read_bytes())
                product = audit.get("product", {})
                if (product.get("version") != VERSION or product.get("package_sha256") != digest
                        or product.get("package_bytes") != size or product.get("source_commit") != row.get("source_commit")
                        or product.get("build", {}).get("source_fingerprint") != source
                        or product.get("build", {}).get("status") != "completed"
                        or audit.get("lifecycle_gate", {}).get("status") != "blocked"
                        or not audit.get("explicitly_not_covered")
                        or not set(audit["explicitly_not_covered"]) <= set(limitations + untested)):
                    raise ValueError("龙芯首发审计与包、构建或完整门禁状态不一致")
                if not isinstance(row.get("source_commit"), str) or len(row["source_commit"]) != 40:
                    raise ValueError("龙芯首发源码提交无效")
                scopes = audit.get("scopes")
                if (not isinstance(scopes, list) or len(scopes) != len(LOONG_SCOPE_STATUSES)
                        or {s.get("id"): s.get("status") for s in scopes} != LOONG_SCOPE_STATUSES):
                    raise ValueError("龙芯原始验收范围无效")
                na = [s for s in scopes if s.get("status") == "not_applicable_candidate"]
                if len(na) != 1 or na[0].get("id") != "old-same-architecture-upgrade-candidate":
                    raise ValueError("仅龙芯旧版同架构升级候选可标记 N/A")
                if any(s.get("package_sha256") != digest for s in scopes):
                    raise ValueError("龙芯原始验收范围或包绑定无效")
                for section in (product["build"], audit["lifecycle_gate"], *scopes):
                    inputs.extend((Path(e["path"]), e["sha256"]) for e in _checked_evidence(section.get("evidence"), label="龙芯首发"))
                raw_build = json.loads(Path(product["build"]["evidence"][0]["path"]).read_bytes())
                raw_lifecycle = json.loads(Path(audit["lifecycle_gate"]["evidence"][0]["path"]).read_bytes())
                if (raw_build.get("status") != "completed" or raw_build.get("exit_code") != 0
                        or raw_build.get("source_before") != source or raw_build.get("source_after") != source
                        or raw_lifecycle.get("status") != "blocked"
                        or raw_lifecycle.get("context", {}).get("package", {}).get("sha256") != digest
                        or raw_lifecycle.get("context", {}).get("source_fingerprint") != source
                        or raw_lifecycle.get("context", {}).get("environment", {}).get("vm_uuid") != product.get("guest_uuid")):
                    raise ValueError("龙芯原始构建或生命周期回执与首发审计不符")
                inputs.append((audit_path, audit_ref["sha256"]))
                evidence = [{"target": targets[0], "scope_audit_sha256": audit_ref["sha256"],
                             "scopes": [{"id": s["id"], "status": s["status"]} for s in scopes],
                             "full_lifecycle_verified": False}]
            else:
                if row.get("scope_audit") or row.get("source_commit") is not None:
                    raise ValueError("旧包不接受无原始提交依据或龙芯审计：" + package_id)
                frozen = frozen_by_id.get(package_id)
                if (frozen is None or frozen.get("artifact", {}).get("sha256") != digest
                        or frozen["artifact"].get("bytes") != size
                        or frozen.get("build", {}).get("source_fingerprint") != source
                        or frozen["build"].get("receipt_sha256") != build_ref["sha256"]
                        or Path(frozen["build"].get("receipt_path", "")) != build_path
                        or build.get("package", {}).get("provenance_status") != frozen["build"].get("provenance_status")
                        or not set(frozen["acceptance"].get("limitations", []) + frozen["build"].get("limitations", []))
                        <= set(limitations + untested)):
                    raise ValueError(f"旧包与六包冻结快照不一致：{package_id}")
                refs = row.get("acceptance_receipts")
                if not isinstance(refs, list) or len(refs) != len(targets):
                    raise ValueError(f"验收回执数量与目标不符：{package_id}")
                evidence = []
                for ref, target in zip(refs, targets, strict=True):
                    receipt_path = Path(ref.get("path", "")) if isinstance(ref, dict) else Path("")
                    if (not receipt_path.is_file() or receipt_path.name not in {"user-acceptance-20260913.json", "user-acceptance-20260919.json"}
                            or receipt_path.resolve().parent != (lab_root / "reports" / target).resolve()
                            or ref.get("sha256") != sha256(receipt_path)):
                        raise ValueError(f"原验收回执路径或摘要不匹配：{target}")
                    receipt = json.loads(receipt_path.read_bytes())
                    expected_scope = WIN7_APPROVED_SCOPE if package_id == "windows7_amd64" else "user-approved-product-use-acceptance"
                    if (receipt.get("target") != target or receipt.get("status") != "passed"
                            or receipt.get("scope") != expected_scope
                            or (receipt.get("version") != VERSION if package_id != "windows7_amd64" else receipt.get("version") not in {None, VERSION})
                            or receipt.get("package_sha256") != digest
                            or receipt.get("source_fingerprint") not in {None, source}
                            or receipt.get("full_lifecycle_verified") is not False
                            or not set(receipt.get("limitations") or []) <= set(limitations + untested)
                            or receipt_path != Path(frozen["acceptance"]["path"])
                            or ref["sha256"] != frozen["acceptance"]["sha256"]):
                        raise ValueError(f"原验收回执的范围或包身份不符：{target}")
                    for e in _checked_evidence(receipt.get("evidence"), label=target):
                        inputs.append((Path(e["path"]), e["sha256"]))
                    inputs.append((receipt_path, ref["sha256"]))
                    evidence.append({"target": target, "receipt_sha256": ref["sha256"], "scope": receipt["scope"],
                                     "full_lifecycle_verified": False, "limitations": receipt.get("limitations", [])})
            accepted.append({"id": package_id, "filename": path.name, "platform": spec["platform"],
                             "package_sha256": digest, "size": size, "source_fingerprint": source,
                             "source_commit": row.get("source_commit"), "build_receipt_sha256": bound["build_receipt_sha256"],
                             "build_log_sha256": build["log"]["sha256"], "targets": evidence,
                             "build_provenance_status": build.get("package", {}).get("provenance_status"),
                             "runtime_source_fingerprint": build.get("runtime_source_fingerprint"),
                             "limitations": limitations, "untested": untested,
                             "untested_targets": row["untested_targets"], "full_lifecycle_verified": False})
    return {"scope": "explicit-user-approved-package-use-and-loong64-first-release",
            "full_lifecycle_verified": False, "plan_sha256": hashlib.sha256(plan_bytes).hexdigest(),
            "packages": accepted, "deferred_packages": deferred,
            "_input_hashes": inputs}


def _build_scoped_manifest(root: Path, output: Path, source_root: Path, lab_root: Path,
                           plan_path: Path, generated_at: str, private_key_path: Path,
                           public_key_path: Path) -> dict[str, object]:
    acceptance = _scoped_acceptance(root, plan_path, source_root, lab_root)
    key, public_key, signing_fingerprint = _verified_key(private_key_path, public_key_path)
    assets = []
    included_names = {row["filename"] for row in acceptance["packages"]}
    for path in sorted(root / name for name in included_names):
        asset: dict[str, object] = {"filename": path.name, "size": path.stat().st_size, "sha256": sha256(path)}
        asset["signature"] = _sign_payload(key, asset)
        assets.append(asset)
    for path, expected in acceptance.pop("_input_hashes"):
        if sha256(path) != expected:
            raise ValueError(f"限域发布输入在签名前发生变化：{path}")
    expected_assets = {item["filename"]: item["package_sha256"] for item in acceptance["packages"]}
    if any(asset["sha256"] != expected_assets[asset["filename"]] for asset in assets if asset["filename"] in expected_assets):
        raise ValueError("验收后安装包在清单生成期间变化")
    tooling_commit = subprocess.run(["git", "-C", str(source_root), "rev-parse", "HEAD"],
                                    check=True, capture_output=True, text=True).stdout.strip()
    manifest: dict[str, object] = {
        "schema_version": 4, "product": "PartyOps", "version": VERSION, "release_tag": f"v{VERSION}",
        "release_tooling_commit": tooling_commit,
        "release_tooling_file_sha256": sha256(Path(__file__)),
        "acceptance": acceptance, "generated_at": generated_at, "timezone": "Asia/Shanghai (UTC+8)",
        "release_type": "prerelease", "prerelease": True, "make_latest": False, "signed": True,
        "public_key": public_key, "signing_fingerprint_sha256": signing_fingerprint,
        "packaged_platforms": [row["platform"] for row in acceptance["packages"]],
        "unavailable_platforms": [RELEASE_PACKAGES[row["id"]]["platform"] for row in acceptance["deferred_packages"]],
        "scope_verified_platforms": [row["platform"] for row in acceptance["packages"]],
        "full_lifecycle_verified": False,
        "limitations": ["逐包验收仅限 acceptance.packages 所列原始范围；完整十三场景生命周期未通过。",
                        "原始 Loong64 首发审计的旧版同架构升级候选为 N/A，不代表升级已验证。"],
        "assets": assets,
    }
    manifest["signature"] = _sign_payload(key, manifest)
    return manifest


def build_manifest(
    *,
    root: Path,
    output: Path,
    qa_report_path: Path | None,
    source_root: Path,
    lab_root: Path,
    generated_at: str,
    private_key_path: Path,
    public_key_path: Path,
    release_mode: str = "full",
    scope_plan_path: Path | None = None,
) -> dict[str, object]:
    timestamp = datetime.fromisoformat(generated_at)
    if timestamp.tzinfo is None:
        raise ValueError("清单生成时间必须包含时区")
    root = root.resolve()
    private_key_path = private_key_path.resolve()
    public_key_path = public_key_path.resolve()
    if private_key_path == root or root in private_key_path.parents:
        raise ValueError("正式发布私钥禁止放入公开制品目录")
    if release_mode not in {"full", "scoped"}:
        raise ValueError("未知发布模式")
    if release_mode == "scoped":
        if scope_plan_path is None or qa_report_path is not None:
            raise ValueError("限域模式必须提供冻结计划且不得传入完整矩阵报告")
        return _build_scoped_manifest(root, output, source_root.resolve(), lab_root.resolve(),
                                      scope_plan_path.resolve(), generated_at, private_key_path, public_key_path)
    if qa_report_path is None or scope_plan_path is not None:
        raise ValueError("完整模式必须提供完整矩阵报告且不得传入限域计划")
    missing = sorted(name for name in INSTALLERS if not (root / name).is_file())
    if missing:
        raise FileNotFoundError(f"缺少当前发布矩阵安装包：{', '.join(missing)}")
    stale = sorted(
        path.name
        for path in root.iterdir()
        if path.is_file()
        and path.suffix.lower() in {".exe", ".deb", ".rpm", ".pkg"}
        and path.name not in INSTALLERS
    )
    if stale:
        raise ValueError(f"发布目录含旧版或未知安装包：{', '.join(stale)}")

    acceptance = _acceptance(root, qa_report_path.resolve(), source_root.resolve(), lab_root.resolve())
    key, public_key, signing_fingerprint = _verified_key(
        private_key_path, public_key_path
    )
    native_verified_platforms, emulated_verified_platforms, virtualized_verified_platforms = [], [], []
    for platform in INSTALLERS.values():
        kinds = {item["environment_type"] for item in acceptance["environments"] if item["platform"] == platform}
        if kinds == {"native-host"}:
            native_verified_platforms.append(platform)
        if kinds.intersection({"full-system-emulated", "experimental-runtime"}):
            emulated_verified_platforms.append(platform)
        if "hardware-virtualized" in kinds:
            virtualized_verified_platforms.append(platform)

    assets = []
    for path in sorted(item for item in root.iterdir() if item.is_file()):
        if path.resolve() == output.resolve():
            continue
        asset: dict[str, object] = {
            "filename": path.name,
            "size": path.stat().st_size,
            "sha256": sha256(path),
        }
        asset["signature"] = _sign_payload(key, asset)
        assets.append(asset)
    checked_hashes = {item["platform"]: item["package_sha256"] for item in acceptance["builds"]}
    for asset in assets:
        platform = INSTALLERS.get(asset["filename"])
        if platform and asset["sha256"] != checked_hashes[platform]:
            raise ValueError(f"验收后安装包在清单生成期间变化：{asset['filename']}")
    if _current_source(source_root.resolve()) != (acceptance["source_commit"], acceptance["source_fingerprint"]):
        raise ValueError("源码在清单生成期间变化")
    manifest: dict[str, object] = {
        "schema_version": 5,
        "product": "PartyOps",
        "version": "1.4.5-rc.6",
        "release_tag": "v1.4.5-rc.6",
        "source_commit": acceptance["source_commit"],
        "release_tooling_commit": acceptance["source_commit"],
        "source_fingerprint": acceptance["source_fingerprint"],
        "acceptance": acceptance,
        "generated_at": generated_at,
        "timezone": "Asia/Shanghai (UTC+8)",
        "release_type": "prerelease",
        "prerelease": True,
        "make_latest": False,
        "signed": True,
        "public_key": public_key,
        "signing_fingerprint_sha256": signing_fingerprint,
        "packaged_platforms": list(INSTALLERS.values()),
        "unavailable_platforms": list(UNAVAILABLE_INSTALLERS.values()),
        "verified_platforms": list(INSTALLERS.values()),
        "native_verified_platforms": list(native_verified_platforms),
        "emulated_verified_platforms": list(emulated_verified_platforms),
        "virtualized_verified_platforms": virtualized_verified_platforms,
        "native_machine_validation": bool(native_verified_platforms) and len(native_verified_platforms) == len(RELEASE_PACKAGES),
        "limitations": [
            "验收环境种类逐项见 acceptance.environments；虚拟或模拟环境通过不代表用户物理设备验证。",
            "Ed25519 清单签名不等同于 Windows/macOS 平台代码签名或 Apple 公证，相关状态须另行核验。",
        ],
        "assets": assets,
    }
    manifest["signature"] = _sign_payload(key, manifest)
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--qa-report", type=Path, help="完整活动矩阵全部必需环境通过的最终 qa-report.json")
    parser.add_argument("--release-mode", choices=("full", "scoped"), default="full")
    parser.add_argument("--scope-plan", type=Path, help="限域发布的冻结计划 JSON；仅 --release-mode scoped 可用")
    parser.add_argument("--source-root", required=True, type=Path, help="已冻结源码及实验室脚本所在仓库")
    parser.add_argument("--lab-root", required=True, type=Path, help="保存控制器状态、构建回执及原始证据的本地实验室")
    parser.add_argument("--generated-at", required=True)
    parser.add_argument("--private-key", required=True, type=Path)
    parser.add_argument(
        "--public-key",
        required=True,
        type=Path,
        help="客户端内置 Ed25519 信任公钥",
    )
    args = parser.parse_args()
    if args.release_mode == "full" and args.qa_report is None:
        parser.error("完整模式必须提供 --qa-report")
    if args.release_mode == "scoped" and (args.scope_plan is None or args.qa_report is not None):
        parser.error("限域模式必须提供 --scope-plan 且不得提供 --qa-report")
    root = args.root.resolve()
    output = args.output.resolve()
    payload = build_manifest(
        root=root,
        output=output,
        qa_report_path=args.qa_report,
        source_root=args.source_root,
        lab_root=args.lab_root,
        generated_at=args.generated_at,
        private_key_path=args.private_key.resolve(),
        public_key_path=args.public_key.resolve(),
        release_mode=args.release_mode,
        scope_plan_path=args.scope_plan,
    )
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8",
            newline="\n",
            dir=output.parent,
            prefix=f".{output.name}.",
            suffix=".incoming",
            delete=False,
        ) as handle:
            temporary = Path(handle.name)
            json.dump(payload, handle, ensure_ascii=False, indent=2)
            handle.write("\n")
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, output)
        temporary = None
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
    print(f"最终发布清单已生成：{output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
