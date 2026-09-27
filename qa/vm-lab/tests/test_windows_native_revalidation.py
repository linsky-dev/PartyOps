"""新轮次沿用合同；只加载函数、合成报告与 mock 账户，不操作真实用户或产品。"""
from __future__ import annotations

import json

import pytest
from evidence import sha256, write_json
from test_windows_native_user import HARNESS, run_powershell

FUNCTIONS = r"""
$env:PSModulePath=(Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\Modules')+';'+(Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules')
# 先完成原生模块自动加载，避免 ConvertTo-Json 导入 Utility 时覆盖随后定义的 Get-FileHash mock。
Import-Module Microsoft.PowerShell.Utility
Import-Module Microsoft.PowerShell.Management
""" + HARNESS[:HARNESS.index("$scenario=$env:PARTYOPS_UNIT_SCENARIO")]
FIXTURE = r"""
$context=[pscustomobject]@{run_directory='D:\PartyOps-VM-Lab\reports\win11-x64-native\native-new-fixture';
 native_workspace='E:\codex\PartyOps\.partyops-vm-lab\native\win11-x64\native-new-fixture\PartyOpsNativeQA';
 state_directory='D:\PartyOps-VM-Lab\state\native\PartyOpsNativeQA';machine_name='FIXTURE';hardware_uuid='host-uuid';controller_sid='controller-sid';
 install_result_sha256=('a'*64);install_binding_sha256=('b'*64);protection_sha256=('c'*64);app_sha256=('d'*64);wizard_sha256=('e'*64);
 build_binding=@{source_fingerprint=('f'*64);package=@{sha256=('1'*64)}}}
$owner=@{schema_version=1;purpose='PartyOps native standard-user validation';status='prepared';username='PartyOpsNativeQA';sid='user-sid';ownership_id=('2'*32);
 run_directory='D:\PartyOps-VM-Lab\reports\win11-x64-native\native-old-fixture';native_workspace='E:\codex\PartyOps\.partyops-vm-lab\native\win11-x64\native-old-fixture\PartyOpsNativeQA';
 data_directory=('E:\codex\PartyOps\.partyops-vm-lab\native\win11-x64\native-old-fixture\PartyOpsNativeQA\中文 空格业务数据-'+('2'*32));
 machine_name='FIXTURE';hardware_uuid='host-uuid';controller_sid='controller-sid';credential_sha256=('3'*64)}
$original=@{record=$owner;path='D:\PartyOps-VM-Lab\state\native\PartyOpsNativeQA\private\ownership.json';sha256=('4'*64)}
$session=[pscustomobject]@{schema_version=2;purpose='PartyOps native standard-user revalidation';status='prepared';runtime_environment_passed=$false;
 username=$owner.username;sid=$owner.sid;ownership_id=$owner.ownership_id;run_directory=$context.run_directory;native_workspace=$context.native_workspace;
 machine_name=$context.machine_name;hardware_uuid=$context.hardware_uuid;controller_sid=$context.controller_sid;
 original_ownership_path=$original.path;original_ownership_sha256=$original.sha256;original_run=$owner.run_directory;original_data_directory=$owner.data_directory;
 install_result_sha256=$context.install_result_sha256;install_binding_sha256=$context.install_binding_sha256;protection_sha256=$context.protection_sha256;
 source_fingerprint=$context.build_binding.source_fingerprint;package_sha256=$context.build_binding.package.sha256;app_sha256=$context.app_sha256;wizard_sha256=$context.wizard_sha256;
 data_directory=$owner.data_directory;data_mode='Retain';account_changed=$false;credential_changed=$false}
"""


@pytest.mark.parametrize("field", ["sid", "run_directory", "native_workspace", "hardware_uuid", "controller_sid", "original_ownership_path",
                                  "original_ownership_sha256", "original_run", "original_data_directory", "install_result_sha256",
                                  "install_binding_sha256", "protection_sha256", "source_fingerprint", "package_sha256", "app_sha256", "wizard_sha256"])
