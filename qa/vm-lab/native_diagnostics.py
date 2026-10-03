"""只读展示 Win11 本机实测阶段；部分诊断永不转换为完整生命周期证据。"""
from __future__ import annotations

import base64
import datetime as dt
import json
import ntpath
import re
import subprocess
import uuid
from pathlib import Path

from evidence import checked_id, now, safe_child, sha256
from native_install import verify as verify_install_binding
from native_install import write_new
from native_user_session import verify_session
from native_windows import identity_digest, read_host, validate_identity

HISTORICAL_RUN = "native-20260908-current"  # 只允许显式检查的历史格式；不作为当前运行默认值。
INSTALL_DIRECTORY = Path("E:/PartyOps1/PartyOps")
STAGES = {
    "replacement-install": ("候选替换安装", "install-result.json"),
    "standard-user-startup": ("普通用户安装版启动诊断", "standard-user-latest.json"),
    "personal-permission": ("个人配置业务目录写权限", "standard-user-personal-permission.json"),
    "resume-configure": ("真实向导首次个人配置", "business/resume-configure.json"),
    "business": ("事项、附件、导出与备份恢复", "business/business.json"),
    "collaboration-business": ("双账号协作业务", "business/collaboration-business.json"),
    "ocr": ("真实中文图片 OCR", "business/ocr.json"),
    "formatter-probe": ("公文服务归属检查", "business/formatter-probe.json"),
    "revalidate": ("新安装版保留数据复验", "business/revalidate.json"),
    "models": ("中文向量检索及本地 LLM", "business/models.json"),
}
REMAINING = ["CLEAN_INSTALL_NOT_VERIFIED", "OLDER_PACKAGE_OVERLAY_UPGRADE_NOT_VERIFIED",
             "NATIVE_SYSTEM_REBOOT_NOT_VERIFIED", "SEPARATE_COLLABORATION_MACHINE_NOT_VERIFIED",
             "FULL_GUI_WPS_OFFLINE_MODELS_AND_DOCUMENT_GOLDENS_NOT_VERIFIED",
             "BOTH_UNINSTALL_DATA_MODES_NOT_VERIFIED", "FULL_LIFECYCLE_EVIDENCE_MISSING"]
CURRENT_FILES = ("candidate.json", "install-binding.json", "install-start.json", "install-result.json",
                 "native-user-session.json", "standard-user-latest.json", "standard-user-runtime.json")


def require(condition, reason):
    if not condition:
        raise RuntimeError(reason)


def same_path(left, right) -> bool:
    return bool(left and right) and ntpath.normcase(ntpath.normpath(str(left))) == ntpath.normcase(ntpath.normpath(str(right)))


def read_account() -> dict:
    """仅查询本轮专用本地账户、组和已登记 profile；不读凭据与用户配置内容。"""
    script = r"""$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$user=Get-LocalUser -Name 'PartyOpsNativeQA'
$admins=@(Get-LocalGroupMember -SID 'S-1-5-32-544'|ForEach-Object {$_.SID.Value})
$users=@(Get-LocalGroupMember -SID 'S-1-5-32-545'|ForEach-Object {$_.SID.Value})
$profile=(Get-ItemProperty -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\'+$user.SID.Value)).ProfileImagePath
[Console]::WriteLine((@{username=$user.Name;sid=$user.SID.Value;enabled=$user.Enabled;
is_admin=($admins -contains $user.SID.Value);is_user=($users -contains $user.SID.Value);
user_profile=$profile;local_appdata=(Join-Path $profile 'AppData\Local');
hardware_uuid=(Get-CimInstance Win32_ComputerSystemProduct).UUID;machine_name=$env:COMPUTERNAME}|ConvertTo-Json -Compress))
"""
    encoded = base64.b64encode(script.encode("utf-16le")).decode("ascii")
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
                            capture_output=True, text=True, encoding="utf-8-sig", timeout=30, check=False,
                            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    require(result.returncode == 0, "NATIVE_DIAGNOSTIC_ACCOUNT_QUERY_FAILED")
    return json.loads(result.stdout)


