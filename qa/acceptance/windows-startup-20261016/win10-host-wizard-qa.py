"""原 Win10 普通用户主机向导：一次启动、只读采集，不提交配置或授权 UAC。"""
from __future__ import annotations

import argparse
import importlib.util
import json
import re
import sys
import time
import uuid
from datetime import datetime
from pathlib import Path
from types import SimpleNamespace

import retention_contract as contract

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = Path(__file__).resolve().parent
LAB_ROOT = Path("D:/PartyOps-VM-Lab")
HOST_DATA = r"C:\Users\partyopsuser\Documents\PartyOps QA\host-wizard-20261017\主机 数据"
TASK = "PartyOps-QA-Host-Wizard-20261017"
sys.path.insert(0, str(ROOT / "qa/vm-lab"))


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def require(value, error):
    if not value: raise RuntimeError(error)


def prefix(binding, since):
    request = {"uuid": contract.UUID, "manifest": binding["manifest_sha256"], "wizard": binding["wizard_sha256"],
               "install": contract.INSTALL, "host_data": HOST_DATA, "since": since, "task": TASK}
    return "$request='" + json.dumps(request, ensure_ascii=False).replace("'", "''") + "'|ConvertFrom-Json\n" + r"""
$ErrorActionPreference='Stop'
$marker=Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw|ConvertFrom-Json
if($marker.uuid -ne $request.uuid -or $marker.purpose -ne 'disposable-qa' -or (Get-CimInstance Win32_ComputerSystemProduct).UUID -ne $request.uuid) {throw 'QA_GUEST_IDENTITY_REQUIRED'}
$wizard=Join-Path $request.install 'PartyOpsWizard.exe'
if((Get-FileHash -LiteralPath (Join-Path $request.install 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $request.manifest -or (Get-FileHash -LiteralPath $wizard -Algorithm SHA256).Hash.ToLowerInvariant() -ne $request.wizard) {throw 'CANDIDATE_WIZARD_CHANGED'}
$account=Get-LocalUser -Name 'partyopsuser'
$sid=$account.SID.Value
if(-not $account.Enabled -or @(Get-LocalGroupMember -SID 'S-1-5-32-544'|Where-Object {$_.SID.Value -eq $sid}).Count) {throw 'ORIGINAL_STANDARD_ACCOUNT_REQUIRED'}
$desktop=@(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'"|ForEach-Object {
  $owner=Invoke-CimMethod -InputObject $_ -MethodName GetOwner
  if($owner.ReturnValue -eq 0 -and $owner.User -eq 'partyopsuser' -and $owner.Domain -eq $env:COMPUTERNAME) {
    @{pid=[int]$_.ProcessId;session_id=[int]$_.SessionId}
  }
})
if($desktop.Count -ne 1 -or $desktop[0].session_id -eq 0) {throw 'ORIGINAL_INTERACTIVE_DESKTOP_REQUIRED'}
$userRoot='C:\Users\partyopsuser\AppData\Local\PartyOps'
$control='C:\ProgramData\PartyOps'
$pending=Test-Path -LiteralPath (Join-Path $control 'host-switch-pending.json')
$journal=Test-Path -LiteralPath (Join-Path $userRoot 'host-config-journal.json')
"""


