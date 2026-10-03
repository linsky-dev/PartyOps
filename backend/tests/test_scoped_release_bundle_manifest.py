"""限域发布清单的原始证据、包身份和签名回归。"""

from __future__ import annotations

import base64
import importlib.util
import json
from contextlib import contextmanager
from pathlib import Path
from types import SimpleNamespace

import pytest
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("scoped_release_bundle_manifest", ROOT / "scripts/generate-release-bundle-manifest.py")
assert SPEC and SPEC.loader
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")


@pytest.fixture
def scoped(tmp_path: Path, monkeypatch):
    root = tmp_path / "artifacts"
    root.mkdir()
    lab = tmp_path / "lab"
    package_id = "linux_amd64"
    spec = module.RELEASE_PACKAGES[package_id]
    package_path = root / spec["filename"]
    package_path.write_bytes(b"synthetic scoped package")
    digest = module.sha256(package_path)
    fingerprint = "a" * 64
    log = lab / "reports" / "build-synthetic" / "build.log"
    log.parent.mkdir(parents=True)
    log.write_text("构建完成", encoding="utf-8")
    build_path = lab / "state/builds" / f"{digest}.json"
    write_json(build_path, {
        "status": "completed", "errors": [], "package": {"id": package_id, "version": module.VERSION,
        "path": str(package_path), "sha256": digest, "bytes": package_path.stat().st_size},
        "source_before": fingerprint, "source_after": fingerprint, "exit_code": 0,
        "output_created_during_build": True,
        "log": {"path": str(log), "sha256": module.sha256(log)},
    })
    target = "uos-deb-x64"
    proof = lab / "reports" / target / "installed.json"
    write_json(proof, {"status": "passed"})
    receipt_path = lab / "reports" / target / "user-acceptance-20260913.json"
    write_json(receipt_path, {
        "target": target, "status": "passed", "scope": "user-approved-product-use-acceptance",
        "version": module.VERSION, "package_sha256": digest, "source_fingerprint": fingerprint,
        "full_lifecycle_verified": False, "limitations": ["未做完整生命周期。"],
        "evidence": [{"path": str(proof), "sha256": module.sha256(proof)}],
    })
    row = {"id": package_id, "filename": package_path.name, "platform": spec["platform"],
           "size": package_path.stat().st_size, "sha256": digest, "targets": [target], "untested_targets": [],
           "source_fingerprint": fingerprint, "source_commit": None,
           "build_receipt": {"path": str(build_path), "sha256": module.sha256(build_path)},
           "acceptance_receipts": [{"path": str(receipt_path), "sha256": module.sha256(receipt_path)}],
           "limitations": ["未做完整生命周期。"], "untested": ["旧版升级未测。"]}
    plan = {"schema_version": 1, "release_mode": "scoped", "version": module.VERSION,
            "packages": [row], "deferred_packages": [
                {"id": item, "targets": list(module.RELEASE_PACKAGES[item]["targets"]), "reason": "本次不纳入。"}
                for item in module.PACKAGE_IDS if item != package_id]}
    freeze_path = tmp_path / "six-freeze.json"
    frozen = {"id": package_id, "artifact": {"sha256": digest, "bytes": package_path.stat().st_size},
              "build": {"source_fingerprint": fingerprint, "receipt_path": str(build_path),
                        "receipt_sha256": module.sha256(build_path), "provenance_status": None},
              "acceptance": {"path": str(receipt_path), "sha256": module.sha256(receipt_path)}}
    write_json(freeze_path, {"schema_version": 1, "packages": [frozen, *(
        {"id": item} for item in ("windows_amd64", "windows7_amd64", "linux_arm64", "rpm_x86_64", "rpm_aarch64"))]})
    monkeypatch.setattr(module, "SIX_PACKAGE_FREEZE_PATH", freeze_path)
    plan["six_package_freeze"] = {"path": str(freeze_path), "sha256": module.sha256(freeze_path)}
    plan_path = tmp_path / "scope.json"
    write_json(plan_path, plan)
    key = Ed25519PrivateKey.generate()
    key_dir = tmp_path / "keys"
    key_dir.mkdir()
    private = key_dir / "key.pem"
    private.write_bytes(key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8,
                                          serialization.NoEncryption()))
    public = key_dir / "public.txt"
    public.write_text(base64.b64encode(key.public_key().public_bytes(
        serialization.Encoding.Raw, serialization.PublicFormat.Raw)).decode("ascii"), encoding="ascii")
    args = {"root": root, "output": root / "manifest.json", "qa_report_path": None,
            "source_root": ROOT, "lab_root": lab, "generated_at": "2026-09-27T12:00:00+08:00",
            "private_key_path": private, "public_key_path": public,
            "release_mode": "scoped", "scope_plan_path": plan_path}
    return args, plan, key, (package_path, build_path, receipt_path, proof)


