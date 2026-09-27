"""隔离执行 Windows 普通用户脚本；PS2 真机仍须另验，宿主账户/任务/注册表禁止变更。"""
import json
import os
import re
import shutil
import subprocess
from pathlib import Path

import pytest

SCRIPT = Path(__file__).resolve().parents[1] / "guest/windows-standard-user.ps1"
POWERSHELL = shutil.which("powershell.exe")


def test_guest_script_avoids_powershell3_and_later_dependencies():
    source = SCRIPT.read_text(encoding="utf-8")
    for unavailable in ("ConvertFrom-Json", "ConvertTo-Json", "Get-CimInstance", "Invoke-CimMethod",
                        "Get-LocalUser", "New-LocalUser", "Get-LocalGroupMember", "Add-LocalGroupMember",
                        "New-ScheduledTask", "Register-ScheduledTask", "Start-ScheduledTask", "[ordered]"):
        assert unavailable not in source
    assert not re.search(r"\s-(?:in|notin|Raw)\b", source, re.IGNORECASE)


HARNESS = r"""
$ErrorActionPreference = 'Stop'
$tokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($env:PARTYOPS_UNIT_SCRIPT, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
# 只装载函数定义，不执行 Guest 入口；随后用内存 mock 覆盖所有系统交互。
foreach ($node in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($node.Extent.Text))
}
$scenario = $env:PARTYOPS_UNIT_SCENARIO
$expected = '406ef8ee-e342-405f-933f-7ddce0a55f1b'
$env:COMPUTERNAME = 'GUEST-FIXTURE'
$script:credentialExists = $scenario -ne 'foreign-user' -and $scenario -ne 'prepare-new'
$script:userExists = $scenario -ne 'prepare-new'
$script:mutations = 0; $script:launched = 0; $script:created = 0; $script:privateWrites = 0
$script:credential = @{uuid=$expected; username='partyopsuser'; password='fixture-secret-never-output'}
function Read-LabJson([string]$Path) {
    if ($Path.EndsWith('identity.json')) {
        return @{uuid=$(if ($scenario -eq 'wrong-marker') {'different'} else {$expected}); purpose='disposable-qa'}
    }
    if ($scenario -eq 'wrong-credential') { return @{uuid='different'; username='partyopsuser'; password='fixture-secret-never-output'} }
    return $script:credential
}
function Write-LabPrivateJson([string]$Path, $Value) {
    if (-not $Path.EndsWith('private\standard-user.local.json')) { throw 'OUTSIDE_GUEST_PRIVATE_DIRECTORY' }
    $script:credential = $Value; $script:credentialExists = $true; $script:privateWrites++
}
function Get-LabLocalUser([string]$Name) {
    if ($script:userExists) { return @{Name=$Name} }
}
function Get-LabUserSid($User) { return 'S-1-5-21-1-2-3-1003' }
function Get-LabAdministratorMemberSids {
    if ($scenario -eq 'administrator') { return 'S-1-5-21-1-2-3-1003' }
    return 'S-1-5-21-1-2-3-500'
}
function New-LabLocalUser([string]$Name, [string]$Password) {
    if (-not $script:credentialExists -or $Password -ne $script:credential['password']) { throw 'CREDENTIAL_NOT_DURABLE' }
    $script:userExists = $true; $script:created++
}
function Test-Path([string]$LiteralPath) {
    if ($LiteralPath.EndsWith('standard-user.local.json')) { return $script:credentialExists }
    if ($LiteralPath.EndsWith('wizard.url')) { return $false }
    if ($LiteralPath.EndsWith('ready.json')) { return $scenario -ne 'not-ready' }
    if ($LiteralPath.EndsWith('PartyOpsLauncher.exe')) { return $scenario -ne 'missing-launcher' }
    throw 'UNEXPECTED_FILESYSTEM_PROBE'
}
function New-Item { param($ItemType, $Path, [switch]$Force) $script:mutations++ }
function icacls.exe { $global:LASTEXITCODE = 0; $script:mutations++ }
function Set-ItemProperty { param($Path, $Name, $Value) $script:mutations++ }
function New-ItemProperty { param($Path, $Name, $Value, $PropertyType, [switch]$Force) $script:mutations++ }
function Start-LabInteractiveTask([string]$Launcher, [string]$Directory, [string]$Name) {
    if ($Launcher -ne 'C:\PartyOps QA\中文 程序\PartyOpsLauncher.exe' -or $Directory -ne 'C:\PartyOps QA\中文 程序') { throw 'UNICODE_PATH_CHANGED' }
    $script:launched++
}
function Get-WmiObject([string]$Class, [string]$Filter) {
    if ($Class -eq 'Win32_ComputerSystemProduct') {
        return @{UUID=$(if ($scenario -eq 'wrong-hardware') {'different'} else {$expected})}
    }
    if ($Class -eq 'Win32_UserProfile') { return @{LocalPath='C:\Users\partyopsuser'} }
    if ($Class -eq 'Win32_Process' -and $Filter -eq "Name='explorer.exe'") {
        $process = New-Object PSObject -Property @{ProcessId=10; SessionId=$(if ($scenario -eq 'session-zero') {0} else {1})}
        $process | Add-Member ScriptMethod GetOwner {
            return @{ReturnValue=0; User='partyopsuser'; Domain=$(if ($scenario -eq 'foreign-desktop') {'FOREIGN'} else {'GUEST-FIXTURE'})}
        }
        return $process
    }
    if ($Class -eq 'Win32_Process' -and $Filter -eq "Name LIKE 'PartyOps%'") { return }
    throw 'UNEXPECTED_WMI_QUERY'
}
$action = if ($scenario.StartsWith('prepare') -or $scenario -eq 'not-ready') {'Prepare'} elseif ($scenario -eq 'inspect') {'Inspect'} else {'Launch'}
try {
    $result = Invoke-StandardUserAcceptance $expected $action 'C:\PartyOps QA\中文 程序'
    $output = @{result=$result; error=$null}
} catch {
    $output = @{result=$null; error=$_.Exception.Message}
}
$output.mutations = $script:mutations; $output.launched = $script:launched
$output.created = $script:created; $output.privateWrites = $script:privateWrites
$serializer = New-LabJsonSerializer
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Write-Output $serializer.Serialize($output)
"""


