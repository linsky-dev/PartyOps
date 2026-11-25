"""Remove only documented upstream LibreOffice build-machine RPATHs from a staged bundle."""
import json
from pathlib import Path
import subprocess
import sys

# Scope to upstream build-tree directories observed in the pinned DMG.
PREFIX = "/Users/cloph/build/workdir/UnpackedTarball/"
MAGICS = {bytes.fromhex(x) for x in ("feedface","cefaedfe","feedfacf","cffaedfe","cafebabe","bebafeca","cafebabf","bfbafeca")}

def run(*args):
    return subprocess.check_output(args, text=True, stderr=subprocess.STDOUT)

def commands(p):
    result = []
    cmd = ""
    for raw in run("/usr/bin/otool", "-l", str(p)).splitlines():
        line = raw.strip()
        if line.startswith("cmd "):
            cmd = line.split()[1]
        elif cmd == "LC_RPATH" and line.startswith("path "):
            result.append(line[5:].split(" (offset ", 1)[0])
            cmd = ""
    return result

def main():
    root, receipt = Path(sys.argv[1]).resolve(), Path(sys.argv[2])
    assert root.name == "LibreOffice.app" and root.is_dir()
    rows = []
    for p in sorted(root.rglob("*")):
        if p.is_symlink() or not p.is_file():
            continue
        with p.open("rb") as f:
            if f.read(4) not in MAGICS:
                continue
        # Java class files and Mach-O universal binaries both start with
        # CAFEBABE. Identify the actual object format before invoking otool.
        kind = run("/usr/bin/file", "-b", str(p)).strip()
        if not kind.startswith("Mach-O"):
            continue
        original = commands(p)
        stale = sorted({r for r in original if r.startswith(PREFIX)})
        if not stale:
            continue
        # The signed original is retained unchanged; only this staged copy changes.
        before = run("/usr/bin/otool", "-L", str(p))
        for path in stale:
            run("/usr/bin/install_name_tool", "-delete_rpath", path, str(p))
        after = commands(p)
        assert not set(stale).intersection(after), p
        assert sorted(set(original) - set(stale)) == sorted(set(after)), p
        assert run("/usr/bin/otool", "-L", str(p)) == before, p
        rows.append({"path": str(p.relative_to(root)), "removed_rpaths": stale})
    receipt.write_text(json.dumps({"status":"STAGED_OFFICE_RPATH_SANITIZED","changes":rows},indent=2)+"\n")
    print("MACOS_STAGED_OFFICE_RPATH_REMOVED", sum(len(r["removed_rpaths"]) for r in rows))

if __name__ == "__main__":
    main()
