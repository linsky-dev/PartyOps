"""Compare newly frozen Debian sid ABI with original Deepin package and hard 2.38 ceiling."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tarfile


def require(ok, msg):
    if not ok:
        raise RuntimeError(msg)


def one(root, pattern):
    rows = list(root.rglob(pattern))
    require(len(rows) == 1, "ambiguous or missing " + pattern)
    return rows[0]


def main():
    fresh, old, out = (Path(x).resolve() for x in sys.argv[1:])
    out.mkdir(parents=True)
    r = json.loads(one(fresh, "freeze-receipt.json").read_text())
    require(r["source_commit"] == "96d1c9bd553b70b0bef0c934b6196d8fee0a265f" and r["new_application_frozen"]
            and r["schema_0028"] and r["release_ready"] is False and not r["target_abi_approved"], "freeze receipt")
    archive = one(fresh, "PartyOps-loong64-DEBIAN-SID-APP-ONLY.tar.gz")
    recorded = one(fresh, "frozen-app-sha256.txt").read_text().split()[0]
    with archive.open("rb") as f:
        require(hashlib.file_digest(f, "sha256").hexdigest() == recorded, "freeze archive hash")
    dest = out.parent / "new-loong-abi"
    dest.mkdir()
    with tarfile.open(archive) as tar:
        tar.extractall(dest, filter="data")
    new = dest / "PartyOps"
    spec = importlib.util.spec_from_file_location("abi", "scripts/verify-linux-elf-glibc.py")
    abi = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(abi)

    def inventory(root):
        rows = []
        for path in sorted(root.rglob("*")):
            if not path.is_file() or path.is_symlink():
                continue
            with path.open("rb") as f:
                header = f.read(64)
                if header[:4] != b"\x7fELF":
                    continue
            require(header[:6] == b"\x7fELF\x02\x01" and struct.unpack_from("<H", header, 18)[0] == 258,
                    "non-native ELF " + str(path))
            flags = struct.unpack_from("<I", header, 48)[0]
            info = subprocess.check_output(["readelf", "-W", "--version-info", str(path)], text=True,
                                           env=dict(os.environ, LC_ALL="C"))
            needed = abi.required_glibc_versions(info)
            highest = max(needed, default=(0, 0))
            rows.append({"file": path.relative_to(root).as_posix(), "glibc": list(highest),
                         "lp64d": flags & 7 == 3, "over_target": highest > (2, 38)})
        require(bool(rows), "empty ELF inventory")
        return rows

    before, after = inventory(old), inventory(new)
    failures = [row for row in after if row["over_target"] or not row["lp64d"]]
    report = {
        "source_commit": r["source_commit"], "old_deb_sha256": "bec5993a12a9a66800fa039045f45329ecc486934cd1d1106c10d5e2ecf78017",
        "target_max_glibc": [2,38], "old_max_glibc": max(x["glibc"] for x in before),
        "new_max_glibc": max(x["glibc"] for x in after), "old_elf_count": len(before),
        "new_elf_count": len(after), "new_blockers": failures,
        "status": "TARGET_ABI_STATIC_BLOCKED" if failures else "TARGET_ABI_STATIC_PASS_ONLY",
        "target_os_tested": False, "installer_created": False, "release_ready": False,
    }
    for name, value in (("old-elf.json", before), ("new-elf.json", after), ("abi-receipt.json", report)):
        (out / name).write_text(json.dumps(value, indent=2), encoding="utf-8")
    print(json.dumps({k:v for k,v in report.items() if k!="new_blockers"}))
    # Original gate remains authoritative; never treat an audit as target acceptance.
    abi.verify(new, (2,38))
    require(not failures, "target ABI findings")


if __name__ == "__main__":
    main()
