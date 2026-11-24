"""Integrate verified native ARM64 app and Office inputs; never a release."""
import hashlib
import json
import pathlib
import shutil
import struct
import subprocess
import sys
import tarfile


def require(ok, reason):
    if not ok:
        raise RuntimeError(reason)


def digest(path):
    with path.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


def one(root, name):
    paths = list(root.rglob(name))
    require(len(paths) == 1, "missing or ambiguous " + name)
    return paths[0]


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def arm64(path):
    with path.open("rb") as f:
        b = f.read(64)
        require(len(b) == 64 and b[:2] == b"MZ", "invalid PE")
        offset = struct.unpack_from("<I", b, 60)[0]
        require(64 <= offset <= 1024 * 1024, "invalid PE offset")
        f.seek(offset)
        require(f.read(6) == b"PE\0\0\x64\xaa", "not ARM64: " + path.name)


def main():
    a, o, out = (pathlib.Path(x).resolve() for x in sys.argv[1:])
    out.mkdir(parents=True, exist_ok=False)
    receipt = load(one(a, "receipt.json"))
    require(receipt["source_commit"] == "96d1c9bd553b70b0bef0c934b6196d8fee0a265f"
            and receipt["status"] == "ARM64_COMPLETE_APP_ENTRIES_ONLY"
            and receipt["full_pdf_layout"] is True and receipt["wizard_gui_passed"] is True
            and receipt["release_ready"] is False, "app receipt")
    names = {"PartyOps.exe", "PartyOpsAgent.exe", "PartyOpsWizard.exe", "PartyOpsUpdater.exe",
             "PartyOpsLauncher.exe", "PartyOpsDataCleanup.exe", "PartyOpsFileOpen.exe",
             "PartyOpsService.exe", "PartyOpsUpdaterService.exe"}
    require({x["name"] for x in receipt["entries"]} == names and len(receipt["entries"]) == 9, "entry closure")
    app = one(a, "PartyOps.exe").parent
    for row in receipt["entries"]:
        p = app / row["name"]
        require(digest(p) == row["sha256"], "app hash")
        arm64(p)
    require(not any(p.is_symlink() or getattr(p.lstat(), "st_file_attributes", 0) & 0x400
                    for p in app.rglob("*")), "linked input")
    bundle = out / "candidate"
    shutil.copytree(app, bundle)
    evidence = load(one(o, "SOURCE.json"))
    require(evidence["scope"] == "WINDOWS_ARM64_VERIFIED_OFFICIAL_LIBREOFFICE_RUNTIME_DOCX_TO_PDF_BUILD_ONLY"
            and evidence["official_msi_version"] == "26.8.3"
            and evidence["official_msi_sha256"] == "8ee2ef81ca4103d3eeb06ca9f4ef0b5cbb83357d21d14a449f767bfb29183158"
            and evidence["official_msi_authenticode"] == "Valid" and evidence["native_docx_to_pdf"] == "PASS"
            and evidence["release_ready"] is False, "office receipt")
    archive = one(o, "libreoffice-arm64-26.8.3-verified-input.tar.gz")
    declared = one(o, archive.name + ".sha256").read_text(encoding="ascii").split()
    require(len(declared) == 2 and declared[1] == archive.name and digest(archive) == declared[0], "office archive hash")
    office = bundle / "office-runtime"
    office.mkdir()
    with tarfile.open(archive, "r:gz") as tar:
        members = tar.getmembers()
        require(len(members) < 100000 and sum(m.size for m in members) < 4 * 1024**3, "archive bounds")
        for m in members:
            require(m.isfile() or m.isdir(), "non-regular member")
            require("\\" not in m.name and ":" not in m.name and not pathlib.PurePosixPath(m.name).is_absolute()
                    and ".." not in pathlib.PurePosixPath(m.name).parts, "archive traversal")
        tar.extractall(office, filter="data")
    for name, field in (("soffice.exe", "office_executable_sha256"), ("soffice.com", "office_cli_sha256")):
        p = office / "program" / name
        arm64(p)
        require(digest(p) == evidence[field], "office executable hash")
    arm64(office / "program/soffice.bin")
    (out / "office-input-evidence.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    (out / "app-input-evidence.json").write_text(json.dumps(receipt, indent=2), encoding="utf-8")
    rows = [{"path": p.relative_to(bundle).as_posix(), "sha256": digest(p), "size": p.stat().st_size}
            for p in sorted(bundle.rglob("*")) if p.is_file()]
    (out / "candidate-inventory.json").write_text(json.dumps({
        "app_source_commit": receipt["source_commit"], "office_source_commit": evidence["source_commit"],
        "architecture": "arm64", "files": rows, "status": "INTEGRATED_INPUTS_ONLY",
        "installer_created": False, "release_ready": False, "update_channel_enabled": False,
        "remaining": ["OCR", "SQLite pinned runtime", "formatter and .NET", "local AI", "installer and package identity", "target OS acceptance"]
    }, indent=2), encoding="utf-8")
    converted = out / "converted"
    converted.mkdir()
    fixture = pathlib.Path("backend/tests/fixtures/document-formatter-source/input-manual-break.docx").resolve()
    profile = (out / "office-profile").as_uri()
    subprocess.check_call([str(office / "program/soffice.com"), "-env:UserInstallation=" + profile,
                           "--headless", "--convert-to", "pdf", "--outdir", str(converted), str(fixture)], timeout=180)
    pdf = converted / "input-manual-break.pdf"
    require(pdf.stat().st_size > 1000 and pdf.read_bytes()[:5] == b"%PDF-", "invalid converted PDF")
    result = subprocess.run([str(bundle / "PartyOps.exe"), "--account-recovery", "--help"], capture_output=True, timeout=90)
    (out / "recovery-help.txt").write_bytes(result.stdout)
    (out / "recovery-stderr.txt").write_bytes(result.stderr)
    require(result.returncode == 0 and b"desktop-reset" in result.stdout, "frozen recovery")
    (out / "integration-receipt.json").write_text(json.dumps({
        "status": "ARM64_NATIVE_APP_OFFICE_INTEGRATION_PASS_BUILD_ONLY",
        "source_commit": receipt["source_commit"], "pdf_sha256": digest(pdf),
        "installer_created": False, "release_ready": False, "update_channel_enabled": False
    }, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
