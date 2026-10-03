"""只载入函数并 mock 账户/ACL 操作；禁止单测创建本机账户或启动 PartyOps。"""
import base64
import json
import os
import shutil
import subprocess
from pathlib import Path

import pytest

SCRIPT = Path(__file__).resolve().parents[1] / "scripts/windows-native-user.ps1"
POWERSHELL = shutil.which("powershell.exe")

HARNESS = r"""
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$source=[IO.File]::ReadAllText($env:PARTYOPS_UNIT_SCRIPT,[Text.Encoding]::UTF8)
$ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw ($errors|Out-String)}
foreach($node in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){
  . ([scriptblock]::Create($node.Extent.Text))
}
$scenario=$env:PARTYOPS_UNIT_SCENARIO
$descriptionLimit=@((Get-Command Microsoft.PowerShell.LocalAccounts\New-LocalUser).Parameters['Description'].Attributes|
  Where-Object {$_ -is [Management.Automation.ValidateLengthAttribute]})[0].MaxLength
$fixtureSid='S-1-5-21-111-222-333-1007'
$context=[pscustomobject]@{run_directory='D:\PartyOps-VM-Lab\reports\win11-x64-native\fixture';
  native_workspace='E:\codex\PartyOps\.partyops-vm-lab\native\win11-x64\fixture\PartyOpsNativeQA';
  state_directory='D:\PartyOps-VM-Lab\state\native\PartyOpsNativeQA';machine_name='NATIVE-FIXTURE';hardware_uuid='fixture-uuid';controller_sid='S-1-5-21-111-222-333-500'}
$script:record=[pscustomobject]@{schema_version=1;purpose='PartyOps native standard-user validation';
  username='PartyOpsNativeQA';sid=$fixtureSid;ownership_id='abc123';status='prepared';
  run_directory=$context.run_directory;native_workspace=$context.native_workspace;
  data_directory=(Join-Path $context.native_workspace '中文 空格业务数据-abc123');
  machine_name=$context.machine_name;hardware_uuid=$context.hardware_uuid;controller_sid=$context.controller_sid}
if($scenario -in @('prepare-new','foreign-user','unregistered-state') -or $scenario.StartsWith('recovery-')){$script:record=$null}
if($scenario -eq 'wrong-run'){$script:record.run_directory+='-other'}
if($scenario -eq 'wrong-host'){$script:record.hardware_uuid='other-host'}
if($scenario -eq 'wrong-data'){$script:record.data_directory='D:\existing-real-business'}
$script:mutations=0;$script:created=0;$script:credentialWrites=0;$script:intentWrites=0;$script:actualDescriptionLength=0
$script:user=[pscustomobject]@{Name='PartyOpsNativeQA';SID=[pscustomobject]@{Value=$fixtureSid};Enabled=$true;Description='PartyOps QA abc123'}
if($scenario -in @('prepare-new','unregistered-state','missing-user') -or $scenario.StartsWith('recovery-')){$script:user=$null}
if($scenario -eq 'wrong-sid'){$script:user.SID.Value='S-1-5-21-111-222-333-1008'}
if($scenario.StartsWith('intent-')){
  $script:record.ownership_id='a'*32;$script:record.sid=$null;$script:record.status='creation-intent'
  $script:record.data_directory=Join-Path $context.native_workspace ('中文 空格业务数据-'+$script:record.ownership_id)
  $script:record|Add-Member NoteProperty credential_sha256 ('b'*64)
  $script:user.Description='PartyOps QA '+$script:record.ownership_id
  if($scenario -eq 'intent-new'){$script:user=$null}
  if($scenario -eq 'intent-wrong-description'){$script:user.Description='another owner'}
  if($scenario -eq 'intent-wrong-host'){$script:record.hardware_uuid='another machine'}
}
if($scenario -eq 'preparing'){$script:record.status='preparing'}
function Get-LocalUser {param($Name,$ErrorAction) return $script:user}
function Get-LocalGroupMember {param($SID)
  if($SID.Value -eq 'S-1-5-32-545' -and $scenario -ne 'not-users'){return [pscustomobject]@{SID=[pscustomobject]@{Value=$fixtureSid}}}
  if($SID.Value -eq 'S-1-5-32-544' -and $scenario -eq 'admin'){return [pscustomobject]@{SID=[pscustomobject]@{Value=$fixtureSid}}}
}
function Test-Path {param($LiteralPath)
  if($LiteralPath.EndsWith('ownership.json')){return $null -ne $script:record}
  if($LiteralPath.EndsWith('credential.clixml')){return $scenario -ne 'missing-credential'}
  if($LiteralPath -eq $context.state_directory){return $scenario -eq 'unregistered-state' -or $scenario.StartsWith('recovery-')}
  throw 'UNEXPECTED_FILESYSTEM_READ'
}
function Get-Content {param($LiteralPath,[switch]$Raw,$Encoding) return $script:record|ConvertTo-Json -Depth 6}
function New-Item {param($ItemType,$Path,[switch]$Force) $script:mutations++}
function Set-NativeDirectoryAcl {param($Path,$UserSid,$Rights) $script:mutations++}
function Write-NativeJson {param($Path,$Value)
  $script:mutations++;$script:record=$Value
  if($Value.status -eq 'creation-intent'){$script:intentWrites++}
}
function Get-FileHash {param($LiteralPath,$Algorithm)
  return @{Hash=$(if($scenario -in @('intent-changed-credential','recovery-wrong-hash')){'c'*64}else{'b'*64})}
}
function Import-Clixml {param($LiteralPath)
  $password=[Security.SecureString]::new();$password.AppendChar('x');$password.MakeReadOnly()
  $username=$(if($scenario -eq 'recovery-wrong-user'){'OTHER\Administrator'}else{'NATIVE-FIXTURE\PartyOpsNativeQA'})
  return [pscredential]::new($username,$password)
}
function Resolve-NativeChild {param($Root,$Candidate) return $Candidate}
function Get-ChildItem {param($LiteralPath,[switch]$Force)
  if($LiteralPath -eq $context.state_directory){return [pscustomobject]@{Name='private';PSIsContainer=$true}}
  if($scenario -eq 'recovery-extra-file'){return @([pscustomobject]@{Name='credential.clixml';PSIsContainer=$false},[pscustomobject]@{Name='unrelated.txt';PSIsContainer=$false})}
  return [pscustomobject]@{Name='credential.clixml';PSIsContainer=$false}
}
function Get-Acl {param($LiteralPath)
  $acl=[pscustomobject]@{AreAccessRulesProtected=$true}
  $acl|Add-Member ScriptMethod GetAccessRules {
    return @([pscustomobject]@{IdentityReference=[pscustomobject]@{Value=$(if($scenario -eq 'recovery-public-acl'){'S-1-1-0'}else{'S-1-5-18'})}})
  }
  return $acl
}
function Export-Clixml {param([Parameter(ValueFromPipeline=$true)]$InputObject,$LiteralPath)
  process{
    if($InputObject -isnot [pscredential] -or $InputObject.UserName -ne 'NATIVE-FIXTURE\PartyOpsNativeQA'){throw 'UNEXPECTED_CREDENTIAL'}
    $script:credentialWrites++
  }
}
function New-LocalUser {param($Name,[Security.SecureString]$Password,$Description)
  if($Name -ne 'PartyOpsNativeQA' -or $script:record.status -ne 'creation-intent'){throw 'ACCOUNT_BEFORE_CREATION_INTENT'}
  if($scenario -eq 'prepare-new' -and $script:credentialWrites -ne 1){throw 'ACCOUNT_BEFORE_PRIVATE_CREDENTIAL'}
  if($Description.Length -gt $descriptionLimit){throw 'DESCRIPTION_EXCEEDS_REAL_API_LIMIT'}
  $script:actualDescriptionLength=$Description.Length
  $script:created++;$script:mutations++
  $script:user=[pscustomobject]@{Name=$Name;SID=[pscustomobject]@{Value=$fixtureSid};Description=$Description;Enabled=$true}
  return $script:user
}
function Add-LocalGroupMember {param($SID,$Member)
  if($SID.Value -ne 'S-1-5-32-545' -or $Member.Name -ne 'PartyOpsNativeQA'){throw 'UNEXPECTED_GROUP_CHANGE'}
  $script:mutations++
}
function Start-Process {throw 'UNIT_TEST_MUST_NOT_START_PROCESS'}
$failure=$null
try{
  $recovery=$(if($scenario.StartsWith('recovery-')){'b'*64}else{''})
  $prepared=Prepare-NativeUser $context $recovery
}catch{$failure=$_.Exception.Message}
[pscustomobject]@{failure=$failure;mutations=$script:mutations;created=$script:created;credential_writes=$script:credentialWrites;
  intent_writes=$script:intentWrites;description_limit=$descriptionLimit;description_length=$script:actualDescriptionLength;
  status=$(if($prepared){$prepared.status}else{$null})}|ConvertTo-Json -Compress
"""