def test_session_rejects_foreign_or_stale_binding_before_writes(field):
    source = FIXTURE + r"""
function Write-NativeJson {throw 'MUST_NOT_MUTATE'}
function Write-NewNativeJson {throw 'MUST_NOT_MUTATE'}
function New-LocalUser {throw 'MUST_NOT_CREATE_USER'}
$session.__FIELD__='foreign'
$failure=$null
try{Assert-NativeRevalidationSession $session $context $original}catch{$failure=$_.Exception.Message}
@{error=$failure}|ConvertTo-Json -Compress
"""
    result = json.loads(run_powershell(FUNCTIONS + source.replace("__FIELD__", field)))
    assert result["error"] == "NATIVE_REVALIDATION_SESSION_BINDING_MISMATCH"


def test_retain_and_fresh_allow_only_registered_data_and_do_not_claim_lifecycle():
    source = FIXTURE + r"""
$values=@()
foreach($mode in @('Retain','Fresh','foreign','wrong-data','missing-build','claimed-pass')){
 $session.data_mode='Retain';$session.data_directory=$owner.data_directory;$session.runtime_environment_passed=$false
 if($mode -eq 'Fresh'){$session.data_mode='Fresh';$session.data_directory=Join-Path $context.native_workspace ('中文 空格业务数据-'+$owner.ownership_id)}
 if($mode -eq 'foreign'){$session.data_mode='Unknown'}
 if($mode -eq 'wrong-data'){$session.data_directory='D:\unrelated-business'}
 if($mode -eq 'missing-build'){$context.build_binding=$null}else{$context.build_binding=@{source_fingerprint=('f'*64);package=@{sha256=('1'*64)}}}
 if($mode -eq 'claimed-pass'){$session.runtime_environment_passed=$true}
 $failure=$null
 try{Assert-NativeRevalidationSession $session $context $original}catch{$failure=$_.Exception.Message}
 $values+=@{mode=$mode;error=$failure}
}
$values|ConvertTo-Json -Compress
"""
    result = json.loads(run_powershell(FUNCTIONS + source))
    assert [item["error"] for item in result] == [None, None, "NATIVE_REVALIDATION_DATA_MODE_INVALID",
        "NATIVE_REVALIDATION_DATA_DIRECTORY_MISMATCH", "NATIVE_REVALIDATION_TRUSTED_INSTALL_REQUIRED", "NATIVE_REVALIDATION_SESSION_BINDING_MISMATCH"]


@pytest.mark.parametrize("mode", ["Retain", "Fresh"])
def test_prepare_registers_new_session_without_changing_original_account_credential_or_data(mode):
    source = FIXTURE + r"""
$script:sessionWritten=$null;$script:directories=@();$script:aclPaths=@();$script:backupChecked=$false
$script:ownerBefore=$owner|ConvertTo-Json -Compress
function Get-NativeOriginalOwnership {param($Context) return $original}
function Assert-NativeRevalidationProtection {param($Context,$Original,[switch]$VerifySource) if(-not $VerifySource){throw 'PREPARE_REQUIRES_SOURCE_BACKUP_CHECK'};$script:backupChecked=$true}
function Test-Path {param($LiteralPath) return $false}
function Get-CimInstance {param($ClassName) return @()}
function Get-PSDrive {param($Name) if($Name -ne 'E'){throw 'WRONG_DRIVE'};return @{Free=40GB}}
function New-Item {param($ItemType,$Path) if(-not $script:backupChecked){throw 'WRITE_BEFORE_BACKUP_CHECK'};$script:directories+=,$Path}
function Set-NativeDirectoryAcl {param($Path,$UserSid,$Rights) if($UserSid -ne $owner.sid){throw 'WRONG_SID'};$script:aclPaths+=,$Path}
function Write-NewNativeJson {param($Path,$Value)
 if($Path -ne (Join-Path $context.run_directory 'native-user-session.json')){throw 'WRITE_OLD_OR_FOREIGN_STATE'}
 $script:sessionWritten=$Value
}
function Read-NativeRevalidationSession {param($Context) Assert-NativeRevalidationSession $script:sessionWritten $Context $original;return $script:sessionWritten}
function Write-NativeJson {throw 'MUST_NOT_OVERWRITE_OLD_RECORD'}
function New-LocalUser {throw 'MUST_NOT_CREATE_USER'}
function Add-LocalGroupMember {throw 'MUST_NOT_CHANGE_GROUP'}
function Export-Clixml {throw 'MUST_NOT_WRITE_CREDENTIAL'}
function Start-Process {throw 'MUST_NOT_START_PRODUCT'}
$result=Prepare-NativeRevalidation $context '__MODE__'
@{record=$result;directories=$script:directories;acl_paths=$script:aclPaths;owner_unchanged=(($owner|ConvertTo-Json -Compress) -eq $script:ownerBefore)}|ConvertTo-Json -Depth 8 -Compress
"""
    result = json.loads(run_powershell(FUNCTIONS + source.replace("__MODE__", mode)))
    session = result["record"]
    assert result["owner_unchanged"] is True
    assert session["data_mode"] == mode and session["account_changed"] is False and session["credential_changed"] is False
    assert session["runtime_environment_passed"] is False
    assert len(result["directories"]) == (3 if mode == "Retain" else 4)
    assert all("native-new-fixture" in path for path in result["directories"] + result["acl_paths"])
    assert ("native-old-fixture" in session["data_directory"]) == (mode == "Retain")


