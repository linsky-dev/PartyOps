"""只读核对本机专用用户的新轮次沿用关系；不修改账户、配置或原报告。"""
from __future__ import annotations

import json
import ntpath
import re
from pathlib import Path

from evidence import checked_id, safe_child, sha256


def same_path(left, right):
    return bool(left and right) and ntpath.normcase(ntpath.normpath(str(left))) == ntpath.normcase(ntpath.normpath(str(right)))


def verify_session(lab, run: Path, binding: dict, install: dict, account: dict) -> dict:
    def require(condition, message):
        if not condition:
            raise RuntimeError("NATIVE_USER_SESSION_" + message)

    session_path = safe_child(run, run / "native-user-session.json")
    session = json.loads(session_path.read_text(encoding="utf-8-sig"))
    owner_path = safe_child(lab.root / "state/native", lab.root / "state/native/PartyOpsNativeQA/private/ownership.json")
    owner = json.loads(owner_path.read_text(encoding="utf-8-sig"))
    require(same_path(session.get("original_ownership_path"), owner_path)
            and session.get("original_ownership_sha256") == sha256(owner_path), "ORIGINAL_OWNERSHIP_CHANGED")
    old_run = safe_child(lab.root / "reports/win11-x64-native", Path(owner["run_directory"]))
    checked_id(old_run.name)
    require(old_run.parent == run.parent and old_run != run, "ORIGINAL_RUN_INVALID")
    work_root = Path(lab.matrix["defaults"]["fallback_root"]) / "native/win11-x64"
    old_workspace = work_root / old_run.name / "PartyOpsNativeQA"
    workspace = work_root / run.name / "PartyOpsNativeQA"
    require(re.fullmatch(r"[a-f0-9]{32}", str(owner.get("ownership_id", ""))), "OWNERSHIP_ID_INVALID")
    old_data = old_workspace / ("中文 空格业务数据-" + owner["ownership_id"])
    require(type(owner.get("schema_version")) is int and owner["schema_version"] == 1
            and owner.get("purpose") == "PartyOps native standard-user validation" and owner.get("status") == "prepared"
            and same_path(owner.get("native_workspace"), old_workspace) and same_path(owner.get("data_directory"), old_data), "ORIGINAL_CONTEXT_INVALID")
    for field in ("sid", "username", "hardware_uuid", "machine_name"):
        require(owner.get(field) and str(owner[field]).casefold() == str(account.get(field, "")).casefold()
                and session.get(field) == owner[field], "ACCOUNT_CHANGED:" + field)
    require(session.get("controller_sid") == owner.get("controller_sid") and owner.get("controller_sid"), "CONTROLLER_CHANGED")
    require(type(session.get("schema_version")) is int and session["schema_version"] == 2
            and session.get("purpose") == "PartyOps native standard-user revalidation" and session.get("status") == "prepared"
            and session.get("runtime_environment_passed") is False and session.get("account_changed") is False
            and session.get("credential_changed") is False and session.get("ownership_id") == owner["ownership_id"], "CONTRACT_INVALID")
    for field, expected in (("run_directory", run), ("native_workspace", workspace), ("original_run", old_run), ("original_data_directory", old_data)):
        require(same_path(session.get(field), expected), "PATH_MISMATCH:" + field)
    payload = binding["windows_payload"]
    for field, expected in {
        "install_result_sha256": sha256(run / "install-result.json"), "install_binding_sha256": sha256(run / "install-binding.json"),
        "protection_sha256": binding["protection_sha256"], "source_fingerprint": binding["source_fingerprint"],
        "package_sha256": binding["package"]["sha256"], "app_sha256": payload["executables"]["PartyOps.exe"]["sha256"],
        "wizard_sha256": payload["executables"]["PartyOpsWizard.exe"]["sha256"],
    }.items():
        require(session.get(field) == expected, "INSTALL_BINDING_CHANGED:" + field)
    require(install.get("installed_executable_sha256") == session["app_sha256"], "INSTALLED_EXECUTABLE_CHANGED")
    protection_path = safe_child(run, run / "protection.json")
    require(sha256(protection_path) == session["protection_sha256"], "PROTECTION_CHANGED")
    protection = json.loads(protection_path.read_text(encoding="utf-8-sig"))
    preserved = protection.get("existing_native_account", {})
    require(preserved.get("sid") == owner["sid"] and preserved.get("ownership_sha256") == session["original_ownership_sha256"]
            and same_path(preserved.get("original_run"), old_run) and same_path(preserved.get("data_directory"), old_data), "BACKUP_OWNERSHIP_CHANGED")
    require(session.get("data_mode") in {"Retain", "Fresh"}, "DATA_MODE_INVALID")
    data = old_data if session["data_mode"] == "Retain" else workspace / ("中文 空格业务数据-" + owner["ownership_id"])
    safe_child(work_root, data)
    require(same_path(session.get("data_directory"), data), "DATA_PATH_INVALID")
    return {"sha256": sha256(session_path), "data_mode": session["data_mode"], "data_directory": str(data),
            "original_run": str(old_run), "original_ownership_sha256": session["original_ownership_sha256"]}