def test_scoped_manifest_signs_only_planned_package(scoped):
    args, plan, key, _ = scoped
    result = module.build_manifest(**args)
    assert result["schema_version"] == 4
    assert result["full_lifecycle_verified"] is False
    assert result["acceptance"]["scope"].startswith("explicit-user-approved")
    assert len(result["acceptance"]["packages"]) == 1
    assert len(result["acceptance"]["deferred_packages"]) == len(plan["deferred_packages"])
    assert "source_commit" not in result
    signature = base64.b64decode(result.pop("signature"))
    key.public_key().verify(signature, json.dumps(result, ensure_ascii=False, sort_keys=True,
                                                   separators=(",", ":")).encode("utf-8"))


@pytest.mark.parametrize("change", ["duplicate", "unknown", "missing", "wrong_scope", "wrong_receipt_hash",
                                     "wrong_package_hash", "wrong_target", "bad_source", "extra_installer",
                                     "evidence_changed", "receipt_failed", "missing_untested_target"])
def test_scoped_rejects_invalid_plan_and_inputs(scoped, change):
    args, plan, _, (package_path, _, receipt_path, proof) = scoped
    row = plan["packages"][0]
    if change == "duplicate":
        plan["packages"].append(row.copy())
    elif change == "unknown":
        plan["deferred_packages"][0]["id"] = "unknown"
    elif change == "missing":
        plan["deferred_packages"].pop()
    elif change == "wrong_scope":
        data = json.loads(receipt_path.read_text(encoding="utf-8"))
        data["scope"] = "full-lifecycle"
        write_json(receipt_path, data)
        row["acceptance_receipts"][0]["sha256"] = module.sha256(receipt_path)
    elif change == "wrong_receipt_hash":
        row["acceptance_receipts"][0]["sha256"] = "0" * 64
    elif change == "wrong_package_hash":
        row["sha256"] = "0" * 64
    elif change == "wrong_target":
        row["targets"] = ["win7-x64"]
    elif change == "bad_source":
        row["source_fingerprint"] = "b" * 64
    elif change == "extra_installer":
        (package_path.parent / module.RELEASE_PACKAGES["rpm_x86_64"]["filename"]).write_bytes(b"extra")
    elif change == "evidence_changed":
        proof.write_bytes(b"changed")
    elif change == "receipt_failed":
        data = json.loads(receipt_path.read_text(encoding="utf-8"))
        data["status"] = "failed"
        write_json(receipt_path, data)
        row["acceptance_receipts"][0]["sha256"] = module.sha256(receipt_path)
    elif change == "missing_untested_target":
        row["untested_targets"] = ["win11-arm64"]
    write_json(args["scope_plan_path"], plan)
    with pytest.raises((ValueError, RuntimeError)):
        module.build_manifest(**args)


def test_full_and_scoped_inputs_are_not_interchangeable(scoped):
    args, _, _, _ = scoped
    with pytest.raises(ValueError):
        module.build_manifest(**{**args, "scope_plan_path": None})
    with pytest.raises(ValueError):
        module.build_manifest(**{**args, "release_mode": "full"})


def test_scoped_rechecks_evidence_before_signing(scoped, monkeypatch):
    args, _, _, (_, _, _, proof) = scoped
    original = module._verified_key

    def mutate(*paths):
        proof.write_bytes(b"changed after acceptance")
        return original(*paths)

    monkeypatch.setattr(module, "_verified_key", mutate)
    with pytest.raises(ValueError, match="签名前发生变化"):
        module.build_manifest(**args)


