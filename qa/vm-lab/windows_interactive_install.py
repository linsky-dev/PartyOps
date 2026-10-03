"""在已登记 Win7 管理员的真实桌面运行安装器，远程通道只负责控制和取证。"""
from __future__ import annotations

import base64
import json
import re
import time
from pathlib import Path

from evidence import now, sha256, write_json
from windows_remote import WinRMFiles

JSON_PS = "Add-Type -AssemblyName System.Web.Extensions\n$json=New-Object Web.Script.Serialization.JavaScriptSerializer\n"


def ps(value):
    return "'" + str(value).replace("'", "''") + "'"


def validate_desktop(value, expected_uuid):
    if (str(value.get("uuid", "")).lower() != expected_uuid.lower()
            or value.get("name", "").split("\\")[-1].lower() != "partyopsqa"
            or not re.fullmatch(r"S-1-5-21-(\d+-){3}\d+", value.get("sid", ""))
            or value.get("administrator") is not True
            or len(value.get("desktop_sessions", [])) != 1
            or value["desktop_sessions"][0] <= 0):
        raise RuntimeError("WIN7_INSTALL_REGISTERED_ADMIN_DESKTOP_REQUIRED")


def validate_task(value, sid):
    if (value.get("sid") != sid or value.get("logon_type") != 3 or value.get("run_level") != 1
            or value.get("action_matches") is not True):
        raise RuntimeError("WIN7_INSTALL_TASK_BINDING_CHANGED")


def stage_package(files, package, destination):
    """只复用 Guest 内现场重新计算哈希相同的包，避免失败诊断后重复传输。"""
    filename = Path(package["path"]).name
    if not re.fullmatch(r"PartyOps_[0-9A-Za-z.-]+_windows7_(amd64|x86)\.exe", filename):
        raise RuntimeError("WIN7_PACKAGE_FILENAME_INVALID")
    remote = "C:\\PartyOps-QA\\incoming\\" + filename
    actual = files.powershell("$path=" + ps(remote) + r"""
if([IO.File]::Exists($path)) {
 $f=[IO.File]::OpenRead($path);$h=[Security.Cryptography.SHA256]::Create()
 try {[Console]::WriteLine([BitConverter]::ToString($h.ComputeHash($f)).Replace('-','').ToLowerInvariant())}finally{$f.Dispose();$h.Clear()}
}else{[Console]::WriteLine('missing')}
""", timeout=180).strip()
    reused = actual == package["sha256"]
    if not reused:
        files.put(Path(package["path"]), remote, package["sha256"])
    write_json(destination / "package-transfer.json", {"generated_at": now(), "remote": remote,
               "sha256": package["sha256"], "reused_after_live_hash_check": reused})


