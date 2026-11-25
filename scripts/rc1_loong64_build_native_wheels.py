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
    "rust-crypto": ("cryptography",),
    "rust-calamine": ("python-calamine",),
    "rust-pydantic": ("pydantic-core",),
}


def sha(p):
    with p.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


RUST_VERSION = "1.98.1"
RUST_HOST = "loongarch64-unknown-linux-gnu"
RUST_KEY = "108F66205EAEB0AAA8DD5E1C85AB96E6FA1BE5FE"
RUST_DIST = "https://static.rust-lang.org/"


def rust_toolchain(root, out):
    """Install upstream host tools inside the exact signed Deepin root.
    The Rust manifest must pass a pinned-key signature check; components
    are hash-verified against that manifest. No weak-signature fallback.
    """
    with tempfile.TemporaryDirectory(prefix="partyops-rust-") as tmp:
        tmp = Path(tmp)
        def download(url, path):
            if not url.startswith(RUST_DIST):
                raise RuntimeError("unexpected Rust origin")
            subprocess.check_call(["curl", "--proto", "=https", "--proto-redir", "=https",
                                   "--fail", "--location", "--retry", "3", "--output", str(path), url])
        key = tmp / "rust-key.asc"
        download(RUST_DIST + "rust-key.gpg.ascii", key)
        home = tmp / "gnupg"
        home.mkdir(mode=0o700)
        gpg = ["gpg", "--batch", "--homedir", str(home)]
        subprocess.check_call(gpg + ["--import", str(key)])
        manifest = out / "rust-channel.toml"
        sig = out / "rust-channel.toml.asc"
        url = RUST_DIST + "dist/channel-rust-" + RUST_VERSION + ".toml"
        download(url, manifest)
        download(url + ".asc", sig)
        verified = subprocess.run(gpg + ["--status-fd", "1", "--verify", str(sig), str(manifest)],
                                  capture_output=True, text=True)
        (out / "rust-signature-status.txt").write_text(verified.stdout + verified.stderr)
        valid = [x.split() for x in verified.stdout.splitlines() if x.startswith("[GNUPG:] VALIDSIG ")]
        if verified.returncode or not any(len(x) >= 11 and RUST_KEY in (x[2], x[-1]) and x[9] in ("8", "9", "10") for x in valid):
            raise RuntimeError("Rust manifest signature not valid for pinned key")
        data = tomllib.loads(manifest.read_text())
        if not data["pkg"]["rust"]["version"].startswith(RUST_VERSION + " "):
            raise RuntimeError("Rust manifest version mismatch")
        receipts = []
        for package in ("rustc", "cargo", "rust-std"):
            entry = data["pkg"][package]["target"][RUST_HOST]
            if not entry["available"]:
                raise RuntimeError("official Rust host component unavailable: " + package)
            archive = tmp / (package + ".tar.xz")
            download(entry["xz_url"], archive)
            if sha(archive) != entry["xz_hash"]:
                raise RuntimeError("Rust component hash mismatch: " + package)
            receipts.append({"package": package, "url": entry["xz_url"], "sha256": sha(archive)})
            (out / "rust-toolchain-inputs.json").write_text(json.dumps(receipts, indent=2))
            extracted = tmp / package
            extracted.mkdir()
            subprocess.check_call(["tar", "-xJf", str(archive), "-C", str(extracted)])
            dirs = list(extracted.iterdir())
            if len(dirs) != 1 or not (dirs[0] / "install.sh").is_file():
                raise RuntimeError("unexpected Rust installer layout")
            guest = "/tmp/partyops-rust-" + package
            subprocess.check_call(["sudo", "cp", "-a", str(dirs[0]), str(root / guest.lstrip("/"))])
            subprocess.check_call(["sudo", "chroot", str(root), guest + "/install.sh",
                                   "--prefix=/opt/partyops-rust", "--disable-ldconfig"])
            subprocess.check_call(["sudo", "rm", "-rf", str(root / guest.lstrip("/"))])
            archive.unlink()
        report = subprocess.check_output(["sudo", "chroot", str(root), "/opt/partyops-rust/bin/rustc", "-vV"], text=True)
        cargo = subprocess.check_output(["sudo", "chroot", str(root), "/opt/partyops-rust/bin/cargo", "--version"], text=True)
        (out / "rust-toolchain-version.txt").write_text(report + cargo)
        if "release: " + RUST_VERSION not in report or "host: " + RUST_HOST not in report or not cargo.startswith("cargo " + RUST_VERSION):
            raise RuntimeError("incorrect native Rust toolchain")


def main():
    root, out = map(lambda x: Path(x).resolve(), sys.argv[1:3])
    group = sys.argv[3]
    if group.startswith("rust-"):
        rust_toolchain(root, out)
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
        env = ["CARGO_BUILD_JOBS=4", "CMAKE_BUILD_PARALLEL_LEVEL=4", "MAKEFLAGS=-j4"]
        if group.startswith("rust-"):
            env.append("PATH=/opt/partyops-rust/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin")
        command = ["sudo", "chroot", str(root), "/usr/bin/env", *env,
                   "/opt/partyops-wheel-env/bin/python", "-m", "pip", "wheel", "--verbose",
                   "--no-deps", "--wheel-dir", guest_wheels]
        if group == "numeric":
            command.append("--config-settings=compile-args=-j4")
        command.append("/tmp/" + filename)
        with log.open("wb") as stream:
            try:
                completed = subprocess.run(command, stdout=stream, stderr=subprocess.STDOUT, timeout=14400)
                code = completed.returncode
            except subprocess.TimeoutExpired:
                code = 124
        if code:
            # Expose the actual backend/compiler failure in job logs as well as artifact.
            with log.open("rb") as stream:
                stream.seek(max(0, log.stat().st_size - 24000))
                print(stream.read().decode("utf-8", errors="replace"), flush=True)
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
