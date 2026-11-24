"""RC1 Win7 input-only audit; never installs or executes downloaded dependencies."""
import argparse
import concurrent.futures
import hashlib
import json
import os
import pathlib
import re
import struct
import subprocess
import sys
import tempfile

def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--arch", choices=("amd64", "x86"), required=True)
    p.add_argument("--output", type=pathlib.Path, required=True)
    args = p.parse_args()
    root = pathlib.Path(__file__).resolve().parents[1]
    bits = 64 if args.arch == "amd64" else 32
    if sys.platform != "win32" or sys.version_info[:3] != (3, 8, 10) or struct.calcsize("P") * 8 != bits:
        raise SystemExit("WIN7_BUILD_INPUT_PYTHON_MISMATCH")
    import tkinter
    tcl = tkinter.Tcl().eval("info patchlevel")
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=str(root), text=True).strip()
    if source != os.environ.get("SOURCE_SHA"):
        raise SystemExit("WIN7_BUILD_INPUT_SOURCE_MISMATCH")
    args.output.mkdir(parents=True, exist_ok=False)
    wheelhouse = args.output / "public-wheelhouse"
    wheelhouse.mkdir()
    lock = root / "backend/legacy" / ("requirements-windows7-" + args.arch + ".lock")
    # The uv lock contains one package per non-indented requirement block.
    blocks = []
    for line in lock.read_text(encoding="utf-8").splitlines():
        if re.match(r"^[A-Za-z0-9_.-]+==", line):
            blocks.append([line])
        elif blocks and line.strip().startswith("--hash=sha256:"):
            blocks[-1].append(line)
    if not blocks:
        raise SystemExit("WIN7_EMPTY_LOCK")
    # Compile without importing app or running migrations, on the actual 3.8 interpreter.
    source_errors = []
    files = list((root / "backend/app").rglob("*.py")) + list((root / "backend/alembic").rglob("*.py"))
    for path in sorted(files):
        try:
            compile(path.read_bytes(), str(path), "exec")
        except SyntaxError as e:
            source_errors.append({"file": str(path.relative_to(root)), "line": e.lineno, "message": e.msg})
    private = []
    public = []
    for block in blocks:
        requirement = block[0].split()[0]
        hashes = re.findall(r"--hash=sha256:([a-f0-9]{64})", "\n".join(block))
        if not hashes:
            raise SystemExit("WIN7_LOCK_MISSING_HASH: " + requirement)
        item = {"requirement": requirement, "hashes": hashes, "block": "\n".join(block)}
        (private if "+partyops." in requirement.lower() else public).append(item)
    def fetch(item):
        requirement = item["requirement"]
        with tempfile.TemporaryDirectory() as tmp:
            folder = pathlib.Path(tmp)
            req = folder / "requirement.txt"
            req.write_text(item["block"] + "\n", encoding="utf-8")
            try:
                run = subprocess.run([sys.executable, "-m", "pip", "download", "--disable-pip-version-check",
                                      "--no-deps", "--only-binary=:all:", "--require-hashes",
                                      "--index-url", "https://pypi.org/simple", "--no-cache-dir",
                                      "--timeout", "20", "--retries", "1", "--dest", str(folder),
                                      "-r", str(req)], capture_output=True, text=True, timeout=150)
            except subprocess.TimeoutExpired:
                return {"requirement": requirement, "status": "BLOCKED", "reason": "download timeout"}
            wheels = list(folder.glob("*.whl"))
            if run.returncode or len(wheels) != 1:
                return {"requirement": requirement, "status": "BLOCKED", "reason": (run.stderr or run.stdout)[-1500:]}
            wheel = wheels[0]
            digest = sha(wheel)
            if digest not in item["hashes"]:
                raise RuntimeError("WIN7_WHEEL_HASH_MISMATCH: " + requirement)
            wheel.rename(wheelhouse / wheel.name)
            return {"requirement": requirement, "status": "HASH_VERIFIED", "wheel": wheel.name, "sha256": digest}
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        results = list(pool.map(fetch, public))
    missing = [{"requirement": item["requirement"], "locked_sha256": item["hashes"],
                "status": "REQUIRES_ORIGINAL_BACKPORT_WHEEL"} for item in private]
    report = {"status": "INPUT_AUDIT_ONLY", "source_commit": source,
              "target": "windows7-" + args.arch, "python": sys.version.split()[0], "bits": bits, "tcl": tcl,
              "lock_sha256": sha(lock), "source_files_checked": len(files), "source_syntax_errors": source_errors,
              "public_wheels": results, "private_backports_missing": missing,
              "wheelhouse_complete": False, "installer_created": False, "release_ready": False}
    (args.output / "receipt.json").write_text(json.dumps(report, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")
    print("WIN7_INPUT_AUDIT", json.dumps({"target": report["target"], "source_errors": len(source_errors),
          "verified_public_wheels": sum(r["status"] == "HASH_VERIFIED" for r in results),
          "missing_public_wheels": sum(r["status"] != "HASH_VERIFIED" for r in results),
          "private_backports": len(missing)}))
    if source_errors:
        raise SystemExit("WIN7_SOURCE_SYNTAX_BLOCKED")

if __name__ == "__main__":
    main()