def run_install(lab, target, destination: Path, package, script):
    state = lab.state(target)
    if target not in {"win7-x64", "win7-x86"} or not re.fullmatch(r"install-[a-f0-9]{12}", destination.name):
        raise RuntimeError("WIN7_INSTALL_CONTEXT_REQUIRED")
    files = WinRMFiles(lab, target)
    desktop = json.loads(files.powershell(JSON_PS + r"""
$id=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($id)
# PS2的Select-Object包装整数会使JavaScriptSerializer访问PSMethod；使用原生int数组。
$desktops=New-Object 'Collections.Generic.List[int]'
foreach($desktopProcess in @(Get-WmiObject Win32_Process -Filter "Name='explorer.exe'")) {
 $owner=$desktopProcess.GetOwnerSid();$session=[int]$desktopProcess.SessionId
 if($owner.Sid -eq $id.User.Value -and -not $desktops.Contains($session)){$desktops.Add($session)}
}
[Console]::WriteLine($json.Serialize(@{uuid=[string](Get-WmiObject Win32_ComputerSystemProduct).UUID;sid=[string]$id.User.Value;name=[string]$id.Name;administrator=[bool]$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator);desktop_sessions=$desktops.ToArray();boot_id=[string](Get-WmiObject Win32_OperatingSystem).LastBootUpTime}))
""", timeout=90))
    validate_desktop(desktop, state["uuid"])
    remote = "C:\\PartyOps-QA\\" + destination.name
    name = "PartyOps-QA-" + destination.name
    result_path = remote + r"\result.json"
    wrapper_path = remote + r"\installer.ps1"
    # 原安装脚本核验包哈希并持有安装器进程句柄；只将最后的输出改为原子结果文件。
    ending = "[Console]::WriteLine($json.Serialize($result))"
    if script.count(ending) != 1:
        raise RuntimeError("WIN7_INSTALL_SCRIPT_RESULT_CONTRACT_CHANGED")
    header = JSON_PS + "$ErrorActionPreference='Stop'\n$expectedSid=" + ps(desktop["sid"]) + ";$expectedBoot=" + ps(desktop["boot_id"]) + ";$expectedUuid=" + ps(state["uuid"]) + r"""
$id=[Security.Principal.WindowsIdentity]::GetCurrent()
$current=Get-WmiObject Win32_Process -Filter ('ProcessId='+$PID)
if($id.User.Value -ne $expectedSid -or $current.SessionId -le 0 -or (Get-WmiObject Win32_OperatingSystem).LastBootUpTime -ne $expectedBoot -or (Get-WmiObject Win32_ComputerSystemProduct).UUID -ne $expectedUuid){throw 'WIN7_INSTALL_INTERACTIVE_IDENTITY_CHANGED'}
$principal=New-Object Security.Principal.WindowsPrincipal($id)
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'WIN7_INSTALL_ADMIN_TOKEN_REQUIRED'}
$execution=@{pid=[int]$PID;parent_pid=[int]$current.ParentProcessId;session_id=[int]$current.SessionId;sid=[string]$id.User.Value;boot_id=$expectedBoot;created_at=[string]$current.CreationDate}
"""
    job_source = Path(__file__).resolve().parent / "guest/winrm-job-memory-diagnostic.ps1"
    header += job_source.read_text(encoding="utf-8").replace(
        "[Console]::WriteLine($serializer.Serialize([PartyOpsReadOnlyJobMemory]::Read()))",
        "$execution['job_memory']=[PartyOpsReadOnlyJobMemory]::Read()")
    script = script.replace("$processHandle=$process.Handle", r"""$processHandle=$process.Handle
$child=Get-WmiObject Win32_Process -Filter ('ProcessId='+$process.Id)
if(($child.GetOwnerSid()).Sid -ne $expectedSid -or $child.ParentProcessId -ne $PID -or $child.SessionId -ne $current.SessionId){throw 'WIN7_INSTALLER_CHILD_IDENTITY_CHANGED'}
$execution['installer_pid']=[int]$child.ProcessId;$execution['installer_created_at']=[string]$child.CreationDate
""")
    finish = "$result['interactive_execution']=$execution\n$result['transport']='winrm-control-interactive-token'\n[IO.File]::WriteAllText(" + ps(result_path + ".tmp") + ",$json.Serialize($result),(New-Object Text.UTF8Encoding($false)))\n[IO.File]::Move(" + ps(result_path + ".tmp") + "," + ps(result_path) + ")"
    wrapper = header + "\ntry {\n" + script.replace(ending, finish) + "\n} catch {\n[IO.File]::WriteAllText(" + ps(remote + r"\error.txt") + ",[string]$_.Exception.Message,(New-Object Text.UTF8Encoding($false)))\nexit 1\n}\n"
    local = destination / "interactive-installer.ps1"
    local.write_text(wrapper, encoding="utf-8")
    record = {"scope": "win7-interactive-administrator-install", "generated_at": now(), "status": "prepared",
              "desktop": desktop, "package_sha256": package["sha256"], "restore_generation": state.get("restore_generation"),
              "task_name": name, "script_sha256": sha256(local), "remote": remote, "runtime_environment_passed": False}
    receipt = destination / "interactive-task.json"
    write_json(receipt, record)
    files.powershell("if(Test-Path -LiteralPath " + ps(remote) + "){throw 'INSTALL_TASK_DIRECTORY_EXISTS'}\nNew-Item -ItemType Directory -Path " + ps(remote) + " | Out-Null", timeout=90)
    files.put(local, wrapper_path, record["script_sha256"])
    command = "& ([scriptblock]::Create([IO.File]::ReadAllText(" + ps(wrapper_path) + ",[Text.Encoding]::UTF8)))"
    encoded = base64.b64encode(command.encode("utf-16le")).decode("ascii")
    common = JSON_PS + "$name=" + ps(name) + ";$sid=" + ps(desktop["sid"]) + ";$directory=" + ps(remote) + ";$encoded=" + ps(encoded) + r"""
$scheduler=New-Object -ComObject 'Schedule.Service';$scheduler.Connect();$folder=$scheduler.GetFolder('\')
$shellPath=Join-Path (Get-WmiObject Win32_OperatingSystem).WindowsDirectory 'System32\WindowsPowerShell\v1.0\powershell.exe'
$arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand '+$encoded
"""
    start = common + r"""
$task=$scheduler.NewTask(0)
$task.Principal.UserId=$sid;$task.Principal.LogonType=3;$task.Principal.RunLevel=1
$task.Settings.Enabled=$true;$task.Settings.AllowDemandStart=$true;$task.Settings.MultipleInstances=2
# 默认7是后台优先级；交互安装采用普通桌面程序的NORMAL_PRIORITY_CLASS。
$task.Settings.Priority=5
$task.Settings.ExecutionTimeLimit='PT2H';$task.Settings.DisallowStartIfOnBatteries=$false;$task.Settings.StopIfGoingOnBatteries=$false
$action=$task.Actions.Create(0);$action.Path=$shellPath;$action.Arguments=$arguments;$action.WorkingDirectory=$directory
# TASK_CREATE，不覆盖已有任务；只发送一次 Run。
$registered=$folder.RegisterTaskDefinition($name,$task,2,$sid,$null,3,$null)
$registered.Run($null) | Out-Null
"""
    record.update(status="start-requested", requested_at=now())
    write_json(receipt, record)
    files.powershell(start, timeout=90)
    return collect_install(files, destination, record, common)


