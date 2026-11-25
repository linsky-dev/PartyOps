"""Recover only verified prerequisite inputs, audit actual new app PE; never a release."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sys
import zipfile


def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def require(ok, reason):
    if not ok:
        raise RuntimeError(reason)


def one(root, name):
    rows = list(root.rglob(name))
    require(len(rows) == 1, "ambiguous or missing " + name)
    return rows[0]


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def main():
    fresh, donor, out = (Path(x).resolve() for x in sys.argv[1:4])
    arch = sys.argv[4]
    out.mkdir(parents=True)
    receipt = read(one(fresh, "freeze-receipt.json"))
    require(receipt["source_commit"] == os.environ["PARTYOPS_QA_EXPECTED_SOURCE"]
            and receipt["architecture"] == arch and receipt["status"] == "HOSTED_APP_ONLY_NOT_WIN7_OS"
            and receipt["wizard_gui_passed"] and receipt["frozen_recovery_entry_passed"]
            and receipt["release_ready"] is False, "fresh receipt")
    src = one(donor, "release-manifest.json").parent
    manifest = read(src / "release-manifest.json")
    require(manifest["version"] == "1.4.5-rc.6" and manifest["architecture"] == arch
            and manifest["platform"] == "windows7", "donor identity")
    index = {}
    for row in manifest["files"]:
        name = row["path"].replace("\\", "/")
        require(name.casefold() not in index, "duplicate manifest path")
        index[name.casefold()] = row
    app = one(fresh, "PartyOps.exe").parent
    bundle = out / "app-with-prerequisites"
    shutil.copytree(str(app), str(bundle))
    if arch == "amd64":
        inventory = read(one(fresh, "numpy-native-dll-closure.json"))
        require(isinstance(inventory, list) and bool(inventory), "missing NumPy DLL inventory")
        # upload-artifact excludes dot-prefixed directories by default. Restore
        # only absent exact DLL members from the same hash-locked wheel closure,
        # never overwrite a mismatching file or accept a different dependency.
        closure_root = fresh.parent / "verified-closure"
        closure = read(one(closure_root, "closure.json"))
        require(closure["source_commit"] == receipt["dependency_source"]
                == "2122c220fd3af2e8040f4b8ffab01c778c70da88"
                and closure["arch"] == arch
                and closure["status"] == "HOSTED_CPYTHON38_FULL_APPLICATION_SCHEMA_PASS_NOT_WIN7_OS"
                and closure["release_ready"] is False, "NumPy closure provenance")
        numpy = closure["packages"]["numpy"]
        require(numpy["version"] == "1.24.4"
                and sha(one(closure_root, "candidate-runtime.lock")) == closure["candidate_lock_sha256"]
                and sha(Path("backend/legacy/requirements-windows7-amd64.lock")) == closure["original_lock_sha256"],
                "NumPy closure lock identity")
        wheel = one(closure_root, numpy["filename"])
        require(sha(wheel) == numpy["sha256"], "NumPy wheel hash")
        restored = []
        seen = set()
        for row in inventory:
            rel = Path(row["path"])
            require(not rel.is_absolute() and not rel.drive and ".." not in rel.parts and rel.parts[0] == "_internal" and rel.suffix.lower() == ".dll", "unsafe NumPy DLL path")
            require(str(rel).casefold() not in seen, "duplicate NumPy DLL path")
            seen.add(str(rel).casefold())
            target = bundle / rel
            if not target.exists() and not target.is_symlink():
                member = Path(*rel.parts[1:]).as_posix()
                require(member.startswith("numpy/") or member.startswith("numpy.libs/"), "unexpected NumPy member")
                with zipfile.ZipFile(wheel) as archive:
                    require(archive.namelist().count(member) == 1, "ambiguous NumPy DLL member")
                    data = archive.read(member)
                require(hashlib.sha256(data).hexdigest() == row["sha256"], "NumPy wheel DLL hash")
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
                restored.append({"path": rel.as_posix(), "sha256": row["sha256"]})
            require(target.is_file() and not target.is_symlink() and sha(target) == row["sha256"], "NumPy DLL input mismatch: " + str(rel))
        (out / "numpy-closure-transfer-receipt.json").write_text(json.dumps({
            "dependency_source": closure["source_commit"], "wheel_sha256": numpy["sha256"],
            "restored_missing_dlls": restored, "verified_dll_count": len(seen)
        }, indent=2), encoding="utf-8")
    records = []

    def verified(relative):
        p = src / relative
        row = index.get(relative.casefold())
        require(row and p.is_file() and not p.is_symlink()
                and p.stat().st_size == row["size"] and sha(p) == row["sha256"], "donor manifest mismatch " + relative)
        records.append({"path": relative, "sha256": row["sha256"]})
        return p

    # Only the original app-local UCRT/VC142 inputs, verified against both
    # source manifests in current repo and the original signed-off package.
    suffix = "x64" if arch == "amd64" else "x86"
    for dest_name, vendor_name, version in (
        ("ucrt-source.json", "ucrt-10.0.19041.0-" + suffix, "10.0.19041.0"),
        ("vc-runtime-source.json", "vc142-14.29.30157-" + suffix, "14.29.30157.0"),
    ):
        source = verified(dest_name)
        old = read(source)
        current = read(Path("vendor/windows") / vendor_name / "SOURCE.json")
        require(old["version"] == version and old["architecture"] == suffix
                and old["files"] == current["files"], "runtime source manifest mismatch")
        shutil.copy2(str(source), str(bundle / dest_name))
        for directory in (bundle, bundle / "_internal"):
            if dest_name.startswith("ucrt"):
                for p in directory.iterdir():
                    if p.is_file() and (p.name.lower().startswith("api-ms-win-") or p.name.lower() == "ucrtbase.dll"):
                        p.unlink()
            for name, expected in current["files"].items():
                require(Path(name).name == name, "invalid runtime filename")
                source_file = verified(name)
                require(sha(source_file) == expected.lower(), "official runtime hash mismatch")
                shutil.copy2(str(source_file), str(directory / name))
    for name in ("ucrt-sdk-license.rtf", "ucrt-sdk-third-party-notices.rtf"):
        shutil.copy2(str(verified(name)), str(bundle / name))
    sqlite = verified("sqlite3.dll")
    expected = {"amd64":"ab57d0437795ecc757cb693f32ea224173fa9856594d95cfa6b5033e645cd1ec",
                "x86":"1c2fcfa7632b6025829e3539142f1b7ebdbc5bb44d4fd6cc0f42f83715d2eb9f"}[arch]
    require(sha(sqlite) == expected, "SQLite official hash differs")
    for path in (bundle / "sqlite3.dll", bundle / "_internal/sqlite3.dll"):
        shutil.copy2(str(sqlite), str(path))

    # Do not copy legacy application, dependencies, updater or formatter.
    # Record exact donor office/OCR inputs separately for independent review.
    for name in ("office-runtime", "ocr"):
        require((src / name).is_dir(), "missing donor " + name)
        for file in sorted((src / name).rglob("*")):
            if file.is_file():
                verified(file.relative_to(src).as_posix())
    (out / "verified-donor-components.json").write_text(json.dumps(records, indent=2), encoding="utf-8")
    spec = importlib.util.spec_from_file_location("pe_gate", "scripts/validate-win7-pe.py")
    pe = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(pe)
    ucrt = read(bundle / "ucrt-source.json")["files"]
    rows = []
    for file in sorted(bundle.rglob("*")):
        if file.is_file() and file.suffix.lower() in {".exe", ".dll", ".pyd"}:
            errors = pe.validate_pe(file, arch, {n.lower(): h.lower() for n,h in ucrt.items()})
            rows.append({"path": file.relative_to(bundle).as_posix(), "sha256": sha(file), "errors": errors})
    require(bool(rows), "missing PE files")
    findings = sum(len(row["errors"]) for row in rows)
    (out / "strict-pe-report.json").write_text(json.dumps(rows, indent=2), encoding="utf-8")
    (out / "preflight-receipt.json").write_text(json.dumps({
        "source_commit": receipt["source_commit"], "architecture": arch, "donor_sha256": os.environ["DONOR_SHA"],
        "status": "QA_APP_PREREQUISITES_PE_PASS" if not findings else "QA_PE_BLOCKED",
        "pe_findings": findings, "installer_created": False, "release_ready": False,
        "security_backports_approved": False, "native_win7_os_tested": False,
        "office_ocr_inputs_manifest_verified": True,
    }, indent=2), encoding="utf-8")
    # A finding is a blocker, not something to suppress.
    require(findings == 0, "strict PE compatibility findings: " + str(findings))


if __name__ == "__main__":
    main()
