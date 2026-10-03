"""只在有同一业务链真实重启回执时接受 UEFI vars 的一次哈希过渡。"""
from __future__ import annotations

import json
import re
from pathlib import Path

from evidence import sha256


def _read_step(run_dir: Path, execution: dict, name: str) -> tuple[dict, dict]:
    step = execution.get("steps", {}).get(name, {})
    if step.get("status") != "completed" or step.get("path") != name + ".json":
        raise RuntimeError("BUSINESS_REBOOT_PROOF_STEP_MISSING:" + name)
    path = run_dir / step["path"]
    if not path.is_file() or sha256(path) != step.get("sha256"):
        raise RuntimeError("BUSINESS_REBOOT_PROOF_STEP_CHANGED:" + name)
    return step, json.loads(path.read_text(encoding="utf-8"))


def _without_live_vars(context: dict) -> dict:
    result = {**context, "environment": dict(context.get("environment", {}))}
    result["environment"].pop("firmware_vars_sha256", None)
    return result


def validate_reboot_transition(lab_root: Path, target: str, previous: dict, current: dict,
                               system: dict, execution_path: Path, guest_script: Path) -> dict:
    """核验独立回执与本轮 Guest 身份，返回可写入业务指针的审计记录。"""
    run_root = (lab_root / "reports" / target).resolve()
    path = execution_path.resolve()
    if (path.name != "execution.json" or path.parent.parent != run_root
            or not path.parent.name.startswith("run-") or not path.is_file()):
        raise RuntimeError("BUSINESS_REBOOT_PROOF_PATH_INVALID")
    execution = json.loads(path.read_text(encoding="utf-8"))
    journal_pointer = lab_root / "state" / ("run-" + target + ".json")
    if not journal_pointer.is_file():
        raise RuntimeError("BUSINESS_REBOOT_JOURNAL_POINTER_MISSING")
    reference = json.loads(journal_pointer.read_text(encoding="utf-8"))
    if (Path(reference.get("directory", "")).resolve() != path.parent
            or reference.get("context") != execution.get("context")):
        raise RuntimeError("BUSINESS_REBOOT_JOURNAL_POINTER_MISMATCH")
    old = previous.get("context", {})
    run_context = execution.get("context", {})
    if (previous.get("last_phase") != "business" or execution.get("target") != target
            or execution.get("run_id") != path.parent.name or old.get("target") != target
            or current.get("target") != target or old.get("package") != current.get("package")
            or old.get("restore_generation") != current.get("restore_generation")
            or _without_live_vars(old) != _without_live_vars(current)
            or any(run_context.get(key) != current.get(key)
                   for key in ("target", "package", "restore_generation"))
            or run_context.get("environment", {}).get("vm_uuid") != current.get("environment", {}).get("vm_uuid")
            or _without_live_vars({"environment": run_context.get("environment", {})}) !=
               _without_live_vars({"environment": current.get("environment", {})})):
        raise RuntimeError("BUSINESS_CONTEXT_CHANGED_RESTART_FROM_CLEAN_BASELINE")
    old_env, new_env = old.get("environment", {}), current.get("environment", {})
    run_env = run_context.get("environment", {})
    old_has = "firmware_vars_sha256" in old_env
    if (old_has != ("firmware_vars_sha256" in new_env)
            or old_has != ("firmware_vars_sha256" in run_env)):
        raise RuntimeError("BUSINESS_REBOOT_FIRMWARE_HASH_PRESENCE_MISMATCH")
    old_hash = old_env.get("firmware_vars_sha256")
    new_hash = new_env.get("firmware_vars_sha256")
    if old_has and not all(isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value)
                           for value in (old_hash, new_hash, run_env.get("firmware_vars_sha256"))):
        raise RuntimeError("BUSINESS_REBOOT_FIRMWARE_HASH_INVALID")
    business_step, business = _read_step(path.parent, execution, "business-business")
    reboot_step, reboot = _read_step(path.parent, execution, "actual-guest-reboot")
    if (business.get("target") != target or business.get("phase") != "business"
            or business.get("exit_code") != 0 or reboot.get("target") != target
            or reboot.get("status") != "passed" or reboot.get("actual") is not True
            or reboot.get("method") not in {"guest-systemctl-reboot", "guest-poweroff-and-cold-start"}):
        raise RuntimeError("BUSINESS_REBOOT_PROOF_INVALID")
    business_dir = Path(business.get("report_path", "")).resolve()
    if (business_dir.parent != run_root or not business_dir.name.startswith("business-business-")
            or not (business_dir / "context.json").is_file()
            or sha256(business_dir / "context.json") != business_step.get("dependencies", {}).get("context.json")):
        raise RuntimeError("BUSINESS_REBOOT_BUSINESS_CONTEXT_CHANGED")
    dependencies = business_step.get("dependencies", {})
    if not dependencies or any(
            not (business_dir / name).resolve().is_relative_to(business_dir)
            or not (business_dir / name).is_file()
            or sha256(business_dir / name) != digest
            for name, digest in dependencies.items()):
        raise RuntimeError("BUSINESS_REBOOT_BUSINESS_DEPENDENCY_CHANGED")
    prior = json.loads((business_dir / "context.json").read_text(encoding="utf-8"))
    if (any(prior.get(key) != old.get(key) for key in ("target", "package", "environment",
                                                       "restore_generation"))
            or prior.get("remote") != previous.get("remote")
            or prior.get("script_sha256") != sha256(guest_script)):
        raise RuntimeError("BUSINESS_REBOOT_BUSINESS_CONTEXT_MISMATCH")
    before, after = reboot.get("boot_id_before"), reboot.get("boot_id_after")
    raw_reboot = run_root / ("reboot-" + str(before) + ".json")
    if (not isinstance(before, str) or not re.fullmatch(r"[0-9a-f-]{36}", before)
            or not raw_reboot.is_file() or sha256(raw_reboot) != reboot_step["sha256"]):
        raise RuntimeError("BUSINESS_REBOOT_RAW_PROOF_CHANGED")
    if (not isinstance(before, str) or not isinstance(after, str) or before == after
            or prior.get("guest_identity", {}).get("boot_id") != before
            or system.get("boot_id") != after
            or prior.get("guest_identity", {}).get("hardware_uuid") != current["environment"]["vm_uuid"]
            or system.get("hardware_uuid") != current["environment"]["vm_uuid"]):
        raise RuntimeError("BUSINESS_REBOOT_BOOT_IDENTITY_MISMATCH")
    audit = {"firmware_vars_transition": "changed" if old_has and old_hash != new_hash else
             "unchanged" if old_has else "not-applicable",
             "execution": str(path), "execution_sha256": sha256(path),
             "business_step_sha256": business_step["sha256"],
             "reboot_step_sha256": reboot_step["sha256"],
             "boot_id_before": before, "boot_id_after": after}
    if old_has:
        audit.update(old_firmware_vars_sha256=old_hash, new_firmware_vars_sha256=new_hash)
    return audit
