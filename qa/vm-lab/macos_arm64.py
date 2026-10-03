"""固定版本的本地 Darwin 实验取证，禁止把命令行启动等同 PKG/GUI 验收。"""
from __future__ import annotations

import json
from pathlib import Path

from evidence import now, sha256, write_json
from providers import run

HERE = Path(__file__).resolve().parent


def inspect_experiment(lab) -> dict:
    config = json.loads((HERE / "config/macos-arm64.json").read_text(encoding="utf-8"))
    if config.get("product") != "macOS" or not config.get("device", "").startswith("Mac") or not config.get("local_only"):
        raise RuntimeError("MACOS_EXPERIMENT_CONFIGURATION_INVALID")
    source = Path(lab.defaults["fallback_root"]) / "tools/darwin-vm"
    errors = []
    repositories = {}
    for key, directory in (("darwin_vm", source), ("qemu_sptm", source / "qemu-sptm")):
        try:
            actual = run(["git", "-C", str(directory), "rev-parse", "HEAD"]).strip()
            if actual != config[key]["commit"]:
                errors.append("PINNED_COMMIT_MISMATCH:" + key)
            repositories[key] = {"expected": config[key]["commit"], "actual": actual}
        except RuntimeError:
            errors.append("PINNED_SOURCE_MISSING:" + key)
    binary = source / "qemu-sptm/build/qemu-system-aarch64"
    if not binary.is_file():
        errors.append("QEMU_SPTM_LOCAL_BUILD_MISSING")
    firmware = source / "firmware"
    info = firmware / "info"
    if not info.is_file():
        errors.append("MACOS_HELPER_COMPONENTS_MISSING")
    else:
        lines = info.read_text().splitlines()
        if not lines or lines[0] != config["device"] or len(lines) < 2 or lines[1] != config["ipsw_url"]:
            errors.append("MAC_DEVICE_OR_MACOS_IPSW_NOT_PROVEN")
    components = {path.name: sha256(path) for path in firmware.glob("*") if path.is_file()}
    for name in ("bootkc", "dtree", "ramdisk.dmg", "ramdisk.tc"):
        if name not in components:
            errors.append("MACOS_FIRMWARE_COMPONENT_MISSING:" + name)
    helper = firmware / "helper-result.json"
    if not helper.is_file():
        errors.append("MACOS_INTEL_HELPER_PATCH_NOT_PROVEN")
    else:
        result = json.loads(helper.read_text(encoding="utf-8"))
        if (result.get("os") != "Darwin" or result.get("arch") != "x86_64"
                or result.get("patched_ramdisk_sha256") != components.get("ramdisk.dmg")):
            errors.append("MACOS_INTEL_HELPER_OUTPUT_MISMATCH")
    result = {"generated_at": now(), "config": config, "repositories": repositories,
              "qemu_binary_sha256": sha256(binary) if binary.is_file() else None,
              "components": components, "source_path": str(source), "errors": errors,
              "stages": {"kernel": "not_run", "userspace": "not_run", "partyops_runtime": "not_run",
                         "full_lifecycle": "blocked: upstream has no full desktop"},
              "status": "blocked", "runtime_environment_passed": False}
    write_json(lab.root / "reports/macos-arm64/local-experiment.json", result)
    return result
