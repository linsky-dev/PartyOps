"""Build-only historical donor staging; never substitute an old application."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import zipfile

DONOR="bec5993a12a9a66800fa039045f45329ecc486934cd1d1106c10d5e2ecf78017"
SOURCE=os.environ["SOURCE_SHA"]
ENTRIES=("partyops","partyops-wizard","partyops-client","partyops-updater")
NATIVE=("formatter-host","ocr","office-runtime")
def sha(path):
    with path.open("rb") as f: return hashlib.file_digest(f,"sha256").hexdigest()
def cmd(*args, cwd=None):
    return subprocess.check_output(args,cwd=cwd,text=True).strip()
def write(p,value):
    p.write_text(json.dumps(value,indent=2,ensure_ascii=False)+"\n")
def require(ok,message):
    if not ok: raise RuntimeError(message)
def safe_tree(root):
    for p in root.rglob("*"):
        if p.is_symlink():
            require(p.resolve().is_relative_to(root.resolve()),"outside symlink "+str(p))
        else:
            require(p.is_dir() or p.is_file(),"unexpected special file "+str(p))
def inventory(runtime):
    return {p.relative_to(runtime).as_posix():sha(p) for name in NATIVE
            for p in sorted((runtime/name).rglob("*")) if p.is_file() and not p.is_symlink()}
def audit(donor,stage,out):
    require(sha(donor)==DONOR,"original donor SHA")
    require(cmd("dpkg-deb","-f",str(donor),"Package")=="partyops","donor package")
    require(cmd("dpkg-deb","-f",str(donor),"Architecture")=="loong64","donor ISA")
    require(not stage.exists(),"stage already exists")
    subprocess.check_call(["dpkg-deb","--raw-extract",str(donor),str(stage)])
    runtime=stage/"opt/partyops"
    require(runtime.is_dir() and not runtime.is_symlink(),"donor layout")
    require(not (stage/"var/lib/partyops").exists(),"donor business data")
    safe_tree(runtime)
    # Full donor SHA binds all historical runtime bytes, not just the entry.
    require(all((runtime/x).is_dir() for x in NATIVE),"native closure missing")
    require(all((runtime/x).is_file() for x in ("formatter-host/partyops-document-formatter-host","formatter-host/source-host.json","formatter-host/runtime-evidence.json","ocr/SOURCE.json","ocr/bin/tesseract","office-runtime/SOURCE.json","office-runtime/program/soffice.bin")),"required source/engine missing")
    source=json.loads((runtime/"formatter-host/source-host.json").read_text())
    evidence=json.loads((runtime/"formatter-host/runtime-evidence.json").read_text())
    host=sha(runtime/"formatter-host/partyops-document-formatter-host")
    require(source.get("host_sha256")==host and evidence.get("host_sha256")==host and evidence.get("status")=="passed","historical WPS evidence mismatch")
    require(source.get("architecture")=="loong64","WPS architecture")
    office=json.loads((runtime/"office-runtime/SOURCE.json").read_text())
    require(office.get("architecture")=="loong64" and office.get("soffice_bin_sha256")==sha(runtime/"office-runtime/program/soffice.bin"),"Office source mismatch")
    ocr=json.loads((runtime/"ocr/SOURCE.json").read_text())
    require(ocr.get("architecture")=="loong64" and ocr.get("origin")=="Deepin crimson/main loong64","OCR provenance")
    for name,item in ocr["files"].items():
        p=(runtime/"ocr"/name).resolve()
        require(p.is_relative_to(runtime/"ocr") and p.is_file() and sha(p)==item["sha256"],"OCR member mismatch "+name)
    files=inventory(runtime)
    require(len(files)>100,"native runtime inventory incomplete")
    write(out/"donor-inventory.json",files)
    write(out/"donor-receipt.json",{"donor_sha256":DONOR,"host_sha256":host,"office_version":office.get("version"),"ocr_version":ocr.get("version"),"native_files":len(files),"historical_wps_only":True,"release_ready":False})
def preview_manifest(runtime):
    # Same historical-preview contract as other Linux QA packages; do not
    # rewrite native evidence or substitute a current snapshot claim.
    pins={"formatter-host/partyops-document-formatter-host":"272338c94d791103e5ab8ac5f6419aff03c76a9e30d1e0e7863a8f8d4a133438",
          "formatter-host/source-host.json":"6490037fcb2a3d2e684daad01cdd3231468b205347d55e36310f3efbfc0bf36c",
          "formatter-host/runtime-evidence.json":"c6742a87299b56d8cd62978cee43d7cdfc33d35470ed85121533462a8140cb73"}
    for name,digest in pins.items():
        file=runtime/name
        require(file.is_file() and not file.is_symlink() and sha(file)==digest,"WPS historical pin "+name)
    write(runtime/"RC1-LINUX-BUILD-ONLY.json",{"schema_version":1,"status":"BUILD_ONLY_UNVERIFIED","version":"1.4.6-rc.1","architecture":"loong64","target_os":"Deepin crimson Loong64 GLIBC 2.38","source_sha":SOURCE,"original_package_sha256":DONOR,"historical_wps_host_sha256":pins["formatter-host/partyops-document-formatter-host"],"historical_wps_source_record_sha256":pins["formatter-host/source-host.json"],"historical_wps_evidence_sha256":pins["formatter-host/runtime-evidence.json"],"historical_wps_snapshot_sha256":"15c21b886f6a958fb61a3b106266b446a2b959b0085510015eeb790efaa770d3","historical_wps_host_unchanged":True,"wps_native_tests_rerun":False,"release_ready":False})
    args=[sys.executable,"scripts/generate-release-manifest.py","--root",str(runtime),"--output",str(runtime/"release-manifest.json"),"--version","1.4.6-rc.1","--tag","v1.4.6-rc.1","--commit",SOURCE,"--platform","linux-deb","--architecture","loong64","--runtime-profile","core"]
    for name in ("partyops",*pins,"RC1-LINUX-BUILD-ONLY.json"): args+=["--only-file",name]
    subprocess.check_call(args)
def stage_new(archive,stage,out):
    runtime=stage/"opt/partyops"
    old=json.loads((out/"donor-inventory.json").read_text())
    require(old==inventory(runtime),"donor native inputs changed")
    with zipfile.ZipFile(archive) as z:
        receipt=json.loads(z.read("freeze-receipt.json"))
        require(receipt["source_commit"]==SOURCE and receipt["status"]=="DEEPIN_238_EMULATED_CORE_FREEZE_ONLY" and receipt["target_abi_static_pass"] and receipt["schema_0028"] and not receipt["release_ready"],"freeze receipt")
        tar=stage.parent/"fresh-loong64.tar.gz"
        with z.open("PartyOps-loong64-DEEPIN238-APP-ONLY.tar.gz") as src,tar.open("wb") as dst:
            shutil.copyfileobj(src,dst)
        require(sha(tar)==z.read("frozen-app-sha256.txt").decode().split()[0],"frozen app digest")
    dest=stage.parent/"fresh-loong64"
    dest.mkdir()
    with tarfile.open(tar) as z: z.extractall(dest,filter="data")
    app=dest/"PartyOps"
    safe_tree(app)
    require(set(p.name for p in app.iterdir())==set(ENTRIES)|{"_internal"},"fresh root entry set")
    for name in ENTRIES:
        p=app/name
        require(p.is_file() and not p.is_symlink(),"entry missing")
        b=p.read_bytes()[:64]
        require(b[:6]==b"\x7fELF\x02\x01" and int.from_bytes(b[18:20],"little")==258 and int.from_bytes(b[48:52],"little")&7==3,"entry ISA")
    for name in ("frontend/index.html","alembic.ini","alembic/env.py"):
        require((app/"_internal"/name).is_file(),"missing app resource")
    require(not (runtime/"_internal").is_symlink(),"unsafe old internal")
    shutil.rmtree(runtime/"_internal")
    for name in ENTRIES: (runtime/name).unlink()
    shutil.copytree(app,runtime,dirs_exist_ok=True,symlinks=True)
    for p in app.rglob("*"):
        if p.is_file() and not p.is_symlink():
            rel=p.relative_to(app)
            (runtime/rel).chmod(0o755 if rel.as_posix() in ENTRIES else 0o644)
    for name in ("start.sh","stop.sh","desktop-launcher.sh","open-local-file.sh","install-desktop-shortcut.sh","install-internal-ca.sh","client-config.example.json","partyops.svg"):
        shutil.copy2(Path("packaging/uos")/name,runtime/name)
        (runtime/name).chmod(0o755 if name.endswith(".sh") else 0o644)
    for name in ("post-install-selftest.sh","post-install-services.sh","post-install-verify.sh","post-install-transaction.sh"):
        shutil.copy2(Path("packaging/linux")/name,runtime/name)
        (runtime/name).chmod(0o755)
    for name in ("README.md","CHANGELOG.md","LICENSE","THIRD_PARTY_NOTICES.md"):
        shutil.copy2(name,runtime/name)
    (runtime/"VERSION").write_text("1.4.6-rc.1\n")
    (runtime/"docs").mkdir(exist_ok=True)
    shutil.copy2("docs/release-notes-v1.4.6-rc.1.md",runtime/"docs/release-notes-v1.4.6-rc.1.md")
    for name in ("partyops.desktop","partyops-client.desktop","partyops-file.desktop"):
        shutil.copy2(Path("packaging/uos")/name,stage/"usr/share/applications"/name)
    for name in ("partyops.service","partyops-updater.service"):
        shutil.copy2(Path("packaging/uos")/name,stage/"lib/systemd/system"/name)
    shutil.copy2("packaging/linux/partyops-install-verify.service",stage/"lib/systemd/system/partyops-install-verify.service")
    shutil.copy2("packaging/uos/cn.partyops.update.policy",stage/"usr/share/polkit-1/actions/cn.partyops.update.policy")
    control=stage/"DEBIAN/control"
    text=control.read_text()
    require(text.count("Version: ")==1,"control version")
    control.write_text("\n".join("Version: 1.4.6~rc.1" if x.startswith("Version: ") else x for x in text.splitlines())+"\n")
    scripts={"preinst":Path("packaging/linux/pre-install-stop.sh").read_bytes(),
             "postinst":Path("packaging/linux/post-install-configure.sh").read_bytes()+b"\n/opt/partyops/post-install-transaction.sh loong64 deb\n",
             "prerm":b'#!/bin/sh\nset -e\ncase "${1:-}" in remove|deconfigure) ;; *) exit 0 ;; esac\nexport PARTYOPS_PACKAGE_REMOVE=1\n'+Path("packaging/linux/pre-install-stop.sh").read_bytes()}
    for name,data in scripts.items():
        p=stage/"DEBIAN"/name
        p.write_bytes(data);p.chmod(0o755)
        subprocess.check_call(["sh","-n",str(p)])
    require(old==inventory(runtime),"native historical payload changed")
    safe_tree(runtime)
    require((runtime/"update-public-key.txt").is_file(),"update trust key missing")
    write(runtime/"RC1-LOONG64-BUILD-ONLY.json",{"source_commit":SOURCE,"donor_sha256":DONOR,"historical_wps_only":True,"release_ready":False})
    (runtime/"release-manifest.json").unlink(missing_ok=True)
    (runtime/"release-files.sha256").unlink(missing_ok=True)
    preview_manifest(runtime)
    (runtime/"release-files.sha256").write_text("".join(f"{sha(p)}  ./{p.relative_to(runtime).as_posix()}\n" for p in sorted(runtime.rglob("*")) if p.is_file() and not p.is_symlink() and p.name!="release-files.sha256"))
    subprocess.check_call(["sha256sum","-c","--strict","release-files.sha256"],cwd=runtime,stdout=subprocess.DEVNULL)
    subprocess.check_call([sys.executable,"scripts/verify-linux-elf-glibc.py","--root",str(runtime),"--max-glibc","2.38"])
    write(out/"stage-receipt.json",{"source_commit":SOURCE,"native_bytes_unchanged":True,"abi_pass":True,"installer_created":False,"release_ready":False})
def seal(stage,out):
    require((out/"full-package-selftest.status").read_text().strip()=="PASS","full emulated package test not passed")
    runtime=stage/"opt/partyops"
    require(json.loads((out/"donor-inventory.json").read_text())==inventory(runtime),"native payload changed")
    subprocess.check_call(["sha256sum","-c","--strict","release-files.sha256"],cwd=runtime,stdout=subprocess.DEVNULL)
    p=out/"PartyOps_1.4.6-rc.1_linux_loong64_BUILD_ONLY_UNVERIFIED.deb"
    subprocess.check_call(["dpkg-deb","--root-owner-group","--build",str(stage),str(p)])
    require(cmd("dpkg-deb","-f",str(p),"Architecture")=="loong64" and cmd("dpkg-deb","-f",str(p),"Version")=="1.4.6~rc.1","candidate metadata")
    (out/(p.name+".sha256")).write_text(f"{sha(p)}  {p.name}\n")
    write(out/"candidate-receipt.json",{"source_commit":SOURCE,"sha256":sha(p),"architecture":"loong64","runtime_profile":"core","installer_created":True,"emulated_package_selftest_passed":True,"native_target_install_tested":False,"current_wps_real_tested":False,"release_ready":False})
if __name__=="__main__":
    action=sys.argv[1]
    args=[Path(x).resolve() for x in sys.argv[2:]]
    {"audit":audit,"stage":stage_new,"seal":seal,"preview_manifest":preview_manifest}[action](*args)
