#!/usr/bin/env python3
"""Build actual unsigned RC1 openEuler x86_64/aarch64 RPM candidates.

Fresh PartyOps RC1 four native entries come from a source-bound PyInstaller
receipt. Historical native WPS/Office/OCR/LLM assets are copied unchanged from
the exact, SHA-pinned public RC6 RPM, and their hashes remain auditable.
No WPS native acceptance, on-device installation, or publication is implied.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
from pathlib import Path

import rc1_uos_assemble_buildonly_deb as common

ROOT = Path(__file__).resolve().parents[1]
SHA = re.compile(r"^[0-9a-f]{64}$")
PINS = {
    "amd64": {
        "rpm_arch": "x86_64",
        "machine": "x86_64",
        "target_os": "openEuler 24.03 LTS-SP2 x86_64",
        "donor": "e7c34a6090689f40dfae3aa3403b7b6ad0b5d4c3748e07005bb73ed1b32632ac",
        "host": "26fba25f527dc66c4ca5b6197e89c873d0397765d6e88e7fb739710374e3da4f",
        "record": "b4fba6efaa24034f02aa242e4b41433e542bf9f6f681f8bdeae2e7c70310abf7",
        "evidence": "77dcfc30c2244a18870c838c479a3f1ef32c0bb754c04e7e3604b087330f348f",
        "office_archive": "a893a4f37a8b3fe110da92bb0135f488f8d695cd40cb7ce59c65bb525849bb67",
        "office_packages": "dd5ddb478f8863533b48baf2273411ab7c110f4609a74590223f7d9716dcb6cb",
        "loader": "ld-linux-x86-64.so.2",
    },
    "arm64": {
        "rpm_arch": "aarch64",
        "machine": "aarch64",
        "target_os": "openEuler 24.03 LTS-SP2 aarch64",
        "donor": "f507da109c1110e2c553ac9cb44a7f05205156132fb241ba057028571344e4f4",
        "host": "76ae4182c03e5cf7e7399379cee614b6055d01e170b4258f3d6b2493cb98adb7",
        "record": "95ba0649d3156ba655bed2ecf5ae084de8b5e11c75720de8a7b8fa899ff25f1a",
        "evidence": "cc9ab788ca58b98ae462b8d55e06ef5a4dec169f8cb0fcfe0b8799a93b2c8403",
        "office_archive": "a47d693dce67d5f5e15ee6f7ed2faaba5a2234fd21c3cd0227cf0567e63f95a4",
        "office_packages": "82b2b3b8c65cc1fcd369b86b0ca0b3ed675304898354b8f15143ba57365e90",
        "loader": "ld-linux-aarch64.so.1",
    },
}

def check_source_application(app: Path, receipt: Path, commit: str, arch: str) -> dict[str, str]:
    metadata=json.loads(receipt.read_text(encoding="utf-8"))
    if (
        metadata.get("source_sha")!=commit
        or metadata.get("architecture")!=arch
        or metadata.get("status")!="PASS_FRESH_APP_ONLY"
        or metadata.get("release_ready") is not False
    ):
        raise RuntimeError("[RPM_FRESH_APP_SOURCE_OR_ARCH_INVALID]")
    entries=metadata.get("source_matched_executable_sha256")
    if not isinstance(entries,dict) or set(entries)!=set(common.ENTRYPOINTS):
        raise RuntimeError("[RPM_FRESH_APP_EXECUTABLE_SET_INVALID]")
    for name,digest in entries.items():
        if not isinstance(digest,str) or not SHA.fullmatch(digest):
            raise RuntimeError("[RPM_FRESH_APP_UNPINNED_BINARY]")
        common.require_bytes(app/name,digest,"RPM_FRESH_APP_BINARY_MISMATCH")
    for rel in ("_internal/alembic.ini","_internal/alembic/env.py","_internal/frontend/index.html"):
        if not (app/rel).is_file():
            raise RuntimeError("[RPM_FRESH_APP_MISSING_RESOURCE] "+rel)
    return entries

def check_original_native(root: Path, pin: dict[str,str]) -> None:
    fmt=root/"formatter-host"
    files={
        "partyops-document-formatter-host":pin["host"],
        "source-host.json":pin["record"],
        "runtime-evidence.json":pin["evidence"],
    }
    for rel,digest in files.items():
        common.require_bytes(fmt/rel,digest,"RPM_NATIVE_RC6_WPS_BYTES_CHANGED")
    source=json.loads((fmt/"source-host.json").read_text(encoding="utf-8"))
    evidence=json.loads((fmt/"runtime-evidence.json").read_text(encoding="utf-8"))
    if (
        source.get("source_snapshot_sha256")!=common.LEGACY_SNAPSHOT_SHA
        or source.get("host_sha256")!=pin["host"]
        or source.get("architecture")!=("amd64" if pin["rpm_arch"]=="x86_64" else "arm64")
        or evidence.get("host_sha256")!=pin["host"]
        or evidence.get("status")!="passed"
        or evidence.get("rendered_pages")!=3
        or evidence.get("feature_cases")!=10
    ):
        raise RuntimeError("[RPM_HISTORICAL_WPS_RECORD_UNBOUND]")
    vtable=fmt/"word-vtable-map.json"
    if not vtable.is_file() or source.get("word_vtable_map_sha256")!=common.sha256(vtable):
        raise RuntimeError("[RPM_HISTORICAL_WPS_MAP_MISMATCH]")
    for name in ("LICENSE-WPS-SDK.txt","LICENSE-MONO-RUNTIME.txt"):
        if not (fmt/name).is_file():
            raise RuntimeError("[RPM_WPS_LICENSE_MISSING]")
    office=root/"office-runtime"
    sfile=office/"SOURCE.json"
    if sfile.is_symlink() or not sfile.is_file():
        raise RuntimeError("[RPM_OFFICE_PROVENANCE_MISSING]")
    meta=json.loads(sfile.read_text(encoding="utf-8"))
    expected={
        "version":"25.8.7.2",
        "architecture":"amd64" if pin["rpm_arch"]=="x86_64" else "arm64",
        "origin":"The Document Foundation official archive",
        "archive_sha256":pin["office_archive"],
        "private_runtime_glibc":"2.34",
        "minimum_host_glibc":"2.17",
        "private_runtime_packages_sha256":pin["office_packages"],
    }
    if any(meta.get(k)!=v for k,v in expected.items()):
        raise RuntimeError("[RPM_OFFICE_PRIVATE_SOURCE_INVALID]")
    packages=office/"PRIVATE_RUNTIME_PACKAGES.txt"
    manifest=office/"PRIVATE_RUNTIME_LIBS.txt"
    if not packages.is_file() or not manifest.is_file() or common.sha256(packages)!=pin["office_packages"]:
        raise RuntimeError("[RPM_OFFICE_PRIVATE_CLOSURE_LIST_UNVERIFIED]")
    verified=set()
    for line in manifest.read_text(encoding="utf-8").splitlines():
        items=line.split("\t")
        if len(items)!=3:
            raise RuntimeError("[RPM_OFFICE_LIB_MANIFEST_RECORD_INVALID]")
        name,_origin,digest=items
        if name!=Path(name).name or name in verified or not SHA.fullmatch(digest):
            raise RuntimeError("[RPM_OFFICE_LIB_MANIFEST_NAME_INVALID]")
        verified.add(name)
        common.require_bytes(office/"private-runtime"/name,digest,"RPM_OFFICE_PRIVATE_LIB_CHANGED")
    if pin["loader"] not in verified or "libICE.so.6" not in verified:
        raise RuntimeError("[RPM_OFFICE_LIBC_234_LOADER_UNVERIFIED]")
    wrapper=office/"program/soffice"
    if not wrapper.is_file() or not all(t in wrapper.read_text() for t in ("../private-runtime","--library-path",'if [[ "$status" -eq 81 ]]')):
        raise RuntimeError("[RPM_OFFICE_PRIVATE_LOADER_WRAPPER_INVALID]")
    for p in office.rglob("*"):
        if p.is_symlink() and not p.resolve().is_relative_to(office.resolve()):
            raise RuntimeError("[RPM_OFFICE_SYMLINK_OUTSIDE_CLOSURE]")
    if not (root/"ocr/bin/tesseract").is_file() or not (root/"llama-server").is_file():
        raise RuntimeError("[RPM_ORIGINAL_OCR_OR_LLM_MISSING]")

def extract_original_rpm(donor: Path, staged: Path) -> None:
    proc=subprocess.Popen(["rpm2cpio",str(donor)],stdout=subprocess.PIPE)
    try:
        result=subprocess.run(
            ["cpio","--extract","--make-directories","--quiet","--no-absolute-filenames"],
            stdin=proc.stdout,cwd=staged,check=False,
        )
    finally:
        assert proc.stdout is not None
        proc.stdout.close()
    status=proc.wait()
    if result.returncode or status:
        raise RuntimeError("[RPM_ORIGINAL_DONOR_EXTRACTION_FAILED]")

def stage_installer_scripts(staged: Path, runtime: Path) -> None:
    common.stage_current_installer_files(staged,runtime)
    old_cache=staged/"var/cache/partyops"
    if old_cache.exists():
        if old_cache.is_symlink():
            raise RuntimeError("[RPM_ORIGINAL_ROLLBACK_CACHE_IS_LINK]")
        shutil.rmtree(old_cache)
    if (staged/"var/lib/partyops").exists():
        raise RuntimeError("[RPM_RC6_CARRIES_BUSINESS_DATA]")

def create_manifest(runtime: Path, commit: str, pin: dict[str,str], arch: str) -> None:
    marker={
        "schema_version":1,"status":"BUILD_ONLY_UNVERIFIED",
        "version":"1.4.6-rc.1","architecture":arch,
        "target_os":pin["target_os"],"source_sha":commit,
        "original_package_sha256":pin["donor"],
        "historical_wps_host_sha256":pin["host"],
        "historical_wps_source_record_sha256":pin["record"],
        "historical_wps_evidence_sha256":pin["evidence"],
        "historical_wps_snapshot_sha256":common.LEGACY_SNAPSHOT_SHA,
        "historical_wps_host_unchanged":True,
        "wps_native_tests_rerun":False,
        "release_ready":False,
        "native_openeuler_install_tested":False,
        "signed":False,
    }
    (runtime/"RC1-LINUX-BUILD-ONLY.json").write_text(
        json.dumps(marker,indent=2,ensure_ascii=False)+"\n",encoding="utf-8"
    )
    (runtime/"release-manifest.json").unlink(missing_ok=True)
    all_hash=[]
    for p in sorted(runtime.rglob("*")):
        if p.is_symlink() and not p.resolve().is_relative_to(runtime.resolve()):
            raise RuntimeError("[RPM_PAYLOAD_EXTERNAL_SYMLINK]")
        if not p.is_file() or p.is_symlink() or p.name in {"release-manifest.json","release-files.sha256"}:
            continue
        all_hash.append(f"{common.sha256(p)}  ./{p.relative_to(runtime).as_posix()}")
    (runtime/"release-files.sha256").write_text("\n".join(all_hash)+"\n",encoding="utf-8")
    common.command(
        sys.executable,str(ROOT/"scripts/generate-release-manifest.py"),
        "--root",str(runtime),"--output",str(runtime/"release-manifest.json"),
        "--version","1.4.6-rc.1","--tag","v1.4.6-rc.1",
        "--commit",commit,"--platform","linux-rpm",
        "--architecture",arch,"--runtime-profile","uos-v20",
    )
    check=json.loads((runtime/"release-manifest.json").read_text(encoding="utf-8"))
    indexes={entry["path"]:entry["sha256"] for entry in check["files"]}
    if any(indexes.get("formatter-host/"+name)!=digest for name,digest in {
        "partyops-document-formatter-host":pin["host"],
        "source-host.json":pin["record"],
        "runtime-evidence.json":pin["evidence"],
    }.items()):
        raise RuntimeError("[RPM_MANIFEST_WPS_NOT_PINNED]")
    common.command("sha256sum","--check","release-files.sha256",cwd=runtime)

def build_rpm_spec(builddir: Path, arch: str) -> Path:
    pre=(ROOT/"packaging/linux/pre-install-stop.sh").read_text(encoding="utf-8").replace("%","%%")
    post=(ROOT/"packaging/linux/post-install-configure.sh").read_text(encoding="utf-8").replace("%","%%")
    spec=builddir/"SPECS/partyops.spec"
    data=(
       "%global __os_install_post %{nil}\n"
       "Name: partyops\n"
       "Version: 1.4.6\n"
       "Release: %{partyops_release}\n"
       "Summary: PartyOps native desktop and local office service (BUILD_ONLY)\n"
       "License: GPL-3.0-or-later AND AGPL-3.0-only\n"
       "BuildArch: "+arch+"\n"
       "AutoReqProv: no\n"
       "Requires: glibc >= 2.28, bash, systemd, util-linux, coreutils, iproute, curl, xdg-utils, polkit\n"
       "Source0: partyops-payload.tar.gz\n\n"
       "%description\n"
       "Non-production build-only PartyOps 1.4.6-rc.1 with unchanged historical native WPS host.\n\n"
       "%prep\n%build\n%install\n"
       "mkdir -p %{buildroot}\n"
       "tar -xzf %{SOURCE0} -C %{buildroot}\n\n"
       "%pre\n"+pre+"\n"
       "%post\n"+post+"\n/opt/partyops/post-install-transaction.sh "+("amd64" if arch=="x86_64" else "arm64")+" rpm\n\n"
       "%preun\nif [ \"$1\" -eq 0 ]; then\n"
       "  export PARTYOPS_PACKAGE_REMOVE=1\n"+pre+"\nfi\n\n"
       "%postun\nsystemctl daemon-reload >/dev/null 2>&1 || true\n"
       "if [ \"$1\" -eq 0 ]; then\n"
       "  echo 'PartyOps data in /var/lib/partyops has NOT been removed.' >&2\n"
       "fi\n\n"
       "%files\n"
       "/opt/partyops\n/etc/partyops\n"
       "/usr/share/applications/partyops.desktop\n"
       "/usr/share/applications/partyops-file.desktop\n"
       "/usr/share/applications/partyops-client.desktop\n"
       "/usr/share/icons/hicolor/scalable/apps/partyops.svg\n"
       "/usr/share/polkit-1/actions/cn.partyops.update.policy\n"
       "/lib/systemd/system/partyops.service\n"
       "/lib/systemd/system/partyops-updater.service\n"
       "/lib/systemd/system/partyops-install-verify.service\n"
       "%if 0%{?with_rollback_cache}\n"
       "%dir %attr(0700,root,root) /var/cache/partyops\n"
       "%dir %attr(0700,root,root) /var/cache/partyops/update-transactions\n"
       "/var/cache/partyops/current.rpm\n"
       "/var/cache/partyops/current.rpm.sha256\n"
       "%endif\n"
    )
    spec.write_text(data,encoding="utf-8")
    return spec

def compose_rpm(donor: Path, app: Path, receipt: Path, arch: str, output: Path) -> None:
    pin=PINS[arch]
    if sys.platform!="linux" or platform.machine()!=pin["machine"]:
        raise RuntimeError("[RPM_BUILD_REQUIRES_NATIVE_CPU]")
    head=common.command("git","rev-parse","HEAD",cwd=ROOT,capture=True)
    if not re.fullmatch(r"[0-9a-f]{40}",head):
        raise RuntimeError("[RPM_SOURCE_COMMIT_INVALID]")
    common.command(
        sys.executable,str(ROOT/"scripts/verify-version-consistency.py"),
        "--root",str(ROOT),"--expected","1.4.6-rc.1",
    )
    common.require_bytes(donor,pin["donor"],"RPM_ORIGINAL_SOURCE_PKG_HASH_INVALID")
    identity=common.command("rpm","-qp","--queryformat","%{NAME}|%{ARCH}",str(donor),capture=True)
    if identity!="partyops|"+pin["rpm_arch"]:
        raise RuntimeError("[RPM_ORIGINAL_PACKAGE_IDENTITY_INVALID]")
    entries=check_source_application(app,receipt,head,arch)
    output.parent.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="partyops-rc1-openeuler-build-") as tmp:
        base=Path(tmp)
        staged=base/"staged"
        staged.mkdir()
        extract_original_rpm(donor,staged)
        runtime=staged/"opt/partyops"
        if not runtime.is_dir() or runtime.is_symlink():
            raise RuntimeError("[RPM_ORIGINAL_LAYOUT_INVALID]")
        check_original_native(runtime,pin)
        original_hashes={
            n:common.sha256(runtime/"formatter-host"/n)
            for n in ("partyops-document-formatter-host","source-host.json","runtime-evidence.json")
        }
        common.copy_new_application(runtime,app)
        stage_installer_scripts(staged,runtime)
        if original_hashes!={n:common.sha256(runtime/"formatter-host"/n) for n in original_hashes}:
            raise RuntimeError("[RPM_WPS_BYTES_CHANGED_WHILE_STAGING]")
        create_manifest(runtime,head,pin,arch)
        common.verify_non_office_host_abi(runtime)
        top=base/"rpmbuild"
        for part in ("BUILD","BUILDROOT","RPMS","SOURCES","SPECS","SRPMS"):
            (top/part).mkdir(parents=True,exist_ok=True)
        spec=build_rpm_spec(top,pin["rpm_arch"])
        def make_payload():
            common.command("tar","-czf",str(top/"SOURCES/partyops-payload.tar.gz"),"-C",str(staged),".")
        def rpmbuild(release: str, cache: int) -> Path:
            common.command(
                "rpmbuild","--target",pin["rpm_arch"],
                "--define","_topdir "+str(top),
                "--define","partyops_release "+release,
                "--define","with_rollback_cache "+str(cache),
                "-bb",str(spec),
            )
            path=top/"RPMS"/pin["rpm_arch"]/f"partyops-1.4.6-{release}.{pin['rpm_arch']}.rpm"
            if not path.is_file():
                raise RuntimeError("[RPM_MISSING_NEWLY_BUILT_OUTPUT]")
            return path
        make_payload()
        seed=rpmbuild("0.rc.1.0",0)
        rollback=staged/"var/cache/partyops"
        (rollback/"update-transactions").mkdir(parents=True,exist_ok=True)
        shutil.copy2(seed,rollback/"current.rpm")
        (rollback/"current.rpm.sha256").write_text(common.sha256(seed)+"\n",encoding="ascii")
        (rollback).chmod(0o700)
        (rollback/"update-transactions").chmod(0o700)
        make_payload()
        final=rpmbuild("0.rc.1.1",1)
        shutil.copyfile(final,output)
        info=common.command(
            "rpm","-qp","--queryformat","%{NAME}|%{VERSION}|%{RELEASE}|%{ARCH}",str(output),capture=True
        )
        if info!=f"partyops|1.4.6|0.rc.1.1|{pin['rpm_arch']}":
            raise RuntimeError("[RPM_VERSION_OR_ARCHITECTURE_MISMATCH]")
    digest=common.sha256(output)
    (output.parent/(output.name+".sha256")).write_text(
        f"{digest}  {output.name}\n",encoding="ascii",
    )
    report={
        "status":"PASS_NATIVE_RPM_BUILD_ONLY_NOT_OPENEULER_INSTALLED",
        "architecture":arch,
        "source_sha":head,
        "target_os":pin["target_os"],
        "old_official_donor_sha256":pin["donor"],
        "fresh_app_exe_sha256":entries,
        "original_wps_native_host_sha256":pin["host"],
        "original_wps_native_evidence_sha256":pin["evidence"],
        "unchanged_native_wps":True,
        "old_rc6_user_data_migrated":False,
        "synthetic_upgrade_executed":False,
        "native_openeuler_install_executed":False,
        "native_wps_tests_rerun":False,
        "actual_rpm_created":True,
        "signed":False,
        "release_ready":False,
        "output_sha256":digest,
        "output_bytes":output.stat().st_size,
    }
    (output.parent/"buildonly-receipt.json").write_text(
        json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8"
    )
    print("RC1_OPENEULER_RPM_SHA256="+digest)
    print("RC1_OPENEULER_RPM_BUILD_ONLY=PASS_NOT_NATIVE_INSTALLED")

def main() -> int:
    parser=argparse.ArgumentParser()
    parser.add_argument("--donor",required=True,type=Path)
    parser.add_argument("--app",required=True,type=Path)
    parser.add_argument("--app-receipt",required=True,type=Path)
    parser.add_argument("--architecture",required=True,choices=sorted(PINS))
    parser.add_argument("--output",required=True,type=Path)
    args=parser.parse_args()
    try:
        compose_rpm(
            args.donor.resolve(),args.app.resolve(),args.app_receipt.resolve(),
            args.architecture,args.output.resolve(),
        )
    except (OSError,ValueError,subprocess.CalledProcessError,RuntimeError) as exc:
        print("[RC1_OPENEULER_BUILD_ONLY_BLOCKED] "+str(exc),file=sys.stderr)
        return 2
    return 0

if __name__=="__main__":
    raise SystemExit(main())