def test_scoped_keeps_independent_source_fingerprints(scoped):
    args, plan, _, _ = scoped
    second_id = "rpm_x86_64"
    spec = module.RELEASE_PACKAGES[second_id]
    path = args["root"] / spec["filename"]
    path.write_bytes(b"independent package")
    digest = module.sha256(path)
    fingerprint = "b" * 64
    log = args["lab_root"] / "reports" / "second-build.log"
    log.parent.mkdir(parents=True, exist_ok=True)
    log.write_bytes(b"completed")
    build_path = args["lab_root"] / "state/builds" / f"{digest}.json"
    write_json(build_path, {"status": "completed", "errors": [], "package": {
        "id": second_id, "version": module.VERSION, "path": str(path), "sha256": digest, "bytes": path.stat().st_size},
        "source_before": fingerprint, "source_after": fingerprint, "exit_code": 0,
        "output_created_during_build": True, "log": {"path": str(log), "sha256": module.sha256(log)}})
    target = "openeuler-iso-x64"
    proof = args["lab_root"] / "reports" / target / "proof.txt"
    proof.parent.mkdir(parents=True, exist_ok=True)
    proof.write_bytes(b"pass")
    receipt_path = proof.parent / "user-acceptance-20260913.json"
    write_json(receipt_path, {"target": target, "status": "passed", "scope": "user-approved-product-use-acceptance",
                              "version": module.VERSION, "package_sha256": digest, "source_fingerprint": fingerprint,
                              "full_lifecycle_verified": False, "limitations": ["独立包。"],
                              "evidence": [{"path": str(proof), "sha256": module.sha256(proof)}]})
    plan["packages"].append({"id": second_id, "filename": path.name, "platform": spec["platform"],
                             "size": path.stat().st_size, "sha256": digest, "source_fingerprint": fingerprint,
                             "source_commit": None, "targets": [target], "untested_targets": [],
                             "build_receipt": {"path": str(build_path), "sha256": module.sha256(build_path)},
                             "acceptance_receipts": [{"path": str(receipt_path), "sha256": module.sha256(receipt_path)}],
                             "limitations": ["独立包。"], "untested": ["旧版升级未测。"]})
    plan["deferred_packages"] = [row for row in plan["deferred_packages"] if row["id"] != second_id]
    freeze_path = Path(plan["six_package_freeze"]["path"])
    freeze = json.loads(freeze_path.read_text(encoding="utf-8"))
    next(item for item in freeze["packages"] if item["id"] == second_id).update({
        "artifact": {"sha256": digest, "bytes": path.stat().st_size},
        "build": {"source_fingerprint": fingerprint, "receipt_path": str(build_path),
                  "receipt_sha256": module.sha256(build_path), "provenance_status": None},
        "acceptance": {"path": str(receipt_path), "sha256": module.sha256(receipt_path)}})
    write_json(freeze_path, freeze)
    plan["six_package_freeze"]["sha256"] = module.sha256(freeze_path)
    write_json(args["scope_plan_path"], plan)
    result = module.build_manifest(**args)
    assert {row["source_fingerprint"] for row in result["acceptance"]["packages"]} == {"a" * 64, "b" * 64}
    assert all(row["source_commit"] is None for row in result["acceptance"]["packages"])