@pytest.mark.skipif(not POWERSHELL, reason="需要 Windows PowerShell；此检查不替代实际 Win7 PS2 验证")
@pytest.mark.parametrize(("scenario", "error"), [
    ("wrong-hardware", "GUEST_OWNERSHIP_MISMATCH"), ("wrong-marker", "GUEST_OWNERSHIP_MISMATCH"),
    ("foreign-user", "STANDARD_USER_NOT_LAB_OWNED"), ("wrong-credential", "STANDARD_USER_CREDENTIAL_MISMATCH"),
    ("administrator", "STANDARD_USER_IS_ADMINISTRATOR"), ("session-zero", "STANDARD_USER_INTERACTIVE_DESKTOP_REQUIRED"),
    ("foreign-desktop", "STANDARD_USER_INTERACTIVE_DESKTOP_REQUIRED"), ("missing-launcher", "INSTALLED_LAUNCHER_MISSING"),
    ("not-ready", "GUEST_BOOTSTRAP_NOT_READY"), ("inspect", None), ("launch", None),
    ("prepare-existing", None), ("prepare-new", None),
])
def test_guest_ownership_standard_account_and_launch_are_enforced_without_host_mutation(tmp_path, scenario, error):
    harness = tmp_path / "standard-user-mock.ps1"
    harness.write_text(HARNESS, encoding="utf-8-sig")
    env = {**os.environ, "PARTYOPS_UNIT_SCRIPT": str(SCRIPT), "PARTYOPS_UNIT_SCENARIO": scenario}
    completed = subprocess.run([POWERSHELL, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(harness)],
                               env=env, capture_output=True, timeout=30, check=False)
    assert completed.returncode == 0, completed.stderr.decode("utf-8", errors="replace")
    output = completed.stdout.decode("utf-8-sig").strip()
    assert "fixture-secret-never-output" not in output
    result = json.loads(output)
    assert result["error"] == error
    if error:
        assert result["mutations"] == result["launched"] == result["created"] == result["privateWrites"] == 0
    else:
        assert result["result"]["administrator"] is False
        assert result["result"]["runtime_environment_passed"] is False
        assert isinstance(result["result"]["desktop"], list) and isinstance(result["result"]["processes"], list)
        assert result["launched"] == (1 if scenario == "launch" else 0)
        assert result["created"] == result["privateWrites"] == (1 if scenario == "prepare-new" else 0)


