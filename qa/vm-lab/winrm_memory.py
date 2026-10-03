"""Win7 实际安装前的专用传输准备；固定 150→512 MiB，不更改产品或系统补丁。"""
from __future__ import annotations

import copy
import json
import uuid
import xml.etree.ElementTree as ET
from pathlib import Path

import requests
import xmltodict
from evidence import now, safe_child, sha256, write_json
from identity import probe
from windows_remote import WinRMFiles, session, transport_error
from winrm.exceptions import WinRMError

URI = "http://schemas.microsoft.com/wbem/wsman/1/config/winrs"
SOAP = "http://www.w3.org/2003/05/soap-envelope"
TRANSFER = "http://schemas.xmlsoap.org/ws/2004/09/transfer/"
HERE = Path(__file__).resolve().parent


def request(protocol, action: str, content=None):
    envelope = ET.fromstring(xmltodict.unparse({"env:Envelope": protocol.build_wsman_header(
        resource_uri=URI, action=TRANSFER + action)}))
    body = ET.SubElement(envelope, "{" + SOAP + "}Body")
    if content is not None:
        body.append(copy.deepcopy(content))
    try:
        response = protocol.send_message(ET.tostring(envelope, encoding="unicode"))
    except (WinRMError, requests.RequestException, OSError, ValueError) as exc:
        raise transport_error(exc, "winrs-" + action.lower()) from None
    root = ET.fromstring(response)
    result = root.find("{" + SOAP + "}Body/{" + URI + "}Winrs")
    if result is None:
        raise RuntimeError("WINRS_CONFIGURATION_RESPONSE_INVALID")
    return result


def values(node):
    return {child.tag: (child.text or "").strip() for child in node}


def configure(protocol, desired: int | None = None, expected: int = 150, *, before_write=None):
    """只改一个配额属性；相同值只读，实际写入前先登记原值与意图。"""
    original = request(protocol, "Get")
    field = original.find("{" + URI + "}MaxMemoryPerShellMB")
    if field is None or not (field.text or "").isdigit():
        raise RuntimeError("WINRS_MEMORY_VALUE_INVALID")
    before = int(field.text)
    if desired is None:
        return {"previous_mib": before, "current_mib": before, "changed": False}
    if desired not in (150, 512):
        raise ValueError("WINRS_MEMORY_CHANGE_OUTSIDE_LAB_BOUNDARY")
    if before not in (expected, desired):
        raise RuntimeError("WINRS_MEMORY_BASELINE_CHANGED")
    if before == desired:
        return {"previous_mib": before, "current_mib": before, "changed": False,
                "other_configuration_unchanged": True, "new_shell_required": False}
    proposed = copy.deepcopy(original)
    proposed.find("{" + URI + "}MaxMemoryPerShellMB").text = str(desired)
    if before_write:
        before_write({"previous_mib": before, "desired_mib": desired,
                      "previous_configuration": values(original)})
    # WinRM 2 的 MaxShellRunTime 等属性只读；Put 只发送获授权的单个属性。
    partial = ET.Element(original.tag)
    partial.append(copy.deepcopy(proposed.find("{" + URI + "}MaxMemoryPerShellMB")))
    request(protocol, "Put", partial)
    actual = request(protocol, "Get")
    if values(actual) != values(proposed):
        raise RuntimeError("WINRS_CONFIGURATION_READBACK_MISMATCH")
    return {"previous_mib": before, "current_mib": desired, "changed": True,
            "other_configuration_unchanged": True, "new_shell_required": True}


ACCOUNT_AND_PROCESS_PROBE = r'''
Add-Type -AssemblyName System.Web.Extensions
$json=New-Object Web.Script.Serialization.JavaScriptSerializer
$current=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($current)
$account=@(Get-WmiObject Win32_UserAccount -Filter "LocalAccount=True AND Name='partyopsqa'")
$installers=@(Get-WmiObject Win32_Process | Where-Object {
  $_.Name -match '^(PartyOps_.*\.exe|ndp48.*\.exe|setup.*\.exe|msiexec\.exe|wusa\.exe|full-layout.*\.exe|sentinel.*\.exe|offline.*\.exe)$' -or
  $_.ExecutablePath -like '*\Temp\is-*'
} | ForEach-Object {@{pid=[int]$_.ProcessId;name=[string]$_.Name;path=[string]$_.ExecutablePath}})
$os=Get-WmiObject Win32_OperatingSystem
[Console]::WriteLine($json.Serialize(@{
  hardware_uuid=[string](Get-WmiObject Win32_ComputerSystemProduct).UUID;
  boot_id=[string]$os.LastBootUpTime;os_version=[string]$os.Version;
  token_name=[string]$current.Name;token_sid=[string]$current.User.Value;
  administrator=[bool]$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator);
  local_accounts=@($account | ForEach-Object {@{name=[string]$_.Name;sid=[string]$_.SID;disabled=[bool]$_.Disabled}});
  active_installers=$installers
}))
'''


