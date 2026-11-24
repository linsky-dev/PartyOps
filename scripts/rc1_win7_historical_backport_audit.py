"""Recover candidate inputs without treating historical evidence as current acceptance."""
import argparse
import hashlib
import json
import pathlib
import re
import subprocess
import sys
import tarfile
import urllib.parse
import urllib.request
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
HISTORY = ROOT / "backend/legacy/historical-backports"
OLD = "21bbe6e26b7dc161f2ac495b3bcf84fc8ed9b1bd"
COMPONENTS = {
    "click": "8.3.3", "cryptography": "50.0.0", "fastapi": "0.115.14",
    "filelock": "3.20.3", "pillow": "12.3.0", "python-dotenv": "1.2.2",
    "python-multipart": "0.0.31", "requests": "2.33.0", "starlette": "1.3.1",
    "urllib3": "2.7.0",
}
NATIVE = {"cryptography", "pillow"}

def sha(p):
    h = hashlib.sha256()
    with p.open("rb") as f:
        for b in iter(lambda: f.read(1048576), b""):
            h.update(b)
    return h.hexdigest()

def lock_hashes(arch):
    result, name = {}, None
    for line in (ROOT / ("backend/legacy/requirements-windows7-" + arch + ".lock")).read_text(encoding="utf-8").splitlines():
        m = re.match(r"^([A-Za-z0-9_.-]+)==([^ ]+)", line)
        if m:
            name = m.group(1).lower().replace("_", "-")
            result[name] = {"version": m.group(2), "sha256": []}
        elif name:
            result[name]["sha256"].extend(re.findall(r"--hash=sha256:([a-f0-9]{64})", line))
    return result

def download(url, host, out=None):
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme != "https" or parsed.hostname != host or parsed.username or parsed.password:
        raise ValueError("unexpected upstream URL")
    with urllib.request.urlopen(url, timeout=45) as response:
        if urllib.parse.urlsplit(response.geturl()).hostname != host:
            raise ValueError("unexpected upstream redirect")
        if out:
            import shutil
            with out.open("wb") as stream:
                shutil.copyfileobj(response, stream)
        else:
            return json.load(response)