def launch_script(binding, since):
    # 复用 windows-standard-user.ps1 的 COM 交互令牌/LUA任务机制，增加固定向导参数。
    return prefix(binding, since) + r"""
if($pending -or $journal) {throw 'PENDING_MODE_SWITCH_REQUIRES_PRODUCT_RECOVERY'}
if(@(Get-CimInstance Win32_Process -Filter "Name='PartyOpsWizard.exe'").Count) {throw 'EXISTING_WIZARD_INSPECT_WITHOUT_RELAUNCH'}
$scheduler=New-Object -ComObject 'Schedule.Service';$scheduler.Connect()
$definition=$scheduler.NewTask(0)
$definition.RegistrationInfo.Description='PartyOps original Win10 QA host wizard only'
$definition.Principal.UserId=$env:COMPUTERNAME+'\partyopsuser'
$definition.Principal.LogonType=3
$definition.Principal.RunLevel=0
$definition.Settings.Enabled=$true;$definition.Settings.AllowDemandStart=$true
$definition.Settings.MultipleInstances=2
$action=$definition.Actions.Create(0)
$action.Path=$wizard;$action.WorkingDirectory=$request.install
$action.Arguments='--reconfigure --initial-role host'
$folder=$scheduler.GetFolder('\')
$registered=$folder.RegisterTaskDefinition($request.task,$definition,2,$definition.Principal.UserId,$null,3,$null)
$registered.Run($null)|Out-Null
@{launch_requested=$true;task_runlevel=0;configuration_submitted=$false;uac_accepted=$false}|ConvertTo-Json -Compress
"""


def inspect_script(binding, since):
    return prefix(binding, since) + r"""
$products=@(Get-CimInstance Win32_Process|Where-Object {$_.Name -in @('PartyOps.exe','PartyOpsWizard.exe')}|ForEach-Object {
  $owner=Invoke-CimMethod -InputObject $_ -MethodName GetOwner
  @{name=$_.Name;pid=[int]$_.ProcessId;installed_path_matches=($_.ExecutablePath -eq (Join-Path $request.install $_.Name));owner_is_original_user=($owner.ReturnValue -eq 0 -and $owner.User -eq 'partyopsuser' -and $owner.Domain -eq $env:COMPUTERNAME);session_id=[int]$_.SessionId;created=$_.CreationDate.ToUniversalTime().ToString('o')}
})
$wizardPids=@($products|Where-Object {$_.name -eq 'PartyOpsWizard.exe'}|ForEach-Object {$_.pid})
$productPids=@($products|ForEach-Object {$_.pid})
$listeners=@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue|Where-Object {$_.OwningProcess -in $productPids -or $_.LocalPort -in @(18765,18775)}|ForEach-Object {
  @{port=[int]$_.LocalPort;pid=[int]$_.OwningProcess;loopback=($_.LocalAddress -in @('127.0.0.1','::1'));owned_by_wizard=($_.OwningProcess -in $wizardPids)}
})
function Public-Mode([string]$path) {
  if(-not(Test-Path -LiteralPath $path -PathType Leaf)) {return 'missing'}
  $value=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
  if($value.mode -in @('personal','host','client')) {return [string]$value.mode}
  return 'unknown'
}
$statusFile=Join-Path $request.host_data 'logs\partyops-host-status.json'
$hostStatus=@{present=$false}
if(Test-Path -LiteralPath $statusFile -PathType Leaf) {
  $value=Get-Content -LiteralPath $statusFile -Raw|ConvertFrom-Json
  $hostStatus=@{present=$true;stage='unknown';code='';pid=$null;exit_code=$null}
  if([string]$value.stage -match '^[a-z_]{1,40}$') {$hostStatus.stage=[string]$value.stage}
  if([string]$value.code -match '^[A-Z][A-Z0-9_]{0,63}$') {$hostStatus.code=[string]$value.code}
  if($value.pid -is [int] -or $value.pid -is [long]) {$hostStatus.pid=$value.pid}
  if($value.exit_code -is [int] -or $value.exit_code -is [long]) {$hostStatus.exit_code=$value.exit_code}
}
$outcomes=@(if(Test-Path -LiteralPath $control) {
  Get-ChildItem -LiteralPath $control -Filter 'host-config-outcome-*.json' -File|Where-Object {$_.LastWriteTimeUtc -ge [DateTimeOffset]::Parse($request.since).UtcDateTime}|ForEach-Object {
    $value=Get-Content -LiteralPath $_.FullName -Raw|ConvertFrom-Json
    if($value.format_version -eq 1 -and $value.outcome -in @('commit','rollback')) {[string]$value.outcome}
  }
})
@{username='partyopsuser';administrator=$false;session_id=$desktop[0].session_id;processes=$products;listeners=$listeners;wizard_exists=($wizardPids.Count -gt 0);user_mode=(Public-Mode (Join-Path $userRoot 'mode.json'));system_mode=(Public-Mode (Join-Path $control 'mode.json'));pending_mode_switch=$pending;user_journal_present=$journal;qa_host_data_marker_present=(Test-Path -LiteralPath (Join-Path $request.host_data '.partyops-data-root.json'));fixed_qa_host_status=$hostStatus;public_outcomes_since_launch=$outcomes;uac_acceptance='not-verified';host_configuration_acceptance='not-verified'}|ConvertTo-Json -Depth 7 -Compress
"""