def collect_install(files, destination, record, common, timeout=7200):
    """只收集原任务，不创建、覆盖或再次运行安装任务。"""
    desktop = record["desktop"]
    remote = record["remote"]
    result_path = remote + r"\result.json"
    receipt = destination / "interactive-task.json"
    status_script = common + r"""
$task=$folder.GetTask($name);$d=$task.Definition;$a=$d.Actions
$actualSid=[string]$d.Principal.UserId
if($actualSid -notmatch '^S-1-'){$actualSid=(New-Object Security.Principal.NTAccount($actualSid)).Translate([Security.Principal.SecurityIdentifier]).Value}
$matches=$a.Count -eq 1
if($matches){$action=$a.Item(1);$matches=$action.Path -eq $shellPath -and $action.Arguments -eq $arguments -and $action.WorkingDirectory -eq $directory}
[Console]::WriteLine($json.Serialize(@{state=[int]$task.State;exit_code=[int]$task.LastTaskResult;sid=$actualSid;logon_type=[int]$d.Principal.LogonType;run_level=[int]$d.Principal.RunLevel;action_matches=[bool]$matches;result_exists=[IO.File]::Exists((Join-Path $directory 'result.json'));error_exists=[IO.File]::Exists((Join-Path $directory 'error.txt'))}))
"""
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = json.loads(files.powershell(status_script, timeout=90))
        validate_task(value, desktop["sid"])
        record.update(status="running", observation=value, updated_at=now())
        write_json(receipt, record)
        if value["state"] != 4 and (value["result_exists"] or value["error_exists"] or value["exit_code"] not in (0, 267009)):
            if value["error_exists"]:
                files.get(remote + r"\error.txt", destination / "interactive-error.txt", timeout=90)
                raise RuntimeError("WIN7_INTERACTIVE_INSTALL_SCRIPT_FAILED")
            if not value["result_exists"] or value["exit_code"] != 0:
                raise RuntimeError("WIN7_INTERACTIVE_INSTALL_RESULT_MISSING")
            files.get(result_path, destination / "interactive-result.json", timeout=90)
            result = json.loads((destination / "interactive-result.json").read_text(encoding="utf-8-sig"))
            execution = result.get("interactive_execution", {})
            if execution.get("sid") != desktop["sid"] or execution.get("session_id") != desktop["desktop_sessions"][0] or execution.get("boot_id") != desktop["boot_id"]:
                raise RuntimeError("WIN7_INTERACTIVE_INSTALL_RESULT_IDENTITY_CHANGED")
            record.update(status="completed", completed_at=now(), result_sha256=sha256(destination / "interactive-result.json"))
            write_json(receipt, record)
            return result
        # 单核TCG内每次WinRM握手也占用Guest CPU，降低无变化阶段的查询频率。
        time.sleep(30)
    raise RuntimeError("WIN7_INTERACTIVE_INSTALL_TIMEOUT_CHECK_EXISTING_TASK_BEFORE_RETRY")


