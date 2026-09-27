"""解析并执行配置权限分支的合成边界；不创建账号、不启动产品、不修改真实 ACL。"""
from __future__ import annotations

import base64
import json
import os
import shutil
import subprocess
from pathlib import Path

import pytest

SCRIPT = Path(__file__).parents[1] / "scripts/windows-native-user.ps1"
HARNESS = r'''
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText($env:PARTYOPS_UNIT_SCRIPT,[Text.Encoding]::UTF8)
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw ($errors|Out-String)}
$function=$ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-NativeProbeScript'},$false)[0]
. ([scriptblock]::Create($function.Extent.Text))
$child=Get-NativeProbeScript
$childAst=[Management.Automation.Language.Parser]::ParseInput($child,[ref]$tokens,[ref]$errors)
if($errors.Count){throw ($errors|Out-String)}
$branches=@($childAst.FindAll({param($n) $n -is [Management.Automation.Language.IfStatementAst] -and $n.Clauses[0].Item1.Extent.Text -like '*context.action*RunConfiguredPersonalProbe*'},$true))
if($branches.Count -ne 2){throw 'PERSONAL_PERMISSION_BRANCHES_MISSING'}
$scenario=$env:PARTYOPS_UNIT_SCENARIO
$local='E:\fixture-profile\AppData\Local'
$context=[pscustomobject]@{action='RunConfiguredPersonalProbe';data_directory='E:\fixture\中文 空格数据-abc'}
$result=[ordered]@{selftest=@{mode='configured-personal-permission';passed=$true;checked=$true;data_dir_writable=$true}}
function Get-Content {param($LiteralPath,[switch]$Raw,$Encoding)
 if($LiteralPath.EndsWith('mode.json')){
  return @{mode=$(if($scenario -eq 'wrong-mode'){'host'}else{'personal'});config_path=$(if($scenario -eq 'wrong-config'){'E:\other\personal.env'}else{Join-Path $local 'PartyOps\personal.env'})}|ConvertTo-Json
 }
 if($scenario -eq 'missing-data'){return 'PARTYOPS_PORT=1234'}
 if($scenario -eq 'duplicate-data'){return @("PARTYOPS_DATA_DIR='$($context.data_directory)'","PARTYOPS_DATA_DIR='$($context.data_directory)'")}
 if($scenario -eq 'wrong-data'){return 'PARTYOPS_DATA_DIR=E:\existing-business'}
 return "PARTYOPS_DATA_DIR='$($context.data_directory)'"
}
function Get-Item {param($LiteralPath) return @{Length=100;Attributes=$(if($scenario -eq 'reparse'){[IO.FileAttributes]::ReparsePoint}else{[IO.FileAttributes]::Normal})}}
$script:hashReads=0
function Get-FileHash {param($LiteralPath,$Algorithm)
 $script:hashReads++
 return @{Hash=$(if($scenario -eq 'changed-config' -and $script:hashReads -gt 1){'b'*64}else{'a'*64})}
}
try{
 . ([scriptblock]::Create($branches[0].Clauses[0].Item2.Extent.Text.TrimStart('{').TrimEnd('}')))
 if($scenario -eq 'not-checked'){$result.selftest.checked=$false}
 if($scenario -eq 'not-writable'){$result.selftest.data_dir_writable=$false}
 . ([scriptblock]::Create($branches[1].Clauses[0].Item2.Extent.Text.TrimStart('{').TrimEnd('}')))
 @{status='accepted';arguments=$arguments;configured_personal=$result.configured_personal}|ConvertTo-Json -Depth 5 -Compress
}catch{@{status='rejected';error=$_.Exception.Message}|ConvertTo-Json -Compress}
'''


def run_case(scenario):
    shell = shutil.which("powershell.exe")
    if not shell:
        pytest.skip("仅 Windows PowerShell 支持此实际语法解析")
    env = dict(os.environ, PARTYOPS_UNIT_SCRIPT=str(SCRIPT), PARTYOPS_UNIT_SCENARIO=scenario)
    encoded = base64.b64encode(HARNESS.encode("utf-16-le")).decode("ascii")
    result = subprocess.run([shell, "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
                            env=env, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=30, check=False)
    assert result.returncode == 0, result.stderr
    return json.loads(result.stdout)


def test_configured_probe_targets_real_config_without_inheriting_other_data():
    result = run_case("valid")
    assert result["status"] == "accepted"
    assert result["arguments"] == "--startup-configured-personal-permission-self-test"
    assert result["configured_personal"]["config_sha256"] == "a" * 64


@pytest.mark.parametrize("scenario,reason", [
    ("wrong-mode", "PERSONAL_MODE_REQUIRED"), ("wrong-config", "PERSONAL_MODE_REQUIRED"),
    ("missing-data", "DATA_BINDING_MISSING"), ("duplicate-data", "DATA_BINDING_MISSING"),
    ("wrong-data", "DATA_BINDING_MISMATCH"), ("reparse", "CONFIG_FILE_INVALID"),
    ("not-checked", "PERMISSION_NOT_CHECKED"), ("not-writable", "PERMISSION_NOT_CHECKED"),
    ("changed-config", "CONFIG_CHANGED_DURING_PROBE"),
])
def test_no_skipped_probe_or_foreign_data_can_pass(scenario, reason):
    result = run_case(scenario)
    assert result["status"] == "rejected"
    assert reason in result["error"]
