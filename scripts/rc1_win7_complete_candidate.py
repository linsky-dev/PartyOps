"""Assemble exact CI-proven candidate wheels; NEVER replace release locks or VEX."""
from __future__ import annotations

import argparse
import email
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path


def require(ok, reason):
    if not ok:
        raise RuntimeError(reason)


def single(root, pattern):
    matches = list(root.rglob(pattern))
    require(len(matches) == 1, "ambiguous or missing " + pattern)
    return matches[0]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def canon(name):
    return re.sub(r"[-_.]+", "-", name).lower()


def wheel_identity(path):
    with zipfile.ZipFile(path) as z:
        names = [n for n in z.namelist() if n.endswith(".dist-info/METADATA") and n.count("/") == 1]
        require(len(names) == 1, "invalid metadata " + path.name)
        m = email.message_from_bytes(z.read(names[0]))
        return canon(m["Name"]), m["Version"]


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("root", type=Path)
    p.add_argument("output", type=Path)
    p.add_argument("arch", choices=["amd64", "x86"])
    a = p.parse_args()
    import struct
    require(sys.version_info[:3] == (3, 8, 10) and struct.calcsize("P") * 8 == (64 if a.arch == "amd64" else 32), "wrong CPython target")
    require(sys.platform == "win32", "Windows hosted runner required")
    a.output.mkdir(parents=True, exist_ok=True)
    wh = a.output / "wheelhouse"
    wh.mkdir(exist_ok=False)
    lock = Path("backend/legacy/requirements-windows7-" + a.arch + ".lock")
    raw = lock.read_text(encoding="utf-8")
    blocks = re.split(r"(?m)(?=^[A-Za-z0-9_.-]+==)", raw)[1:]
    expected = {}
    for block in blocks:
        m = re.match(r"([A-Za-z0-9_.-]+)==([^\s\\]+)", block)
        require(m is not None, "invalid lock")
        expected[canon(m[1])] = (m[2], set(re.findall(r"--hash=sha256:([a-f0-9]{64})", block)))
    selected = {}

    def add(path, digest, origin):
        name, version = wheel_identity(path)
        require(name in expected and name not in selected, "unexpected or duplicate wheel: " + name)
        require(version == expected[name][0] and sha(path) == digest, "identity/hash mismatch: " + name)
        if "+partyops." not in version:
            require(digest in expected[name][1], "public wheel differs from release lock")
        dest = wh / path.name
        shutil.copy2(path, dest)
        selected[name] = {"version": version, "filename": path.name, "sha256": digest, "origin": origin,
                          "matches_historical_lock": digest in expected[name][1]}

    public = load(single(a.root / "public", "receipt.json"))
    require(public["target"] == "windows7-" + a.arch and public["source_commit"] == "400dd924faaf3195aef69c24c160a2e6f109a5f0", "public receipt mismatch")
    for row in public["public_wheels"]:
        require(row["status"] == "HASH_VERIFIED", "missing public dependency")
        add(single(a.root / "public", row["wheel"]), row["sha256"], "original lock")
    history = load(single(a.root / "pure", "receipt.json"))
    for name, row in history["components"].items():
        if row["status"] != "REBUILT_CANDIDATE_NOT_APPROVED" or row["matches_current_lock"][a.arch] is None:
            continue
        add(single(a.root / "pure", row["candidate_wheel"]), row["candidate_sha256"], "historical pure source rebuild")
    for component, folder, commit in (
        ("cryptography", "crypto", "15ece8fddd7ce58fd95940ed83ea692ebb787abe"),
        ("pillow", "pillow", "d19d6f4288753a716c1f02647302af9120c4ac03"),
        ("python-calamine", "calamine", "ddaa6139045f02d605de2a0c79a05d22a646727d"),
    ):
        receipt = load(single(a.root / folder, "wheel-receipt.json"))
        require(receipt["source_commit"] == commit and receipt["release_ready"] is False, "native receipt mismatch")
        status = {"crypto":"HOSTED_PYTHON38_CRYPTO_PASS_NOT_WIN7_OS","pillow":"HOSTED_PYTHON38_IMAGE_PASS_NOT_WIN7_OS","calamine":"HOSTED_PYTHON38_STATIC_PE_AND_XLSX_PASS_NOT_WIN7_OS"}[folder]
        require(receipt["status"] == status, "native test receipt missing")
        wheel = single(a.root / folder, receipt["wheel"])
        require(wheel_identity(wheel)[0] == component, "wrong native package")
        add(wheel, receipt.get("sha256") or receipt.get("wheel_sha256"), folder + " native CI")
    require(set(selected) == set(expected), "closure missing: " + repr(sorted(set(expected) - set(selected))))
    candidate_lock = a.output / "candidate-runtime.lock"
    candidate_lock.write_text("\n".join(name + "==" + row["version"] + " --hash=sha256:" + row["sha256"] for name,row in sorted(selected.items())) + "\n", encoding="utf-8")
    report = {"source_commit": os.environ["SOURCE_SHA"], "arch": a.arch, "packages": selected,
              "original_lock_sha256": sha(lock), "candidate_lock_sha256": sha(candidate_lock),
              "status": "CANDIDATE_CLOSURE_ONLY", "security_backports_approved": False,
              "native_win7_os_tested": False, "installer_created": False, "release_ready": False}
    receipt_path = a.output / "closure.json"
    receipt_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    subprocess.check_call([sys.executable,"-m","pip","install","--no-index","--only-binary=:all:","--require-hashes","--find-links",str(wh),"-r",str(candidate_lock)])
    subprocess.check_call([sys.executable,"-m","pip","check"])
    env = os.environ.copy()
    env.update({"PARTYOPS_DATA_DIR": str((a.output / "synthetic-data").resolve()), "PARTYOPS_ENVIRONMENT": "test", "PARTYOPS_MODE": "personal", "PYTHONPATH": str(Path("backend").resolve())})
    subprocess.check_call([sys.executable,"scripts/validate-win7-cryptography-runtime.py"], env=env)
    subprocess.check_call([sys.executable,"-c","import tkinter, app.main; assert app.main.app.version=='1.4.6-rc.1'; print(tkinter.Tcl().eval('info patchlevel')); from app.database import DatabaseRuntime; DatabaseRuntime().create_schema(); print('WIN7_COMPLETE_CANDIDATE_SCHEMA_PASS')"], env=env)
    subprocess.check_call([sys.executable,"-c","import os,sqlite3,pathlib; d=sqlite3.connect(str(pathlib.Path(os.environ['PARTYOPS_DATA_DIR'])/'partyops.db')); assert d.execute('select version_num from alembic_version').fetchone()[0]=='0028'"], env=env)
    report["status"] = "HOSTED_CPYTHON38_FULL_APPLICATION_SCHEMA_PASS_NOT_WIN7_OS"
    receipt_path.write_text(json.dumps(report, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
