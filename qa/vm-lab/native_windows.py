"""Windows 本机只读身份与准备诊断；不借用可丢弃 Guest 生命周期。"""
from __future__ import annotations

import base64
import hashlib
import json
import os
import re
import subprocess

from evidence import now, write_json

# 不采集凭据或业务内容，不更改服务、账户、注册表、安装和启动状态。
HOST_PROBE = r'''$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$system=Get-CimInstance Win32_OperatingSystem
$hardware=Get-CimInstance Win32_ComputerSystemProduct
$version=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$machine=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Cryptography'
$user=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($user)
$arch=if($env:PROCESSOR_ARCHITEW6432){$env:PROCESSOR_ARCHITEW6432}else{$env:PROCESSOR_ARCHITECTURE}
$result=@{
  os='windows'; architecture=$arch; caption=$system.Caption; version=$system.Version
  product_type=[int]$system.ProductType; build=[string]$system.BuildNumber
  update_build_revision=[string]$version.UBR; display_version=[string]$version.DisplayVersion
  edition=[string]$version.EditionID; machine_guid=[string]$machine.MachineGuid
  hardware_uuid=[string]$hardware.UUID; boot_id=$system.LastBootUpTime.ToUniversalTime().ToString('o')
  user_sid=$user.User.Value; is_admin=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
[Console]::WriteLine(($result|ConvertTo-Json -Compress))
'''

IDENTITY_FIELDS = ("os", "arch", "os_release", "os_version", "os_build", "display_version", "edition", "host_id")
VM_FIELDS = {"vm_uuid", "media_sha256", "baseline_id", "marker"}
BOOT_PATTERN = r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z"


def digest(value: dict) -> str:
    return hashlib.sha256(json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode()).hexdigest()


def identity_digest(system: dict) -> str:
    """启动时间、当前账户不属于稳定系统身份；系统更新会使旧身份失效。"""
    if any(not isinstance(system.get(field), str) or not system[field] for field in IDENTITY_FIELDS):
        raise RuntimeError("NATIVE_HOST_IDENTITY_INCOMPLETE")
    return digest({field: system[field] for field in IDENTITY_FIELDS})


def normalize(raw: dict) -> dict:
    """使用本机 Windows 客户端版本及真实机器标识，不读取历史 VM 登记。"""
    if not isinstance(raw, dict):
        raise TypeError("NATIVE_HOST_PROBE_NOT_OBJECT")
    if (raw.get("os") != "windows" or raw.get("product_type") != 1
            or not re.fullmatch(r"10\.0\.\d+", str(raw.get("version", "")))
            or not str(raw.get("build", "")).isdigit()
            or not str(raw.get("update_build_revision", "")).isdigit()):
        raise RuntimeError("NATIVE_WINDOWS_CLIENT_VERSION_UNPROVEN")
    build = int(raw["build"])
    if raw["version"].split(".")[-1] != str(build):
        raise RuntimeError("NATIVE_WINDOWS_BUILD_MISMATCH")
    identifiers = {field: str(raw.get(field, "")).strip().casefold() for field in ("machine_guid", "hardware_uuid")}
    for value in identifiers.values():
        if (not re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}", value)
                or len(set(value.replace("-", ""))) == 1):
            raise RuntimeError("NATIVE_MACHINE_IDENTITY_MISSING")
    system = {
        "os": "windows", "arch": {"amd64": "x86_64", "arm64": "arm64", "x86": "i686"}.get(str(raw.get("architecture", "")).casefold(), ""),
        "os_release": "11" if build >= 22000 else "10", "os_version": raw["version"],
        "os_build": f"{build}.{raw['update_build_revision']}",
        "display_version": str(raw.get("display_version", "")), "edition": str(raw.get("edition", "")),
        "host_id": digest(identifiers), "boot_id": str(raw.get("boot_id", "")),
        "user_sid": str(raw.get("user_sid", "")), "is_admin": raw.get("is_admin"),
        "environment_type": "native-host", "generated_at": now(),
    }
    # 原始 MachineGuid / 硬件 UUID 仅参与哈希；报告不额外披露设备标识。
    identity_digest(system)
    if not re.fullmatch(BOOT_PATTERN, system["boot_id"]):
        raise RuntimeError("NATIVE_BOOT_ID_MISSING")
    if not re.fullmatch(r"S-1-\d+(?:-\d+)+", system["user_sid"]) or not isinstance(system["is_admin"], bool):
        raise RuntimeError("NATIVE_CURRENT_USER_IDENTITY_MISSING")
    return system


