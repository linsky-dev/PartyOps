from __future__ import annotations

import base64
import copy
import importlib.util
import json
import os
import subprocess
import sys
from contextlib import contextmanager
from pathlib import Path

import pytest
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey

# 仅在发布修复暂存验证时覆盖；应用补丁后默认使用当前仓库，不修改生产路径。
ROOT = Path(os.environ.get("PARTYOPS_RELEASE_TEST_ROOT", Path(__file__).resolve().parents[2]))
SCRIPT = Path(os.environ.get("PARTYOPS_RELEASE_TEST_SCRIPT", ROOT / "scripts/generate-release-bundle-manifest.py"))
SPEC = importlib.util.spec_from_file_location("release_bundle_manifest", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def _installers(root: Path) -> None:
    for name in MODULE.INSTALLERS:
        (root / name).write_bytes(name.encode("utf-8"))


def _release_key(root: Path) -> tuple[Path, Path, Ed25519PrivateKey]:
    root.mkdir(parents=True, exist_ok=True)
    key = Ed25519PrivateKey.generate()
    private = root / "release-private.pem"
    public = root / "release-public.txt"
    private.write_bytes(
        key.private_bytes(
            serialization.Encoding.PEM,
            serialization.PrivateFormat.PKCS8,
            serialization.NoEncryption(),
        )
    )
    public.write_text(
        base64.b64encode(
            key.public_key().public_bytes(
                serialization.Encoding.Raw,
                serialization.PublicFormat.Raw,
            )
        ).decode("ascii"),
        encoding="ascii",
    )
    return private, public, key


def _json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value), encoding="utf-8")