def validate_wizard(snapshot, since):
    require(snapshot.get("username") == "partyopsuser" and snapshot.get("administrator") is False
            and snapshot.get("session_id", 0) > 0, "ORDINARY_USER_SNAPSHOT_REQUIRED")
    rows = [p for p in snapshot.get("processes", []) if p.get("name") == "PartyOpsWizard.exe"]
    require(len(rows) == 1, "SINGLE_WIZARD_REQUIRED")
    wizard = rows[0]
    require(wizard.get("installed_path_matches") is True and wizard.get("owner_is_original_user") is True
            and wizard.get("session_id") == snapshot["session_id"], "CANDIDATE_WIZARD_OWNER_REQUIRED")
    require(datetime.fromisoformat(wizard["created"].replace("Z", "+00:00")) >= datetime.fromisoformat(since.replace("Z", "+00:00")), "WIZARD_MUST_START_AFTER_ATTEMPT")
    require(any(row.get("pid") == wizard["pid"] and row.get("owned_by_wizard") is True and row.get("loopback") is True
                for row in snapshot.get("listeners", [])), "WIZARD_LOOPBACK_LISTENER_REQUIRED")


def transfer(client, execute, script, name):
    require(re.fullmatch(r"host-wizard-[a-f0-9]{12}", name), "OWNED_QA_TRANSPORT_NAME_REQUIRED")
    remote = "C:/ProgramData/PartyOps-VM-Lab/" + name
    with client.open_sftp() as fs:
        fs.mkdir(remote)
        with fs.file(remote + "/operation.ps1", "wx") as stream: stream.write(script.encode("utf-8"))
    command = "& ([scriptblock]::Create([IO.File]::ReadAllText('" + remote + "/operation.ps1',[Text.Encoding]::UTF8)))"
    try: return json.loads(execute(client, command, timeout=120))
    except Exception: raise RuntimeError("HOST_WIZARD_GUEST_OPERATION_FAILED_INSPECT_WITHOUT_RELAUNCH") from None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["launch", "inspect"])
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--sha256", required=True)
    args = parser.parse_args()
    windows = load(ROOT / "qa/vm-lab/scripts/exercise-windows-business.py", "host_qa_windows")
    transition = load(OUTPUT / "runtime-transition.py", "host_qa_transition")
    matrix, media = windows.load_configuration()
    matrix["defaults"].update(primary_root=str(LAB_ROOT), fallback_root=str(LAB_ROOT))
    lab = windows.QemuLab(matrix, LAB_ROOT, media)
    binding = contract.candidate(lab, ROOT, args.package, args.sha256)
    contract.completed_install(OUTPUT / "overlay-install", binding)
    preserved = transition.preserved_inputs()[0]
    contract.bind_guest(lab, windows.qmp)
    attempt_file = OUTPUT / "host-wizard-launch.attempt.json"
    if args.action == "launch": require(not attempt_file.exists(), "HOST_WIZARD_ALREADY_ATTEMPTED_INSPECT_ONLY")
    since = contract.read(attempt_file)["time_utc"] if attempt_file.exists() else contract.now()
    if attempt_file.exists():
        previous = contract.read(attempt_file)
        require(previous.get("candidate") == binding and previous.get("preserved_inputs") == preserved,
                "HOST_WIZARD_ATTEMPT_BINDING_CHANGED")
    client = windows.windows.connect(lab, matrix["targets"]["win10-x64"])
    try:
        runner = object.__new__(windows.WindowsRun)
        runner.client, runner.args = client, SimpleNamespace(uuid=contract.UUID)
        desktop = runner.desktop("Inspect")
        transition.standard_desktop(desktop)
        if args.action == "launch": windows.WindowsRun.require_configuration_owner(desktop)
        actual = contract.probe(client, windows.windows.execute)
        for key in ("manifest_sha256", "executable_sha256", "wizard_sha256"):
            require(actual.get(key) == binding[key], "INSTALLED_CANDIDATE_CHANGED")
        if args.action == "launch":
            contract.runtime_matches(actual, binding, contract.read(OUTPUT / "overlay-install/attempt.json")["time_utc"])
            since = contract.now()
            preflight = transfer(client, windows.windows.execute, inspect_script(binding, since), "host-wizard-" + uuid.uuid4().hex[:12])
            require(not preflight["wizard_exists"] and not preflight["pending_mode_switch"] and not preflight["user_journal_present"], "EXISTING_WIZARD_OR_PENDING_SWITCH_INSPECT_ONLY")
            contract.bind_guest(lab, windows.qmp)
            contract.exclusive(attempt_file, {"time_utc": since, "guest_uuid": contract.UUID, "candidate": binding,
                "preserved_inputs": preserved, "qa_host_data_directory": HOST_DATA, "action": "ordinary-user-wizard-only",
                "configuration_submitted": False, "automatic_elevation": False})
            try:
                requested = transfer(client, windows.windows.execute, launch_script(binding, since), "host-wizard-" + uuid.uuid4().hex[:12])
                contract.exclusive(OUTPUT / "host-wizard-launch.requested.json", {
                    "attempt_sha256": contract.digest(attempt_file), "request": requested})
            except Exception:
                contract.exclusive(OUTPUT / "host-wizard-launch.failed.json", {"status": "inspect-without-relaunch", "time_utc": contract.now()})
                raise
        snapshot = transfer(client, windows.windows.execute, inspect_script(binding, since), "host-wizard-" + uuid.uuid4().hex[:12])
        if args.action == "launch":
            deadline = time.monotonic() + 30
            while True:
                try: validate_wizard(snapshot, since); break
                except RuntimeError:
                    require(time.monotonic() < deadline, "WIZARD_START_TIMEOUT_INSPECT_WITHOUT_RELAUNCH")
                    time.sleep(2)
                    snapshot = transfer(client, windows.windows.execute, inspect_script(binding, since), "host-wizard-" + uuid.uuid4().hex[:12])
        require(transition.preserved_inputs()[0] == preserved, "PRESERVED_BASELINE_CHANGED")
        contract.bind_guest(lab, windows.qmp)
        destination = OUTPUT / ("host-wizard-" + args.action + "-" + uuid.uuid4().hex[:12] + ".json")
        contract.exclusive(destination, {"time_utc": contract.now(), "candidate": binding, "since": since,
            "snapshot": snapshot, "scope": "qa-wizard-status-only-not-uac-or-host-acceptance"})
        print(json.dumps({"status": "recorded", "evidence": str(destination), "qa_host_data_directory": HOST_DATA,
                          "form_directory_requires_manual_selection": True, "uac_acceptance": "not-verified"}, ensure_ascii=False))
    finally: client.close()


if __name__ == "__main__":
    try: main()
    except Exception as error:
        print(json.dumps({"status": "failed", "error_type": type(error).__name__, "retry": "inspect-only"}))
        raise SystemExit(2)