def read_record(root: Path, path: Path, *, allow_list=False) -> tuple[dict, str]:
    path = safe_child(root, path)
    # 本入口不扫描目录，引用的底层文件也仅允许公开 result.json。
    require(not {"private", "protected.local"}.intersection(part.casefold() for part in path.parts),
            "NATIVE_DIAGNOSTIC_PRIVATE_PATH_REJECTED")
    require(path.stat().st_size <= 1024 * 1024, "NATIVE_DIAGNOSTIC_RECORD_TOO_LARGE")
    content = path.read_bytes()
    import hashlib
    value = json.loads(content.decode("utf-8-sig"))
    require(isinstance(value, dict) or (allow_list and isinstance(value, list)), "NATIVE_DIAGNOSTIC_RECORD_INVALID")
    return value, hashlib.sha256(content).hexdigest()


def current_path(lab, target):
    checked_id(target)
    return safe_child(lab.root / "state/native", lab.root / "state/native" / (target + "-diagnostics-current.json"))


def read_current(lab, target) -> tuple[Path, dict]:
    """只读明确登记，不依据目录名称、mtime 或文件枚举猜测最新运行。"""
    path = current_path(lab, target)
    value, _ = read_record(lab.root / "state/native", path)
    require(type(value.get("schema_version")) is int and value["schema_version"] == 1
            and value.get("target") == target and value.get("scope") == "verified-native-current-run"
            and value.get("runtime_environment_passed") is False, "NATIVE_CURRENT_REGISTRATION_INVALID")
    root = safe_child(lab.root / "reports" / target, Path(value["run_directory"]))
    require(root.parent == (lab.root / "reports" / target).resolve(), "NATIVE_CURRENT_RUN_PATH_INVALID")
    records = value.get("records", {})
    require(set(records) == set(CURRENT_FILES), "NATIVE_CURRENT_REQUIRED_RECORDS_MISSING")
    for name, expected in records.items():
        _, actual = read_record(root, root / name)
        require(actual == expected, "NATIVE_CURRENT_RECORD_CHANGED:" + name)
    for item in value.get("wps_evidence", []):
        evidence_path = safe_child(root / "business", Path(item["path"]))
        _, actual = read_record(root, evidence_path)
        require(actual == item["sha256"], "NATIVE_CURRENT_WPS_EVIDENCE_CHANGED")
    return root, value


def register_current(lab, target, package, source, run_directory: Path, *, wps_evidence=()) -> dict:
    """验证后显式登记当前运行，旧登记按原字节保留，既有实测回执不改写。"""
    root = safe_child(lab.root / "reports" / target, run_directory)
    result = summary(lab, target, package, source, run_directory=root, wps_evidence=wps_evidence)
    require(result["status"] == "partial" and not result["binding_errors"] and result.get("standard_user_session"),
            "NATIVE_CURRENT_RUN_NOT_VERIFIED:" + ";".join(result["binding_errors"]))
    require(result["stages"].get("retained-personal-runtime", {}).get("status") == "passed", "NATIVE_CURRENT_RUNTIME_SESSION_NOT_VERIFIED")
    records = {name: read_record(root, root / name)[1] for name in CURRENT_FILES}
    items = []
    for path in wps_evidence:
        path = safe_child(root / "business", path)
        require(result["stages"]["wps:" + path.parent.name]["status"] in {"failed", "partial"}, "NATIVE_CURRENT_WPS_EVIDENCE_REJECTED")
        items.append({"path": str(path), "sha256": read_record(root, path)[1]})
    registration = {"schema_version": 1, "scope": "verified-native-current-run", "registered_at": now(),
                    "target": target, "run_directory": str(root), "binding": result["binding"],
                    "records": records, "wps_evidence": items, "runtime_environment_passed": False}
    path = current_path(lab, target)
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.is_file():
        _, previous_sha = read_record(path.parent, path)
        history = path.parent / (target + "-diagnostics-history-" + uuid.uuid4().hex + ".json")
        with history.open("xb") as stream:
            stream.write(path.read_bytes())
        registration["previous_registration"] = {"path": str(history), "original_sha256": previous_sha, "sha256": sha256(history)}
    pending = path.parent / (path.name + "." + uuid.uuid4().hex + ".tmp")
    write_new(pending, registration)
    pending.replace(path)
    return registration


def same_timestamp(left, right):
    try:
        return dt.datetime.fromisoformat(str(left).replace("Z", "+00:00")) == dt.datetime.fromisoformat(str(right).replace("Z", "+00:00"))
    except ValueError:
        return False


