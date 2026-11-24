"""Integrate verified native ARM64 app and Office inputs; never a release."""
import hashlib
import json
import os
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
    a, o, out = (pathlib.Path(x).resolve() for x in sys.argv[1:4])
    out.mkdir(parents=True, exist_ok=False)
    receipt = load(one(a, "receipt.json"))
    require(receipt["source_commit"] == os.environ["SOURCE_SHA"]
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
    sqlite_input, ocr_input, ai_input, formatter_input, repo = (pathlib.Path(x).resolve() for x in sys.argv[4:])
    sqlite = load(one(sqlite_input, "SOURCE.json"))
    require(sqlite["architecture"] == "arm64" and sqlite["version"] == "3.53.4"
            and sqlite["status"] == "NATIVE_SQLITE_INPUT_PASS_ONLY" and not sqlite["release_ready"], "SQLite receipt")
    dll = one(sqlite_input, "sqlite3.dll")
    arm64(dll)
    require(digest(dll) == sqlite["dll_sha256"], "SQLite hash")
    for target in (bundle / "sqlite3.dll", bundle / "_internal/sqlite3.dll"):
        shutil.copy2(dll, target)
    ocr = load(one(ocr_input, "SOURCE.json"))
    require(ocr["architecture"] == "arm64" and ocr["version"] == "5.5.4"
            and ocr["status"] == "NATIVE_OCR_ENGLISH_RECOGNITION_PASS"
            and not ocr["release_ready"], "OCR receipt")
    ocr_runtime = one(ocr_input, "SOURCE.json").parent
    engine = ocr_runtime / "bin/tesseract.exe"
    arm64(engine)
    require(digest(engine) == ocr["engine_sha256"], "OCR hash")
    for name, h in ocr["models"].items():
        require(pathlib.Path(name).name == name and digest(ocr_runtime / "tessdata" / name) == h, "OCR model hash")
    shutil.copytree(ocr_runtime, bundle / "ocr")
    ai = load(one(ai_input, "SOURCE.json"))
    require(ai["architecture"] == "arm64" and ai["tag"] == "b10331"
            and ai["status"] == "NATIVE_RUNTIME_VERSION_HELP_PASS" and not ai["release_ready"], "AI receipt")
    server = one(ai_input, "llama-server.exe")
    for row in ai["files"]:
        name = row["name"]
        require(pathlib.Path(name).name == name, "AI path")
        p = server.parent / name
        arm64(p)
        require(digest(p) == row["sha256"] and not (bundle / name).exists(), "AI hash or collision")
        shutil.copy2(p, bundle / name)
    # Preserve upstream notices as delivered in the exact hash-verified archive.
    ai_notices = bundle / "licenses/llama-b10331"
    ai_notices.mkdir(parents=True)
    for p in server.parent.iterdir():
        if p.is_file() and p.name.lower().startswith(("license", "notice")):
            shutil.copy2(p, ai_notices / p.name)
    formatter = load(one(formatter_input, "receipt.json"))
    require(formatter["source_commit"] == "5ea9c6a128f3d8aa71ff77e7e549f4fbd6115fdb"
            and formatter["status"] == "WINDOWS_ARM64_ANYCPU_FORMATTER_HOST_SELFTEST_PASS_ONLY"
            and formatter["native_clr_probe"] and not formatter["release_ready"], "formatter receipt")
    formatter_root = one(formatter_input, "source-host.json").parent
    require(digest(formatter_root / "PartyOps.DocumentFormatter.Host.exe") == formatter["host_sha256"], "formatter hash")
    shutil.copytree(formatter_root, bundle / "formatter-host")
    for relative in ("packaging/windows/partyops.ico", "packaging/uos/update-public-key.txt"):
        p = repo / relative
        shutil.copy2(p, bundle / p.name)
    images = [p for p in repo.rglob("partyops-1024.png") if p.is_file() and ".git" not in p.parts]
    require(images and len({digest(p) for p in images}) == 1, "brand asset missing or ambiguous")
    shutil.copy2(images[0], bundle / "partyops-1024.png")
    for name in ("README.md", "CHANGELOG.md", "LICENSE", "THIRD_PARTY_NOTICES.md"):
        shutil.copy2(repo / name, bundle / name)
    (bundle / "QA-CANDIDATE-ONLY.txt").write_text(
        "Unsigned Windows 11 ARM64 QA only. Production update channel disabled. "
        "Native WPS and Windows 10 ARM64 NOT accepted. No production use.\n", encoding="utf-8")
    for name, value in (("sqlite", sqlite), ("ocr", ocr), ("ai", ai), ("formatter", formatter)):
        (out / (name + "-input.json")).write_text(json.dumps(value, indent=2), encoding="utf-8")
    converted = out / "converted"
    converted.mkdir()
    fixture = repo / "backend/tests/fixtures/document-formatter-source/input-manual-break.docx"
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