def test_resume_uses_existing_bound_session_and_refuses_mode_change_without_mutation():
    source = FIXTURE + r"""
function Test-Path {param($LiteralPath) return $LiteralPath.EndsWith('native-user-session.json')}
function Read-NativeRevalidationSession {param($Context) Assert-NativeRevalidationSession $session $Context $original;return $session}
function New-Item {throw 'RESUME_MUST_NOT_MUTATE'}
function Write-NewNativeJson {throw 'RESUME_MUST_NOT_OVERWRITE'}
$same=Prepare-NativeRevalidation $context
$failure=$null
try{Prepare-NativeRevalidation $context 'Fresh'|Out-Null}catch{$failure=$_.Exception.Message}
@{same_sid=($same.sid -eq $owner.sid);error=$failure}|ConvertTo-Json -Compress
"""
    assert json.loads(run_powershell(FUNCTIONS + source)) == {"same_sid": True, "error": "NATIVE_REVALIDATION_DATA_MODE_CHANGED_USE_NEW_RUN"}


@pytest.mark.parametrize("change", ["none", "backup", "source", "unbacked", "owner", "protection"])
def test_real_backup_files_are_required_before_revalidation(tmp_path, change):
    run = tmp_path / "new-run"
    config = tmp_path / "profile/AppData/Local/PartyOps"
    data = tmp_path / "old-run/data"
    config.mkdir(parents=True)
    data.mkdir(parents=True)
    sources = [(config, "profile"), (data, "data")]
    records = []
    for source, label in sources:
        payload = source / "fixture.txt"
        payload.write_bytes(b"synthetic registered config/data")
        backup = run / "protected.local" / label
        backup.mkdir(parents=True)
        (backup / payload.name).write_bytes(payload.read_bytes())
        records.append({"source": str(source), "backup": str(backup), "verified": True,
                        "files": [{"relative_path": payload.name, "bytes": payload.stat().st_size, "sha256": sha256(payload)}]})
    protection = {"target": "win11-x64-native", "environment_type": "native-host", "protected": records,
                  "existing_native_account": {"username": "PartyOpsNativeQA", "sid": "user-sid", "ownership_id": "2" * 32,
                   "ownership_sha256": "4" * 64, "original_run": "old-run", "data_directory": str(data), "account_changed": False, "credential_changed": False}}
    path = run / "protection.json"
    write_json(path, protection)
    checksum = sha256(path)
    if change == "backup":
        (run / "protected.local/data/fixture.txt").write_bytes(b"changed")
    elif change == "source":
        (data / "fixture.txt").write_bytes(b"changed")
    elif change == "unbacked":
        (data / "not-backed-up.txt").write_bytes(b"new")
    elif change == "owner":
        protection["existing_native_account"]["sid"] = "foreign"
        write_json(path, protection)
        checksum = sha256(path)
    elif change == "protection":
        checksum = "f" * 64
    payload = {"run": str(run), "local": str(config.parent), "data": str(data), "sha256": checksum}
    source = FIXTURE + "$fixture='" + json.dumps(payload).replace("'", "''") + "'|ConvertFrom-Json\n" + r"""
$context.run_directory=$fixture.run;$context.protection_sha256=$fixture.sha256
$owner.run_directory='old-run';$owner.data_directory=$fixture.data
function Get-NativeProfileEnvironment {param($Context,$Owner) return @{LOCALAPPDATA=$fixture.local}}
$failure=$null
try{Assert-NativeRevalidationProtection $context $original -VerifySource}catch{$failure=$_.Exception.Message}
@{error=$failure}|ConvertTo-Json -Compress
"""
    expected = {"none": None, "backup": "NATIVE_REVALIDATION_BACKUP_CHANGED", "source": "NATIVE_REVALIDATION_SOURCE_CHANGED_AFTER_BACKUP",
                "unbacked": "NATIVE_REVALIDATION_UNBACKED_SOURCE_FILE", "owner": "NATIVE_REVALIDATION_BACKUP_OWNERSHIP_MISMATCH",
                "protection": "NATIVE_REVALIDATION_PROTECTION_CHANGED"}
    assert json.loads(run_powershell(FUNCTIONS + source))["error"] == expected[change]