def test_win7_scope_exception_is_exact_and_receipt_bound(scoped):
    args, plan, _, (old_package, old_build, old_receipt, proof) = scoped
    target, package_id = "win7-x64", "windows7_amd64"
    spec = module.RELEASE_PACKAGES[package_id]
    package_path = old_package.with_name(spec["filename"])
    old_package.rename(package_path)
    digest = module.sha256(package_path)
    build_path = old_build.with_name(f"{digest}.json")
    build = json.loads(old_build.read_text(encoding="utf-8"))
    build["package"].update({"id": package_id, "path": str(package_path), "sha256": digest})
    old_build.unlink()
    write_json(build_path, build)
    receipt_path = args["lab_root"] / "reports" / target / "user-acceptance-20260913.json"
    receipt = json.loads(old_receipt.read_text(encoding="utf-8"))
    receipt.update({"target": target, "scope": module.WIN7_APPROVED_SCOPE, "package_sha256": digest})
    receipt.pop("version")
    receipt.pop("source_fingerprint")
    receipt.pop("limitations")
    write_json(receipt_path, receipt)
    row = plan["packages"][0]
    row.update({"id": package_id, "filename": package_path.name, "platform": spec["platform"], "sha256": digest,
                "targets": [target], "source_fingerprint": "a" * 64,
                "build_receipt": {"path": str(build_path), "sha256": module.sha256(build_path)},
                "acceptance_receipts": [{"path": str(receipt_path), "sha256": module.sha256(receipt_path)}]})
    plan["deferred_packages"] = [item for item in plan["deferred_packages"] if item["id"] != package_id]
    plan["deferred_packages"].append({"id": "linux_amd64", "targets": ["uos-deb-x64"], "reason": "本次不纳入。"})
    freeze_path = Path(plan["six_package_freeze"]["path"])
    freeze = json.loads(freeze_path.read_text(encoding="utf-8"))
    next(item for item in freeze["packages"] if item["id"] == "linux_amd64").clear()
    next(item for item in freeze["packages"] if item.get("id") == package_id).update({
        "artifact": {"sha256": digest, "bytes": package_path.stat().st_size},
        "build": {"source_fingerprint": "a" * 64, "receipt_path": str(build_path),
                  "receipt_sha256": module.sha256(build_path), "provenance_status": None},
        "acceptance": {"path": str(receipt_path), "sha256": module.sha256(receipt_path)}})
    freeze["packages"] = [item for item in freeze["packages"] if item]
    freeze["packages"].append({"id": "linux_amd64"})
    write_json(freeze_path, freeze)
    plan["six_package_freeze"]["sha256"] = module.sha256(freeze_path)
    write_json(args["scope_plan_path"], plan)
    result = module.build_manifest(**args)
    assert result["acceptance"]["packages"][0]["targets"][0]["scope"] == module.WIN7_APPROVED_SCOPE
    receipt["scope"] += "。"
    write_json(receipt_path, receipt)
    row["acceptance_receipts"][0]["sha256"] = module.sha256(receipt_path)
    next(item for item in freeze["packages"] if item["id"] == package_id)["acceptance"]["sha256"] = module.sha256(receipt_path)
    write_json(freeze_path, freeze)
    plan["six_package_freeze"]["sha256"] = module.sha256(freeze_path)
    write_json(args["scope_plan_path"], plan)
    with pytest.raises(ValueError, match="范围或包身份"):
        module.build_manifest(**args)