def read_host() -> dict:
    if os.name != "nt":
        raise RuntimeError("NATIVE_WINDOWS_HOST_REQUIRED")
    encoded = base64.b64encode(HOST_PROBE.encode("utf-16le")).decode()
    try:
        process = subprocess.run(
            ["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            capture_output=True, text=True, encoding="utf-8-sig", errors="strict", timeout=45, check=False,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
    except (OSError, subprocess.TimeoutExpired, UnicodeError) as exc:
        raise RuntimeError("NATIVE_HOST_PROBE_FAILED:" + type(exc).__name__) from None
    if process.returncode:
        raise RuntimeError("NATIVE_HOST_PROBE_EXIT:" + str(process.returncode))
    try:
        return normalize(json.loads(process.stdout))
    except (ValueError, TypeError, KeyError) as exc:
        raise RuntimeError("NATIVE_HOST_PROBE_INVALID:" + type(exc).__name__) from None


def validate_identity(system: dict, spec: dict) -> None:
    if spec.get("backend") != "native-host" or system.get("environment_type") != "native-host":
        raise RuntimeError("NATIVE_HOST_BACKEND_MISMATCH")
    if VM_FIELDS.intersection(system):
        raise RuntimeError("NATIVE_HOST_VM_IDENTITY_NOT_ALLOWED")
    for field in ("os", "arch", "os_release", "os_build", "edition"):
        if spec.get(field) and system.get(field) != spec[field]:
            raise RuntimeError("NATIVE_HOST_IDENTITY_MISMATCH:" + field)
    if not re.fullmatch(BOOT_PATTERN, str(system.get("boot_id", ""))):
        raise RuntimeError("NATIVE_BOOT_ID_MISSING")
    identity_digest(system)


def observed_boots(lab, target: str) -> list[dict]:
    path = lab.root / "reports" / target / "native-observed-boots.json"
    if not path.is_file():
        return []
    observations = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(observations, list) or any(
        not isinstance(item, dict)
        or not re.fullmatch(BOOT_PATTERN, str(item.get("boot_id", "")))
        or any(not re.fullmatch(r"[a-f0-9]{64}", str(item.get(key, ""))) for key in ("host_id", "identity_sha256"))
        or not item.get("observed_at")
        for item in observations
    ):
        raise RuntimeError("NATIVE_BOOT_OBSERVATIONS_INVALID")
    return observations


def probe(lab, target: str) -> dict:
    system = read_host()
    validate_identity(system, lab.matrix["targets"][target])
    observations = observed_boots(lab, target)
    key = {"boot_id": system["boot_id"], "host_id": system["host_id"], "identity_sha256": identity_digest(system)}
    if not any(all(item[field] == value for field, value in key.items()) for item in observations):
        observations.append({**key, "observed_at": now()})
        write_json(lab.root / "reports" / target / "native-observed-boots.json", observations)
    write_json(lab.root / "reports" / target / "native-identity.json", system)
    return system


def runtime_binding(lab, target: str) -> dict:
    system = probe(lab, target)
    return {"backend": "native-host", "host_id": system["host_id"],
            "identity_sha256": identity_digest(system), "current_boot_id": system["boot_id"],
            "observed_boot_ids": [item["boot_id"] for item in observed_boots(lab, target)
                                  if item["host_id"] == system["host_id"] and item["identity_sha256"] == identity_digest(system)]}


def readiness(lab, target: str) -> dict:
    return {"scope": "native-host-readiness-only", "system": probe(lab, target),
            "status": "blocked", "runtime_environment_passed": False,
            "lifecycle_cases": {case: "not_run" for case in lab.matrix["required_cases"]},
            "reason": "NATIVE_WINDOWS_LIFECYCLE_EXECUTION_NOT_IMPLEMENTED",
            "required_preparation": ["保护既有安装和数据，登记独立测试路径", "登记专用普通用户并验证实际桌面首启", "逐场景执行及收集绑定当前包的真实证据"],
            "host_changes": []}
