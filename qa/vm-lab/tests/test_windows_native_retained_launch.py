"""仅执行抽出的普通用户启动函数和 mock OS；不启动安装版或接触真实账户。"""
from __future__ import annotations

import json

import pytest
from test_windows_native_revalidation import FUNCTIONS
from test_windows_native_user import run_powershell


@pytest.mark.parametrize("scenario,expected", [
    ("parallel-other-user", None), ("occupied-port", "NATIVE_RETAIN_PORT_ALREADY_IN_USE"),
    ("old-owned-process", "NATIVE_RETAIN_OLD_RUNTIME_STILL_RUNNING"),
    ("invalid-port", "NATIVE_RETAIN_PORT_INVALID"), ("stale-process", "NATIVE_RETAIN_RUNTIME_IDENTITY_MISMATCH"),
    ("foreign-runtime", "NATIVE_RETAIN_RUNTIME_IDENTITY_MISMATCH"),
    ("changed-exe", "NATIVE_RETAIN_RUNTIME_HASH_MISMATCH"),
    ("null-exit", "NATIVE_RETAIN_LAUNCHER_EXIT_UNAVAILABLE"), ("failed-exit", "NATIVE_RETAIN_LAUNCHER_FAILED_7"),
])
def test_retained_launcher_rejects_old_identity_and_allows_other_account_instance(scenario, expected):
    source = FUNCTIONS + r"""
$inner=Get-NativeProbeScript
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseInput($inner,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'CHILD_SCRIPT_SYNTAX_INVALID'}
$function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Start-NativeRetainedPersonal'},$false)
. ([scriptblock]::Create($function.Extent.Text))
$scenario='__SCENARIO__';$script:launched=$false
$context=@{data_mode='Retain';native_user_session_sha256=('a'*64);launcher_path='E:\owned\PartyOpsLauncher.exe';
 launcher_sha256=('b'*64);app_path='E:\owned\PartyOps.exe';app_sha256=('c'*64);data_directory='E:\owned\data';stdout_path='stdout';stderr_path='stderr'}
function Get-Content {param($LiteralPath,$Encoding,[switch]$Raw)
 if($LiteralPath -eq 'personal.env'){if($scenario -eq 'invalid-port'){return 'PARTYOPS_PORT=bad'};return 'PARTYOPS_PORT=18825'}
 return @{format_version=1;executable=$context.app_path;pid=42}|ConvertTo-Json -Compress
}
function Get-NetTCPConnection {param($LocalPort,$State,$ErrorAction)
 if($script:launched -or $scenario -eq 'occupied-port'){return @{LocalAddress='127.0.0.1';OwningProcess=42}}
 return @()
}
function Get-CimInstance {param($ClassName,$Filter)
 if($Filter -eq "Name='PartyOps.exe'"){return @{ProcessId=998;Name='PartyOps.exe'}}
 return @{ProcessId=42;ExecutablePath=$context.app_path;SessionId=2;
 CreationDate=$(if($scenario -eq 'stale-process'){[DateTime]::UtcNow.AddDays(-1)}else{[DateTime]::UtcNow})}
}
function Invoke-CimMethod {param($InputObject,$MethodName)
 if($InputObject.ProcessId -eq 998){return @{ReturnValue=0;Sid=$(if($scenario -eq 'old-owned-process'){'ordinary-sid'}else{'admin-sid'})}}
 return @{ReturnValue=0;Sid=$(if($scenario -eq 'foreign-runtime'){'admin-sid'}else{'ordinary-sid'})}
}
function Get-FileHash {param($LiteralPath,$Algorithm)
 return @{Hash=$(if($LiteralPath -eq $context.launcher_path){'b'*64}elseif($scenario -eq 'changed-exe'){'d'*64}else{'c'*64})}
}
function Start-NativeInstalledProcess {param($FilePath,$Arguments,$WorkingDirectory,$StdoutPath,$StderrPath)
 if($FilePath -ne $context.launcher_path -or $Arguments -ne '--background' -or $WorkingDirectory -ne $context.data_directory){throw 'UNEXPECTED_LAUNCH'}
 $script:launched=$true
 $process=[pscustomobject]@{ExitCode=$(if($scenario -eq 'null-exit'){$null}elseif($scenario -eq 'failed-exit'){7}else{0})}
 $process|Add-Member ScriptMethod WaitForExit {param($Timeout) return $true}
 $process|Add-Member ScriptMethod Refresh {}
 return $process
}
function Stop-Process {throw 'MUST_NOT_STOP_ANY_PROCESS'}
function New-LocalUser {throw 'MUST_NOT_CREATE_ACCOUNT'}
$failure=$null;$result=$null
try{$result=Start-NativeRetainedPersonal $context 'personal.env' 'ordinary-sid' 2}catch{$failure=$_.Exception.Message}
@{error=$failure;launched=$script:launched;runtime=$result}|ConvertTo-Json -Depth 5 -Compress
"""
    result = json.loads(run_powershell(source.replace("__SCENARIO__", scenario)))
    assert result["error"] == expected
    if expected is None:
        assert result["runtime"]["pid"] == 42 and result["runtime"]["owner_sid"] == "ordinary-sid"
        assert result["runtime"]["launcher_exit_code"] == 0
    if scenario in {"occupied-port", "old-owned-process", "invalid-port"}:
        assert result["launched"] is False
