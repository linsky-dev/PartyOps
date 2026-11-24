#!/usr/bin/env python3
"""Assemble an UNSIGNED UOS V20 AMD64 RC1 installer from new application bytes.

The older RC6 WPS/Office/OCR/LLM native assets are copied unchanged, and
cryptographically bound to their official historical package. This is not
a new WPS acceptance and not a production release. No host install is done.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import re
import shutil
import stat
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LEGACY_DEB_SHA = "4557129b9f081c1b525d6dd6c6d444f7e0d35d63021b4b1c20c8daeffd4c30ae"
LEGACY_HOST_SHA = "26fba25f527dc66c4ca5b6197e89c873d0397765d6e88e7fb739710374e3da4f"
LEGACY_SOURCE_SHA = "b4fba6efaa24034f02aa242e4b41433e542bf9f6f681f8bdeae2e7c70310abf7"
LEGACY_EVIDENCE_SHA = "77dcfc30c2244a18870c838c479a3f1ef32c0bb754c04e7e3604b087330f348f"
LEGACY_SNAPSHOT_SHA = "15c21b886f6a958fb61a3b106266b446a2b959b0085510015eeb790efaa770d3"
SHA_PATTERN = re.compile(r"^[0-9a-f]{64}$")
ENTRYPOINTS = ("partyops", "partyops-client", "partyops-wizard", "partyops-updater")


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def command(*args: str, cwd: Path | None = None, capture: bool = False) -> str:
    completed = subprocess.run(
        list(args), cwd=cwd, check=True,
        capture_output=capture, text=True,
    )
    return completed.stdout.strip() if capture else ""


def require_bytes(path: Path, expected: str, reason: str) -> None:
    if path.is_symlink() or not path.is_file() or sha256(path) != expected:
        raise RuntimeError(f"[{reason}] {path}")


def copy_from_repo(relative: str, destination: Path) -> None:
    source = ROOT / relative
    if source.is_symlink() or not source.is_file():
        raise RuntimeError(f"[UOS_PREVIEW_SOURCE_FILE_MISSING] {relative}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)


def validate_original_wps(runtime: Path) -> None:
    hostdir = runtime / "formatter-host"
    host = hostdir / "partyops-document-formatter-host"
    source = hostdir / "source-host.json"
    evidence = hostdir / "runtime-evidence.json"
    require_bytes(host, LEGACY_HOST_SHA, "UOS_PREVIEW_OFFICIAL_WPS_HOST_CHANGED")
    require_bytes(source, LEGACY_SOURCE_SHA, "UOS_PREVIEW_OFFICIAL_WPS_SOURCE_CHANGED")
    require_bytes(evidence, LEGACY_EVIDENCE_SHA, "UOS_PREVIEW_OFFICIAL_WPS_EVIDENCE_CHANGED")
    record = json.loads(source.read_text(encoding="utf-8"))
    historical = json.loads(evidence.read_text(encoding="utf-8"))
    expected_features = ["format", "replace", "redheader", "rename", "convert", "pdf-to-word"]
    if (
        record.get("source_snapshot_sha256") != LEGACY_SNAPSHOT_SHA
        or record.get("host_sha256") != LEGACY_HOST_SHA
        or record.get("platform") != "linux"
        or record.get("architecture") != "amd64"
        or record.get("features") != expected_features
        or historical.get("host_sha256") != LEGACY_HOST_SHA
        or historical.get("status") != "passed"
        or historical.get("features") != expected_features
        or historical.get("rendered_pages") != 3
        or historical.get("feature_cases") != 10
    ):
        raise RuntimeError("[UOS_PREVIEW_HISTORICAL_WPS_RECORD_INVALID]")
    for required in (
        hostdir / "word-vtable-map.json",
        hostdir / "LICENSE-WPS-SDK.txt",
        hostdir / "LICENSE-MONO-RUNTIME.txt",
        runtime / "office-runtime/program/soffice",
        runtime / "ocr/bin/tesseract",
        runtime / "llama-server",
    ):
        if required.is_symlink() or not required.is_file():
            raise RuntimeError(f"[UOS_PREVIEW_HISTORICAL_RUNTIME_INCOMPLETE] {required}")
    if record.get("word_vtable_map_sha256") != sha256(hostdir / "word-vtable-map.json"):
        raise RuntimeError("[UOS_PREVIEW_WPS_VTABLE_HASH_INVALID]")


def validate_original_private_office(runtime: Path) -> None:
    """The old Office runs with an independently packaged glibc 2.34 loader.

    Preserve and verify the source + every private library; these files have
    a separate ABI namespace and must never be passed to host-glibc 2.28
    checks or substituted with host libX11/ICE.
    """
    office = runtime / "office-runtime"
    loader = office / "private-runtime/ld-linux-x86-64.so.2"
    source_file = office / "SOURCE.json"
    manifest = office / "PRIVATE_RUNTIME_LIBS.txt"
    package_list = office / "PRIVATE_RUNTIME_PACKAGES.txt"
    wrapper = office / "program/soffice"
    for path in (loader, source_file, manifest, package_list, wrapper):
        if path.is_symlink() or not path.is_file():
            raise RuntimeError(f"[UOS_PREVIEW_OFFICE_PRIVATE_INPUT_MISSING] {path}")
    source = json.loads(source_file.read_text(encoding="utf-8"))
    expected = {
        "version": "25.8.7.2",
        "architecture": "amd64",
        "origin": "The Document Foundation official archive",
        "archive_sha256": "a893a4f37a8b3fe110da92bb0135f488f8d695cd40cb7ce59c65bb525849bb67",
        "private_runtime_glibc": "2.34",
        "minimum_host_glibc": "2.17",
        "private_runtime_packages_sha256": "dd5ddb478f8863533b48baf2273411ab7c110f4609a74590223f7d9716dcb6cb",
    }
    if any(source.get(key) != value for key, value in expected.items()):
        raise RuntimeError("[UOS_PREVIEW_OFFICE_SOURCE_MISMATCH]")
    if sha256(package_list) != expected["private_runtime_packages_sha256"]:
        raise RuntimeError("[UOS_PREVIEW_OFFICE_PACKAGE_LIST_HASH_MISMATCH]")
    seen: set[str] = set()
    for line in manifest.read_text(encoding="utf-8").splitlines():
        fields = line.split("\t")
        if len(fields) != 3:
            raise RuntimeError("[UOS_PREVIEW_OFFICE_PRIVATE_MANIFEST_FORMAT_INVALID]")
        name, _origin, digest = fields
        if (
            name in seen
            or not name
            or name != Path(name).name
            or not SHA_PATTERN.fullmatch(digest)
        ):
            raise RuntimeError("[UOS_PREVIEW_OFFICE_PRIVATE_MANIFEST_PATH_INVALID]")
        seen.add(name)
        require_bytes(office / "private-runtime" / name, digest, "UOS_PREVIEW_OFFICE_PRIVATE_LIBRARY_CHANGED")
    if "ld-linux-x86-64.so.2" not in seen or "libICE.so.6" not in seen:
        raise RuntimeError("[UOS_PREVIEW_OFFICE_PRIVATE_ABI_INCOMPLETE]")
    wrapper_text = wrapper.read_text(encoding="utf-8")
    for phrase in ("../private-runtime", "--library-path", 'if [[ "$status" -eq 81 ]]'):
        if phrase not in wrapper_text:
            raise RuntimeError("[UOS_PREVIEW_OFFICE_LOADER_WRAPPER_MISMATCH]")
    for path in office.rglob("*"):
        if path.is_symlink():
            target = path.resolve()
            if not target.is_relative_to(office):
                raise RuntimeError("[UOS_PREVIEW_OFFICE_LINK_OUTSIDE_PAYLOAD]")
    print("UOS_PRIVATE_OFFICE_234_PROVENANCE=PASS_SOURCE_AND_ALL_LIB_HASHES")


def verify_non_office_host_abi(runtime: Path) -> None:
    """Only shared-namespace ELF files must stay within host glibc 2.28.

    Rather than weakening the generic ABI verifier, create a temporary
    hardlink view of non-Office ELF binaries. The separately verified Office
    private runtime is proven with an actual glibc 2.28 --version smoke in CI.
    """
    with tempfile.TemporaryDirectory(prefix="uos-v20-shared-abi-") as work:
        view = Path(work)
        count = 0
        for file in runtime.rglob("*"):
            if file.is_symlink() or not file.is_file():
                continue
            relative = file.relative_to(runtime)
            if relative.parts[0] == "office-runtime":
                continue
            with file.open("rb") as handle:
                if handle.read(4) != b"\x7fELF":
                    continue
            target = view / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            os.link(file, target)
            count += 1
        if count < 4:
            raise RuntimeError("[UOS_PREVIEW_SHARED_ABI_NOT_INSPECTED]")
        command(
            sys.executable, str(ROOT / "scripts/verify-linux-elf-glibc.py"),
            "--root", str(view), "--max-glibc", "2.28",
        )
        print(f"UOS_HOST_ELF_ABI_228_PASS count={count}; Office uses separate pinned glibc 2.34 loader")


def ensure_fresh_application(app: Path, receipt: Path, source_sha: str) -> dict[str, str]:
    if not app.is_dir() or not receipt.is_file():
        raise RuntimeError("[UOS_PREVIEW_NEW_APP_MISSING]")
    metadata = json.loads(receipt.read_text(encoding="utf-8"))
    if (
        metadata.get("status") != "PASS_FRESH_APP_ONLY"
        or metadata.get("source_sha") != source_sha
        or metadata.get("architecture") != "amd64"
        or metadata.get("release_ready") is not False
    ):
        raise RuntimeError("[UOS_PREVIEW_NEW_APP_PROVENANCE_INVALID]")
    expected = metadata.get("source_matched_executable_sha256")
    if not isinstance(expected, dict) or set(expected) != set(ENTRYPOINTS):
        raise RuntimeError("[UOS_PREVIEW_APP_ENTRYPOINT_SET_INVALID]")
    for name, digest in expected.items():
        if not isinstance(digest, str) or not SHA_PATTERN.fullmatch(digest):
            raise RuntimeError("[UOS_PREVIEW_APP_SHA_INVALID]")
        require_bytes(app / name, digest, "UOS_PREVIEW_APP_SHA_CHANGED")
    for path in ("_internal/frontend/index.html", "_internal/alembic.ini", "_internal/alembic/env.py"):
        if not (app / path).is_file():
            raise RuntimeError(f"[UOS_PREVIEW_APP_RESOURCE_MISSING] {path}")
    return expected


def copy_new_application(runtime: Path, app: Path) -> None:
    old_internal = runtime / "_internal"
    if old_internal.is_symlink():
        raise RuntimeError("[UOS_PREVIEW_INTERNAL_SYMLINK_REJECTED]")
    if old_internal.is_dir():
        shutil.rmtree(old_internal)
    for binary in ENTRYPOINTS:
        path = runtime / binary
        if path.is_symlink() or (path.exists() and not path.is_file()):
            raise RuntimeError("[UOS_PREVIEW_OLD_ENTRYPOINT_NOT_REGULAR]")
        if path.exists():
            path.unlink()
    allowed_entries = set(ENTRYPOINTS) | {"_internal"}
    extra_entries = sorted(p.name for p in app.iterdir() if p.name not in allowed_entries)
    if extra_entries:
        raise RuntimeError(f"[UOS_PREVIEW_NEW_APP_UNEXPECTED_ROOT_FILES] {extra_entries}")
    shutil.copytree(app, runtime, dirs_exist_ok=True, symlinks=True)
    for path in app.rglob("*"):
        if path.is_symlink() or not path.is_file():
            continue
        relative = path.relative_to(app)
        target = runtime / relative
        target.chmod(0o755 if relative.as_posix() in ENTRYPOINTS else 0o644)


def stage_current_installer_files(root: Path, runtime: Path) -> None:
    helpers = (
        "start.sh", "stop.sh", "desktop-launcher.sh", "open-local-file.sh",
        "install-desktop-shortcut.sh", "install-internal-ca.sh",
    )
    for name in helpers:
        copy_from_repo(f"packaging/uos/{name}", runtime / name)
        (runtime / name).chmod(0o755)
    for name in ("client-config.example.json", "partyops.svg"):
        copy_from_repo(f"packaging/uos/{name}", runtime / name)
    for name in ("README.md", "CHANGELOG.md", "LICENSE", "THIRD_PARTY_NOTICES.md"):
        copy_from_repo(name, runtime / name)
    (runtime / "VERSION").write_text("1.4.6-rc.1\n", encoding="utf-8")
    (runtime / "docs").mkdir(parents=True, exist_ok=True)
    copy_from_repo("docs/release-notes-v1.4.6-rc.1.md", runtime / "docs/release-notes-v1.4.6-rc.1.md")
    for name in (
        "post-install-selftest.sh", "post-install-services.sh",
        "post-install-verify.sh", "post-install-transaction.sh",
    ):
        copy_from_repo(f"packaging/linux/{name}", runtime / name)
        (runtime / name).chmod(0o755)
    mappings = {
        "packaging/uos/partyops.desktop": "usr/share/applications/partyops.desktop",
        "packaging/uos/partyops-client.desktop": "usr/share/applications/partyops-client.desktop",
        "packaging/uos/partyops-file.desktop": "usr/share/applications/partyops-file.desktop",
        "packaging/uos/partyops.svg": "usr/share/icons/hicolor/scalable/apps/partyops.svg",
        "packaging/uos/partyops.service": "lib/systemd/system/partyops.service",
        "packaging/uos/partyops-updater.service": "lib/systemd/system/partyops-updater.service",
        "packaging/linux/partyops-install-verify.service": "lib/systemd/system/partyops-install-verify.service",
        "packaging/uos/cn.partyops.update.policy": "usr/share/polkit-1/actions/cn.partyops.update.policy",
    }
    for old, dest in mappings.items():
        copy_from_repo(old, root / dest)
        (root / dest).chmod(0o644)


def write_package_scripts(staged: Path) -> None:
    debian = staged / "DEBIAN"
    debian.mkdir(exist_ok=True)
    control = (
        "Package: partyops\n"
        "Version: 1.4.6~rc.1\n"
        "Section: office\n"
        "Priority: optional\n"
        "Architecture: amd64\n"
        "Maintainer: PartyOps Local\n"
        "Depends: libc6 (>= 2.28), bash, systemd, util-linux, coreutils, iproute2, curl, xdg-utils, policykit-1\n"
        "Description: 党建智办 PartyOps UOS V20 AMD64 BUILD_ONLY preview\n"
        " Source-matched RC1 application and byte-identical historical RC6 WPS native runtime; not production verified.\n"
    )
    (debian / "control").write_text(control, encoding="utf-8")
    copy_from_repo("packaging/linux/pre-install-stop.sh", debian / "preinst")
    configure = (ROOT / "packaging/linux/post-install-configure.sh").read_bytes()
    (debian / "postinst").write_bytes(
        configure + b"\n/opt/partyops/post-install-transaction.sh amd64 deb\n"
    )
    prerm = (
        b"#!/bin/sh\nset -e\n"
        b"case \"${1:-}\" in remove|deconfigure) ;; *) exit 0 ;; esac\n"
        b"export PARTYOPS_PACKAGE_REMOVE=1\n"
    ) + (ROOT / "packaging/linux/pre-install-stop.sh").read_bytes()
    (debian / "prerm").write_bytes(prerm)
    (debian / "postrm").write_text(
        "#!/bin/sh\nset -e\n"
        "systemctl daemon-reload >/dev/null 2>&1 || true\n"
        "echo 'PartyOps 业务数据保留在 /var/lib/partyops，卸载不会自动删除。' >&2\n",
        encoding="utf-8",
    )
    for name in ("preinst", "postinst", "prerm", "postrm"):
        p = debian / name
        p.chmod(0o755)
        command("sh", "-n", str(p))


def build_manifest(runtime: Path, source_sha: str) -> None:
    # Symlink targets may not escape the staged package root. Otherwise a
    # manifest could accidentally hash or depend on builder machine files.
    for path in runtime.rglob("*"):
        if path.is_symlink() and not path.resolve().is_relative_to(runtime):
            raise RuntimeError(f"[UOS_PREVIEW_SYMLINK_OUTSIDE_PAYLOAD] {path}")
    provenance = {
        "schema_version": 1,
        "status": "BUILD_ONLY_UNVERIFIED",
        "version": "1.4.6-rc.1",
        "architecture": "amd64",
        "target_os": "UOS Desktop V20 1070",
        "source_sha": source_sha,
        "historical_donor_deb_sha256": LEGACY_DEB_SHA,
        "historical_wps_host_sha256": LEGACY_HOST_SHA,
        "historical_wps_source_record_sha256": LEGACY_SOURCE_SHA,
        "historical_wps_evidence_sha256": LEGACY_EVIDENCE_SHA,
        "historical_wps_snapshot_sha256": LEGACY_SNAPSHOT_SHA,
        "historical_wps_host_unchanged": True,
        "wps_native_tests_rerun": False,
        "release_ready": False,
        "real_uos_install_tested": False,
        "official_pdf_security_review_complete": False,
    }
    marker = runtime / "UOS-RC1-BUILD-ONLY.json"
    marker.write_text(json.dumps(provenance, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    (runtime / "release-manifest.json").unlink(missing_ok=True)
    contents: list[str] = []
    for path in sorted(runtime.rglob("*")):
        if path.is_symlink() or not path.is_file() or path.name in {
            "release-files.sha256", "release-manifest.json"
        }:
            continue
        contents.append(f"{sha256(path)}  ./{path.relative_to(runtime).as_posix()}")
    (runtime / "release-files.sha256").write_text("\n".join(contents) + "\n", encoding="utf-8")
    command(
        sys.executable, str(ROOT / "scripts/generate-release-manifest.py"),
        "--root", str(runtime),
        "--output", str(runtime / "release-manifest.json"),
        "--version", "1.4.6-rc.1", "--tag", "v1.4.6-rc.1",
        "--commit", source_sha, "--platform", "linux-deb",
        "--architecture", "amd64", "--runtime-profile", "uos-v20",
    )
    manifest = json.loads((runtime / "release-manifest.json").read_text(encoding="utf-8"))
    index = {record["path"]: record["sha256"] for record in manifest["files"]}
    for filename, digest in {
        "formatter-host/partyops-document-formatter-host": LEGACY_HOST_SHA,
        "formatter-host/source-host.json": LEGACY_SOURCE_SHA,
        "formatter-host/runtime-evidence.json": LEGACY_EVIDENCE_SHA,
    }.items():
        if index.get(filename) != digest:
            raise RuntimeError(f"[UOS_PREVIEW_RELEASE_MANIFEST_MISMATCH] {filename}")
    command("sha256sum", "--check", "release-files.sha256", cwd=runtime)


def build(donor: Path, app: Path, receipt: Path, output: Path) -> Path:
    if sys.platform != "linux" or platform.machine() not in {"x86_64", "amd64"}:
        raise RuntimeError("[UOS_PREVIEW_NATIVE_AMD64_BUILDER_REQUIRED]")
    head = command("git", "rev-parse", "HEAD", cwd=ROOT, capture=True)
    if not re.fullmatch(r"[0-9a-f]{40}", head):
        raise RuntimeError("[UOS_PREVIEW_COMMIT_INVALID]")
    command(sys.executable, str(ROOT / "scripts/verify-version-consistency.py"),
            "--root", str(ROOT), "--expected", "1.4.6-rc.1")
    require_bytes(donor, LEGACY_DEB_SHA, "UOS_PREVIEW_DONOR_DEB_HASH_INVALID")
    metadata = (
        command("dpkg-deb", "--field", str(donor), "Package", capture=True),
        command("dpkg-deb", "--field", str(donor), "Architecture", capture=True),
    )
    if metadata != ("partyops", "amd64"):
        raise RuntimeError("[UOS_PREVIEW_DONOR_DEB_IDENTITY_INVALID]")
    new_hashes = ensure_fresh_application(app, receipt, head)
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="partyops-rc1-uos-preview-") as tmp:
        staging = Path(tmp) / "staging"
        command("dpkg-deb", "--raw-extract", str(donor), str(staging))
        runtime = staging / "opt/partyops"
        if runtime.is_symlink() or not runtime.is_dir():
            raise RuntimeError("[UOS_PREVIEW_DONOR_RUNTIME_LAYOUT_INVALID]")
        if (staging / "var/lib/partyops").exists():
            raise RuntimeError("[UOS_PREVIEW_DONOR_CONTAINS_BUSINESS_DATA]")
        validate_original_wps(runtime)
        validate_original_private_office(runtime)
        original = {
            name: sha256(runtime / "formatter-host" / name)
            for name in (
                "partyops-document-formatter-host", "source-host.json",
                "runtime-evidence.json",
            )
        }
        copy_new_application(runtime, app)
        stage_current_installer_files(staging, runtime)
        write_package_scripts(staging)
        if {
            name: sha256(runtime / "formatter-host" / name)
            for name in original
        } != original:
            raise RuntimeError("[UOS_PREVIEW_NATIVE_WPS_BYTES_CHANGED]")
        build_manifest(runtime, head)
        verify_non_office_host_abi(runtime)
        command("dpkg-deb", "--root-owner-group", "--build", str(staging), str(output))
        if command("dpkg-deb", "--field", str(output), "Version", capture=True) != "1.4.6~rc.1":
            raise RuntimeError("[UOS_PREVIEW_INSTALLER_VERSION_MISMATCH]")
        if command("dpkg-deb", "--field", str(output), "Architecture", capture=True) != "amd64":
            raise RuntimeError("[UOS_PREVIEW_INSTALLER_ARCH_MISMATCH]")
    value = sha256(output)
    (output.parent / (output.name + ".sha256")).write_text(
        f"{value}  {output.name}\n", encoding="ascii"
    )
    report = {
        "status": "PASS_HOSTED_BUILD_ONLY_DEB_NOT_NATIVE_UOS_INSTALLED",
        "source_sha": head,
        "version": "1.4.6-rc.1",
        "format": "deb",
        "target_os": "UOS Desktop V20 1070",
        "architecture": "amd64",
        "package_filename": output.name,
        "package_sha256": value,
        "package_size_bytes": output.stat().st_size,
        "new_application_entrypoint_sha256": new_hashes,
        "historical_wps_host_sha256": LEGACY_HOST_SHA,
        "historical_wps_source_sha256": LEGACY_SOURCE_SHA,
        "historical_wps_evidence_sha256": LEGACY_EVIDENCE_SHA,
        "historical_donor_deb_sha256": LEGACY_DEB_SHA,
        "legacy_wps_host_unchanged": True,
        "native_wps_tests_rerun": False,
        "native_uos_install_executed": False,
        "old_rc6_uos_to_rc1_user_data_upgrade_tested": False,
        "new_wps_source_snapshot_accepted": False,
        "preview_legacy_wps_pin_bounded_to_amd64": True,
        "signed": False,
        "release_ready": False,
    }
    (output.parent / "buildonly-receipt.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(f"UOS_ACTUAL_BUILD_ONLY_DEB_SHA256={value}")
    print("UOS_WPS_CARRIED_UNCHANGED=TRUE_ONLY_HISTORIC_ACCEPTANCE")
    print("UOS_REAL_MACHINE_INSTALL=NOT_RUN")
    return output


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--donor", type=Path, required=True)
    parser.add_argument("--app", type=Path, required=True)
    parser.add_argument("--app-receipt", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        build(args.donor.resolve(), args.app.resolve(), args.app_receipt.resolve(), args.output.resolve())
    except (OSError, RuntimeError, subprocess.CalledProcessError, ValueError) as exc:
        print(f"[UOS_PREVIEW_BUILD_ONLY_BLOCKED] {exc}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