@pytest.fixture
def loong_scope(scoped, tmp_path: Path, monkeypatch):
    args, plan, _, (old_package, old_build, _, _) = scoped
    package_id = "linux_loong64"
    spec = module.RELEASE_PACKAGES[package_id]
    package_path = old_package.with_name(spec["filename"])
    old_package.rename(package_path)
    digest = module.sha256(package_path)
    fingerprint = "c" * 64
    build_path = old_build.with_name(f"{digest}.json")
    build = json.loads(old_build.read_text(encoding="utf-8"))
    build["package"].update({"id": package_id, "path": str(package_path), "sha256": digest})
    build["source_before"] = build["source_after"] = fingerprint
    old_build.unlink()
    write_json(build_path, build)
    source_root = tmp_path / "source"
    audit_path = source_root / "qa/vm-lab/release-preparation/loong64/first-release-audit-20260927.json"
    evidence_dir = tmp_path / "audit-evidence"
    raw_build = evidence_dir / "build.json"
    write_json(raw_build, {"status": "completed", "exit_code": 0,
                           "source_before": fingerprint, "source_after": fingerprint})
    raw_lifecycle = evidence_dir / "execution.json"
    write_json(raw_lifecycle, {"status": "blocked", "context": {"package": {"sha256": digest},
               "source_fingerprint": fingerprint, "environment": {"vm_uuid": "guest-uuid"}}})
    reference = lambda path: {"path": str(path), "sha256": module.sha256(path)}
    scopes = []
    for scope_id, status in module.LOONG_SCOPE_STATUSES.items():
        proof = evidence_dir / f"{scope_id}.json"
        write_json(proof, {"scope": scope_id, "status": status})
        scopes.append({"id": scope_id, "status": status, "package_sha256": digest,
                       "evidence": [reference(proof)]})
    audit = {"product": {"version": module.VERSION, "package_sha256": digest,
                         "package_bytes": package_path.stat().st_size, "source_commit": "d" * 40,
                         "guest_uuid": "guest-uuid", "build": {"status": "completed",
                         "source_fingerprint": fingerprint, "evidence": [reference(raw_build)]}},
             "lifecycle_gate": {"status": "blocked", "evidence": [reference(raw_lifecycle)]},
             "scopes": scopes, "explicitly_not_covered": ["旧版升级未验证。"]}
    write_json(audit_path, audit)
    row = plan["packages"][0]
    row.update({"id": package_id, "filename": package_path.name, "platform": spec["platform"],
                "sha256": digest, "targets": ["deepin-deb-loong64"], "source_fingerprint": fingerprint,
                "source_commit": "d" * 40, "build_receipt": {"path": str(build_path),
                "sha256": module.sha256(build_path)}, "scope_audit": reference(audit_path),
                "limitations": ["生命周期原门禁 blocked。"], "untested": ["旧版升级未验证。"]})
    row.pop("acceptance_receipts")
    plan["deferred_packages"] = [item for item in plan["deferred_packages"] if item["id"] != package_id]
    plan["deferred_packages"].append({"id": "linux_amd64", "targets": ["uos-deb-x64"], "reason": "本次不纳入。"})
    write_json(args["scope_plan_path"], plan)

    @contextmanager
    def laboratory(*_unused):
        yield None, SimpleNamespace(root=args["lab_root"])

    monkeypatch.setattr(module, "_laboratory", laboratory)
    monkeypatch.syspath_prepend(str(ROOT / "qa/vm-lab"))
    return args, plan, audit, audit_path, raw_lifecycle, source_root


def test_loong_first_release_scope_and_original_blocked(loong_scope):
    args, _, _, _, _, source_root = loong_scope
    result = module._scoped_acceptance(args["root"], args["scope_plan_path"], source_root, args["lab_root"])
    row = result["packages"][0]
    assert row["full_lifecycle_verified"] is False
    assert row["source_commit"] == "d" * 40
    assert len(row["targets"][0]["scopes"]) == len(module.LOONG_SCOPE_STATUSES)
    assert [s["id"] for s in row["targets"][0]["scopes"] if s["status"] == "not_applicable_candidate"] == [
        "old-same-architecture-upgrade-candidate"]


@pytest.mark.parametrize("change", ["scope", "blocked", "identity", "evidence_hash"])
def test_loong_rejects_scope_or_raw_lifecycle_changes(loong_scope, change):
    args, plan, audit, audit_path, raw_lifecycle, source_root = loong_scope
    if change == "scope":
        audit["scopes"][0]["status"] = "passed"
    elif change == "blocked":
        raw = json.loads(raw_lifecycle.read_text(encoding="utf-8"))
        raw["status"] = "passed"
        write_json(raw_lifecycle, raw)
        audit["lifecycle_gate"]["evidence"][0]["sha256"] = module.sha256(raw_lifecycle)
    elif change == "identity":
        raw = json.loads(raw_lifecycle.read_text(encoding="utf-8"))
        raw["context"]["environment"]["vm_uuid"] = "another-guest"
        write_json(raw_lifecycle, raw)
        audit["lifecycle_gate"]["evidence"][0]["sha256"] = module.sha256(raw_lifecycle)
    else:
        audit["lifecycle_gate"]["evidence"][0]["sha256"] = "0" * 64
    write_json(audit_path, audit)
    plan["packages"][0]["scope_audit"]["sha256"] = module.sha256(audit_path)
    write_json(args["scope_plan_path"], plan)
    with pytest.raises(ValueError):
        module._scoped_acceptance(args["root"], args["scope_plan_path"], source_root, args["lab_root"])
