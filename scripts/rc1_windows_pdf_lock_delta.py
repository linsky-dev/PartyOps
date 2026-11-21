"""Reject unrelated dependencies silently updated in a PyMuPDF-only RC1 candidate.

This check operates on uv.lock package records, not installer or WPS evidence.
It never modifies the production dependency lock or declares release readiness.
"""

from __future__ import annotations

import argparse
import copy
import json
import tomllib
from collections import defaultdict
from pathlib import Path


def _packages(lock: dict) -> dict[str, list[dict]]:
    packages = lock.get("package")
    if not isinstance(packages, list):
        raise ValueError("not a uv lock with package records")
    index: dict[str, list[dict]] = defaultdict(list)
    for item in packages:
        if (
            not isinstance(item, dict)
            or not isinstance(item.get("name"), str)
            or not isinstance(item.get("version"), str)
        ):
            raise ValueError("invalid uv lock package record")
        index[item["name"].casefold().replace("_", "-")].append(item)
    return dict(index)


def validate_lock_delta(baseline: Path, candidate: Path) -> dict:
    with baseline.open("rb") as source:
        old = tomllib.load(source)
    with candidate.open("rb") as source:
        new = tomllib.load(source)
    for key in ("version", "revision", "requires-python", "manifest", "options"):
        if old.get(key) != new.get(key):
            raise ValueError("top-level dependency lock drift: " + key)
    prior, updated = _packages(old), _packages(new)
    if set(prior) - set(updated):
        raise ValueError("existing distribution removed from candidate")
    if len(prior.get("pymupdf", [])) != 1 or len(updated.get("pymupdf", [])) != 1:
        raise ValueError("exactly one PyMuPDF distribution required")
    if (
        prior["pymupdf"][0]["version"] != "1.26.0"
        or updated["pymupdf"][0]["version"] != "2.1"
    ):
        raise ValueError("candidate must upgrade PyMuPDF 1.26.0 to exactly 2.1")

    unrelated_changes = []
    for name in prior:
        if name == "pymupdf":
            continue
        original, resolved = prior[name], updated[name]
        if name == "partyops" and len(original) == len(resolved) == 1:
            original, resolved = copy.deepcopy(original), copy.deepcopy(resolved)
            for item in (original[0], resolved[0]):
                requirements = item.get("metadata", {}).get("requires-dist", [])
                requirements[:] = [
                    entry for entry in requirements
                    if entry.get("name", "").casefold() != "pymupdf"
                ]
        if original != resolved:
            unrelated_changes.append(name)
    if unrelated_changes:
        raise ValueError(
            "unrelated pre-existing distributions changed: "
            + ", ".join(sorted(unrelated_changes))
        )

    added = set(updated) - set(prior)
    reachable = {"pymupdf"}
    frontier = ["pymupdf"]
    while frontier:
        current = frontier.pop()
        for package in updated.get(current, []):
            for dependency in package.get("dependencies", []):
                name = dependency.get("name", "").casefold().replace("_", "-")
                if name in added and name not in reachable:
                    reachable.add(name)
                    frontier.append(name)
    unexpected = added - reachable
    if unexpected:
        raise ValueError("new distribution unrelated to PyMuPDF: " + ", ".join(sorted(unexpected)))

    # PyMuPDF 2.1 adds python-version-dependent NetworkX wheels. uv must
    # partition the same original Python range into two resolver branches.
    # This is necessary packaging metadata, not an unrelated version upgrade.
    # When the baseline had no partitions, accept ONLY exact branch markers
    # declared on the newly introduced PyMuPDF dependency edges.
    original_markers = old.get("resolution-markers")
    candidate_markers = new.get("resolution-markers")
    if original_markers != candidate_markers:
        allowed = {
            dependency.get("marker")
            for package in updated["pymupdf"]
            for dependency in package.get("dependencies", [])
            if dependency.get("name", "").casefold().replace("_", "-") in added
            and isinstance(dependency.get("marker"), str)
        }
        if (
            original_markers not in (None, [])
            or not isinstance(candidate_markers, list)
            or not candidate_markers
            or any(not isinstance(marker, str) for marker in candidate_markers)
            or set(candidate_markers) != allowed
            or len(candidate_markers) != len(set(candidate_markers))
        ):
            raise ValueError("resolver partitions changed without a matching PyMuPDF dependency edge")
    return {
        "schema_version": 1,
        "scope": "ISOLATED_WINDOWS_X64_PYMUPDF_CANDIDATE_LOCK_DELTA_ONLY",
        "upgrade": {"distribution": "pymupdf", "from": "1.26.0", "to": "2.1"},
        "all_other_locked_distributions_unchanged": True,
        "new_transitive_distributions": {
            name: sorted({item["version"] for item in updated[name]})
            for name in sorted(added)
        },
        "production_lock_modified": False,
        "real_wps_validated": False,
        "release_approved": False,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        report = validate_lock_delta(args.baseline, args.candidate)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print("PARTYOPS_WINDOWS_PYMUPDF_LOCK_DELTA_VERIFIED")
    except (OSError, ValueError, KeyError, TypeError) as error:
        print("PARTYOPS_WINDOWS_PYMUPDF_LOCK_DELTA_BLOCKED: " + str(error))
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