def run(args, cwd, log):
    p = subprocess.run(args, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=300)
    with log.open("a", encoding="utf-8") as f:
        f.write("$ " + " ".join(map(str, args)) + "\n" + p.stdout + p.stderr + "\n")
    return p.returncode

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", type=pathlib.Path, required=True)
    args = parser.parse_args()
    out = args.out.resolve()
    out.mkdir(parents=True, exist_ok=False)
    locks = {a: lock_hashes(a) for a in ("amd64", "x86")}
    receipt = {"status":"HISTORICAL_BACKPORT_INPUT_RECOVERY_ONLY","historical_commit":OLD,
               "components":{},"production_lock_changed":False,"installer_created":False,"release_ready":False}
    for name, version in COMPONENTS.items():
        row = {"status":"BLOCKED","upstream_version":version}
        receipt["components"][name] = row
        folder = out/name
        folder.mkdir()
        try:
            relative = HISTORY.relative_to(ROOT)/"patches"/(name+"-"+version+"-partyops-win7.patch")
            # Read Git blob bytes, not a checkout with platform newline conversions.
            patch = folder/"original.patch"
            patch.write_bytes(subprocess.check_output(["git","show","HEAD:"+relative.as_posix()],cwd=ROOT))
            row["patch_sha256"] = sha(patch)
            row["current_locked_hashes"] = {a:locks[a].get(name) for a in locks}
            historical = HISTORY/"evidence"/(name+".json")
            historical_pin = None
            if historical.exists():
                evidence=json.loads(historical.read_text(encoding="utf-8"))
                historical_pin=evidence["source_archive_sha256"]
                assert row["patch_sha256"] == evidence["build_parameters"]["patch_sha256"], "historical patch digest mismatch"
                row["historical_wheel_hash_matches_current_lock"] = {
                    a: evidence["wheel_sha256"]["win_amd64" if a=="amd64" else "win32"] in locks[a][name]["sha256"] for a in locks}
            meta=download("https://pypi.org/pypi/"+name+"/"+version+"/json","pypi.org")
            files=[f for f in meta["urls"] if f["packagetype"]=="sdist"]
            assert len(files)==1, "ambiguous source distribution"
            source=files[0]
            filename=pathlib.PurePosixPath(source["filename"])
            assert filename.name==str(filename) and str(filename).endswith(".tar.gz"), "unsupported sdist"
            archive=folder/filename.name
            download(source["url"],"files.pythonhosted.org",archive)
            digest=sha(archive)
            assert digest==source["digests"]["sha256"], "published source digest mismatch"
            row.update(upstream_sdist_sha256=digest, upstream_url=source["url"], historical_archive_sha256=historical_pin)
            if historical_pin and digest != historical_pin:
                row["status"]="HISTORICAL_ARCHIVE_DIFFERS_FROM_PYPI_SDIST"
                continue
            extract=folder/"source"
            extract.mkdir()
            with tarfile.open(archive) as tar:
                tar.extractall(extract,filter="data")
            roots=[p for p in extract.iterdir() if p.is_dir()]
            assert len(roots)==1, "unexpected sdist root"
            source_root=roots[0]
            log=folder/"build.log"
            # Prevent git apply from interpreting the nested source as a path in
            # the PartyOps repository and silently skipping every patch hunk.
            if run(["git","init","--quiet"],source_root,log):
                raise RuntimeError("isolated git init failed")
            if run(["git","apply","--check",str(patch)],source_root,log):
                row["status"]="PATCH_CHECK_FAILED"
                continue
            if run(["git","apply",str(patch)],source_root,log):
                raise RuntimeError("patch apply failed")
            import shutil
            shutil.rmtree(source_root/".git")
            row["status"]="PATCH_APPLIED_SOURCE_ONLY"
            if name in NATIVE:
                row["native_rebuild_required"]=True
                continue
            wheels=folder/"candidate-wheels"
            wheels.mkdir()
            if run([sys.executable,"-m","pip","wheel","--disable-pip-version-check","--no-deps","--wheel-dir",str(wheels),str(source_root)],source_root,log):
                row["status"]="CANDIDATE_WHEEL_BUILD_FAILED"
                continue
            candidates=list(wheels.glob("*.whl"))
            assert len(candidates)==1, "ambiguous candidate wheels"
            wheel=candidates[0]
            with zipfile.ZipFile(wheel) as z:
                meta_names=[n for n in z.namelist() if n.endswith(".dist-info/METADATA")]
                assert len(meta_names)==1
                from email import message_from_bytes
                metadata=message_from_bytes(z.read(meta_names[0]))
                assert metadata["Version"]==version+"+partyops.1", "backport version mismatch"
                assert wheel.name.endswith("-none-any.whl"), "expected architecture independent wheel"
            wheel_digest=sha(wheel)
            row.update(status="REBUILT_CANDIDATE_NOT_APPROVED",candidate_wheel=wheel.name,candidate_sha256=wheel_digest,
                       matches_current_lock={a:wheel_digest in locks[a][name]["sha256"] for a in locks})
        except Exception as e:
            row["error"]=str(e)
        finally:
            (out/"receipt.json").write_text(json.dumps(receipt,indent=2)+"\n",encoding="utf-8")
    missing=sorted({name for lock in locks.values() for name,row in lock.items() if "+partyops." in row["version"] and name not in COMPONENTS})
    receipt["private_patch_not_recovered"]=missing
    (out/"receipt.json").write_text(json.dumps(receipt,indent=2)+"\n",encoding="utf-8")
    print(json.dumps({n:r["status"] for n,r in receipt["components"].items()},indent=2))
    print("WIN7_HISTORICAL_INPUT_AUDIT_COMPLETE_NOT_INSTALLER",missing)

if __name__=="__main__":
    main()
