"""Rebuild the historical Pillow source backport; never approve release locks."""
import hashlib
import json
import subprocess
import sys
import tarfile
import urllib.request
from pathlib import Path

stage = Path(sys.argv[1]).resolve()
stage.mkdir(parents=True, exist_ok=False)
cache = stage / "depends"
cache.mkdir()
root = Path(__file__).resolve().parents[1]
with urllib.request.urlopen("https://pypi.org/pypi/Pillow/12.3.0/json", timeout=60) as response:
    metadata = json.load(response)
sdist = [x for x in metadata["urls"] if x["filename"] == "pillow-12.3.0.tar.gz"]
assert len(sdist) == 1
sources = [
    (stage / "pillow-12.3.0.tar.gz", sdist[0]["url"], "3b8182a766685eaa002637e28b4ec8d6b18819a0c71f579bf0dbaa5830297cce"),
    (cache / "libjpeg-turbo-3.1.4.1.tar.gz", "https://github.com/libjpeg-turbo/libjpeg-turbo/releases/download/3.1.4.1/libjpeg-turbo-3.1.4.1.tar.gz", "ecae8008e2cc9ade2f2c1bb9d5e6d4fb73e7c433866a056bd82980741571a022"),
    (cache / "zlib-ng-2.3.3.tar.gz", "https://github.com/zlib-ng/zlib-ng/archive/refs/tags/2.3.3.tar.gz", "f9c65aa9c852eb8255b636fd9f07ce1c406f061ec19a2e7d508b318ca0c907d1"),
]
for dest, url, expected in sources:
    with urllib.request.urlopen(url, timeout=120) as response:
        data = response.read(128 * 1024 * 1024 + 1)
    if len(data) > 128 * 1024 * 1024 or hashlib.sha256(data).hexdigest() != expected:
        raise RuntimeError("unverified source: " + dest.name)
    dest.write_bytes(data)
with tarfile.open(sources[0][0]) as tar:
    members = tar.getmembers()
    assert len(members) < 100000 and sum(m.size for m in members) < 1024**3
    tar.extractall(stage, filter="data")
source = stage / "pillow-12.3.0"
patch = stage / "verified.patch"
patch.write_bytes(subprocess.check_output(["git", "show", "HEAD:backend/legacy/patches/pillow-12.3.0-partyops-win7.patch"], cwd=root))
patch_sha = hashlib.sha256(patch.read_bytes()).hexdigest()
assert patch_sha == "90f3b31536dbdb96e4731ae231746f1e1cc0090be7a2e81de512b7763d6039de"
for args in (["git", "apply", "--check", str(patch)], ["git", "apply", str(patch)]):
    subprocess.run(args, cwd=source, check=True)
dep_json = source / ".github" / "dependencies.json"
# PyPI sdist omits .github; retrieve only the exact upstream Git blob.
if not dep_json.exists():
    url = "https://raw.githubusercontent.com/python-pillow/Pillow/bb1d8e8ab8d29048624d96e3ee53cecf7c13d13d/.github/dependencies.json"
    with urllib.request.urlopen(url, timeout=60) as response:
        data = response.read(4097)
    if len(data) != 378 or hashlib.sha1(b"blob 378\0" + data).hexdigest() != "cc6e74fbac5f96fe795f653aa107bff4d9554c32":
        raise RuntimeError("upstream dependency manifest identity mismatch")
    dep_json.parent.mkdir(exist_ok=True)
    dep_json.write_bytes(data)
dep = json.loads(dep_json.read_text(encoding="utf-8"))
# Fail closed if the historical recipe differs from source; no unpinned fallback downloads.
assert dep["jpegturbo"] == "3.1.4.1" and dep["zlib-ng"] == "2.3.3", dep
(stage / "source-receipt.json").write_text(json.dumps({
    "sources": [{"filename": p.name, "url": url, "sha256": digest} for p, url, digest in sources],
    "patch_sha256": patch_sha, "release_ready": False,
    "status": "HISTORICAL_SOURCE_INPUTS_ONLY"
}, indent=2), encoding="utf-8")