def validate_runtime(value, child, account, install):
    runtime = value.get("runtime", {})
    require(child.get("scope") == "standard-token-retained-personal-runtime-ready"
            and value.get("gui_verified") is False and value.get("first_configuration_verified") is False
            and child.get("gui_verified") is False and child.get("first_configuration_verified") is False,
            "NATIVE_DIAGNOSTIC_RUNTIME_SCOPE_MISMATCH")
    require(runtime.get("owner_sid") == account["sid"] and runtime.get("session_id") == value["session_id"]
            and type(runtime.get("pid")) is int and runtime["pid"] > 0
            and same_path(runtime.get("executable_path"), INSTALL_DIRECTORY / "PartyOps.exe")
            and runtime.get("sha256") == install["installed_executable_sha256"]
            and type(runtime.get("launcher_exit_code")) is int and runtime["launcher_exit_code"] == 0
            and runtime.get("address") == "127.0.0.1" and type(runtime.get("port")) is int and 1024 <= runtime["port"] <= 65534
            and runtime.get("started_at") and runtime.get("created_at"), "NATIVE_DIAGNOSTIC_RUNTIME_PROCESS_MISMATCH")
    nested = child.get("runtime", {})
    require(set(nested) == set(runtime) and all(same_timestamp(runtime[key], nested[key]) if key in {"created_at", "started_at"}
            else runtime[key] == nested[key] for key in runtime), "NATIVE_DIAGNOSTIC_RUNTIME_CHILD_CHANGED")
    return runtime


