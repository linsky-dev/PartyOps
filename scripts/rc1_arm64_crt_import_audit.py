"""Fail closed on architecture-specific CRT imports before native ARM64 QA packaging.

MSVC vcruntime140_1 is x64-specific in upstream Chromium's redistribution
implementation (vs_toolchain.py at 3841ebe40d1c686afd3bdf8fb2a96eb7242a9d90).
Do not remove it from independent Office/formatter trees. Check actual regular
and delay-loaded imports, and preserve evidence; native startup is a separate gate.
"""
import hashlib
import json
from pathlib import Path
import sys

import pefile

TARGET = "vcruntime140_1.dll"
EXPECTED_X64_HASH = "47b622f5d31d62c5609d2b439ceab507ab3dda8f212969f8a5c968a04f3c3ec5"
NATIVE = 0xAA64


def digest(path):
    with path.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


def audit(bundle, report):
    bundle = bundle.resolve(strict=True)
    internal = bundle / "_internal"
    if not internal.is_dir():
        raise RuntimeError("missing frozen runtime")
    roots = sorted(p for p in bundle.iterdir() if p.is_file())
    paths = roots + sorted(p for p in internal.rglob("*") if p.is_file())
    rows, problems, candidates = [], [], []
    for p in paths:
        if p.suffix.lower() not in {".exe", ".dll", ".pyd", ".com"}:
            continue
        rel = p.relative_to(bundle).as_posix()
        if p.is_symlink() or getattr(p.lstat(), "st_file_attributes", 0) & 0x400:
            problems.append("linked PE " + rel)
            continue
        try:
            pe = pefile.PE(str(p), fast_load=True)
            try:
                pe.parse_data_directories(directories=[
                    pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"],
                    pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT"],
                ])
                imports = sorted({
                    entry.dll.decode("ascii").lower()
                    for attr in ("DIRECTORY_ENTRY_IMPORT", "DIRECTORY_ENTRY_DELAY_IMPORT")
                    for entry in getattr(pe, attr, [])
                })
                machine = pe.FILE_HEADER.Machine
            finally:
                pe.close()
        except (OSError, UnicodeError, pefile.PEFormatError) as exc:
            problems.append("PE audit failed " + rel + ": " + str(exc))
            continue
        row = {"path": rel, "machine": hex(machine), "imports": imports}
        rows.append(row)
        if p.name.lower() == TARGET:
            if p.parent not in {bundle, internal} or machine != 0x8664 or digest(p) != EXPECTED_X64_HASH:
                problems.append("unexpected CRT copy " + rel)
            else:
                candidates.append(p)
                row["sha256"] = digest(p)
        elif TARGET in imports:
            problems.append("CRT still imported by " + rel + " (" + hex(machine) + ")")
    for name in ("vcruntime140.dll", "msvcp140.dll"):
        if not any(r["path"] == "_internal/" + name and r["machine"] == hex(NATIVE) for r in rows):
            problems.append("missing native " + name)
    if len(rows) < 10:
        problems.append("incomplete PE inventory")
    evidence = {"scope": "native frozen shared runtime only; independent Office/formatter untouched",
                "source_commit": "1a554181429a073e7349d73f62e1ae766d4fb479",
                "pe_count": len(rows), "files": rows, "problems": problems,
                "removed": [], "native_startup_verified": False, "release_ready": False}
    report.write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    if problems:
        raise RuntimeError("; ".join(problems))
    for p in candidates:
        evidence["removed"].append({"path": p.relative_to(bundle).as_posix(), "sha256": digest(p)})
        p.unlink()
    report.write_text(json.dumps(evidence, indent=2), encoding="utf-8")


if __name__ == "__main__":
    audit(Path(sys.argv[1]), Path(sys.argv[2]))