def run_powershell(source: str, scenario: str = "") -> str:
    if not POWERSHELL:
        pytest.skip("此测试需要 Windows PowerShell；不替代其他平台测试")
    result = subprocess.run(
        [POWERSHELL, "-NoProfile", "-NonInteractive", "-EncodedCommand",
         base64.b64encode(source.encode("utf-16le")).decode("ascii")],
        capture_output=True, timeout=30, check=False,
        env={**os.environ, "PARTYOPS_UNIT_SCRIPT": str(SCRIPT), "PARTYOPS_UNIT_SCENARIO": scenario},
    )
    assert result.returncode == 0, result.stderr.decode("utf-8", errors="replace")
    return result.stdout.decode("utf-8", errors="replace").strip()


@pytest.mark.parametrize("scenario,code", [
    ("foreign-user", "NATIVE_USER_NOT_OWNED_BY_THIS_RUN"),
    ("wrong-run", "NATIVE_USER_NOT_OWNED_BY_THIS_RUN"),
    ("wrong-host", "NATIVE_USER_NOT_OWNED_BY_THIS_RUN"),
    ("wrong-sid", "NATIVE_OWNED_USER_IDENTITY_CHANGED"),
    ("missing-user", "NATIVE_OWNED_USER_IDENTITY_CHANGED"),
    ("admin", "NATIVE_STANDARD_USER_IS_ADMINISTRATOR"),
    ("not-users", "NATIVE_STANDARD_USER_NOT_IN_USERS"),
    ("unregistered-state", "NATIVE_UNREGISTERED_STATE_DIRECTORY"),
    ("missing-credential", "NATIVE_USER_NOT_READY"),
    ("wrong-data", "NATIVE_OWNED_DATA_DIRECTORY_CHANGED"),
    ("intent-wrong-description", "NATIVE_OWNED_USER_IDENTITY_CHANGED"),
    ("intent-wrong-host", "NATIVE_USER_NOT_OWNED_BY_THIS_RUN"),
    ("intent-changed-credential", "NATIVE_CREATION_CREDENTIAL_CHANGED"),
    ("recovery-extra-file", "NATIVE_RECOVERY_UNEXPECTED_FILES"),
    ("recovery-public-acl", "NATIVE_RECOVERY_ACL_NOT_PRIVATE"),
    ("recovery-wrong-hash", "NATIVE_RECOVERY_CREDENTIAL_HASH_MISMATCH"),
    ("recovery-wrong-user", "NATIVE_CREDENTIAL_IDENTITY_MISMATCH"),
])
def test_ownership_or_group_failure_occurs_before_mutation(scenario, code):
    result = json.loads(run_powershell(HARNESS, scenario))
    assert result["failure"] == code
    assert result["mutations"] == 0
    assert result["created"] == 0
    assert result["credential_writes"] == 0