@pytest.fixture
def accepted(tmp_path: Path, monkeypatch):
    """合成成功样本替代实体快照、Git 身份及当前已知缺能力项，真实证据验证仍执行。"""
    artifacts = tmp_path / "artifacts"
    artifacts.mkdir()
    _installers(artifacts)
    private, public, key = _release_key(tmp_path / "keys")
    lab_root = tmp_path / "lab"
    fingerprint = "b" * 64
    with MODULE._laboratory(ROOT, lab_root) as (controller, lab):
        import identity
        import native_windows
        import windows_execution
        # 当前旧包的 AI 缺项在专门反例中恢复验证；这里仅构造发布器签名成功路径。
        monkeypatch.setattr(windows_execution, "PROFILE_BLOCKERS", {})
        matrix = lab.matrix
        expected = {}
        report = {"version": MODULE.VERSION, "required_packages": len(MODULE.RELEASE_PACKAGES), "passed_packages": len(MODULE.RELEASE_PACKAGES),
                  "all_packages_runtime_gate": "passed", "source_fingerprint": fingerprint,
                  "generated_at": "2026-09-08T10:00:00+08:00", "package_errors": {},
                  "packages": [], "package_inventory": {}}
        for package_id, release in MODULE.RELEASE_PACKAGES.items():
            name = release["filename"]
            path = artifacts / name
            package = {"id": package_id, "version": MODULE.VERSION, "path": str(path),
                       "sha256": MODULE.sha256(path), "bytes": path.stat().st_size}
            log = lab_root / "reports" / package_id / "build.log"
            log.parent.mkdir(parents=True, exist_ok=True)
            log.write_text("本地测试构建成功", encoding="utf-8")
            receipt = lab_root / "state/builds" / (package["sha256"] + ".json")
            _json(receipt, {"status": "completed", "errors": [], "package": package,
                            "source_before": fingerprint, "source_after": fingerprint,
                            "exit_code": 0, "output_created_during_build": True,
                            "log": {"path": str(log), "sha256": MODULE.sha256(log)}})
            report["package_inventory"][package_id] = {
                **package, "source_fingerprint": fingerprint, "provenance_status": "verified",
                "build_receipt": str(receipt), "build_receipt_sha256": MODULE.sha256(receipt)}
            targets = matrix["packages"][package_id]["required_targets"]
            row = {"id": package_id, "required_targets": targets, "runtime_environment_passed": True,
                   "package_sha256": package["sha256"], "targets": {}}
            for target in targets:
                spec = matrix["targets"][target]
                evidence = lab_root / "reports" / target / "result.json"
                evidence.parent.mkdir(parents=True, exist_ok=True)
                proof = evidence.parent / "lifecycle.txt"
                proof.write_text("完整场景测试证据", encoding="utf-8")
                expected[target] = {"vm_uuid": target + "-uuid", "media_sha256": "a" * 64,
                                    "baseline_id": target + "-baseline", "identity_sha256": "c" * 64}
                environment = "full-system-emulated" if spec["arch"] in {"arm64", "aarch64", "loongarch64"} else "hardware-virtualized"
                system = {field: spec[field] for field in (
                    "os", "arch", "os_release", "os_build", "edition", "distribution_id", "distribution"
                ) if field in spec}
                before, after = "before", "after"
                system.update(boot_id=before, environment_type=environment)
                if spec["backend"] == "native-host":
                    before, after = "2026-09-08T01:00:00Z", "2026-09-08T02:00:00Z"
                    environment = "native-host"
                    system.update(boot_id=before, environment_type=environment, os_version="10.0.26200",
                                  os_build="26200.9168", display_version="25H2", edition="Professional", host_id="e" * 64)
                    expected[target] = {"backend": "native-host", "host_id": system["host_id"],
                                        "identity_sha256": native_windows.identity_digest(system),
                                        "current_boot_id": after, "observed_boot_ids": [before, after]}
                if spec.get("firmware"):
                    from firmware import FIELDS
                    expected[target].update({field: "f" * 64 for field in FIELDS})
                extra = {}
                if spec.get("windows_execution_contract") == 1:
                    system.update(os_isa=spec["arch"], isa_probe_api="IsWow64Process2", os_version="10.0." + spec.get("os_build", "19045"),
                                  secure_boot_enabled=True, tpm_spec_version="2.0", installation_requirement_bypasses=False)
                    runtime = {"schema_version": 1, "pid": 42, "owner_sid": "S-1-5-21-1-2-3-1001", "is_admin": False,
                               "executable_path": "C:\\PartyOps-QA\\PartyOps.exe", "guest_uuid": expected[target]["vm_uuid"],
                               "boot_id": before, "os_version": system["os_version"], "os_isa": spec["arch"],
                               "process_isa": spec["package_arch"], "probe_api": "IsWow64Process2", "pe_isa": spec["package_arch"],
                               "package_isa": spec["package_arch"], "execution_kind": spec["application_execution"],
                               "package_platform": package_id.rsplit("_", 1)[0], "package_version": package["version"],
                               "runtime_profile": spec["expected_runtime_profile"], "installer_sha256": package["sha256"],
                               "installed_executable_sha256": "a" * 64, "installed_manifest_sha256": "b" * 64,
                               "manifest_executable_sha256": "a" * 64, "installed_executable_size": 100, "manifest_executable_size": 100}
                    runtime_path = evidence.parent / "windows-runtime.json"
                    _json(runtime_path, runtime)
                    extra["windows_runtime_evidence"] = {"path": runtime_path.name, "sha256": MODULE.sha256(runtime_path)}
                _json(evidence, {"schema_version": 2, "run_id": "local-fixture-123", "target": target,
                                 "generated_at": report["generated_at"], "status": "passed",
                                 "source_fingerprint": fingerprint, "package": package, "system": system,
                                 "environment": expected[target], "distribution_match": True,
                                 "restart": {"actual": True, "boot_id_before": before, "boot_id_after": after}, **extra,
                                 "cases": [{"id": case, "status": "passed", "evidence": [
                                     {"path": proof.name, "sha256": MODULE.sha256(proof)}
                                 ]} for case in matrix["required_cases"]]})
                row["targets"][target] = {"target": target, "status": "passed", "errors": [],
                                          "package_sha256": package["sha256"], "runtime_environment_passed": True,
                                          "evidence_path": str(evidence), "environment_type": environment}
            report["packages"].append(row)
        monkeypatch.setattr(identity, "runtime_binding", lambda _lab, target: expected[target])
        @contextmanager
        def laboratory(*_arguments):
            yield controller, lab
        monkeypatch.setattr(MODULE, "_laboratory", laboratory)
        monkeypatch.setattr(MODULE, "_current_source", lambda _root: ("d" * 40, fingerprint))
        report_path = lab_root / "reports/final/qa-report.json"
        _json(report_path, report)
        arguments = {"root": artifacts, "output": artifacts / "manifest.json", "qa_report_path": report_path,
                     "source_root": ROOT, "lab_root": lab_root, "generated_at": "2026-09-08T11:00:00+08:00",
                     "private_key_path": private, "public_key_path": public}
        yield arguments, report, key


