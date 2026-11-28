"""Inventory each target dependency independently, without fake wheels or mutable production locks."""
from __future__ import annotations

import email
import hashlib
import json
import shutil
import subprocess
import sys
import tomllib
import zipfile
from collections import defaultdict, deque
from pathlib import Path

from packaging.markers import default_environment
from packaging.requirements import Requirement
from packaging.utils import canonicalize_name, parse_wheel_filename

LOCK = tomllib.loads(Path("backend/uv.lock").read_text())
LOCKED = defaultdict(list)
for pkg in LOCK["package"]:
    LOCKED[canonicalize_name(pkg["name"])].append(pkg)
ENV = default_environment()
ENV.update(platform_machine="aarch64", platform_system="Linux", sys_platform="linux",
           python_version="3.11", python_full_version="3.11.15", implementation_version="3.11.15")
BLOCKED = {"onnxruntime", "hf-xet"}
SEEDS = {canonicalize_name(parse_wheel_filename(p.name)[0]): p for p in Path("seeds").glob("*.whl")}
ROOTS = ["backend/requirements.txt", "backend/requirements-local-ai.txt", "packaging/uos/requirements-build.txt"]

def digest(p):
    with p.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

def metadata(path):
    with zipfile.ZipFile(path) as archive:
        names = [n for n in archive.namelist() if n.endswith(".dist-info/METADATA") and n.count("/") == 1]
        assert len(names) == 1, path
        return email.message_from_bytes(archive.read(names[0]))

def main():
    queue = deque()
    for file in ROOTS:
        for line in Path(file).read_text().splitlines():
            line = line.split("#", 1)[0].strip()
            if line:
                queue.append((Requirement(line), ""))
    queue.append((Requirement("pysqlite3==0.5.4"), ""))
    chosen = {}
    extras_seen = defaultdict(set)
    failures = {}
    provenance = []
    while queue:
        req, parent_extra = queue.popleft()
        if req.marker and not req.marker.evaluate({**ENV, "extra": parent_extra}):
            continue
        name = canonicalize_name(req.name)
        extras = set(req.extras) | {""}
        if name in failures:
            continue
        if name not in chosen:
            try:
                entries = LOCKED.get(name, [])
                candidates = [p for p in entries if not req.specifier or req.specifier.contains(p["version"], prereleases=True)]
                if entries and len(candidates) != 1:
                    raise ValueError(f"ambiguous/incompatible lock entries: {name}")
                entry = candidates[0] if candidates else None
                if name in SEEDS:
                    wheel = SEEDS[name]
                    source = "verified-prior-artifact"
                else:
                    dest = Path("official" if name in BLOCKED else "resolved")
                    dest.mkdir(exist_ok=True)
                    platform_tags = ["manylinux2014_aarch64", "manylinux_2_17_aarch64"] if name not in BLOCKED else ["manylinux_2_28_aarch64", "manylinux_2_27_aarch64"]
                    spec = f"{name}=={entry['version']}" if entry else str(req).split(";")[0].strip()
                    args = [sys.executable,"-m","pip","download","--no-deps","--only-binary=:all:","--python-version","311","--implementation","cp","--abi","cp311","--dest",str(dest)]
                    for tag in platform_tags:
                        args += ["--platform",tag]
                    result = subprocess.run(args+[spec], capture_output=True, text=True)
                    Path("evidence",f"intake-{name}.log").write_text(result.stdout+result.stderr)
                    if result.returncode:
                        raise RuntimeError("no compatible wheel; see intake log")
                    wheels = [p for p in dest.glob("*.whl") if canonicalize_name(parse_wheel_filename(p.name)[0]) == name]
                    if len(wheels) != 1:
                        raise ValueError(f"unexpected wheel count: {name}")
                    wheel = wheels[0]
                    source = "lock-verified-pypi" if entry else "build-tool-resolution"
                    if entry and not any(w.get("hash") == "sha256:"+digest(wheel) for w in entry.get("wheels",[])):
                        raise ValueError("wheel digest does not match original lock")
                meta = metadata(wheel)
                version = meta["Version"]
                assert canonicalize_name(meta["Name"]) == name
                if req.specifier and not req.specifier.contains(version,prereleases=True):
                    raise ValueError(f"version {version} violates {req}")
                if entry and version != entry["version"]:
                    raise ValueError("version differs from original lock")
                if name not in BLOCKED and wheel.parent != Path("resolved"):
                    target = Path("resolved",wheel.name)
                    shutil.copyfile(wheel,target)
                    wheel = target
                chosen[name]=(version,meta)
                provenance.append({"name":name,"version":version,"filename":wheel.name,"sha256":digest(wheel),"source":source,"metadata_only":name in BLOCKED})
            except (ValueError,RuntimeError,OSError,AssertionError,zipfile.BadZipFile) as exc:
                failures[name]=str(exc)
                # Do not let a failed single package stop unrelated root intake.
                for candidate in Path("resolved").glob("*.whl"):
                    if canonicalize_name(parse_wheel_filename(candidate.name)[0])==name:
                        candidate.unlink()
                continue
        version,meta=chosen[name]
        if req.specifier and not req.specifier.contains(version,prereleases=True):
            failures[name]=f"resolved {version} violates later requirement {req}"
            continue
        new=extras-extras_seen[name]
        extras_seen[name].update(new)
        for extra in new:
            for dependency in meta.get_all("Requires-Dist",[]):
                queue.append((Requirement(dependency),extra))
    sys.path.insert(0,"scripts")
    import importlib
    checker=importlib.import_module("validate-uos-wheelhouse")
    wheels=checker.read_wheels(Path("resolved"),"arm64","full")
    assert not (BLOCKED & set(wheels))
    for row in provenance:
        if row["name"] in failures and row["name"] in wheels:
            wheels[row["name"]].path.unlink()
    report={"status":"INVENTORY_ONLY_NOT_FULL_CLOSURE","release_ready":False,"intentionally_not_packaged":sorted(BLOCKED),"other_failures":failures,"entries":provenance}
    Path("evidence/independent-inventory.json").write_text(json.dumps(report,indent=2))
    print(json.dumps({"available":len(list(Path("resolved").glob("*.whl"))),"blocked":failures,"metadata_only":sorted(BLOCKED)},indent=2))
    # A successful inventory is not a successful release gate.
    return 0 if not failures else 2

if __name__=="__main__":
    raise SystemExit(main())