def test_new_session_write_never_overwrites_existing_evidence(tmp_path):
    path = tmp_path / "session.json"
    path.write_bytes(b"old evidence")
    source = "$path='" + str(path).replace("'", "''") + "'\n" + r"""
$failure=$null
try{Write-NewNativeJson $path @{status='prepared'}}catch{$failure=$_.Exception.Message}
@{blocked=($null -ne $failure)}|ConvertTo-Json -Compress
"""
    assert json.loads(run_powershell(FUNCTIONS + source))["blocked"] is True
    assert path.read_bytes() == b"old evidence"


@pytest.mark.parametrize("changed", [False, True])
def test_reuse_reads_original_credential_and_detects_rotation_without_recreating_it(changed):
    source = FIXTURE + r"""
function Resolve-NativeChild {param($Root,$Candidate) return $Candidate}
function Get-Content {param($LiteralPath,[switch]$Raw,$Encoding)
 if($LiteralPath -ne $original.path){throw 'UNEXPECTED_PRIVATE_READ'};return $owner|ConvertTo-Json -Depth 8
}
function Get-LocalUser {param($Name,$ErrorAction) return @{SID=@{Value=$owner.sid};Description=('PartyOps QA '+$owner.ownership_id);Enabled=$true}}
function Get-NativeMembership {param($Sid) if($Sid -ne $owner.sid){throw 'WRONG_SID'}}
function Test-Path {param($LiteralPath) return $true}
function Get-FileHash {param($LiteralPath,$Algorithm)
 if($LiteralPath.EndsWith('ownership.json')){return @{Hash=('4'*64)}}
 if($LiteralPath.EndsWith('credential.clixml')){return @{Hash=('__HASH__'*64)}}
 throw 'UNEXPECTED_HASH_READ'
}
function Import-Clixml {param($LiteralPath)
 $password=[Security.SecureString]::new();$password.AppendChar('x');$password.MakeReadOnly()
 return [pscredential]::new('FIXTURE\PartyOpsNativeQA',$password)
}
function Export-Clixml {throw 'MUST_NOT_REWRITE_CREDENTIAL'}
function New-LocalUser {throw 'MUST_NOT_CREATE_USER'}
function Write-NativeJson {throw 'MUST_NOT_CHANGE_ORIGINAL_OWNERSHIP'}
$failure=$null;$value=$null
try{$value=Get-NativeOriginalOwnership $context}catch{$failure=$_.Exception.Message}
@{error=$failure;old_run=$(if($value){$value.record.run_directory}else{$null})}|ConvertTo-Json -Compress
"""
    result = json.loads(run_powershell(FUNCTIONS + source.replace("__HASH__", "5" if changed else "3")))
    assert result["error"] == ("NATIVE_CREATION_CREDENTIAL_CHANGED" if changed else None)
    if not changed:
        assert result["old_run"].endswith("native-old-fixture")