def test_manifest_derives_verified_environments_and_signs(accepted):
    arguments, _report, key = accepted
    payload = MODULE.build_manifest(**arguments)
    assert payload["schema_version"] == 5
    assert payload["prerelease"] is True and payload["make_latest"] is False
    assert payload["signed"] is True
    assert payload["verified_platforms"] == list(MODULE.INSTALLERS.values())
    assert payload["native_verified_platforms"] == []
    assert payload["native_machine_validation"] is False
    assert len(payload["acceptance"]["environments"]) == len(MODULE.TARGET_REQUIREMENTS)
    assert len(payload["acceptance"]["builds"]) == len(MODULE.RELEASE_PACKAGES)
    assert "linux-deb/loong64" in payload["emulated_verified_platforms"]
    arm = next(item for item in payload["acceptance"]["environments"] if item["target"] == "win11-arm64")
    assert arm["os_isa"] == "arm64" and arm["package_isa"] == "x86_64"
    assert arm["application_execution"] == "windows-x64-emulation"
    assert payload["acceptance"]["required_packages"] == len(MODULE.RELEASE_PACKAGES)
    assert len(payload["acceptance"]["matrix_contract_sha256"]) == 64
    assert "macos/arm64" in payload["emulated_verified_platforms"]
    assert "supplemental_sources" not in payload
    assert payload["source_commit"] == "d" * 40
    unsigned = dict(payload)
    signature = base64.b64decode(unsigned.pop("signature"))
    canonical = json.dumps(unsigned, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    key.public_key().verify(signature, canonical)


@pytest.mark.parametrize("change", ["zero", "missing_target", "source", "package", "receipt", "log"])
def test_manifest_rejects_stale_or_incomplete_evidence(accepted, change):
    arguments, report, _key = accepted
    first = report["package_inventory"]["windows_amd64"]
    if change == "zero":
        report["passed_packages"] = 0
    elif change == "missing_target":
        del report["packages"][0]["targets"]["win10-x64"]
    elif change == "source":
        report["source_fingerprint"] = "f" * 64
    elif change == "package":
        Path(first["path"]).write_bytes(b"changed installer")
    elif change == "receipt":
        receipt = Path(first["build_receipt"])
        value = json.loads(receipt.read_text(encoding="utf-8"))
        value["source_before"] = "e" * 64
        _json(receipt, value)
    else:
        receipt = json.loads(Path(first["build_receipt"]).read_text(encoding="utf-8"))
        Path(receipt["log"]["path"]).write_text("changed", encoding="utf-8")
    _json(arguments["qa_report_path"], report)
    with pytest.raises(ValueError):
        MODULE.build_manifest(**arguments)
    assert not arguments["output"].exists()


@pytest.mark.parametrize("change", ["deepin", "process_restart", "partial", "case_removed", "hosted", "proof_changed", "wrong_uuid"])
def test_manifest_rechecks_raw_guest_evidence(accepted, change):
    arguments, report, _key = accepted
    row = next(item for item in report["packages"] if item["id"] == "linux_amd64")
    path = Path(row["targets"]["uos-deb-x64"]["evidence_path"])
    value = json.loads(path.read_text(encoding="utf-8"))
    if change == "deepin":
        value["system"]["distribution"] = "Deepin"
        value["distribution_match"] = False
    elif change == "process_restart":
        value["restart"]["boot_id_after"] = "before"
    elif change == "partial":
        value["status"] = "partial"
    elif change == "case_removed":
        value["cases"].pop()
    elif change == "hosted":
        value["system"]["environment_type"] = "hosted-runner"
    elif change == "wrong_uuid":
        value["environment"]["vm_uuid"] = "a-foreign-guest"
    else:
        (path.parent / "lifecycle.txt").write_text("changed", encoding="utf-8")
    _json(path, value)
    with pytest.raises(ValueError, match="原始环境证据复核失败"):
        MODULE.build_manifest(**arguments)


def test_manifest_rejects_handwritten_pass_and_remote_workflow_fields(accepted):
    arguments, _report, _key = accepted
    with pytest.raises(TypeError):
        MODULE.build_manifest(**arguments, verified_platforms=("macos/arm64",))
    with pytest.raises(TypeError):
        MODULE.build_manifest(**arguments, macos_build_run="unverified")


def test_manifest_rejects_old_installer(accepted):
    arguments, _report, _key = accepted
    (arguments["root"] / "PartyOps_1.4.3-rc.4_windows_amd64.exe").write_bytes(b"old")
    with pytest.raises(ValueError, match="旧版或未知安装包"):
        MODULE.build_manifest(**arguments)


def test_authorized_catalog_keeps_ten_packages_and_fourteen_environments():
    with MODULE._laboratory(ROOT, Path("D:/PartyOps-VM-Lab")) as (_controller, lab):
        contract = MODULE._matrix_contract(lab.matrix)
    assert contract["required_packages"] == 10
    assert contract["required_environments"] == 14
    assert MODULE.RELEASE_PACKAGES["windows_amd64"]["targets"] == ("win11-x64-native", "win10-x64", "win11-arm64")
    assert MODULE.RELEASE_PACKAGES["windows7_x86"]["targets"] == ("win7-x86", "win10-x86", "win10-arm64")
    assert MODULE.RELEASE_PACKAGES["linux_loong64"]["targets"] == ("deepin-deb-loong64",)


@pytest.mark.parametrize("change", ["remove_compatibility", "replace_same_count", "remove_case", "remove_loong", "remote"])
def test_editable_matrix_cannot_reduce_frozen_release_requirements(change):
    with MODULE._laboratory(ROOT, Path("D:/PartyOps-VM-Lab")) as (_controller, lab):
        matrix = copy.deepcopy(lab.matrix)
    if change in {"remove_compatibility", "replace_same_count"}:
        matrix["packages"]["windows_amd64"]["required_targets"].remove("win11-arm64")
        removed = matrix["targets"].pop("win11-arm64")
        if change == "replace_same_count":
            matrix["targets"]["historical-substitute"] = removed
            matrix["packages"]["windows_amd64"]["required_targets"].append("historical-substitute")
            assert len(matrix["targets"]) == len(MODULE.TARGET_REQUIREMENTS)
        assert len(matrix["packages"]) == len(MODULE.RELEASE_PACKAGES)
    elif change == "remove_case":
        matrix["required_cases"].remove("offline_models")
    elif change == "remove_loong":
        matrix["packages"].pop("linux_loong64")
        matrix["targets"].pop("deepin-deb-loong64")
    else:
        matrix["targets"]["macos-arm64"]["hosted_fallback"] = True
    with pytest.raises(ValueError, match="矩阵|十三项|执行层"):
        MODULE._matrix_contract(matrix)


@pytest.mark.parametrize("target,field,value", [
    ("win11-arm64", "arch", "x86_64"), ("win10-arm64", "windows_execution_contract", 0),
    ("win11-arm64", "application_execution", "native"), ("win10-arm64", "package_arch", "arm64"),
    ("win11-arm64", "requires_uefi_secureboot_tpm2", False), ("win11-x64-native", "backend", "qemu"),
    ("deepin-deb-loong64", "firmware", None), ("uos-deb-x64", "distribution_id", "deepin"),
])
def test_frozen_matrix_rejects_identity_or_execution_contract_changes(target, field, value):
    with MODULE._laboratory(ROOT, Path("D:/PartyOps-VM-Lab")) as (_controller, lab):
        matrix = copy.deepcopy(lab.matrix)
    matrix["targets"][target][field] = value
    with pytest.raises(ValueError, match="执行层合同"):
        MODULE._matrix_contract(matrix)


@pytest.mark.parametrize("target", ["win10-x86", "win10-arm64", "win11-arm64", "deepin-deb-loong64"])
def test_report_cannot_drop_environment_while_package_count_stays_same(accepted, target):
    arguments, report, _key = accepted
    row = next(item for item in report["packages"] if target in item["required_targets"])
    row["required_targets"] = [item for item in row["required_targets"] if item != target]
    del row["targets"][target]
    _json(arguments["qa_report_path"], report)
    with pytest.raises(ValueError, match="必需环境门禁不完整"):
        MODULE.build_manifest(**arguments)


def test_legacy_nine_package_report_is_rejected(accepted):
    arguments, report, _key = accepted
    report["required_packages"] = report["passed_packages"] = 9
    report["packages"] = [item for item in report["packages"] if item["id"] != "linux_loong64"]
    report["package_inventory"].pop("linux_loong64")
    _json(arguments["qa_report_path"], report)
    with pytest.raises(ValueError, match="完整活动矩阵生命周期门禁"):
        MODULE.build_manifest(**arguments)


def test_missing_loong_installer_cannot_be_declared_unavailable(accepted):
    arguments, _report, _key = accepted
    (arguments["root"] / MODULE.RELEASE_PACKAGES["linux_loong64"]["filename"]).unlink()
    with pytest.raises(FileNotFoundError, match="linux_loong64"):
        MODULE.build_manifest(**arguments)


@pytest.mark.parametrize("change", ["missing_runtime", "native_claim", "wrong_package_isa", "loong_firmware"])
def test_new_environment_raw_evidence_is_rechecked(accepted, change):
    arguments, report, _key = accepted
    target = "deepin-deb-loong64" if change == "loong_firmware" else "win11-arm64"
    row = next(item for item in report["packages"] if target in item["required_targets"])
    path = Path(row["targets"][target]["evidence_path"])
    result = json.loads(path.read_text(encoding="utf-8"))
    if change == "loong_firmware":
        result["environment"].pop("baseline_vars_sha256")
    elif change == "missing_runtime":
        result.pop("windows_runtime_evidence")
    else:
        runtime_path = path.parent / result["windows_runtime_evidence"]["path"]
        runtime = json.loads(runtime_path.read_text(encoding="utf-8"))
        if change == "native_claim":
            runtime["execution_kind"] = "native"
        else:
            runtime["package_isa"] = "arm64"
        _json(runtime_path, runtime)
        result["windows_runtime_evidence"]["sha256"] = MODULE.sha256(runtime_path)
    _json(path, result)
    with pytest.raises(ValueError, match="原始环境证据复核失败"):
        MODULE.build_manifest(**arguments)


@pytest.mark.parametrize("package_id,error", [
    ("windows7_x86", "WINDOWS_LEGACY_CORE_OFFLINE_AI_NOT_BUNDLED"),
    ("windows7_amd64", "WINDOWS_LEGACY_SMART_LOCAL_LLM_NOT_BUNDLED"),
])
def test_known_legacy_package_capability_gap_blocks_real_release(accepted, monkeypatch, package_id, error):
    import windows_execution
    arguments, _report, _key = accepted
    monkeypatch.setattr(windows_execution, "PROFILE_BLOCKERS", {package_id: error})
    with pytest.raises(ValueError, match=error):
        MODULE.build_manifest(**arguments)


def test_manifest_rejects_mismatched_or_public_private_key(accepted, tmp_path):
    arguments, _report, _key = accepted
    _private, public, _key = _release_key(tmp_path / "another-key")
    with pytest.raises(ValueError, match="不匹配"):
        MODULE.build_manifest(**{**arguments, "public_key_path": public})
    private, public, _key = _release_key(arguments["root"] / "leaked-keys")
    with pytest.raises(ValueError, match="禁止放入公开制品目录"):
        MODULE.build_manifest(**{**arguments, "private_key_path": private, "public_key_path": public})


@pytest.mark.parametrize("change", ["clean", "missing_quality_gate", "stale_quality_gate", "dirty", "untracked"])
def test_current_source_requires_quality_gate_and_frozen_git_content(tmp_path, change):
    source = tmp_path / "source"
    (source / "scripts").mkdir(parents=True)
    gate_path = source / "scripts/verify-full-function-gate.py"
    gate_path.write_bytes((ROOT / "scripts/verify-full-function-gate.py").read_bytes())
    product = source / "backend/app/fixture.py"
    product.parent.mkdir(parents=True)
    product.write_text("FIXTURE = 1\n", encoding="utf-8")
    website = source / "website/src/fixture.js"
    website.parent.mkdir(parents=True)
    website.write_text("// 仅用于隔离测试的门禁样本\n", encoding="utf-8")
    def git(*arguments):
        return subprocess.run(["git", "-C", str(source), "-c", "user.name=Release fixture",
                               "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false",
                               *arguments], check=True, capture_output=True, encoding="utf-8").stdout.strip()
    git("init")
    git("add", "scripts", "backend", "website")
    git("commit", "-m", "isolated release fixture")
    gate = MODULE._load_module(gate_path, "fixture_quality_gate")
    # 门禁记录仅写入 pytest 的隔离目录，不修改真实仓库或其质量记录。
    if change != "missing_quality_gate":
        assert gate.record(source) == 0
    if change in {"dirty", "stale_quality_gate"}:
        product.write_text("FIXTURE = 2\n", encoding="utf-8")
    if change == "untracked":
        (product.parent / "new.py").write_text("NEW = True\n", encoding="utf-8")
    if change in {"dirty", "untracked"}:
        assert gate.record(source) == 0
    if change == "clean":
        commit, fingerprint = MODULE._current_source(source)
        assert commit == git("rev-parse", "HEAD")
        assert fingerprint == gate.source_fingerprint(source, "package")[0]
    else:
        with pytest.raises(ValueError, match="质量门禁|干净提交"):
            MODULE._current_source(source)


@pytest.mark.parametrize(
    ("platform", "architecture", "runtime_profile"),
    [
        ("windows", "amd64", "full"),
        ("windows7", "amd64", "legacy-smart"),
        ("windows7", "x86", "legacy-core"),
    ],
)
def test_embedded_windows_manifest_preserves_target_identity(
    tmp_path: Path,
    platform: str,
    architecture: str,
    runtime_profile: str,
) -> None:
    bundle = tmp_path / "bundle"
    bundle.mkdir()
    (bundle / "PartyOps.exe").write_bytes(b"MZ")
    output = bundle / "release-manifest.json"
    subprocess.run(
        [
            sys.executable,
            str(ROOT / "scripts" / "generate-release-manifest.py"),
            "--root",
            str(bundle),
            "--output",
            str(output),
            "--version",
            "1.4.3-rc.8",
            "--tag",
            "v1.4.3-rc.8",
            "--commit",
            "a" * 40,
            "--platform",
            platform,
            "--architecture",
            architecture,
            "--runtime-profile",
            runtime_profile,
        ],
        check=True,
    )
    manifest = json.loads(output.read_text(encoding="utf-8"))
    assert manifest["platform"] == platform
    assert manifest["architecture"] == architecture
    assert manifest["runtime_profile"] == runtime_profile