def resume_install(lab, target, destination, package):
    """恢复原进程结果采集，拒绝变更的包、快照、脚本及启动身份。"""
    record = json.loads((destination / "interactive-task.json").read_text(encoding="utf-8"))
    state = lab.state(target)
    validate_desktop(record["desktop"], state["uuid"])
    local = destination / "interactive-installer.ps1"
    if (record.get("package_sha256") != package["sha256"]
            or record.get("restore_generation") != state.get("restore_generation")
            or sha256(local) != record.get("script_sha256")
            or record.get("task_name") != "PartyOps-QA-" + destination.name):
        raise RuntimeError("WIN7_INSTALL_RESUME_BINDING_CHANGED")
    remote = record["remote"]
    wrapper_path = remote + r"\installer.ps1"
    command = "& ([scriptblock]::Create([IO.File]::ReadAllText(" + ps(wrapper_path) + ",[Text.Encoding]::UTF8)))"
    encoded = base64.b64encode(command.encode("utf-16le")).decode("ascii")
    files = WinRMFiles(lab, target)
    check = "$expectedBoot=" + ps(record["desktop"]["boot_id"]) + ";$path=" + ps(wrapper_path) + r"""
if((Get-WmiObject Win32_OperatingSystem).LastBootUpTime -ne $expectedBoot){throw 'WIN7_INSTALL_RESUME_BOOT_CHANGED'}
$f=[IO.File]::OpenRead($path);$h=[Security.Cryptography.SHA256]::Create()
try{[Console]::WriteLine([BitConverter]::ToString($h.ComputeHash($f)).Replace('-','').ToLowerInvariant())}finally{$f.Close();$h.Clear()}
"""
    if files.powershell(check, timeout=180).strip() != record["script_sha256"]:
        raise RuntimeError("WIN7_INSTALL_RESUME_REMOTE_SCRIPT_CHANGED")
    common = JSON_PS + "$name=" + ps(record["task_name"]) + ";$directory=" + ps(remote) + ";$encoded=" + ps(encoded) + r"""
$scheduler=New-Object -ComObject 'Schedule.Service';$scheduler.Connect();$folder=$scheduler.GetFolder('\')
$shellPath=Join-Path (Get-WmiObject Win32_OperatingSystem).WindowsDirectory 'System32\WindowsPowerShell\v1.0\powershell.exe'
$arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand '+$encoded
"""
    return collect_install(files, destination, record, common)
