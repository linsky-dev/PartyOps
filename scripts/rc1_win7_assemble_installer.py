"""Assemble Win7 QA candidate from new app + byte-verified legacy non-business inputs."""
import hashlib
import json
from pathlib import Path
import shutil
import sys


def sha(p):
    with p.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


def require(ok, message):
    if not ok:
        raise RuntimeError(message)


def main():
    donor, bundle, repo = (Path(x).resolve() for x in sys.argv[1:4])
    arch = sys.argv[4]
    manifests = list(donor.rglob("release-manifest.json"))
    require(len(manifests) == 1, "ambiguous original manifest")
    source = manifests[0].parent
    manifest = json.loads(manifests[0].read_text(encoding="utf-8-sig"))
    require(manifest["version"] == "1.4.5-rc.6" and manifest["architecture"] == arch
            and manifest["platform"] == "windows7", "wrong donor")
    rows = {}
    for row in manifest["files"]:
        key = row["path"].replace("\\", "/").casefold()
        require(key not in rows, "duplicate donor manifest entry")
        rows[key] = row
    for name in ("office-runtime", "ocr"):
        folder = source / name
        require(folder.is_dir() and not (bundle / name).exists(), "missing or duplicate " + name)
        for p in folder.rglob("*"):
            require(not p.is_symlink() and not getattr(p.lstat(), "st_file_attributes", 0) & 0x400, "linked donor component")
            if p.is_file():
                rel = p.relative_to(source).as_posix()
                row = rows.get(rel.casefold())
                require(row and row["size"] == p.stat().st_size and sha(p) == row["sha256"], "unverified " + rel)
                dest = bundle / rel
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(p, dest)
                require(sha(dest) == row["sha256"], "copy hash " + rel)
    for relative in ("packaging/windows/partyops.ico", "packaging/uos/update-public-key.txt"):
        p = repo / relative
        require(p.is_file(), "missing " + relative)
        shutil.copy2(p, bundle / p.name)
    # Brand image path is resolved by exact basename in the source tree.
    images = [p for p in repo.rglob("partyops-1024.png") if p.is_file() and ".git" not in p.parts]
    require(images and len({sha(p) for p in images}) == 1, "brand asset missing or ambiguous")
    shutil.copy2(images[0], bundle / "partyops-1024.png")
    for name in ("README.md", "CHANGELOG.md", "LICENSE", "THIRD_PARTY_NOTICES.md"):
        shutil.copy2(repo / name, bundle / name)
    # Preserve notices and exact private candidate lock provenance; no security approval.
    (bundle / "QA-CANDIDATE-ONLY.txt").write_text(
        "Unsigned candidate. Native Win7 SP1 and current WPS acceptance NOT RUN.\n"
        "Rebuilt backports and historical runtime inputs require independent security review.\n"
        "Do not publish or install on production systems.\n", encoding="utf-8")


if __name__ == "__main__":
    main()
