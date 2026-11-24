"""Build exact uv.lock source distributions in signed Deepin 2.38 chroot, not a release."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import tomllib
import urllib.request
import zipfile

GROUPS = {
    "numeric": ("numpy",),
    "images": ("lxml", "pillow"),
    "c-extensions": ("cffi", "argon2-cffi-bindings", "greenlet", "sqlalchemy", "markupsafe"),
    "rust-extensions": ("cryptography", "python-calamine", "pydantic-core"),
}


def sha(p):
    with p.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


def main():
    root, out = map(lambda x: Path(x).resolve(), sys.argv[1:3])
    group = sys.argv[3]
    lock = tomllib.loads(Path("backend/uv.lock").read_text())
    spec = importlib.util.spec_from_file_location("abi", "scripts/verify-linux-elf-glibc.py")
    abi = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(abi)
    wheel_dir = out / "wheelhouse"
    wheel_dir.mkdir(exist_ok=False)
    inputs = out / "source-hashes.json"
    rows = []
    results = []
    for name in GROUPS[group]:
        matches = [x for x in lock["package"] if x["name"] == name]
        if len(matches) != 1 or "sdist" not in matches[0]:
            raise RuntimeError("missing exact locked source: " + name)
        p = matches[0]
        source = p["sdist"]
        url = source["url"]
        if not url.startswith("https://files.pythonhosted.org/") or not source["hash"].startswith("sha256:"):
            raise RuntimeError("invalid source provenance")
        filename = url.rsplit("/", 1)[1]
        dest = out / filename
        with urllib.request.urlopen(url, timeout=120) as response, dest.open("wb") as stream:
            remaining = source["size"]
            while remaining:
                block = response.read(min(1024 * 1024, remaining))
                if not block:
                    raise RuntimeError("truncated source")
                stream.write(block)
                remaining -= len(block)
            if response.read(1):
                raise RuntimeError("oversized source")
        if sha(dest) != source["hash"].split(":")[1]:
            raise RuntimeError("source hash mismatch")
        rows.append({"name": name, "version": p["version"], "filename": filename, "sha256": sha(dest)})
        inputs.write_text(json.dumps(rows, indent=2))
        subprocess.check_call(["sudo", "cp", str(dest), str(root / "tmp" / filename)])
        # Don't upload source archives again: they are public and hash-pinned.
        dest.unlink()
        guest_wheels = "/tmp/partyops-wheels-" + name
        subprocess.check_call(["sudo", "chroot", str(root), "mkdir", "-p", guest_wheels])
        log = out / (name + "-build.log")
        command = ["sudo", "chroot", str(root), "/usr/bin/env",
                   "CARGO_BUILD_JOBS=2", "CMAKE_BUILD_PARALLEL_LEVEL=2", "MAKEFLAGS=-j2",
                   "/opt/partyops-wheel-env/bin/python", "-m", "pip", "wheel",
                   "--no-deps", "--wheel-dir", guest_wheels, "/tmp/" + filename]
        with log.open("wb") as stream:
            try:
                completed = subprocess.run(command, stdout=stream, stderr=subprocess.STDOUT, timeout=3600)
                code = completed.returncode
            except subprocess.TimeoutExpired:
                code = 124
        wheels = list((root / guest_wheels.lstrip("/")).glob("*.whl"))
        row = {"name": name, "version": p["version"], "exit_code": code, "status": "BUILD_BLOCKED"}
        if code == 0 and len(wheels) == 1:
            wheel = wheel_dir / wheels[0].name
            subprocess.check_call(["sudo", "cp", str(wheels[0]), str(wheel)])
            subprocess.check_call(["sudo", "chown", f"{os.getuid()}:{os.getgid()}", str(wheel)])
            row.update(wheel=wheel.name, wheel_sha256=sha(wheel))
            try:
                with tempfile.TemporaryDirectory() as td:
                    dest = Path(td)
                    with zipfile.ZipFile(wheel) as z:
                        for m in z.infolist():
                            parts = Path(m.filename).parts
                            if m.filename.startswith("/") or ".." in parts:
                                raise RuntimeError("unsafe wheel member")
                        z.extractall(dest)
                    elf = []
                    for f in dest.rglob("*"):
                        if f.is_file():
                            with f.open("rb") as stream:
                                header = stream.read(64)
                            if header[:4] == b"\x7fELF":
                                if len(header) != 64 or header[:6] != b"\x7fELF\x02\x01" or int.from_bytes(header[18:20], "little") != 258 or int.from_bytes(header[48:52], "little") & 7 != 3:
                                    raise RuntimeError("not LoongArch LP64D")
                                elf.append(f)
                    if not elf:
                        raise RuntimeError("no native ELF in expected extension wheel")
                    count, major, minor = abi.verify(dest, (2, 38))
                    row.update(status="NATIVE_WHEEL_ABI_PASS", elf_count=count, max_glibc=f"{major}.{minor}")
            except (ValueError, OSError, RuntimeError) as exc:
                row.update(status="ABI_BLOCKED", error=str(exc))
                wheel.unlink()
        results.append(row)
        print(json.dumps(row), flush=True)
        (out / "native-wheel-results.json").write_text(json.dumps(results, indent=2))
    (out / "wheel-build-receipt.json").write_text(json.dumps({
        "source_commit": os.environ["SOURCE_SHA"], "group": group, "target_glibc":"2.38",
        "python":"3.12.13", "results":results, "candidate_only":True,
        "full_dependency_closure":False, "installer_created":False, "release_ready":False,
    }, indent=2))
    if any(x["status"] != "NATIVE_WHEEL_ABI_PASS" for x in results):
        raise RuntimeError("one or more target native wheels remain blocked")


if __name__ == "__main__":
    main()