def test_new_account_is_exclusive_and_credentials_are_durable_first():
    result = json.loads(run_powershell(HARNESS, "prepare-new"))
    assert result["failure"] is None
    assert result["created"] == 1
    assert result["credential_writes"] == 1
    assert result["intent_writes"] == 1
    assert result["description_length"] == 44
    assert result["description_length"] <= result["description_limit"] == 48
    assert result["status"] == "prepared"


def test_owned_account_is_reused_without_changing_password_or_membership():
    result = json.loads(run_powershell(HARNESS, "owned"))
    assert result["failure"] is None
    assert result["mutations"] == 0
    assert result["credential_writes"] == 0


@pytest.mark.parametrize("scenario,created,intents", [
    ("recovery-confirmed", 1, 1), ("intent-new", 1, 0),
    ("intent-created", 0, 0), ("preparing", 0, 0),
])
def test_interrupted_prepare_resumes_only_owned_intent_and_preserves_credential(scenario, created, intents):
    result = json.loads(run_powershell(HARNESS, scenario))
    assert result["failure"] is None
    assert result["created"] == created
    assert result["intent_writes"] == intents
    assert result["credential_writes"] == 0
    assert result["status"] == "prepared"


def test_parent_and_child_parse_and_do_not_change_global_login_or_guest_identity():
    source = SCRIPT.read_text(encoding="utf-8")
    for forbidden in ("AutoAdminLogon", "DefaultPassword", "disposable-qa", "Restart-Computer",
                      "Register-ScheduledTask", "Set-LocalUser", "Remove-LocalUser"):
        assert forbidden not in source
    before_invocation = HARNESS[:HARNESS.index("$scenario=$env:PARTYOPS_UNIT_SCENARIO")]
    child_parse = r"""
$child=Get-NativeProbeScript
$errors=$null;$tokens=$null
[Management.Automation.Language.Parser]::ParseInput($child,[ref]$tokens,[ref]$errors)|Out-Null
if($errors.Count){throw ($errors|Out-String)}
'parsed-without-entrypoint-execution'
"""
    assert run_powershell(before_invocation + child_parse) == "parsed-without-entrypoint-execution"


