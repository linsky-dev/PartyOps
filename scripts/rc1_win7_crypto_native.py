"""Isolated Win7 crypto source candidate; no production lock edits."""
import hashlib
import json
import subprocess
import sys
import tarfile
import urllib.request
from pathlib import Path

root = Path(__file__).resolve().parents[1]
stage = Path(sys.argv[1]).resolve()
arch = sys.argv[2]
assert arch in ("amd64", "x86")
stage.mkdir(parents=True, exist_ok=False)
sources = {
    "openssl-3.5.7": (
        "https://github.com/openssl/openssl/releases/download/openssl-3.5.7/openssl-3.5.7.tar.gz",
        "a8c0d28a529ca480f9f36cf5792e2cd21984552a3c8e4aa11a24aa31aeac98e8"),
    "cryptography-50.0.0": (
        "https://files.pythonhosted.org/packages/de/41/6cbdcf9142d00fe82836fbb51e503e58088575cf7a0fe1dbff6695bf0840/cryptography-50.0.0.tar.gz",
        "eeac2acb5a20ed25e0ad6d1df9891a520b78b404266b6d11778f25d5d691a6c9"),
}
for name, (url, digest) in sources.items():
    with urllib.request.urlopen(url, timeout=120) as response:
        payload = response.read(128 * 1024 * 1024 + 1)
    if len(payload) > 128 * 1024 * 1024 or hashlib.sha256(payload).hexdigest() != digest:
        raise RuntimeError("unverified source: " + name)
    archive = stage / (name + ".tar.gz")
    archive.write_bytes(payload)
    with tarfile.open(archive) as tar:
        members = tar.getmembers()
        if len(members) > 100000 or sum(m.size for m in members) > 1024**3:
            raise RuntimeError("archive bounds exceeded")
        tar.extractall(stage, filter="data")
patch = stage / "verified.patch"
patch.write_bytes(subprocess.check_output(["git", "show", "HEAD:backend/legacy/patches/cryptography-50.0.0-partyops-win7.patch"], cwd=root))
patch_sha = hashlib.sha256(patch.read_bytes()).hexdigest()
if patch_sha != "dd12813eb095e4d7aeba7668696647ce2db625c68a159bffc4968daf7c182760":
    raise RuntimeError("historical patch mismatch")
for args in (["git", "apply", "--check", str(patch)], ["git", "apply", str(patch)]):
    subprocess.run(args, cwd=stage / "cryptography-50.0.0", check=True)
lock = root / f"backend/legacy/requirements-windows7-{arch}.lock"
lines = lock.read_text(encoding="utf-8").splitlines()
selected = []
for pin in ("cffi==1.17.1", "pycparser==2.23", "typing-extensions==4.13.2"):
    hits = [i for i, line in enumerate(lines) if line.startswith(pin + " ")]
    if len(hits) != 1:
        raise RuntimeError("missing exact pin " + pin)
    i = hits[0]
    selected.append(lines[i])
    while selected[-1].rstrip().endswith("\\"):
        i += 1
        if not lines[i].strip().startswith("--hash=sha256:"):
            raise RuntimeError("missing lock hash")
        selected.append(lines[i])
(stage / "runtime-deps.lock").write_text("\n".join(selected) + "\n", encoding="utf-8")
(stage / "source-receipt.json").write_text(json.dumps({
    "architecture": arch, "sources": sources, "patch_sha256": patch_sha,
    "lock_sha256": hashlib.sha256(lock.read_bytes()).hexdigest(),
    "release_ready": False, "status": "SOURCE_INPUTS_ONLY"
}, indent=2), encoding="utf-8")
