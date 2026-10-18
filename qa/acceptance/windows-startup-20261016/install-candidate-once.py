"""在原 Win10 QA 安装目录覆盖一次；中断后只收集状态，不自动重装。"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
EVIDENCE = Path(__file__).resolve().parent
LAB_ROOT = Path("D:/PartyOps-VM-Lab")
sys.path.insert(0, str(ROOT / "qa/vm-lab"))
from lab import load_configuration  # noqa: E402
from providers import QemuLab  # noqa: E402
from providers import qmp  # noqa: E402
import retention_contract as contract

spec = importlib.util.spec_from_file_location(
    "existing_windows_installer", ROOT / "qa/vm-lab/scripts/exercise-windows-install.py"
)
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("package", type=Path)
    parser.add_argument("--sha256", required=True)
    parser.add_argument("--preflight", action="store_true", help="只核对正式构建回执与候选哈希，不连接 Guest、不安装")
    args = parser.parse_args()
    package = args.package.resolve(strict=True)
    if not package.is_relative_to((ROOT / "artifacts").resolve()):
        raise RuntimeError("ONLY_THIS_WORKSPACE_CANDIDATE_ALLOWED")
    if package.name != "PartyOps_1.4.5-rc.6_windows_amd64.exe":
        raise RuntimeError("EXPECTED_WIN10_X64_PACKAGE_REQUIRED")
    actual_digest = digest(package)
    if actual_digest != args.sha256.lower():
        raise RuntimeError("CANDIDATE_SHA256_MISMATCH")
    baseline = json.loads((EVIDENCE / "retention-baseline.json").read_text(encoding="utf-8"))
    backup = ROOT / ".qa/windows-retention/before-overlay.partyops-backup"
    if baseline["status"] != "passed" or digest(backup) != baseline["backup"]["sha256"]:
        raise RuntimeError("VERIFIED_PRE_OVERLAY_BACKUP_REQUIRED")
    matrix, media = load_configuration()
    matrix["defaults"]["fallback_root"] = str(LAB_ROOT)
    matrix["defaults"]["primary_root"] = str(LAB_ROOT)
    lab = QemuLab(matrix, LAB_ROOT, media)
    binding = contract.candidate(lab, ROOT, package, actual_digest)
    if args.preflight:
        print(json.dumps({"status": "preflight-passed", "candidate": binding}, ensure_ascii=False))
        return
    state = contract.bind_guest(lab, qmp)
    boot_file = EVIDENCE / "retention-boot-before.json"
    boot = contract.read(boot_file)
    if (boot.get("guest_uuid") != contract.UUID or boot.get("disk") != str(contract.DISK.resolve())
            or boot.get("baseline_sha256") != digest(EVIDENCE / "retention-baseline.json")):
        raise RuntimeError("READ_ONLY_BOOT_BEFORE_REQUIRED")
    target = matrix["targets"]["win10-x64"]
    client = installer.connect(lab, target)
    try:
        current = contract.probe(client, installer.execute)
        if current["boot_id"] != boot["boot_id"]:
            raise RuntimeError("BOOT_CHANGED_SINCE_PRE_OVERLAY_RECORD")
        before = json.loads(installer.execute(client, r"""
$ErrorActionPreference='Stop'
$manifest='C:\PartyOps QA\中文 程序\release-manifest.json'
$running=@(Get-Process -Name '*setup*','PartyOps_1.4.5-rc.6_windows_amd64*' -ErrorAction SilentlyContinue)
@{manifest_sha256=(Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant();installer_count=$running.Count}|ConvertTo-Json -Compress
"""))
        if before["manifest_sha256"] != baseline["installed_manifest_sha256"]:
            raise RuntimeError("INSTALLATION_CHANGED_INSPECT_WITHOUT_REINSTALLING")
        if before["installer_count"]:
            raise RuntimeError("EXISTING_INSTALLER_PROCESS_INSPECT_BEFORE_RETRY")
    finally:
        client.close()
    # 写独占安装意图前再次核对实际 QMP 绑定，不能只依赖连接前的登记。
    state = contract.bind_guest(lab, qmp)
    if digest(package) != actual_digest:
        raise RuntimeError("CANDIDATE_CHANGED_BEFORE_INSTALL_ATTEMPT")
    # 独占创建记录：即使远端断线也不允许再次启动安装器。先检查实际安装状态。
    destination = EVIDENCE / "overlay-install"
    destination.mkdir(exist_ok=True)
    with (destination / "attempt.json").open("x", encoding="utf-8") as stream:
        json.dump({"time_utc": datetime.now(timezone.utc).isoformat(),
                   "guest_uuid": state["uuid"], "package_sha256": actual_digest,
                   "disk": str(contract.DISK.resolve()), "candidate": binding,
                   "boot_before_sha256": digest(boot_file),
                   "package": package.name, "operation": "overlay-preserve-existing-data"}, stream, ensure_ascii=False, indent=2)
    identity, result = installer.install_modern(
        lab, target, state, {"path": str(package), "sha256": actual_digest}, destination
    )
    contract.exclusive(destination / "completed.json", {
        "status": "completed", "candidate": binding, "attempt_sha256": digest(destination / "attempt.json"),
        "identity": identity, "install": result, "business_retention": "not-yet-verified"})
    print("覆盖安装器已完成；仍须核对候选身份、启动与原数据，不能据此标记整体验收通过。")


if __name__ == "__main__":
    main()
