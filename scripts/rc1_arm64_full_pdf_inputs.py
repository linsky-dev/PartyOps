"""Recover exact companion source commits from the pinned official full 2.1 wheel.

Never execute wheel contents during input discovery. Only fetch named public
repositories at their embedded immutable commits. Candidate evidence only.
"""
from __future__ import annotations

import ast
import email
import hashlib
import json
import re
import struct
import subprocess
import sys
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

VERSION = "2.1"
REFERENCE = "pymupdf-2.1-cp311-abi3-win_amd64.whl"
REFERENCE_SHA = "e1122bbc3a1212396f0d70d19f78e084991b59ebdfc9d846c28ca8b509de01ad"
SDIST = "pymupdf-2.1.tar.gz"
SDIST_SHA = "9ac95f98016e83eafb8f1d786396f9c60e400d559590596e5b2df9eb6384948b"
REPOS = (
    ("layout", "pymupdf/layout/_build.py", "git_sha",
     "https://github.com/ArtifexSoftware/pymupdf_layout.git", "source/layout"),
    ("4llm", "pymupdf4llm/_build.py", "pymupdf4llm_git_sha",
     "https://github.com/pymupdf/pymupdf4llm.git", "src"),
)


def require(ok, message):
    if not ok:
        raise RuntimeError(message)


def digest(path):
    with path.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


def get_input(release, name, expected, root):
    matches = [x for x in release["urls"] if x["filename"] == name]
    require(len(matches) == 1, "missing or ambiguous PyPI file: " + name)
    item = matches[0]
    require(item["digests"]["sha256"] == expected, "PyPI digest mismatch: " + name)
    url = item["url"]
    u = urllib.parse.urlparse(url)
    require(u.scheme == "https" and u.hostname == "files.pythonhosted.org",
            "untrusted distribution host")
    path = root / name
    with urllib.request.urlopen(url, timeout=90) as response, path.open("wb") as f:
        final = urllib.parse.urlparse(response.geturl())
        require(final.scheme == "https" and final.hostname == "files.pythonhosted.org",
                "unexpected distribution redirect")
        total = 0
        for chunk in iter(lambda: response.read(1024 * 1024), b""):
            total += len(chunk)
            require(total <= item["size"], "oversized distribution")
            f.write(chunk)
    require(total == item["size"] and digest(path) == expected, "distribution hash mismatch")
    return path


def literal_sha(wheel, member, variable):
    require(wheel.getinfo(member).file_size < 1024 * 1024, "oversized build metadata")
    tree = ast.parse(wheel.read(member).decode("utf-8"))
    values = [ast.literal_eval(node.value) for node in tree.body
              if isinstance(node, ast.Assign)
              and any(isinstance(t, ast.Name) and t.id == variable for t in node.targets)]
    require(len(values) == 1 and isinstance(values[0], str)
            and re.fullmatch(r"[0-9a-f]{40}", values[0]), "no exact source commit: " + member)
    return values[0]


def metadata(wheel):
    matches = [p for p in wheel.namelist() if p.endswith(".dist-info/METADATA")]
    require(len(matches) == 1, "unexpected wheel metadata")
    m = email.message_from_bytes(wheel.read(matches[0]))
    require(m["Name"].lower() == "pymupdf" and m["Version"] == VERSION,
            "not full PyMuPDF 2.1")
    return sorted(m.get_all("Requires-Dist") or [])


def prepare(root, output):
    root.mkdir(parents=True, exist_ok=False)
    with urllib.request.urlopen("https://pypi.org/pypi/pymupdf/2.1/json", timeout=90) as f:
        release = json.load(f)
    reference = get_input(release, REFERENCE, REFERENCE_SHA, root)
    get_input(release, SDIST, SDIST_SHA, root)
    receipt = {"reference_sha256": REFERENCE_SHA, "sdist_sha256": SDIST_SHA,
               "status": "PINNED_FULL_SOURCE_INPUTS_ONLY", "release_ready": False}
    with zipfile.ZipFile(reference) as z:
        receipt["requires_dist"] = metadata(z)
        for name, member, var, url, required in REPOS:
            sha = literal_sha(z, member, var)
            dest = root / name
            subprocess.run(["git", "init", str(dest)], check=True)
            subprocess.run(["git", "-C", str(dest), "fetch", "--depth=1", url, sha], check=True)
            subprocess.run(["git", "-C", str(dest), "checkout", "--detach", "FETCH_HEAD"], check=True)
            actual = subprocess.check_output(["git", "-C", str(dest), "rev-parse", "HEAD"],
                                             text=True).strip()
            require(actual == sha and (dest / required).is_dir(), "companion source mismatch")
            require(not subprocess.check_output(["git", "-C", str(dest), "status", "--porcelain"],
                                                text=True).strip(), "dirty companion checkout")
            receipt[name] = {"repository": url, "commit": actual}
    output.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


def verify(root, output):
    wheels = list(root.glob("pymupdf-2.1-*-win_arm64.whl"))
    require(len(wheels) == 1, "full ARM64 wheel missing")
    receipt = json.loads(output.read_text(encoding="utf-8"))
    with zipfile.ZipFile(wheels[0]) as z:
        require(metadata(z) == receipt["requires_dist"], "full dependency metadata mismatch")
        for name, member, var, _, _ in REPOS:
            require(literal_sha(z, member, var) == receipt[name]["commit"], "source provenance mismatch")
        native = [p for p in z.namelist() if p.lower().endswith((".pyd", ".dll"))]
        require(any("tgif" in p.lower() for p in native) and
                any("features" in p.lower() for p in native), "layout native modules missing")
        for p in native:
            with z.open(p) as f:
                header = f.read(64)
                require(header[:2] == b"MZ", "non-PE native file: " + p)
                offset = struct.unpack_from("<I", header, 60)[0]
                require(64 <= offset <= 1024 * 1024, "invalid PE offset: " + p)
                f.seek(offset)
                pe = f.read(6)
                require(pe[:4] == b"PE\0\0" and struct.unpack_from("<H", pe, 4)[0] == 0xAA64,
                        "non-ARM64 native file: " + p)
    receipt.update({"wheel": wheels[0].name, "wheel_sha256": digest(wheels[0]),
                    "native_files": native, "status": "FULL_ARM64_WHEEL_STRUCTURE_VERIFIED_BUILD_ONLY"})
    output.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    mode, directory, evidence = sys.argv[1:]
    if mode not in ("prepare", "verify"):
        raise ValueError(mode)
    (prepare if mode == "prepare" else verify)(Path(directory).resolve(), Path(evidence).resolve())