def summary(lab, target: str, package: dict, source: str, system: dict | None = None,
            *, run_directory: Path | None = None, wps_evidence=()) -> dict:
    """可显式选择新 run；重核制品/主机/账户，不调用产品或发放 accepted case。"""
    report_root = lab.root / "reports" / target
    registration = None
    if run_directory is None:
        try:
            root, registration = read_current(lab, target)
            wps_evidence = [Path(item["path"]) for item in registration["wps_evidence"]]
        except (RuntimeError, OSError, ValueError, TypeError, KeyError) as exc:
            return {"scope": "native-host-bound-partial-diagnostics", "run_directory": None, "status": "blocked",
                    "runtime_environment_passed": False, "full_lifecycle_passed": False, "accepted_case_count": 0,
                    "observed_passed_stage_count": 0, "stages": {},
                    "binding_errors": ["NATIVE_CURRENT_RUN_REGISTRATION_REQUIRED_OR_INVALID:" + str(exc)], "remaining_blockers": list(REMAINING)}
    else:
        root = safe_child(report_root, run_directory)
    checked_id(root.name)
    require(root.parent.resolve() == report_root.resolve(), "NATIVE_DIAGNOSTIC_RUN_PATH_INVALID")
    stages = {name: {"label": label, "status": "not_run", "evidence_path": str(root / filename)}
              for name, (label, filename) in STAGES.items()}
    for path in wps_evidence:
        path = safe_child(root / "business", path)
        require(path.name == "evidence.json" and re.fullmatch(r"formatter-golden-[a-f0-9]+", path.parent.name)
                and path.parent.parent == (root / "business").resolve(), "NATIVE_DIAGNOSTIC_WPS_PATH_INVALID")
        stages["wps:" + path.parent.name] = {"label": "真实 WPS 公文与金样尝试", "status": "not_run", "evidence_path": str(path)}
    result = {"scope": "native-host-bound-partial-diagnostics", "run_directory": str(root),
              "status": "blocked", "runtime_environment_passed": False, "full_lifecycle_passed": False,
              "accepted_case_count": 0, "observed_passed_stage_count": 0, "stages": stages, "binding_errors": [],
              "remaining_blockers": list(REMAINING)}
    if not root.is_dir():
        result["binding_errors"].append("NATIVE_DIAGNOSTIC_RUN_MISSING")
        return result
    try:
        require(target == "win11-x64-native", "NATIVE_DIAGNOSTIC_TARGET_MISMATCH")
        candidate, _ = read_record(root, root / "candidate.json")
        require(re.fullmatch(r"[0-9a-f]{64}", str(candidate.get("source_fingerprint", ""))),
                "NATIVE_DIAGNOSTIC_RECORDED_SOURCE_INVALID")
        result["recorded_package"] = {key: candidate.get(key) for key in ("id", "version", "sha256", "source_fingerprint")}
        result["current_source_fingerprint"] = source
        if candidate["source_fingerprint"] != source:
            result["status"] = "stale_source"
            result["binding_errors"].append("NATIVE_DIAGNOSTIC_STALE_SOURCE_FINGERPRINT")
            for stage in stages.values():
                if Path(stage["evidence_path"]).is_file():
                    stage["status"] = "stale_source"
            return result
        current_binding = None
        if root.name != HISTORICAL_RUN or (root / "install-binding.json").is_file():
            current_binding = verify_install_binding(lab, root, source)
            require(current_binding["package"] == package, "NATIVE_DIAGNOSTIC_INSTALL_BINDING_PACKAGE_MISMATCH")
        system = system or read_host()
        validate_identity(system, lab.matrix["targets"][target])
        require(package.get("provenance_status") == "verified" and package.get("source_fingerprint") == source,
                "NATIVE_DIAGNOSTIC_CURRENT_PACKAGE_UNBOUND")
        require(package.get("id") == "windows_amd64", "NATIVE_DIAGNOSTIC_PACKAGE_PLATFORM_MISMATCH")
        install, install_hash = read_record(root, root / "install-result.json")
        start, _ = read_record(root, root / "install-start.json")
        if current_binding is not None:
            digest = sha256(root / "install-binding.json")
            payload = current_binding["windows_payload"]
            require(install.get("install_binding_sha256") == digest and start.get("install_binding_sha256") == digest
                    and install.get("expected_payload") == payload
                    and install.get("installed_executable_sha256") == payload["executables"]["PartyOps.exe"]["sha256"]
                    and install.get("installed_manifest_sha256") == payload["manifest"]["sha256"],
                    "NATIVE_DIAGNOSTIC_EXPECTED_PAYLOAD_MISMATCH")
        for record in (candidate, install.get("package", {}), start.get("package", {})):
            for field in ("id", "version", "sha256", "source_fingerprint", "build_receipt_sha256"):
                require(record.get(field) == package.get(field) and record.get(field),
                        "NATIVE_DIAGNOSTIC_PACKAGE_BINDING_MISMATCH:" + field)
        require(install.get("target") == target and start.get("target") == target
                and install.get("environment_type") == "native-host" and start.get("environment_type") == "native-host",
                "NATIVE_DIAGNOSTIC_INSTALL_TARGET_MISMATCH")
        require(start.get("boot_id") == system.get("boot_id"), "NATIVE_DIAGNOSTIC_BOOT_CHANGED")
        require(install.get("scope") in {"same-version-current-candidate-replacement", "current-candidate-replacement"}
                and start.get("scope") == install["scope"] and install.get("runtime_environment_passed") is False,
                "NATIVE_DIAGNOSTIC_INSTALL_SCOPE_MISMATCH")
        require(same_path(install.get("install_dir"), INSTALL_DIRECTORY)
                and same_path(install.get("registered_path"), INSTALL_DIRECTORY)
                and install.get("registered_version") == package.get("version"), "NATIVE_DIAGNOSTIC_INSTALL_REGISTRATION_MISMATCH")
        executable = safe_child(INSTALL_DIRECTORY, INSTALL_DIRECTORY / "PartyOps.exe")
        require(sha256(executable) == install.get("installed_executable_sha256"), "NATIVE_DIAGNOSTIC_INSTALLED_EXECUTABLE_CHANGED")
        account = read_account()
        require(account.get("username") == "PartyOpsNativeQA" and account.get("enabled") is True
                and account.get("is_admin") is False and account.get("is_user") is True
                and re.fullmatch(r"S-1-5-21-(?:\d+-){3}\d+", str(account.get("sid", ""))),
                "NATIVE_DIAGNOSTIC_CURRENT_STANDARD_ACCOUNT_UNPROVEN")
        workspace = Path(lab.matrix["defaults"]["fallback_root"]) / "native/win11-x64" / root.name / "PartyOpsNativeQA"
        session = None
        if (root / "native-user-session.json").is_file():
            require(current_binding is not None, "NATIVE_DIAGNOSTIC_SESSION_REQUIRES_CURRENT_INSTALL_BINDING")
            session = verify_session(lab, root, current_binding, install, account)
            result["standard_user_session"] = session

        def receipt(filename):
            value, checksum = read_record(root, root / filename)
            require(value.get("status") == "passed" and value.get("runtime_environment_passed") is False
                    and value.get("username") == account["username"] and value.get("sid") == account["sid"]
                    and value.get("is_admin") is False and type(value.get("session_id")) is int and value["session_id"] > 0,
                    "NATIVE_DIAGNOSTIC_RECEIPT_ACCOUNT_MISMATCH")
            require(str(value.get("hardware_uuid", "")).casefold() == str(account.get("hardware_uuid", "")).casefold()
                    and str(value.get("machine_name", "")).casefold() == str(account.get("machine_name", "")).casefold(),
                    "NATIVE_DIAGNOSTIC_RECEIPT_HOST_MISMATCH")
            for key, expected in (("user_profile", account["user_profile"]), ("local_appdata", account["local_appdata"]),
                                  ("run_directory", root), ("native_workspace", workspace)):
                require(same_path(value.get(key), expected), "NATIVE_DIAGNOSTIC_RECEIPT_PATH_MISMATCH:" + key)
            require(value.get("app_sha256") == install["installed_executable_sha256"]
                    and value.get("install_result_sha256") == install_hash, "NATIVE_DIAGNOSTIC_RECEIPT_INSTALL_MISMATCH")
            data = value.get("data_directory", "")
            if session is not None:
                require(value.get("native_user_session_sha256") == session["sha256"] and value.get("data_mode") == session["data_mode"]
                        and same_path(data, session["data_directory"]), "NATIVE_DIAGNOSTIC_RECEIPT_SESSION_MISMATCH")
            else:
                require(not value.get("native_user_session_sha256") and same_path(ntpath.dirname(data), workspace)
                        and re.fullmatch(r"中文 空格业务数据-[0-9a-f]{32}", ntpath.basename(data)), "NATIVE_DIAGNOSTIC_DATA_DIRECTORY_MISMATCH")
            directory = safe_child(root, Path(value.get("report_directory", "")))
            require(directory.parent == root.resolve() and re.fullmatch(r"standard-user-probe-[0-9a-f]{32}", directory.name),
                    "NATIVE_DIAGNOSTIC_CHILD_REPORT_PATH_INVALID")
            child, child_hash = read_record(root, directory / "result.json")
            if session is not None:
                require(child.get("native_user_session_sha256") == session["sha256"] and child.get("data_mode") == session["data_mode"],
                        "NATIVE_DIAGNOSTIC_CHILD_SESSION_MISMATCH")
            require(child_hash == value.get("result_sha256"), "NATIVE_DIAGNOSTIC_CHILD_REPORT_CHANGED")
            require(child.get("status") == "passed" and child.get("target") == target
                    and child.get("environment_type") == "native-host" and child.get("runtime_environment_passed") is False
                    and child.get("sid") == account["sid"] and child.get("is_admin") is False
                    and child.get("session_id") == value["session_id"] and child.get("app_sha256") == value["app_sha256"]
                    and same_path(child.get("run_directory"), root), "NATIVE_DIAGNOSTIC_CHILD_IDENTITY_MISMATCH")
            if filename != "standard-user-runtime.json":
                expected_executable = INSTALL_DIRECTORY / ("PartyOpsWizard.exe" if filename == "standard-user-wizard.json" else "PartyOps.exe")
                process = child.get("process", {})
                require(process.get("owner_sid") == account["sid"] and process.get("session_id") == value["session_id"]
                        and type(process.get("pid")) is int and process["pid"] > 0
                        and same_path(process.get("executable_path"), expected_executable), "NATIVE_DIAGNOSTIC_CHILD_PROCESS_MISMATCH")
            return value, checksum, child

        ordinary, ordinary_hash, child = receipt("standard-user-latest.json")
        require(child.get("exit_code") == 0 and ordinary.get("secondary_logon_exit_code") == 0
                and child.get("selftest", {}).get("passed") is True and child["selftest"].get("desktop_user") is True
                and child["selftest"].get("version") == package["version"], "NATIVE_DIAGNOSTIC_STARTUP_RESULT_INVALID")
        result["binding"] = {"host_id": system["host_id"], "identity_sha256": identity_digest(system),
                             "boot_id": system["boot_id"], "package_sha256": package["sha256"], "source_fingerprint": source,
                             "executable_sha256": install["installed_executable_sha256"], "account_sid": account["sid"]}
        if registration is not None:
            require(registration.get("binding") == result["binding"], "NATIVE_CURRENT_HOST_SOURCE_PACKAGE_BINDING_CHANGED")
        stages["replacement-install"].update(status="passed" if install.get("installer_exit_code") == 0 else "failed",
                                               sha256=install_hash, recorded_scope=install.get("scope"))
        stages["standard-user-startup"].update(status="passed", sha256=ordinary_hash)
        result["status"] = "partial"
    except (RuntimeError, OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError) as exc:
        result["binding_errors"].append(str(exc))
        for stage in stages.values():
            if Path(stage["evidence_path"]).is_file():
                stage["status"] = "rejected"
        return result

    try:
        permission, checksum, permission_child = receipt("standard-user-personal-permission.json")
        require(permission.get("data_directory") == ordinary["data_directory"]
                and permission_child.get("exit_code") == 0 and permission.get("secondary_logon_exit_code") == 0
                and permission_child.get("selftest", {}).get("passed") is True
                and permission_child["selftest"].get("data_dir_writable") is True,
                "NATIVE_DIAGNOSTIC_PERSONAL_PERMISSION_UNPROVEN")
        stages["personal-permission"].update(status="passed", sha256=checksum)
    except (RuntimeError, OSError, ValueError, TypeError, KeyError) as exc:
        stages["personal-permission"].update(status="rejected", error=str(exc))

    wizard_hash = runtime_hash = None
    runtime = None
    try:
        if session is not None and session["data_mode"] == "Retain":
            filename = "standard-user-runtime.json"
            stages["retained-personal-runtime"] = {"label": "保留配置的普通用户真实进程启动", "status": "not_run", "evidence_path": str(root / filename)}
            launched, runtime_hash, launched_child = receipt(filename)
            require(same_path(launched.get("data_directory"), ordinary["data_directory"]), "NATIVE_DIAGNOSTIC_RUNTIME_DATA_MISMATCH")
            runtime = validate_runtime(launched, launched_child, account, install)
            stages["retained-personal-runtime"].update(status="passed", sha256=runtime_hash,
                limitations=["只证明新安装版真实进程启动；不追认旧首次配置或 GUI 操作。", "profile holder 仍运行时退出码为 null，不当作退出成功。"])
        else:
            wizard, candidate_wizard_hash, _ = receipt("standard-user-wizard.json")
            require(wizard.get("data_directory") == ordinary["data_directory"], "NATIVE_DIAGNOSTIC_WIZARD_DATA_MISMATCH")
            require(wizard.get("wizard", {}).get("sha256") == sha256(safe_child(INSTALL_DIRECTORY, INSTALL_DIRECTORY / "PartyOpsWizard.exe")),
                    "NATIVE_DIAGNOSTIC_WIZARD_EXECUTABLE_CHANGED")
            wizard_hash = candidate_wizard_hash
    except (RuntimeError, OSError, ValueError, TypeError, KeyError) as exc:
        result["binding_errors"].append(str(exc))
        if "retained-personal-runtime" in stages:
            stages["retained-personal-runtime"].update(status="rejected", error=str(exc))
    expected_context = {**result["binding"], "target": target, "data_dir": ordinary["data_directory"],
                        "identity_receipt_sha256": ordinary_hash, "wizard_receipt_sha256": wizard_hash}
    if session is not None and session["data_mode"] == "Retain":
        expected_context.update(native_user_session_sha256=session["sha256"], runtime_receipt_sha256=runtime_hash,
                                port=runtime["port"] if runtime else None)
    for phase, (_, filename) in STAGES.items():
        if not filename.startswith("business/"):
            continue
        try:
            record, checksum = read_record(root, root / filename)
            require((wizard_hash is not None or runtime is not None) and record.get("scope") == "native-standard-user-installed-business"
                    and record.get("environment_type") == "native-host" and record.get("phase") == phase
                    and record.get("runtime_environment_passed") is False, "NATIVE_DIAGNOSTIC_BUSINESS_SCOPE_MISMATCH")
            for field, expected in expected_context.items():
                require(record.get("context", {}).get(field) == expected, "NATIVE_DIAGNOSTIC_BUSINESS_BINDING_MISMATCH:" + field)
            require(not (session is not None and session["data_mode"] == "Retain" and phase == "resume-configure"),
                    "NATIVE_DIAGNOSTIC_RETAIN_IS_NOT_FIRST_CONFIGURATION")
            checks = record.get("checks", [])
            require(isinstance(checks, list) and all(isinstance(check, dict) and isinstance(check.get("id"), str) for check in checks),
                    "NATIVE_DIAGNOSTIC_CHECKS_INVALID")
            status = record.get("status")
            require(status in {"passed", "failed"}, "NATIVE_DIAGNOSTIC_STAGE_STATUS_INVALID")
            if status == "passed":
                require(checks and all(check.get("status") == "passed" for check in checks), "NATIVE_DIAGNOSTIC_PASSED_WITHOUT_CHECKS")
            if phase == "revalidate":
                require(session is not None and session["data_mode"] == "Retain", "NATIVE_DIAGNOSTIC_REVALIDATION_RETAIN_SESSION_REQUIRED")
                items = [item for item in checks if item["id"] == "retained-account-task-attachment-on-new-installed-runtime"]
                require(len(items) == 1 and items[0].get("result", {}).get("previous_evidence_reaccepted") is False,
                        "NATIVE_DIAGNOSTIC_OLD_EVIDENCE_REACCEPTED")
            if phase == "models" and status == "passed":
                required = {"signed-model-embedding", "actual-semantic-search", "signed-model-llm", "real-local-llm-inference"}
                require(required.issubset(item["id"] for item in checks), "NATIVE_DIAGNOSTIC_MODEL_CHECKS_INCOMPLETE")
            stages[phase].update(status=status, sha256=checksum, checks=[{"id": check["id"], "status": check.get("status")} for check in checks],
                                 limitations=record.get("limitations", []))
            if phase == "models":
                stages[phase].update(coverage="vector-search-and-local-llm-only", offline_isolation_verified=False,
                    needle_verified=False, complete_models_case_passed=False)
                stages[phase]["limitations"] = list(stages[phase]["limitations"]) + ["仅向量检索和本地 LLM 实测；未验证断网隔离。", "Needle/intent_router 尚未验证。"]
                result["remaining_blockers"].extend(["MODEL_NETWORK_ISOLATION_NOT_VERIFIED", "NEEDLE_INTENT_ROUTER_NOT_VERIFIED"])
            if record.get("error"):
                stages[phase]["error"] = record["error"]
                result["remaining_blockers"].append(record["error"])
        except FileNotFoundError:
            pass
        except (RuntimeError, OSError, ValueError, TypeError, KeyError) as exc:
            stages[phase].update(status="rejected", error=str(exc))
    for key, stage in stages.items():
        if not key.startswith("wps:"):
            continue
        try:
            evidence_path = Path(stage["evidence_path"])
            record, checksum = read_record(root, evidence_path)
            require((wizard_hash is not None or runtime is not None) and record.get("scope") == "native-standard-user-installed-wps-golden"
                    and record.get("runtime_environment_passed") is False, "NATIVE_DIAGNOSTIC_WPS_SCOPE_MISMATCH")
            for field, expected in expected_context.items():
                require(record.get("context", {}).get(field) == expected, "NATIVE_DIAGNOSTIC_WPS_BINDING_MISMATCH:" + field)
            require(record.get("status") in {"failed", "passed", "partial"}, "NATIVE_DIAGNOSTIC_WPS_STATUS_INVALID")
            controls = []
            codes = set()
            for job in record.get("jobs", []):
                for item in job.get("host_controls", []):
                    path = safe_child(evidence_path.parent, evidence_path.parent / item["evidence"])
                    _, digest = read_record(root, path, allow_list=True)
                    require(digest == item["sha256"], "NATIVE_DIAGNOSTIC_WPS_CONTROL_CHANGED")
                    controls.append({"path": str(path), "sha256": digest})
                for snapshot in job.get("snapshots", []):
                    for item in snapshot.get("response", {}).get("items", []):
                        if item.get("error_code"):
                            codes.add(item["error_code"])
            # 本摘要仅报告实际尝试及故障；金样全部通过另有正式门禁，不在此追认。
            stage.update(status="failed" if record["status"] == "failed" else "partial", sha256=checksum,
                         recorded_status=record["status"], error=record.get("error"), error_codes=sorted(codes),
                         controls=controls, outputs_count=len(record.get("outputs", [])), complete_wps_case_passed=False)
            if record.get("error"):
                result["remaining_blockers"].append(record["error"])
            result["remaining_blockers"].extend(sorted(codes))
        except (RuntimeError, OSError, ValueError, TypeError, KeyError) as exc:
            stage.update(status="rejected", error=str(exc))
    result["remaining_blockers"] = list(dict.fromkeys(result["remaining_blockers"]))
    result["observed_passed_stage_count"] = sum(stage["status"] == "passed" for stage in stages.values())
    return result
