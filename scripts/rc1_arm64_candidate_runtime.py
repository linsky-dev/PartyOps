"""Native ARM64 candidate; retain the original lock's unaffected runtime hashes."""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import tomllib
from packaging.tags import sys_tags
from packaging.markers import default_environment
from packaging.utils import canonicalize_name
from audit_windows_arm64_lock import review_lock

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts/arm64-application-only"
REPLACE = {
    "numpy": ("numpy==2.3.5", "f28620fe26bee16243be2b7b874da327312240a7cdc38b769a697578d2100013"),
    "onnxruntime": ("onnxruntime==1.32.0", "64d394c69d561af3efd4d4f49dcac8f11d0ce135a20701c16d53b74056cd4cf8"),
    "tokenizers": ("tokenizers==0.23.3", "7e48734d2de9260d86f03ab056d2cfeeff3869f61dbd49aaa15a2793b5f3458b"),
    "pymupdf": ("pymupdf-lite==2.1", "8a56ae09707f13aa412b2ab3c451e898b9a3f650e687dd2f3cdf5c9e28175f2d"),
}
def call(*args):
    subprocess.run([sys.executable, *args], check=True, cwd=ROOT)

def main():
    assert sys.platform == "win32" and platform.machine().lower() in ("arm64", "aarch64")
    assert sys.version_info[:2] == (3, 12)
    assert subprocess.check_output(["git","rev-parse","HEAD"],cwd=ROOT,text=True).strip() == os.environ["SOURCE_SHA"]
    OUT.mkdir(parents=True, exist_ok=True)
    lock = (ROOT/"backend/uv.lock").read_bytes()
    packages = {canonicalize_name(p["name"]):p for p in tomllib.loads(lock.decode())["package"]}
    report = review_lock(lock,set(sys_tags()),default_environment())
    (OUT/"lock-audit.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
    if set(report["missing_locked_wheels"]) != set(REPLACE)|{"cryptography"}:
        raise SystemExit("ARM64_LOCK_GAPS_CHANGED")
    lines=[]
    for row in report["dependencies"]:
        name=row["name"]
        if name == "cryptography":
            continue
        if name in REPLACE:
            req,digest=REPLACE[name]
            lines.append(req+" --hash=sha256:"+digest)
            continue
        pkg=packages[name]
        hashes=[w["hash"] for w in pkg["wheels"] if w["url"].split("/")[-1] in row["matching_wheels"]]
        assert hashes and all(h.startswith("sha256:") for h in hashes), name
        lines.append(name+"=="+row["version"]+" "+" ".join("--hash="+h for h in hashes))
    req=OUT/"candidate-runtime.txt"
    req.write_text("\n".join(lines)+"\n",encoding="utf-8")
    wh=OUT/"wheels"
    wh.mkdir(exist_ok=False)
    call("-m","pip","download","--no-deps","--only-binary=:all:","--require-hashes","--dest",str(wh),"-r",str(req))
    call("-m","pip","install","--no-index","--no-deps","--require-hashes","--find-links",str(wh),"-r",str(req))
    check=subprocess.run([sys.executable,"-m","pip","check"],capture_output=True,text=True)
    (OUT/"pip-check.txt").write_text(check.stdout+check.stderr,encoding="utf-8")
    print(check.stdout,check.stderr)
    if check.returncode:
        raise SystemExit("ARM64_CANDIDATE_DEPENDENCY_CONFLICT")
    os.environ["PARTYOPS_DATA_DIR"]=str(OUT/"synthetic-data")
    os.environ["PARTYOPS_ENVIRONMENT"]="test"
    os.environ["PARTYOPS_MODE"]="personal"
    sys.path.insert(0,str(ROOT/"backend"))
    import app.main
    from app.database import DatabaseRuntime
    assert app.main.app.version == "1.4.6-rc.1"
    DatabaseRuntime().create_schema()
    receipt={"source_commit":os.environ["SOURCE_SHA"],"status":"SOURCE_RUNTIME_CANDIDATE_ONLY",
             "lock_sha256":hashlib.sha256(lock).hexdigest(),"candidate_overrides":REPLACE,
             "native_application_import":True,"synthetic_schema":True,"full_pdf_layout":False,
             "production_lock_changed":False,"installer_created":False,"release_ready":False}
    (OUT/"runtime-receipt.json").write_text(json.dumps(receipt,indent=2),encoding="utf-8")

if __name__ == "__main__":
    main()