def verify_account(observation: dict, system: dict, state: dict, credential: dict):
    """认证账号来自登记文件，并与 Guest 本地账号 SID/实际 token 对齐。"""
    if credential.get("uuid") != state["uuid"] or credential.get("username") != "partyopsqa":
        raise RuntimeError("WINRM_PREPARATION_REGISTERED_ACCOUNT_MISMATCH")
    if (str(observation.get("hardware_uuid", "")).casefold() != state["uuid"].casefold()
            or observation.get("boot_id") != system["boot_id"]
            or observation.get("os_version") != system["os_version"]):
        raise RuntimeError("WINRM_PREPARATION_GUEST_CHANGED")
    accounts = observation.get("local_accounts")
    if not isinstance(accounts, list) or len(accounts) != 1:
        raise RuntimeError("WINRM_PREPARATION_LOCAL_ACCOUNT_NOT_PROVEN")
    account = accounts[0]
    if (account.get("name") != "partyopsqa" or account.get("disabled") is not False
            or not str(account.get("sid", "")).startswith("S-1-5-21-")
            or account["sid"] != observation.get("token_sid")
            or str(observation.get("token_name", "")).split("\\")[-1].casefold() != "partyopsqa"
            or observation.get("administrator") is not True):
        raise RuntimeError("WINRM_PREPARATION_ACCOUNT_TOKEN_MISMATCH")
    if observation.get("active_installers") != []:
        raise RuntimeError("WINRM_PREPARATION_INSTALLER_ACTIVE_OR_UNKNOWN")


def inspect_win7_context(lab, target, state):
    """配置入口和实际安装入口共用的原始系统、登记账号与空闲状态核验。"""
    system = probe(lab, target)
    if system.get("os_release") != "7 SP1" or system.get("os_version") != "6.1.7601":
        raise RuntimeError("WINRM_PREPARATION_REQUIRES_ORIGINAL_WIN7")
    credential = json.loads((lab.vm_dir(target) / "guest-credential.local.json").read_text(encoding="utf-8"))
    observation = json.loads(WinRMFiles(lab, target).powershell(ACCOUNT_AND_PROCESS_PROBE, timeout=90))
    verify_account(observation, system, state, credential)
    return system, observation


def prepare_win7_transport(lab, target: str, destination: Path):
    """每次执行都重新检查；恢复干净基线后可重做准备，不复用旧成功结论。"""
    spec = lab.matrix["targets"][target]
    if target not in ("win7-x64", "win7-x86") or spec.get("os") != "windows" or spec.get("os_release") != "7 SP1" or not spec.get("winrm_port"):
        raise RuntimeError("WINRM_PREPARATION_REQUIRES_ORIGINAL_WIN7")
    state = lab.state(target)
    if not lab.live(state):
        raise RuntimeError("WINRM_PREPARATION_GUEST_STOPPED")
    destination = safe_child(lab.root / "reports" / target, Path(destination))
    directory = destination / ("winrm-transport-" + uuid.uuid4().hex[:12])
    directory.mkdir(parents=True, exist_ok=False)
    report = {"schema_version": 1, "generated_at": now(), "target": target, "uuid": state["uuid"],
              "restore_generation": state.get("restore_generation"), "scope": "dedicated-lab-transport-preparation",
              "runtime_environment_passed": False, "status": "checking"}
    report_path = directory / "preparation.json"
    write_json(report_path, report)
    try:
        system, observation = inspect_win7_context(lab, target, state)
        files = WinRMFiles(lab, target)
        # 只保留非秘密身份字段，不把凭据或凭据文件内容/摘要写入报告。
        report.update(guest_identity=system, account_and_processes=observation)
        write_json(report_path, report)
        client = session(lab, target, 60)

        def intent(value):
            write_json(directory / "change-intent.json", {
                "generated_at": now(), "uuid": state["uuid"], "restore_generation": state.get("restore_generation"),
                "scope": report["scope"], **value})

        report["configuration"] = configure(client.protocol, desired=512, expected=150, before_write=intent)
        write_json(report_path, report)
        job_source = HERE / "guest/winrm-job-memory-diagnostic.ps1"
        job = json.loads(files.powershell(job_source.read_text(encoding="utf-8"), timeout=90))
        report["new_shell_job"] = job
        report["job_probe_sha256"] = sha256(job_source)
        if (job.get("query_succeeded") is not True or job.get("job_memory_limit_enabled") is not True
                or job.get("process_memory_limit_enabled") is not True
                or job.get("job_memory_limit_bytes") != 512 * 1024**2
                or job.get("process_memory_limit_bytes") != 512 * 1024**2):
            raise RuntimeError("WINRM_PREPARATION_NEW_SHELL_QUOTA_NOT_PROVEN")
        report.update(status="prepared" if report["configuration"]["changed"] else "checked",
                      transport_memory_preflight_passed=True, completed_at=now())
        write_json(report_path, report)
        return {"report_path": str(directory), "status": report["status"], "runtime_environment_passed": False,
                "transport_memory_preflight_passed": True, "preparation_sha256": sha256(report_path)}
    except (RuntimeError, OSError, ValueError, KeyError) as exc:
        report.update(status="blocked", reason=str(exc), transport_memory_preflight_passed=False, completed_at=now())
        write_json(report_path, report)
        raise
