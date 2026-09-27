#!/usr/bin/env python3
"""核验从 Deepin crimson/main 封入的 Loong64 私有 OCR 候选闭包。"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
from pathlib import Path

SOURCE_ORIGIN = "Deepin crimson/main loong64"
PACKAGE_VERSIONS = {
    "tesseract-ocr": "5.5.0-1deepin1",
    "tesseract-ocr-chi-sim": "1:4.1.0-2",
    "tesseract-ocr-eng": "1:4.1.0-2",
}
FIXTURE_SHA256 = "fa6214516c340d208f134f0ef935bcef7c97da0da23ff224aba3d4a8db24b074"
GLIBC_HOST_LIBRARIES = {
    "libc.so.6", "libm.so.6", "libdl.so.2", "libpthread.so.0", "librt.so.1",
    "libresolv.so.2", "libnsl.so.1", "libutil.so.1",
}
SHA256 = re.compile(r"[0-9a-f]{64}\Z")


def _unique_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("OCR_SOURCE_DUPLICATE_KEY")
        result[key] = value
    return result


def _hash(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _loong_lp64d(path: Path) -> bool:
    with path.open("rb") as stream:
        header = stream.read(64)
    return (len(header) == 64 and header[:6] == b"\x7fELF\x02\x01"
            and int.from_bytes(header[18:20], "little") == 258
            and int.from_bytes(header[48:52], "little") & 7 == 3)


def _files_and_source(root: Path) -> dict[str, object]:
    source_path = root / "SOURCE.json"
    if source_path.is_symlink():
        raise ValueError("OCR_SOURCE_LINK_REJECTED")
    source = json.loads(source_path.read_text(encoding="utf-8"), object_pairs_hook=_unique_object)
    if (not isinstance(source, dict) or source.get("schema") != 1
            or source.get("origin") != SOURCE_ORIGIN or source.get("architecture") != "loong64"
            or source.get("version") != "5.5.0"):
        raise ValueError("OCR_SOURCE_IDENTITY_INVALID")
    packages = source.get("packages")
    if not isinstance(packages, dict) or not PACKAGE_VERSIONS.keys() <= packages.keys():
        raise ValueError("OCR_SOURCE_PACKAGES_INVALID")
    for package, detail in packages.items():
        if (not isinstance(package, str) or not isinstance(detail, dict)
                or detail.get("origin") != SOURCE_ORIGIN
                or detail.get("architecture") not in {"loong64", "all"}
                or not isinstance(detail.get("version"), str) or not detail["version"]
                or package in PACKAGE_VERSIONS and detail["version"] != PACKAGE_VERSIONS[package]
                or not isinstance(detail.get("deb_sha256"), str)
                or not SHA256.fullmatch(detail["deb_sha256"])):
            raise ValueError(f"OCR_SOURCE_PACKAGE_INVALID:{package}")
    entries = source.get("files")
    if not isinstance(entries, dict):
        raise TypeError("OCR_SOURCE_FILES_INVALID")
    actual = {path.relative_to(root).as_posix(): path for path in root.rglob("*") if path.is_file()}
    actual.pop("SOURCE.json", None)
    required = {"bin/tesseract", "tessdata/chi_sim.traineddata", "tessdata/eng.traineddata"}
    if not required <= set(actual) or not any(name.startswith("lib/") for name in actual):
        raise ValueError("OCR_PRIVATE_CLOSURE_INCOMPLETE")
    if not any(name.startswith("licenses/") for name in actual):
        raise ValueError("OCR_LICENSES_MISSING")
    if set(entries) != set(actual):
        raise ValueError("OCR_SOURCE_FILES_MISMATCH")
    for name, path in actual.items():
        if path.is_symlink() or path.stat().st_size <= 0:
            raise ValueError(f"OCR_FILE_INVALID:{name}")
        entry = entries[name]
        if not isinstance(entry, dict):
            raise TypeError(f"OCR_SOURCE_ENTRY_INVALID:{name}")
        package = entry.get("package")
        if (package not in packages or entry.get("package_version") != packages[package]["version"]
                or not isinstance(entry.get("source_path"), str)
                or not entry["source_path"].startswith("/")
                or type(entry.get("bytes")) is not int or entry["bytes"] != path.stat().st_size
                or not isinstance(entry.get("sha256"), str) or not SHA256.fullmatch(entry["sha256"])
                or entry["sha256"] != _hash(path)):
            raise ValueError(f"OCR_SOURCE_ENTRY_MISMATCH:{name}")
        if (name == "tessdata/chi_sim.traineddata" and package != "tesseract-ocr-chi-sim") or (
            name == "tessdata/eng.traineddata" and package != "tesseract-ocr-eng"
        ):
            raise ValueError(f"OCR_LANGUAGE_SOURCE_MISMATCH:{name}")
        if (name == "bin/tesseract" or name.startswith("lib/")) and not _loong_lp64d(path):
            raise ValueError(f"OCR_ELF_ARCH_ABI_INVALID:{name}")
    return source


def _private_dependencies(root: Path, environment: dict[str, str]) -> None:
    result = subprocess.run([str(root / "bin/tesseract"), "--version"],
                            capture_output=True, text=True, env=environment, timeout=30, check=False)
    if result.returncode != 0 or not re.search(r"\btesseract 5\.5\.0(?!\d)", result.stdout + result.stderr):
        raise ValueError("OCR_RUNTIME_VERSION_INVALID")
    dependencies = subprocess.run(["ldd", str(root / "bin/tesseract")],
                                  capture_output=True, text=True, env=environment, timeout=30, check=False)
    if dependencies.returncode != 0 or "not found" in dependencies.stdout + dependencies.stderr:
        raise ValueError("OCR_RUNTIME_DEPENDENCY_MISSING")
    private_lib = (root / "lib").resolve()
    private_tesseract = False
    for line in dependencies.stdout.splitlines():
        if "=>" not in line:
            continue
        name, target = (part.strip() for part in line.split("=>", 1))
        location = target.split("(", 1)[0].strip()
        if not Path(location).is_absolute():
            raise ValueError(f"OCR_RUNTIME_DEPENDENCY_UNRESOLVED:{name}")
        resolved = Path(location).resolve()
        if name in GLIBC_HOST_LIBRARIES:
            continue
        if private_lib not in resolved.parents:
            raise ValueError(f"OCR_RUNTIME_DEPENDENCY_NOT_PRIVATE:{name}")
        private_tesseract |= name.startswith("libtesseract.so")
    if not private_tesseract:
        raise ValueError("OCR_RUNTIME_PRIVATE_ENGINE_MISSING")


def validate(root: Path, fixture: Path, *, check_runtime: bool = True) -> dict[str, object]:
    root = root.resolve()
    if not root.is_dir() or fixture.is_symlink() or not fixture.is_file() or _hash(fixture) != FIXTURE_SHA256:
        raise ValueError("OCR_CHINESE_FIXTURE_INVALID")
    source = _files_and_source(root)
    if not check_runtime:
        return source
    environment = dict(os.environ)
    environment["LD_LIBRARY_PATH"] = str(root / "lib")
    environment["TESSDATA_PREFIX"] = str(root / "tessdata")
    _private_dependencies(root, environment)
    languages = subprocess.run([str(root / "bin/tesseract"), "--list-langs"],
                               capture_output=True, text=True, env=environment, timeout=30, check=False)
    if languages.returncode != 0 or not {"chi_sim", "eng"} <= set(languages.stdout.splitlines()):
        raise ValueError("OCR_LANGUAGES_UNAVAILABLE")
    ocr = subprocess.run([str(root / "bin/tesseract"), str(fixture), "stdout", "-l", "chi_sim+eng", "--psm", "6"],
                         capture_output=True, text=True, env=environment, timeout=90, check=False)
    compact = "".join(ocr.stdout.split())
    if ocr.returncode != 0 or not all(text in compact for text in ("同志们", "格式文字")):
        raise ValueError("OCR_CHINESE_RECOGNITION_FAILED")
    return source


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, required=True)
    args = parser.parse_args()
    try:
        source = validate(args.root, args.fixture)
    except (OSError, TypeError, ValueError, subprocess.TimeoutExpired) as exc:
        parser.exit(2, f"[LOONG_OCR_INVALID] {exc}\n")
    print(f"Loong64 Deepin OCR 私有闭包、中文识别通过：{source['version']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
