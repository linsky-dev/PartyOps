"""Freeze only an isolated candidate; production PE/lock/VEX gates remain unchanged."""
from __future__ import annotations
import hashlib
import json
import os
import pathlib
import shutil
import struct
import subprocess
import sys


def check(condition, message):
    if not condition:
        raise RuntimeError(message)


def digest(path):
    with path.open("rb") as f:
        h = hashlib.sha256()
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
        return h.hexdigest()


def one(root, name):
    paths = list(root.rglob(name))
    check(len(paths) == 1, "missing or ambiguous " + name)
    return paths[0]


def verify(root, arch):
    r = json.loads(one(root, "closure.json").read_text(encoding="utf-8-sig"))
    check(r["source_commit"] == os.environ["CLOSURE_SHA"] and r["arch"] == arch, "closure identity")
    check(r["status"] == "HOSTED_CPYTHON38_FULL_APPLICATION_SCHEMA_PASS_NOT_WIN7_OS" and r["release_ready"] is False, "closure status")
    lock = one(root, "candidate-runtime.lock")
    check(digest(lock) == r["candidate_lock_sha256"], "candidate lock hash")
    original = pathlib.Path("backend/legacy/requirements-windows7-" + arch + ".lock")
    check(digest(original) == r["original_lock_sha256"], "production lock changed")
    wheels = list(root.rglob("*.whl"))
    check(len(wheels) == len(r["packages"]), "wheel count")
    for row in r["packages"].values():
        check(digest(one(root, row["filename"])) == row["sha256"], "wheel hash")
    wheel_dirs = {p.parent for p in wheels}
    check(len(wheel_dirs) == 1, "wheel directory")
    subprocess.check_call([sys.executable, "-m", "pip", "install", "--no-index", "--only-binary=:all:", "--require-hashes", "--find-links", str(wheel_dirs.pop()), "-r", str(lock)])
    subprocess.check_call([sys.executable, "-m", "pip", "check"])


