"""每 VM 独立 UEFI 变量盘及成对快照；固件/变量变化不得追认旧证据。"""
from __future__ import annotations

import re
import shutil
from pathlib import Path

from evidence import checked_id, safe_child, sha256

FIELDS = ("firmware_code_sha256", "firmware_vars_template_sha256", "baseline_vars_sha256", "firmware_vars_sha256")


def sources(lab, target: str) -> dict:
    config = lab.matrix["targets"][target].get("firmware", {})
    result = {}
    for field in ("code", "vars_template"):
        expected = config.get(field + "_sha256", "")
        if not config.get(field) or not re.fullmatch(r"[0-9a-f]{64}", expected):
            raise RuntimeError("UEFI_FIRMWARE_CONFIGURATION_MISSING:" + field)
        path = safe_child(lab.qemu_home, lab.qemu_home / config[field])
        if not path.is_file():
            raise RuntimeError("UEFI_FIRMWARE_MISSING:" + field)
        if sha256(path) != expected:
            raise RuntimeError("UEFI_FIRMWARE_HASH_MISMATCH:" + field)
        result.update({field: str(path), field + "_sha256": expected})
    return result


def prepare(lab, target: str, *, source_vars: Path | None = None, source_sha256: str | None = None) -> dict:
    result = sources(lab, target)
    path = safe_child(lab.vm_dir(target), lab.vm_dir(target) / "uefi-vars.fd")
    if path.exists():
        raise RuntimeError("UEFI_VARS_ALREADY_EXISTS")
    source = source_vars or Path(result["vars_template"])
    expected = source_sha256 if source_vars else result["vars_template_sha256"]
    if sha256(source) != expected:
        raise RuntimeError("UEFI_VARS_SOURCE_CHANGED")
    path.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, path)
    if sha256(path) != expected:
        raise RuntimeError("UEFI_VARS_COPY_FAILED")
    result.update(vars=str(path), vars_sha256=expected)
    return result


def validate(lab, target: str, state: dict, *, checkpoint: bool = False) -> dict:
    expected = sources(lab, target)
    recorded = state.get("firmware", {})
    if any(recorded.get(field) != value for field, value in expected.items()):
        raise RuntimeError("UEFI_FIRMWARE_BINDING_MISMATCH")
    if state.get("firmware_restore_pending"):
        raise RuntimeError("UEFI_SNAPSHOT_RESTORE_INCOMPLETE")
    path = safe_child(lab.vm_dir(target), Path(recorded.get("vars", "")))
    if path != (lab.vm_dir(target) / "uefi-vars.fd").resolve() or not path.is_file():
        raise RuntimeError("UEFI_VARS_IDENTITY_MISMATCH")
    if path.stat().st_size != Path(expected["vars_template"]).stat().st_size:
        raise RuntimeError("UEFI_VARS_SIZE_CHANGED")
    actual = sha256(path)
    if checkpoint and actual != recorded.get("vars_sha256"):
        raise RuntimeError("UEFI_VARS_CHANGED_WHILE_STOPPED")
    return {**expected, "vars": str(path), "vars_sha256": actual}


def checkpoint_stopped(lab, target: str, state: dict) -> None:
    """仅在本控制器启动的 QEMU 已退出时接收其正常 NVRAM 写入，之后拒绝停机篡改。"""
    if not lab.matrix["targets"][target].get("firmware"):
        return
    if lab.live(state) or lab.process_active(state):
        raise RuntimeError("UEFI_CHECKPOINT_REQUIRES_STOPPED_VM")
    was_started = bool(state.get("pid") and state.get("started_at") and state.get("status") in
                       {"running", "starting", "stop_requested", "failed"})
    value = validate(lab, target, state, checkpoint=not was_started)
    state["firmware"]["vars_sha256"] = value["vars_sha256"]


def snapshot_path(lab, target: str, name: str) -> Path:
    return safe_child(lab.vm_dir(target), lab.vm_dir(target) / "uefi-snapshots" / (checked_id(name) + ".fd"))


def snapshot_record(lab, target: str, state: dict, name: str) -> dict:
    expected = sources(lab, target)
    record = state.get("firmware_snapshots", {}).get(name, {})
    if (record.get("code_sha256") != expected["code_sha256"]
            or record.get("vars_template_sha256") != expected["vars_template_sha256"]):
        raise RuntimeError("UEFI_SNAPSHOT_BINDING_MISSING_OR_CHANGED")
    path = snapshot_path(lab, target, name)
    if not path.is_file() or sha256(path) != record.get("vars_sha256"):
        raise RuntimeError("UEFI_SNAPSHOT_VARS_CHANGED")
    return {key: record[key] for key in ("code_sha256", "vars_template_sha256", "vars_sha256")}


def save_snapshot(lab, target: str, state: dict, name: str) -> dict:
    value = validate(lab, target, state, checkpoint=True)
    path = snapshot_path(lab, target, name)
    if path.exists():
        raise RuntimeError("UEFI_SNAPSHOT_UNREGISTERED_FILE_EXISTS")
    path.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(value["vars"], path)
    if sha256(path) != value["vars_sha256"]:
        raise RuntimeError("UEFI_SNAPSHOT_COPY_FAILED")
    return {key: value[key] for key in ("code_sha256", "vars_template_sha256", "vars_sha256")}


def restore_snapshot(lab, target: str, state: dict, name: str) -> None:
    record = snapshot_record(lab, target, state, name)
    destination = safe_child(lab.vm_dir(target), Path(state["firmware"]["vars"]))
    if destination != (lab.vm_dir(target) / "uefi-vars.fd").resolve():
        raise RuntimeError("UEFI_VARS_IDENTITY_MISMATCH")
    temporary = destination.with_suffix(".restore.tmp")
    safe_child(lab.vm_dir(target), temporary)
    shutil.copyfile(snapshot_path(lab, target, name), temporary)
    if sha256(temporary) != record["vars_sha256"]:
        raise RuntimeError("UEFI_SNAPSHOT_COPY_FAILED")
    temporary.replace(destination)
    state["firmware"]["vars_sha256"] = record["vars_sha256"]
    state.pop("firmware_restore_pending", None)


def runtime_binding(lab, target: str, state: dict) -> dict:
    value = validate(lab, target, state, checkpoint=not (lab.live(state) or lab.process_active(state)))
    baseline = snapshot_record(lab, target, state, state["clean_baseline"]["name"])
    return {"firmware_code_sha256": value["code_sha256"],
            "firmware_vars_template_sha256": value["vars_template_sha256"],
            "baseline_vars_sha256": baseline["vars_sha256"], "firmware_vars_sha256": value["vars_sha256"]}
