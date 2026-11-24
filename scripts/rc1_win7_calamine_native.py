"""Rebuild the recorded Win7 python-calamine source, then test native wheel.

No historical wheel/VEX is reused as evidence for the new binary.
"""
from __future__ import annotations

import email
import hashlib
import importlib.util
import json
import re
import sys
import tarfile
import urllib.request
import zipfile
from pathlib import Path

VERSION = "0.3.2"
LOCAL = "0.3.2+partyops.1"
URL = "https://files.pythonhosted.org/packages/6b/21/387b92059909e741af7837194d84250335d2a057f614752b6364aaaa2f56/python_calamine-0.3.2.tar.gz"
SHA = "5cf12f2086373047cdea681711857b672cba77a34a66dd3755d60686fc974e06"


def require(ok, message):
    if not ok:
        raise RuntimeError(message)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare(root):
    root.mkdir(parents=True, exist_ok=False)
    archive = root / "source.tar.gz"
    with urllib.request.urlopen(URL, timeout=60) as f:
        require(f.geturl() == URL, "unexpected source redirect")
        content = f.read(32 * 1024 * 1024 + 1)
    require(len(content) <= 32 * 1024 * 1024, "source too large")
    archive.write_bytes(content)
    require(digest(archive) == SHA, "source hash mismatch")
    with tarfile.open(archive) as t:
        t.extractall(root, filter="data")
    candidates = [p.parent for p in root.rglob("Cargo.toml")
                  if (p.parent / "Cargo.lock").is_file() and (p.parent / "pyproject.toml").is_file()]
    require(len(candidates) == 1, "ambiguous source root")
    src = candidates[0]
    # Historical evidence explicitly limits changes to root package version fields.
    for name in ("Cargo.toml", "Cargo.lock"):
        p = src / name
        original = p.read_text(encoding="utf-8")
        pattern = r'(?ms)(^\[package\]\s*\n.*?^version\s*=\s*")0\.3\.2(")' if name == "Cargo.toml" else r'(?m)(^name = "python[-_]calamine"\s*\nversion = ")0\.3\.2(")'
        updated, n = re.subn(pattern, lambda m: m.group(1) + LOCAL + m.group(2), original)
        require(n == 1, "historical root version not found: " + name)
        p.write_text(updated, encoding="utf-8", newline="\n")
    (root / "source-path.txt").write_text(str(src), encoding="utf-8")
    (root / "source-receipt.json").write_text(json.dumps({
        "source_sha256": SHA, "version": LOCAL,
        "cargo_toml_sha256": digest(src / "Cargo.toml"),
        "cargo_lock_sha256": digest(src / "Cargo.lock"),
        "source_changes": "root Cargo package and lock version only",
        "release_ready": False,
    }, indent=2), encoding="utf-8")


def verify(out, arch):
    platform = {"amd64": "win_amd64", "x86": "win32"}[arch]
    wheels = list(out.glob("*.whl"))
    require(len(wheels) == 1 and wheels[0].name ==
            f"python_calamine-{LOCAL}-cp38-cp38-{platform}.whl", "unexpected wheel identity")
    module_path = Path(__file__).with_name("validate-win7-pe.py")
    spec = importlib.util.spec_from_file_location("win7_pe_validator", module_path)
    validator = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(validator)
    native = out / "native-pe"
    native.mkdir(exist_ok=False)
    with zipfile.ZipFile(wheels[0]) as z:
        md = [n for n in z.namelist() if n.endswith(".dist-info/METADATA")]
        require(len(md) == 1, "ambiguous METADATA")
        m = email.message_from_bytes(z.read(md[0]))
        require(m["Version"] == LOCAL and re.sub(r"[-_]+", "-", m["Name"].lower()) == "python-calamine",
                "unexpected metadata")
        members = [n for n in z.namelist() if n.lower().endswith((".pyd", ".dll"))]
        require(members and any(n.endswith(".pyd") for n in members), "native module missing")
        pe_rows = []
        for i, member in enumerate(members):
            p = native / (str(i) + "-" + Path(member).name)
            p.write_bytes(z.read(member))
            errors = validator.validate_pe(p, arch, {})
            require(not errors, "; ".join(errors))
            pe_rows.append({"path": member, "sha256": digest(p)})
    (out / "wheel-receipt.json").write_text(json.dumps({
        "architecture": arch, "wheel": wheels[0].name, "wheel_sha256": digest(wheels[0]),
        "native": pe_rows, "static_pe_passed": True,
        "status": "STATIC_PE_BUILD_ONLY", "release_ready": False,
    }, indent=2), encoding="utf-8")


if __name__ == "__main__":
    if sys.argv[1] == "prepare":
        prepare(Path(sys.argv[2]).resolve())
    elif sys.argv[1] == "verify":
        verify(Path(sys.argv[2]).resolve(), sys.argv[3])
    else:
        raise ValueError(sys.argv[1])