def freeze(out, arch):
    root = pathlib.Path.cwd()
    expected = 64 if arch == "amd64" else 32
    check(sys.version_info[:3] == (3, 8, 10) and struct.calcsize("P") * 8 == expected, "wrong interpreter")
    subprocess.check_call([sys.executable, "scripts/validate-win7-cryptography-runtime.py"])
    import tkinter
    print("TCL", tkinter.Tcl().eval("info patchlevel"))
    entries = [
        ("PartyOps", "packaging/uos/entrypoint.py", False),
        ("PartyOpsAgent", "packaging/uos/client_entrypoint.py", False),
        ("PartyOpsWizard", "packaging/uos/wizard_entrypoint.py", True),
        ("PartyOpsUpdater", "packaging/uos/updater_entrypoint.py", False),
        ("PartyOpsLauncher", "packaging/windows/windows_launcher.py", True),
        ("PartyOpsDataCleanup", "packaging/windows/data_cleanup.py", True),
        ("PartyOpsFileOpen", "packaging/windows/windows_file_open.py", True),
        ("PartyOpsService", "packaging/windows/windows_service.py", False),
        ("PartyOpsUpdaterService", "packaging/windows/windows_updater_service.py", False),
    ]
    dist = pathlib.Path(os.environ["RUNNER_TEMP"]) / "win7-freeze-dist"
    out = out.resolve()
    common = ["sqlalchemy.dialects.sqlite.pysqlite", "uvicorn.logging", "uvicorn.loops.asyncio", "uvicorn.protocols.http.h11_impl", "cryptography", "cryptography.fernet", "httpx", "win32timezone"]
    for name, entry, gui in entries:
        args = [sys.executable, "-m", "PyInstaller", "--noconfirm", "--clean", "--onedir",
                "--name", name, "--paths", str(root / "backend"), "--distpath", str(dist),
                "--workpath", str(dist.parent / "win7-freeze-work"), "--specpath", str(dist.parent / "win7-spec"),
                "--icon", str(root / "packaging/windows/partyops.ico"),
                "--runtime-hook", str(root / "packaging/windows/win7_openssl_hook.py"),
                "--add-data", str(root / "packaging/windows/partyops-openssl.cnf") + ";."]
        if gui:
            args += ["--noconsole"]
        for module in common:
            args += ["--hidden-import", module]
        for module in ("numpy", "onnxruntime", "tokenizers"):
            args += ["--hidden-import" if arch == "amd64" else "--exclude-module", module]
        if name == "PartyOps":
            for path, target in (("frontend/dist/client", "frontend"), ("backend/alembic", "alembic"), ("backend/alembic.ini", ".")):
                args += ["--add-data", str(root / path) + ";" + target]
        args.append(str(root / entry))
        subprocess.check_call(args)
    bundle = out / "app"
    bundle.mkdir(exist_ok=False)
    for name, _, _ in entries:
        for p in (dist / name).iterdir():
            dest = bundle / p.name
            if p.is_dir():
                shutil.copytree(str(p), str(dest), dirs_exist_ok=True)
            else:
                shutil.copy2(p, dest)
    # Hosted-only smoke checks; never claim target Win7 or release PE pass.
    report = out / "wizard.json"
    env = dict(os.environ, PARTYOPS_WIZARD_SELFTEST_REPORT=str(report), PYINSTALLER_RESET_ENVIRONMENT="1")
    subprocess.check_call([str(bundle / "PartyOpsWizard.exe"), "--self-test"], env=env, timeout=90)
    check(json.loads(report.read_text(encoding="utf-8"))["passed"] is True, "wizard GUI")
    help_result = subprocess.run([str(bundle / "PartyOps.exe"), "--account-recovery", "--help"], env=env, capture_output=True, timeout=90)
    (out / "recovery-help.txt").write_bytes(help_result.stdout)
    (out / "recovery-help-stderr.txt").write_bytes(help_result.stderr)
    (out / "recovery-help-status.json").write_text(json.dumps({"returncode": help_result.returncode}), encoding="utf-8")
    check(help_result.returncode == 0 and b"desktop-reset" in help_result.stdout, "frozen recovery entry: exit=" + str(help_result.returncode) + "; stderr=" + help_result.stderr.decode("utf-8", errors="replace")[-2000:])
    # Produce a complete inventory, including failures; production validator
    # still requires verified UCRT, VC142, .NET and full package closure.
    import importlib.util
    spec = importlib.util.spec_from_file_location("pe_gate", str(root / "scripts/validate-win7-pe.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    rows = []
    for path in sorted(bundle.rglob("*")):
        if path.is_file() and path.suffix.lower() in (".exe", ".dll", ".pyd"):
            rows.append({"file": str(path.relative_to(bundle)), "sha256": digest(path),
                         "raw_pe_findings": mod.validate_pe(path, arch, {})})
    check(bool(rows), "no binaries")
    (out / "raw-pe-inventory.json").write_text(json.dumps(rows, indent=2), encoding="utf-8")
    (out / "freeze-receipt.json").write_text(json.dumps({
        "source_commit": os.environ["SOURCE_SHA"], "dependency_source": os.environ["CLOSURE_SHA"],
        "architecture": arch, "status": "HOSTED_APP_ONLY_NOT_WIN7_OS",
        "wizard_gui_passed": True, "frozen_recovery_entry_passed": True,
        "raw_pe_findings": sum(len(r["raw_pe_findings"]) for r in rows),
        "official_ucrt_vc142_closure_verified": False, "security_backports_approved": False,
        "native_win7_os_tested": False, "installer_created": False, "release_ready": False
    }, indent=2), encoding="utf-8")


if __name__ == "__main__":
    action, root, arch = sys.argv[1:]
    (verify if action == "verify" else freeze)(pathlib.Path(root), arch)