@pytest.mark.skipif(not POWERSHELL, reason="需要 Windows PowerShell；不启动真实任务计划程序")
def test_scheduler_uses_interactive_limited_token_and_unicode_paths(tmp_path):
    loader = HARNESS.split("$scenario =")[0]
    body = r"""
$env:COMPUTERNAME = 'GUEST-FIXTURE'
$script:execution = [PSCustomObject]@{Path=$null; WorkingDirectory=$null}
$actions = [PSCustomObject]@{}
$actions | Add-Member ScriptMethod Create { param($kind) if ($kind -ne 0) {throw 'INVALID_ACTION'}; return $script:execution }
$script:definition = [PSCustomObject]@{
    RegistrationInfo=[PSCustomObject]@{Description=$null}
    Principal=[PSCustomObject]@{UserId=$null; LogonType=$null; RunLevel=$null}
    Settings=[PSCustomObject]@{Enabled=$null; AllowDemandStart=$null; MultipleInstances=$null}
    Actions=$actions
}
$script:ran = $false
$script:registered = [PSCustomObject]@{}
$script:registered | Add-Member ScriptMethod Run { param($parameters) $script:ran=$true }
$script:folder = [PSCustomObject]@{}
$script:folder | Add-Member ScriptMethod RegisterTaskDefinition {
    param($name, $definition, $flags, $user, $password, $logonType, $securityDescriptor)
    if ($name -ne 'PartyOps-QA-Standard-Launch' -or $flags -ne 6 -or $user -ne 'GUEST-FIXTURE\partyopsuser') { throw 'TASK_IDENTITY_CHANGED' }
    if ($password -ne $null -or $logonType -ne 3 -or $securityDescriptor -ne $null) { throw 'TASK_CREDENTIAL_OR_LOGON_CHANGED' }
    return $script:registered
}
$script:scheduler = [PSCustomObject]@{}
$script:scheduler | Add-Member ScriptMethod Connect {}
$script:scheduler | Add-Member ScriptMethod NewTask { param($flags) return $script:definition }
$script:scheduler | Add-Member ScriptMethod GetFolder { param($path) if ($path -ne '\') {throw 'WRONG_FOLDER'}; return $script:folder }
function New-Object {
    param($TypeName, $ComObject)
    if ($ComObject -eq 'Schedule.Service') { return $script:scheduler }
    throw 'UNEXPECTED_OBJECT_CREATION'
}
Start-LabInteractiveTask 'C:\PartyOps QA\中文 程序\PartyOpsLauncher.exe' 'C:\PartyOps QA\中文 程序' 'partyopsuser'
if ($script:definition.Principal.RunLevel -ne 0 -or $script:definition.Principal.LogonType -ne 3 -or -not $script:ran) {throw 'INTERACTIVE_LIMITED_TOKEN_REQUIRED'}
if ($script:execution.Path -ne 'C:\PartyOps QA\中文 程序\PartyOpsLauncher.exe' -or $script:execution.WorkingDirectory -ne 'C:\PartyOps QA\中文 程序') {throw 'UNICODE_PATH_CHANGED'}
Write-Output 'scheduler-contract-passed'
"""
    harness = tmp_path / "scheduler-mock.ps1"
    harness.write_text(loader + body, encoding="utf-8-sig")
    result = subprocess.run([POWERSHELL, "-NoProfile", "-NonInteractive", "-File", str(harness)],
                            env={**os.environ, "PARTYOPS_UNIT_SCRIPT": str(SCRIPT)}, capture_output=True, timeout=30, check=False)
    assert result.returncode == 0, result.stderr.decode("utf-8", errors="replace")
    assert b"scheduler-contract-passed" in result.stdout


@pytest.mark.skipif(not POWERSHELL, reason="需要 Windows .NET Framework 的 JavaScriptSerializer")
def test_private_json_roundtrip_uses_utf8_and_no_powershell_json_cmdlets(tmp_path):
    loader = HARNESS.split("$scenario =")[0]
    body = r"""
Write-LabPrivateJson $env:PARTYOPS_UNIT_JSON @{uuid='unit-uuid'; username='中文 用户'; password='synthetic-only-password'}
$value = Read-LabJson $env:PARTYOPS_UNIT_JSON
if ($value['uuid'] -ne 'unit-uuid' -or $value['username'] -ne '中文 用户' -or $value['password'] -ne 'synthetic-only-password') {throw 'PRIVATE_JSON_ROUNDTRIP_FAILED'}
Write-Output 'private-json-roundtrip-passed'
"""
    harness = tmp_path / "json-roundtrip.ps1"
    harness.write_text(loader + body, encoding="utf-8-sig")
    path = tmp_path / "synthetic.local.json"
    result = subprocess.run([POWERSHELL, "-NoProfile", "-NonInteractive", "-File", str(harness)],
                            env={**os.environ, "PARTYOPS_UNIT_SCRIPT": str(SCRIPT), "PARTYOPS_UNIT_JSON": str(path)},
                            capture_output=True, timeout=30, check=False)
    assert result.returncode == 0, result.stderr.decode("utf-8", errors="replace")
    assert b"private-json-roundtrip-passed" in result.stdout and b"synthetic-only-password" not in result.stdout
    assert not path.read_bytes().startswith(b"\xef\xbb\xbf")
    assert json.loads(path.read_text(encoding="utf-8"))["username"] == "中文 用户"
