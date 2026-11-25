"""Collect exact lock wheels from verified prior CI and build remaining target inputs.

No source or dependency substitutions; candidate only. Build tools and nested
upstream build inputs are not release-signed; keep evidence for later audit.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tomllib
import urllib.request
import zipfile

GROUPS = [
    (45520102002,14982242710,"rust-crypto","ba1894bde4ce8b181106471f4bba7997d5eb12d3"),
    (45520102002,14982096691,"rust-pydantic","ba1894bde4ce8b181106471f4bba7997d5eb12d3"),
    (45520102002,14981167417,"rust-calamine","ba1894bde4ce8b181106471f4bba7997d5eb12d3"),
    (45520102002,14980452974,"numeric","ba1894bde4ce8b181106471f4bba7997d5eb12d3"),
    (45418034341,14934911310,"images","451bf87fcc12b570bd8e7cdd56bd18e7616a5bac"),
    (45418034341,14934335056,"c-extensions","451bf87fcc12b570bd8e7cdd56bd18e7616a5bac"),
]
repo = os.environ["GITHUB_REPOSITORY"]
stage=sys.argv[1]
root, out = (Path(x).resolve() for x in sys.argv[2:])
wheelhouse = out/"wheelhouse"
wheelhouse.mkdir(exist_ok=True)
lock = {p["name"]:p for p in tomllib.loads(Path("backend/uv.lock").read_text())["package"]}
def sha(p):
    with p.open("rb") as f:
        return hashlib.file_digest(f,"sha256").hexdigest()
def gh(path):
    return json.loads(subprocess.check_output(["gh","api",f"repos/{repo}/"+path]))
def download(entry, dest):
    url=entry["url"]
    assert url.startswith("https://files.pythonhosted.org/"),url
    with urllib.request.urlopen(url,timeout=180) as src,dest.open("wb") as f:
        while b:=src.read(1024*1024): f.write(b)
    assert dest.stat().st_size==entry["size"] and sha(dest)==entry["hash"].removeprefix("sha256:"),dest
def guest(*args):
    return subprocess.check_call(["sudo","chroot",str(root),*args])
def log_guest(name,*args):
    with (out/name).open("wb") as f:
        proc=subprocess.Popen(["sudo","timeout","--kill-after=60","10000","chroot",str(root),*args],stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        assert proc.stdout is not None
        for line in iter(proc.stdout.readline,b""):
            f.write(line); f.flush()
            sys.stdout.buffer.write(line); sys.stdout.buffer.flush()
        if proc.wait():
            raise RuntimeError(name+" failed")

py="/opt/partyops-freeze-env/bin/python"
if stage=="collect":
    rows=[]
    native=set()
    for run,aid,group,head in GROUPS:
        print(f"IMPORT {group} run={run} artifact={aid}",flush=True)
        info=gh(f"actions/runs/{run}")
        assert info["head_sha"]==head and info["head_repository"]["full_name"]==repo
        assert info["path"].split("@")[0]==".github/workflows/cloud-rc1-loong64-deepin-native-wheels.yml"
        jobs=gh(f"actions/runs/{run}/jobs?per_page=100")["jobs"]
        assert any(j["name"]=="baseline ("+group+")" and j["conclusion"]=="success" for j in jobs),group
        a=gh(f"actions/artifacts/{aid}")
        assert a["name"]==f"RC1-Loong64-Deepin238-{group}-NATIVE-WHEELS-NOT-RELEASE-{head}" and not a["expired"]
        assert a["workflow_run"]["id"]==run
        zpath=out/(group+".zip")
        with zpath.open("wb") as f:
            subprocess.check_call(["gh","api",f"repos/{repo}/actions/artifacts/{aid}/zip"],stdout=f)
        assert a["digest"]=="sha256:"+sha(zpath)
        with zipfile.ZipFile(zpath) as z:
            receipt=json.loads(z.read("wheel-build-receipt.json"))
            inputs={p["name"]:p for p in json.loads(z.read("source-hashes.json"))}
            assert receipt["target_glibc"]=="2.38" and receipt["python"]=="3.12.13" and receipt["group"]==group
            for item in receipt["results"]:
                name=item["name"]
                p=lock[name]
                assert name not in native and item["version"]==p["version"]
                assert inputs[name]["sha256"]==p["sdist"]["hash"].removeprefix("sha256:")
                assert item["status"]=="NATIVE_WHEEL_ABI_PASS"
                wheel=item["wheel"]
                assert Path(wheel).name==wheel and wheel.endswith(".whl")
                dest=wheelhouse/wheel
                dest.write_bytes(z.read("wheelhouse/"+wheel))
                assert sha(dest)==item["wheel_sha256"]
                rows.append({"name":name,"version":p["version"],"wheel":wheel,"sha256":sha(dest),"artifact":aid,"run":run})
                native.add(name)
        zpath.unlink()
    assert native=={"numpy","cryptography","pydantic-core","python-calamine","lxml","pillow","cffi","argon2-cffi-bindings","greenlet","sqlalchemy","markupsafe"}
    # Select only applicable universal locked wheels; avoid dev and optional model stack.
    pure=("alembic","annotated-doc","annotated-types","anyio","argon2-cffi","certifi","click","defusedxml","et-xmlfile","fastapi","h11","httpcore","httpx","idna","mako","openpyxl","packaging","pycparser","pydantic","pydantic-settings","pytesseract","python-docx","python-dotenv","python-multipart","sse-starlette","starlette","typing-extensions","typing-inspection","uvicorn")
    for name in pure:
        p=lock[name]
        entries=[e for e in p["wheels"] if e["url"].endswith("-none-any.whl")]
        assert len(entries)==1,name
        e=entries[0]
        dest=wheelhouse/e["url"].rsplit("/",1)[1]
        download(e,dest)
        rows.append({"name":name,"version":p["version"],"wheel":dest.name,"sha256":sha(dest),"lock":True})
    (out/"verified-inputs.json").write_text(json.dumps(rows,indent=2))
    guest("mkdir","-p","/tmp/wheelhouse","/tmp/build-input")
    subprocess.check_call(["sudo","cp","-a",str(wheelhouse)+"/.",str(root/"tmp/wheelhouse")])
    print("LOCKED_NATIVE_AND_PURE_WHEEL_COLLECTION_PASS",flush=True)

elif stage=="restore":
    # Prior staged job is still running: the completed collection is immutable.
    run=45566241640
    aid=15001246798
    head="37557af9ce9c553af1982b6726c51654eeef1386"
    info=gh(f"actions/runs/{run}")
    assert info["head_sha"]==head and info["head_repository"]["full_name"]==repo
    assert info["path"].split("@")[0]==".github/workflows/cloud-rc1-loong64-converged-deb.yml"
    jobs=gh(f"actions/runs/{run}/jobs?per_page=100")["jobs"]
    assert any(j["name"]=="freeze" and any(s["name"]=="Verify exact source and import six immutable native wheel groups" and s["conclusion"]=="success" for s in j["steps"]) for j in jobs)
    a=gh(f"actions/artifacts/{aid}")
    assert a["name"]=="RC1-Loong64-converged-locked-wheel-collection-"+os.environ["SOURCE_SHA"] and not a["expired"] and a["workflow_run"]["id"]==run
    zpath=out/"verified-collection.zip"
    with zpath.open("wb") as f:
        subprocess.check_call(["gh","api",f"repos/{repo}/actions/artifacts/{aid}/zip"],stdout=f)
    assert a["digest"]=="sha256:"+sha(zpath)
    with zipfile.ZipFile(zpath) as z:
        rows=json.loads(z.read("verified-inputs.json"))
        assert len(rows)==40 and len({x["name"] for x in rows})==40
        assert {x["wheel"] for x in rows}=={Path(n).name for n in z.namelist() if n.endswith(".whl")}
        for row in rows:
            name=row["name"]
            assert lock[name]["version"]==row["version"] and Path(row["wheel"]).name==row["wheel"]
            p=wheelhouse/row["wheel"]
            p.write_bytes(z.read("wheelhouse/"+p.name))
            assert sha(p)==row["sha256"]
            if row.get("lock"):
                assert any(e["hash"]=="sha256:"+sha(p) for e in lock[name]["wheels"])
            else:
                assert (row["run"],row["artifact"]) in {(run,aid) for run,aid,_,_ in GROUPS}
        (out/"verified-inputs.json").write_bytes(z.read("verified-inputs.json"))
    zpath.unlink()
    guest("mkdir","-p","/tmp/wheelhouse","/tmp/build-input")
    subprocess.check_call(["sudo","cp","-a",str(wheelhouse)+"/.",str(root/"tmp/wheelhouse")])
    print("RESTORED_EXACT_LOCKED_40_WHEEL_COLLECTION",flush=True)

elif stage=="restore_complete":
    # The prior run failed AFTER the completed offline closure and schema gates.
    # Reuse only that completed stage, not its failed app/installer.
    run,aid=45575869941,15009913031
    head="e40d5c1182a874dea5f60347f3d4dd70ec5cde27"
    info=gh(f"actions/runs/{run}")
    assert info["head_sha"]==head and info["head_repository"]["full_name"]==repo
    assert info["path"].split("@")[0]==".github/workflows/cloud-rc1-loong64-accelerated-deb.yml"
    jobs=gh(f"actions/runs/{run}/jobs?per_page=100")["jobs"]
    names={"Compile exact PyMuPDF 1.26.0 target wheel with live output","Compile target freeze tools and validate complete offline wheelhouse","Compile pinned SQLite and run dependency and schema gates without networking"}
    assert any(j["name"]=="freeze" and names <= {s["name"] for s in j["steps"] if s["conclusion"]=="success"} for j in jobs)
    a=gh(f"actions/artifacts/{aid}")
    assert a["name"]=="RC1-Loong64-accelerated-complete-closure-740251bc84d7209a5effe24e6c6711274560fc76" and not a["expired"] and a["workflow_run"]["id"]==run
    zpath=out/"complete-closure.zip"
    with zpath.open("wb") as f:
        subprocess.check_call(["gh","api",f"repos/{repo}/actions/artifacts/{aid}/zip"],stdout=f)
    assert a["digest"]=="sha256:"+sha(zpath)
    with zipfile.ZipFile(zpath) as z:
        manifest=z.read("closure-sha256.txt").decode()
        rows=[line.split("  ",1) for line in manifest.splitlines()]
        assert len(rows)==46 and len({name for _,name in rows})==46
        assert {name for _,name in rows}=={Path(n).name for n in z.namelist() if n.endswith(".whl")}
        for digest,name in rows:
            assert re.fullmatch("[0-9a-f]{64}",digest) and Path(name).name==name and name.endswith(".whl")
            p=wheelhouse/name
            p.write_bytes(z.read("wheelhouse/"+name))
            assert sha(p)==digest
        assert any(name=="pymupdf-1.26.0-cp312-abi3-linux_loongarch64.whl" and digest=="d7529efa8ad9dd5913209c68e463efbbcef50edd8bb0944bde4a56dcd49d3cdf" for digest,name in rows)
        (out/"closure-sha256.txt").write_text(manifest)
    zpath.unlink()
    guest("mkdir","-p","/tmp/wheelhouse","/tmp/build-input")
    subprocess.check_call(["sudo","cp","-a",str(wheelhouse)+"/.",str(root/"tmp/wheelhouse")])
    (out/"reused-closure-receipt.json").write_text(json.dumps({"source_commit":os.environ["SOURCE_SHA"],"run":run,"artifact":aid,"artifact_digest":a["digest"],"wheel_count":len(rows),"pdf_engine":"1.26.0","release_ready":False},indent=2))
    print("RESTORED_EXACT_COMPLETE_46_WHEEL_CLOSURE",flush=True)

elif stage=="pdf":
    py="/opt/partyops-freeze-env/bin/python"
    # Build locked PDF from exact source archive. No Debian sid binary is accepted.
    p=lock["pymupdf"]
    pdf=out/p["sdist"]["url"].rsplit("/",1)[1]
    download(p["sdist"],pdf)
    subprocess.check_call(["sudo","cp",str(pdf),str(root/"tmp/build-input"/pdf.name)])
    # Use signed target-distribution SWIG and clang bindings. The old isolated
    # PEP517 path tried to compile CMake from source for six hours before
    # starting MuPDF. No application or PDF engine version changes.
    clang_path=subprocess.check_output(["sudo","chroot",str(root),"/usr/bin/python3.12","-c","from clang import cindex; from pathlib import Path; print(Path(cindex.__file__).resolve().parents[1])"],text=True).strip()
    assert clang_path.startswith("/usr/lib/python3/"),clang_path
    libclang=subprocess.check_output(["sudo","chroot",str(root),"/bin/sh","-c","find /usr/lib/llvm-* -path '*/lib/libclang.so' -print | sort -V | tail -1"],text=True).strip()
    assert libclang.startswith("/usr/lib/llvm-") and libclang.endswith("/lib/libclang.so"),libclang
    env=["/usr/bin/env","-i","HOME=/root","PATH=/opt/partyops-freeze-env/bin:/usr/bin:/bin","LANG=C.UTF-8","PYTHONPATH="+clang_path,"LD_LIBRARY_PATH="+str(Path(libclang).parent),"MAKEFLAGS=-j4","PYMUPDF_SETUP_MUPDF_BUILD_TYPE=release"]
    guest(*env,py,"-c","from clang import cindex; print(cindex.__file__); print(cindex.Index.create())")
    log_guest("pdf-native-build.log",*env,py,"-m","pip","wheel","--verbose","--no-build-isolation","--no-deps","--wheel-dir","/tmp/wheelhouse","/tmp/build-input/"+pdf.name)
    print("NATIVE_PDF_WHEEL_BUILD_PASS",flush=True)

elif stage=="tools":
    # Pin top-level tool versions from requirements-build; pip's transitive build tools
    # remain candidate evidence, not an approved reproducible release toolchain.
    log_guest("freeze-tool-build.log",py,"-m","pip","wheel","--wheel-dir","/tmp/wheelhouse","PyInstaller==6.21.0","wheel==0.45.1","setuptools==80.9.0","packaging==25.0")
    print("NATIVE_FREEZE_TOOL_WHEEL_BUILD_PASS",flush=True)

elif stage=="verify":
    for file in (root/"tmp/wheelhouse").glob("*.whl"):
        dst=wheelhouse/file.name
        if dst.exists():
            assert sha(file)==sha(dst),file
        else:
            subprocess.check_call(["sudo","cp",str(file),str(dst)])
            subprocess.check_call(["sudo","chown",f"{os.getuid()}:{os.getgid()}",str(dst)])
    # Real repository gate checks transitive versions, tags and no duplicate names.
    subprocess.check_call([sys.executable,"scripts/validate-uos-wheelhouse.py","--architecture","loong64","--runtime-profile","core","--wheelhouse",str(wheelhouse),"--requirements","backend/requirements.txt","packaging/uos/requirements-core.txt","packaging/uos/requirements-build.txt"])
    # Inspect every wheel's native code, even new PDF/tool wheels.
    import importlib.util
    spec=importlib.util.spec_from_file_location("abi","scripts/verify-linux-elf-glibc.py")
    abi=importlib.util.module_from_spec(spec);spec.loader.exec_module(abi)
    import tempfile
    for wheel in wheelhouse.glob("*.whl"):
        with tempfile.TemporaryDirectory() as td:
            d=Path(td)
            with zipfile.ZipFile(wheel) as z:
                for m in z.infolist():
                    assert not m.filename.startswith("/") and ".." not in Path(m.filename).parts
                z.extractall(d)
            elf=[]
            for f in d.rglob("*"):
                if not f.is_file(): continue
                with f.open("rb") as h: b=h.read(64)
                if b[:4]==b"\x7fELF":
                    # PyInstaller ships foreign bootloaders in source wheels; do not
                    # admit any foreign ELF to this target closure.
                    assert b[:6]==b"\x7fELF\x02\x01" and int.from_bytes(b[18:20],"little")==258 and int.from_bytes(b[48:52],"little")&7==3,str(f)
                    elf.append(f)
            if elf: abi.verify(d,(2,38))
    (out/"closure-sha256.txt").write_text("".join(f"{sha(p)}  {p.name}\n" for p in sorted(wheelhouse.glob("*.whl"))))
    # Install only through the verified local wheelhouse, no network.
    guest("mkdir","-p","/tmp/requirements")
    for path in ("backend/requirements.txt","packaging/uos/requirements-core.txt","packaging/uos/requirements-build.txt"):
        subprocess.check_call(["sudo","cp",path,str(root/"tmp/requirements"/Path(path).name)])
    subprocess.check_call(["sudo","unshare","--net","chroot",str(root),py,"-m","pip","install","--no-index","--find-links","/tmp/wheelhouse","-r","/tmp/requirements/requirements.txt","-r","/tmp/requirements/requirements-core.txt","-r","/tmp/requirements/requirements-build.txt"])
    guest(py,"-m","pip","check")

else:
    raise ValueError("unknown stage")