def test_path_guard_rejects_outside_target_and_reparse_before_writes():
    before_invocation = HARNESS[:HARNESS.index("$scenario=$env:PARTYOPS_UNIT_SCENARIO")]
    script = r"""
$outside=$null;$link=$null
try{Resolve-NativeChild 'D:\PartyOps-VM-Lab\state\native' 'C:\Users\Administrator'}catch{$outside=$_.Exception.Message}
function Test-Path {param($LiteralPath) return $true}
function Get-Item {param($LiteralPath,[switch]$Force) return @{Attributes=[IO.FileAttributes]::ReparsePoint}}
try{Resolve-NativeChild 'D:\PartyOps-VM-Lab\state\native' 'D:\PartyOps-VM-Lab\state\native\link'}catch{$link=$_.Exception.Message}
@{outside=$outside;link=$link}|ConvertTo-Json -Compress
"""
    assert json.loads(run_powershell(before_invocation + script)) == {
        "outside": "NATIVE_PATH_OUTSIDE_LAB", "link": "NATIVE_REPARSE_POINT_REJECTED",
    }


def test_secondary_logon_short_loader_reads_utf8_from_real_working_directory(tmp_path):
    """只运行无产品行为的 UTF-8 夹具；验证二次登录启动参数而不创建账号。"""
    before_invocation = HARNESS[:HARNESS.index("$scenario=$env:PARTYOPS_UNIT_SCENARIO")]
    probe_id = "standard-user-probe-" + "a" * 32
    launch = json.loads(run_powershell(before_invocation + r"""
$shell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
Get-NativeSecondaryLogonArguments ('standard-user-probe-'+('a'*32)) $shell|ConvertTo-Json -Compress
"""))
    expected_length = len(f'"{POWERSHELL}" {launch["arguments"]}') + 1
    assert launch["command_line_characters_including_nul"] == expected_length <= 1024
    fixture = tmp_path / "普通用户 中文 空格"
    fixture.mkdir()
    context_path = fixture / f"{probe_id}.json"
    context_path.write_text(json.dumps({"name": "本轮专用数据"}, ensure_ascii=False), encoding="utf-8")
    (fixture / f"{probe_id}.ps1").write_text(r"""
param([string]$ContextPath)
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$data=Get-Content -LiteralPath $ContextPath -Raw -Encoding UTF8|ConvertFrom-Json
@{context_path=$ContextPath;fixture_text='普通用户首启';data_name=$data.name}|ConvertTo-Json -Compress
""", encoding="utf-8")
    result = subprocess.run(
        [POWERSHELL, *launch["arguments"].split()], cwd=fixture,
        capture_output=True, timeout=30, check=False,
    )
    assert result.returncode == 0, result.stderr.decode("utf-8", errors="replace")
    assert json.loads(result.stdout.decode("utf-8")) == {
        "context_path": str(context_path), "fixture_text": "普通用户首启", "data_name": "本轮专用数据",
    }


def test_secondary_logon_rejects_unsafe_leaf_and_total_length_over_api_limit():
    before_invocation = HARNESS[:HARNESS.index("$scenario=$env:PARTYOPS_UNIT_SCENARIO")]
    script = r"""
$leaf=$null;$length=$null
try{Get-NativeSecondaryLogonArguments '../untrusted' 'C:\Windows\powershell.exe'}catch{$leaf=$_.Exception.Message}
try{Get-NativeSecondaryLogonArguments ('standard-user-probe-'+('a'*32)) ('C:\'+('x'*1024)+'.exe')}catch{$length=$_.Exception.Message}
@{leaf=$leaf;length=$length}|ConvertTo-Json -Compress
"""
    assert json.loads(run_powershell(before_invocation + script)) == {
        "leaf": "NATIVE_PROBE_ID_INVALID", "length": "NATIVE_SECONDARY_LOGON_COMMAND_TOO_LONG",
    }


