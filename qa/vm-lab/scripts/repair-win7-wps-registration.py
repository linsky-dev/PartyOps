"""在原Win7的已登记普通用户下调用已签名WPS配置工具；同一任务只启动一次。"""
import argparse
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from evidence import now, write_json
from lab import load_configuration
from providers import QemuLab
from windows_remote import WinRMFiles

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--inspect', action='store_true')
args = parser.parse_args()
matrix, media = load_configuration()
lab = QemuLab(matrix, Path(matrix['defaults']['primary_root']), media)
script = r'''
Add-Type -AssemblyName System.Web.Extensions
$json=New-Object Web.Script.Serialization.JavaScriptSerializer
$name='PartyOps-QA-WPS-register-public-20260912'
$sid='S-1-5-21-1355956109-3807972244-525148999-1001'
$tool='C:\Program Files\Kingsoft\WPS Office\12.1.0.28488\office6\ksomisc.exe'
$scheduler=New-Object -ComObject Schedule.Service;$scheduler.Connect();$folder=$scheduler.GetFolder('\')
'''
if not args.inspect:
    script += r'''
$stream=[IO.File]::OpenRead($tool);$hash=[Security.Cryptography.SHA256]::Create()
try{$actual=[BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}finally{$stream.Dispose();$hash.Clear()}
if($actual -ne '0ee984596e80929f2a847fba6e7af82cc00c96c8d0a96c1045cceb840074e6a9'){throw 'WPS_TOOL_CHANGED'}
$desktops=@(Get-WmiObject Win32_Process -Filter "Name='explorer.exe'"|Where-Object {$_.GetOwnerSid().Sid -eq $sid -and $_.SessionId -gt 0})
if($desktops.Count -ne 1){throw 'STANDARD_DESKTOP_NOT_UNIQUE'}
$definition=$scheduler.NewTask(0)
$definition.RegistrationInfo.Description='PartyOps isolated WPS ordinary-user COM registration'
$definition.Principal.UserId=$sid;$definition.Principal.LogonType=3;$definition.Principal.RunLevel=0
$definition.Settings.Enabled=$true;$definition.Settings.AllowDemandStart=$true;$definition.Settings.MultipleInstances=2
$action=$definition.Actions.Create(0);$action.Path=$tool;$action.Arguments='-repaircom';$action.WorkingDirectory=Split-Path -Path $tool -Parent
# TASK_CREATE=2：已有任务即拒绝，不更新后再次启动。
$task=$folder.RegisterTaskDefinition($name,$definition,2,$sid,$null,3,$null)
$task.Run($null)|Out-Null
'''
script += r'''
$task=$folder.GetTask($name)
$key=[Microsoft.Win32.Registry]::Users.OpenSubKey($sid+'\Software\Classes\KWPS.Application\CLSID')
$clsid=$null;if($key){$clsid=[string]$key.GetValue('');$key.Close()}
$server=$null
if($clsid){$key=[Microsoft.Win32.Registry]::Users.OpenSubKey($sid+'\Software\Classes\CLSID\'+$clsid+'\LocalServer32');if($key){$server=[string]$key.GetValue('');$key.Close()}}
[Console]::WriteLine($json.Serialize(@{task=$name;state=[int]$task.State;last_result=[int]$task.LastTaskResult;run_level=[int]$task.Definition.Principal.RunLevel;clsid=$clsid;local_server=$server;runtime_environment_passed=$false}))
'''
result = json.loads(WinRMFiles(lab, 'win7-x64').powershell(script, timeout=180))
result.update(at=now(), action='inspect' if args.inspect else 'start-once')
suffix = 'observed' if args.inspect else 'started'
write_json(lab.root / f'reports/win7-x64/wps-registration-public-{suffix}-20260912.json', result)
print(json.dumps(result, ensure_ascii=False))
