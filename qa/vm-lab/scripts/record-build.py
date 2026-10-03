"""运行现有本地构建命令，按保留策略登记源码、日志与新制品的对应关系。"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import time
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, sha256, write_json
from lab import REPO, fingerprint, inventory, load_configuration
from providers import QemuLab
from windows_build_payload import CONTRACTS, capture_payload


def preserve_candidate(lab: QemuLab, artifacts: Path, package: dict) -> Path:
    """相同内容只保留一份；兼容旧回执中的历史目录，不改写或删除旧证据。"""
    path = Path(package["path"])
    digest = package["sha256"]
    if sha256(path) != digest:
        raise RuntimeError("CANDIDATE_CHANGED_BEFORE_BACKUP")
    history = artifacts / "candidate-history"
    backup = history / digest / path.name
    if backup.exists():
        if sha256(backup) != digest:
            raise RuntimeError("CANDIDATE_HISTORY_HASH_MISMATCH")
        return backup
    # 旧目录保持原路径，避免重复失败的构建再次复制同一个安装包。
    if history.exists():
        root = history.resolve()
        for existing in sorted(history.glob("*/" + path.name)):
            if (existing.is_file() and existing.resolve().is_relative_to(root)
                    and existing.stat().st_size == path.stat().st_size and sha256(existing) == digest):
                return existing
    lab.check_space(backup.parent, path.stat().st_size / 1024**3)
    backup.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, backup)
    if sha256(backup) != digest:
        raise RuntimeError("CANDIDATE_BACKUP_HASH_MISMATCH")
    return backup


def build(package_ids: list[str], command: list[str]) -> dict:
    matrix, media = load_configuration()
    if not command or not package_ids or any(key not in matrix["packages"] for key in package_ids):
        raise RuntimeError("BUILD_COMMAND_AND_KNOWN_PACKAGES_REQUIRED")
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    lab.initialize()
    directory = lab.root / "reports" / ("build-" + uuid.uuid4().hex[:12])
    directory.mkdir()
    artifacts = REPO / "artifacts"
    old, _ = inventory(matrix, artifacts)
    previous = []
    for key in package_ids:
        if key in old:
            if matrix.get("retain_obsolete_packages", True):
                backup = preserve_candidate(lab, artifacts, old[key])
                previous.append({**old[key], "preserved_path": str(backup)})
            else:
                previous.append({**old[key], "retention": "metadata-only-latest-user-policy"})
    record = {"started_at": now(), "source_before": fingerprint(), "command": command,
              "previous_candidates": previous, "package_ids": package_ids, "status": "running"}
    write_json(directory / "build.json", record)
    log = directory / "build.log"
    flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    gate = [sys.executable, str(REPO / "scripts/verify-full-function-gate.py"),
            "verify", "--root", str(REPO), "--scope", "package"]
    started_ns = time.time_ns()
    with log.open("wb") as stream:
        result = subprocess.run(gate, cwd=REPO, stdout=stream, stderr=subprocess.STDOUT, creationflags=flags, check=False)
        if result.returncode == 0:
            result = subprocess.run(command, cwd=REPO, stdout=stream, stderr=subprocess.STDOUT, creationflags=flags, check=False)
        if result.returncode == 0:
            result = subprocess.run(gate, cwd=REPO, stdout=stream, stderr=subprocess.STDOUT, creationflags=flags, check=False)
    record.update(finished_at=now(), source_after=fingerprint(), exit_code=result.returncode,
                  log={"path": str(log), "sha256": sha256(log)})
    fresh, errors = inventory(matrix, artifacts)
    failures = [errors[key] for key in package_ids if key in errors]
    if record["source_before"] != record["source_after"]:
        failures.append("SOURCE_CHANGED_DURING_BUILD")
    if result.returncode:
        failures.append("LOCAL_BUILD_FAILED")
    for key in package_ids:
        if key in fresh and Path(fresh[key]["path"]).stat().st_mtime_ns < started_ns:
            failures.append("OUTPUT_NOT_CREATED_DURING_BUILD:" + key)
    payloads = {}
    if not failures:
        for key in package_ids:
            if key in CONTRACTS:
                try:
                    payloads[key] = capture_payload(lab, artifacts, fresh[key], directory, started_ns)
                except (OSError, ValueError, TypeError, RuntimeError) as exc:
                    failures.append("WINDOWS_PAYLOAD_CAPTURE_FAILED:" + key + ":" + str(exc))
        # 封存清单期间源码或安装器仍可能变化，必须在写回执前再次校验。
        if fingerprint() != record["source_before"]:
            failures.append("SOURCE_CHANGED_DURING_PAYLOAD_CAPTURE")
        for key in package_ids:
            if sha256(Path(fresh[key]["path"])) != fresh[key]["sha256"]:
                failures.append("PACKAGE_CHANGED_DURING_PAYLOAD_CAPTURE:" + key)
            if (lab.root / "state/builds" / (fresh[key]["sha256"] + ".json")).exists():
                failures.append("EXISTING_BUILD_RECEIPT_PRESERVED:" + key)
    record["windows_payloads"] = payloads
    record.update(status="failed" if failures else "completed", errors=failures)
    write_json(directory / "build.json", record)
    if not failures:
        for key in package_ids:
            receipt = {**record, "package": fresh[key], "output_created_during_build": True}
            if key in payloads:
                receipt["windows_payload"] = payloads[key]
            write_json(lab.root / "state/builds" / (fresh[key]["sha256"] + ".json"), receipt)
    return record


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", action="append", required=True)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    result = build(args.package, command)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    raise SystemExit(0 if result["status"] == "completed" else 2)