def test_profile_environment_uses_sid_registration_and_does_not_change_controller():
    before_invocation = HARNESS[:HARNESS.index("$scenario=$env:PARTYOPS_UNIT_SCENARIO")]
    script = r"""
$before=@{USERPROFILE=$env:USERPROFILE;LOCALAPPDATA=$env:LOCALAPPDATA;USERNAME=$env:USERNAME}
$record=@{sid='S-1-5-21-111-222-333-1007';username='PartyOpsNativeQA'}
$context=@{machine_name='FIXTURE';native_workspace='E:\native\fixture\PartyOpsNativeQA'}
function Test-Path {param($LiteralPath)
  return $LiteralPath -in @('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\S-1-5-21-111-222-333-1007','C:\Users\PartyOpsNativeQA')
}
function Get-ItemProperty {param($LiteralPath)
  if(-not $LiteralPath.EndsWith($record.sid)){throw 'PROFILE_LOOKUP_NOT_BOUND_TO_TARGET_SID'}
  return @{ProfileImagePath='C:\Users\PartyOpsNativeQA'}
}
$target=Get-NativeProfileEnvironment $context $record
$unchanged=($before.USERPROFILE -eq $env:USERPROFILE -and $before.LOCALAPPDATA -eq $env:LOCALAPPDATA -and $before.USERNAME -eq $env:USERNAME)
@{environment=$target;controller_unchanged=$unchanged}|ConvertTo-Json -Compress
"""
    result = json.loads(run_powershell(before_invocation + script))
    assert result["controller_unchanged"] is True
    assert result["environment"] == {
        "USERPROFILE": r"C:\Users\PartyOpsNativeQA",
        "LOCALAPPDATA": r"C:\Users\PartyOpsNativeQA\AppData\Local",
        "APPDATA": r"C:\Users\PartyOpsNativeQA\AppData\Roaming",
        "USERNAME": "PartyOpsNativeQA", "USERDOMAIN": "FIXTURE",
        "HOMEDRIVE": "C:", "HOMEPATH": r"\Users\PartyOpsNativeQA",
        "TEMP": r"E:\native\fixture\PartyOpsNativeQA\temp",
        "TMP": r"E:\native\fixture\PartyOpsNativeQA\temp",
    }


def test_receipt_mismatch_preserves_original_child_failure_without_accepting_identity():
    before_invocation = HARNESS[:HARNESS.index("$scenario=$env:PARTYOPS_UNIT_SCENARIO")]
    script = r"""
$record=@{sid='expected-sid'};$context=@{session_id=1;app_sha256='current-hash'}
$result=@{sid='different-sid';session_id=1;app_sha256='current-hash';status='failed';error='NATIVE_CHILD_PROFILE_MISMATCH'}
$failure=$null
try{Assert-NativeProbeReceipt $result $record $context}catch{$failure=$_.Exception.Message}
@{failure=$failure}|ConvertTo-Json -Compress
"""
    assert json.loads(run_powershell(before_invocation + script))["failure"] == (
        "NATIVE_PROBE_RECEIPT_IDENTITY_MISMATCH; child_error=NATIVE_CHILD_PROFILE_MISMATCH"
    )


@pytest.mark.parametrize("exit_code", [0, 7])
def test_windows_powershell_retains_real_process_exit_code(exit_code, tmp_path):
    """运行纯 OS 的 cmd /c exit，验证快速退出后的真实 Handle 与退出码，不运行产品。"""
    before_invocation = HARNESS[:HARNESS.index("$scenario=$env:PARTYOPS_UNIT_SCENARIO")]
    script = r"""
$child=Get-NativeProbeScript
$tokens=$null;$errors=$null
$childAst=[Management.Automation.Language.Parser]::ParseInput($child,[ref]$tokens,[ref]$errors)
$node=$childAst.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Start-NativeInstalledProcess'},$false)
. ([scriptblock]::Create($node.Extent.Text))
$directory='__DIRECTORY__'
$process=Start-NativeInstalledProcess (Join-Path $env:WINDIR 'System32\cmd.exe') '/d /c exit __EXIT_CODE__' $directory (Join-Path $directory 'stdout.log') (Join-Path $directory 'stderr.log')
if(-not $process.WaitForExit(10000)){throw 'FIXTURE_TIMEOUT'}
$process.Refresh()
@{exit_code=$process.ExitCode;has_exited=$process.HasExited;handle_available=($process.Handle -ne [IntPtr]::Zero)}|ConvertTo-Json -Compress
"""
    script = script.replace("__DIRECTORY__", str(tmp_path).replace("'", "''")).replace("__EXIT_CODE__", str(exit_code))
    assert json.loads(run_powershell(before_invocation + script)) == {
        "exit_code": exit_code, "has_exited": True, "handle_available": True,
    }
