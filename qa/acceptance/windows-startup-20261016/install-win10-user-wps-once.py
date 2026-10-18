"""原Win10普通用户一次性WPS安装QA；只用已核实asInvoker内层，不修改安装器或ACL。"""
from __future__ import annotations
import argparse
import importlib.util
import json
import re
import sys
import uuid
from pathlib import Path
import retention_contract as contract

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = Path(__file__).resolve().parent
LAB_ROOT = Path("D:/PartyOps-VM-Lab")
INSTALLER = r"C:\ProgramData\kingsoft\20261017_140008\WPS_Setup_x64_28488.exe"
INSTALLER_SHA = "5e574529af84aa0d5a7916e1ee4fec00a3ede12ac213781b79b5318ced464bd8"
INSTALLER_SIZE = 346496264
ATTEMPT = OUTPUT / "wps-ordinary-install.attempt.json"
sys.path.insert(0, str(ROOT / "qa/vm-lab"))


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


fonts = load(OUTPUT / "stage-win10-qa-fonts.py", "ordinary_wps_task_helpers")


def request():
    suffix = uuid.uuid4().hex[:12]
    return {"uuid": contract.UUID, "username": "partyopsuser", "installer": INSTALLER,
        "sha256": INSTALLER_SHA, "bytes": INSTALLER_SIZE,
        "transport": r"C:\Users\partyopsuser\AppData\Local\PartyOpsQa\wps-install-" + suffix,
        "task": "PartyOps-QA-Ordinary-WPS-" + suffix}


def source_proof_script():
    return r"""
$source=Get-Item -LiteralPath $request.installer
if($source.Length -ne $request.bytes -or (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $request.sha256){throw 'FIXED_INNER_WPS_INSTALLER_CHANGED'}
$signature=Get-AuthenticodeSignature -LiteralPath $source.FullName
if($signature.Status.ToString() -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Kingsoft'){throw 'VALID_KINGSOFT_INSTALLER_REQUIRED'}
# LOAD_LIBRARY_AS_DATAFILE只读取资源，不运行EXE；重新核对实际RT_MANIFEST。
Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public class QAWpsResource{[DllImport("kernel32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr LoadLibraryEx(string p,IntPtr f,uint flags);[DllImport("kernel32.dll")]public static extern IntPtr FindResource(IntPtr h,IntPtr name,IntPtr type);[DllImport("kernel32.dll")]public static extern IntPtr LoadResource(IntPtr h,IntPtr r);[DllImport("kernel32.dll")]public static extern IntPtr LockResource(IntPtr r);[DllImport("kernel32.dll")]public static extern uint SizeofResource(IntPtr h,IntPtr r);[DllImport("kernel32.dll")]public static extern bool FreeLibrary(IntPtr h);}'
$module=[QAWpsResource]::LoadLibraryEx($source.FullName,[IntPtr]::Zero,2)
if($module -eq [IntPtr]::Zero){throw 'INSTALLER_RESOURCE_READ_FAILED'}
try{
  $resource=[QAWpsResource]::FindResource($module,[IntPtr]1,[IntPtr]24)
  $size=[QAWpsResource]::SizeofResource($module,$resource)
  if($resource -eq [IntPtr]::Zero -or $size -eq 0){throw 'INSTALLER_MANIFEST_REQUIRED'}
  $buffer=New-Object byte[] $size
  [Runtime.InteropServices.Marshal]::Copy([QAWpsResource]::LockResource([QAWpsResource]::LoadResource($module,$resource)),$buffer,0,$buffer.Length)
  $memory=New-Object IO.MemoryStream(,$buffer)
  try{$xml=New-Object Xml.XmlDocument;$xml.Load($memory)}finally{$memory.Close()}
  $level=$xml.SelectSingleNode("//*[local-name()='requestedExecutionLevel']")
  if($level.level -ne 'asInvoker' -or $level.uiAccess -ne 'false'){throw 'ACTUAL_ASINVOKER_INSTALLER_REQUIRED'}
  $hash=[Security.Cryptography.SHA256]::Create()
  try{$manifestHash=([BitConverter]::ToString($hash.ComputeHash($buffer))).Replace('-','').ToLowerInvariant()}finally{$hash.Dispose()}
  $sourceProof=@{sha256=$request.sha256;bytes=$source.Length;signature_status=$signature.Status.ToString();signer_is_kingsoft=$true;requested_execution_level=[string]$level.level;ui_access=[string]$level.uiAccess;manifest_sha256=$manifestHash}
}finally{[QAWpsResource]::FreeLibrary($module)|Out-Null}
"""


def precheck_script(value):
    return fonts.prefix(value) + source_proof_script() + r"""
$userSoftware='Registry::HKEY_USERS\'+$sid+'\Software'
$vendor=Join-Path $userSoftware 'Kingsoft\Office\6.0\Common'
$root=if(Test-Path -LiteralPath $vendor){[string](Get-Item -LiteralPath $vendor).GetValue('InstallRoot')}else{$null}
$uninstall=Join-Path $userSoftware 'Microsoft\Windows\CurrentVersion\Uninstall'
$apps=@(if(Test-Path -LiteralPath $uninstall){Get-ChildItem -LiteralPath $uninstall|ForEach-Object {Get-ItemProperty -LiteralPath $_.PSPath}|Where-Object {$_.DisplayName -match 'WPS Office'}})
$ordinaryCom=Test-Path -LiteralPath (Join-Path $userSoftware 'Classes\KWPS.Application\CLSID')
$appPath=Test-Path -LiteralPath (Join-Path $userSoftware 'Microsoft\Windows\CurrentVersion\App Paths\wps.exe')
$installers=@(Get-CimInstance Win32_Process|Where-Object {$_.Name -in @('WPS_Setup_x64_28488.exe','setup_CN_2052_12.1.0.28488_x64_Unified.exe','wpssetup.exe')}|ForEach-Object {
  $owner=Invoke-CimMethod -InputObject $_ -MethodName GetOwner
  @{pid=[int]$_.ProcessId;name=$_.Name;owner=if($owner.ReturnValue -eq 0){$owner.User}else{'unknown'};session_id=[int]$_.SessionId}
})
@{guest_uuid=$marker.uuid;username='partyopsuser';session_id=$desktop[0];standard_account=$true;source=$sourceProof;ordinary_wps_registered=([bool]$root -or $apps.Count -gt 0 -or $ordinaryCom -or $appPath);installer_processes=$installers}|ConvertTo-Json -Depth 6 -Compress
"""


def validate_precheck(value):
    source = value.get("source", {})
    if not (value.get("guest_uuid") == contract.UUID and value.get("username") == "partyopsuser"
            and value.get("standard_account") is True and value.get("session_id", 0) > 0
            and source.get("sha256") == INSTALLER_SHA and source.get("bytes") == INSTALLER_SIZE
            and source.get("signature_status") == "Valid" and source.get("signer_is_kingsoft") is True
            and source.get("requested_execution_level") == "asInvoker" and source.get("ui_access") == "false"
            and re.fullmatch(r"[a-f0-9]{64}", source.get("manifest_sha256", ""))):
        raise RuntimeError("ORIGINAL_USER_ASINVOKER_INSTALLER_PROOF_REQUIRED")
    if value.get("ordinary_wps_registered") is not False:
        raise RuntimeError("ORDINARY_USER_WPS_ALREADY_REGISTERED_INSPECT_ONLY")
    if value.get("installer_processes") != []:
        raise RuntimeError("INSTALLER_PROCESS_STILL_PRESENT_MANUAL_INSPECTION_REQUIRED")


def user_script(value):
    return fonts.prefix(value) + r"""
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($identity)
$session=[Diagnostics.Process]::GetCurrentProcess().SessionId
if($identity.User.Value -ne $sid -or $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or $session -ne $desktop[0]){throw 'ORIGINAL_INTERACTIVE_LUA_REQUIRED'}
function Save-New([string]$name,$value){
  $path=Join-Path $request.transport $name
  $bytes=[Text.Encoding]::UTF8.GetBytes(($value|ConvertTo-Json -Depth 6 -Compress))
  $stream=New-Object IO.FileStream($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
  try{$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Close()}
}
try{
""" + source_proof_script() + r"""
  if(Test-Path -LiteralPath 'HKCU:\Software\Kingsoft\Office\6.0\Common'){
    if((Get-Item -LiteralPath 'HKCU:\Software\Kingsoft\Office\6.0\Common').GetValue('InstallRoot')){throw 'ORDINARY_WPS_ALREADY_PRESENT'}
  }
  if((Test-Path -LiteralPath 'HKCU:\Software\Classes\KWPS.Application\CLSID') -or (Test-Path -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\wps.exe')){throw 'ORDINARY_WPS_ALREADY_REGISTERED'}
  if(@(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue|Where-Object {$_.DisplayName -match 'WPS Office'}).Count){throw 'ORDINARY_WPS_UNINSTALL_ENTRY_EXISTS'}
  if(@(Get-CimInstance Win32_Process|Where-Object {$_.Name -in @('WPS_Setup_x64_28488.exe','setup_CN_2052_12.1.0.28488_x64_Unified.exe','wpssetup.exe')}).Count){throw 'EXISTING_INSTALLER_REQUIRES_MANUAL_INSPECTION'}
  Save-New 'started.json' @{status='starting';username=$env:USERNAME;sid=$sid;session_id=$session;administrator=$false;source=$sourceProof}
  # 原内层真实asInvoker；不使用提权、兼容垫片、ACL改写或安装器修改。
  $process=Start-Process -FilePath $request.installer -ArgumentList '/S' -WindowStyle Hidden -PassThru
  $null=$process.Handle
  Save-New 'process.json' @{status='running';installer_pid=$process.Id;username=$env:USERNAME;administrator=$false}
  $process.WaitForExit();$process.Refresh()
  $exit=$process.ExitCode
  $vendor='HKCU:\Software\Kingsoft\Office\6.0\Common'
  $root=if(Test-Path -LiteralPath $vendor){[string](Get-Item -LiteralPath $vendor).GetValue('InstallRoot')}else{$null}
  $ownRoot=$root -and $root.StartsWith(($env:LOCALAPPDATA+'\Kingsoft\'),[StringComparison]::OrdinalIgnoreCase)
  $exe=if($root){Join-Path $root 'office6\wps.exe'}else{$null}
  $exeHash=if($exe -and (Test-Path -LiteralPath $exe -PathType Leaf)){(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()}else{$null}
  $result=@{status='installer-exited-needs-acceptance';exit_code=$exit;username=$env:USERNAME;sid=$sid;session_id=$session;administrator=$false;source=$sourceProof;vendor_install_root=$root;own_user_profile_scope=[bool]$ownRoot;installed_executable_sha256=$exeHash;user_com_registered=(Test-Path -LiteralPath 'HKCU:\Software\Classes\KWPS.Application\CLSID');gui_confirmation='manual-if-required';wps_acceptance_passed=$false}
}catch{$result=@{status='failed-inspect-without-relaunch';error_type=$_.Exception.GetType().FullName;username=$env:USERNAME;administrator=$false;wps_acceptance_passed=$false}}
Save-New 'result.json' $result
"""


def launch_script(value):
    # 直接复用已审查字体工具的InteractiveToken/LUA/TASK_CREATE机制，只换本任务脚本名。
    return fonts.launch_script(value).replace("'fonts.ps1'", "'install.ps1'")


def status_script(value):
    return fonts.prefix(value) + r"""
$scheduler=New-Object -ComObject 'Schedule.Service';$scheduler.Connect()
$task=$scheduler.GetFolder('\').GetTask($request.task)
$records=@{}
foreach($name in @('started.json','process.json','result.json')){
  $path=Join-Path $request.transport $name
  if(Test-Path -LiteralPath $path){$records[$name]=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json}
}
@{guest_uuid=$marker.uuid;username='partyopsuser';task_state=[int]$task.State;last_task_result=$task.LastTaskResult;records=$records;wps_acceptance_passed=$false}|ConvertTo-Json -Depth 8 -Compress
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["inspect", "start", "status"], nargs="?", default="inspect")
    parser.add_argument("--execute-start", action="store_true")
    args = parser.parse_args()
    if args.execute_start != (args.action == "start"):
        raise RuntimeError("EXPLICIT_ORDINARY_INSTALL_START_REQUIRED")
    if args.action == "start" and ATTEMPT.exists():
        raise RuntimeError("ORDINARY_INSTALL_ALREADY_ATTEMPTED_STATUS_ONLY")
    value = contract.read(ATTEMPT)["request"] if args.action == "status" else request()
    windows = load(ROOT / "qa/vm-lab/scripts/exercise-windows-business.py", "ordinary_wps_windows")
    matrix, media = windows.load_configuration()
    matrix["defaults"].update(primary_root=str(LAB_ROOT), fallback_root=str(LAB_ROOT))
    lab = windows.QemuLab(matrix, LAB_ROOT, media)
    contract.bind_guest(lab, windows.qmp)
    client = windows.windows.connect(lab, matrix["targets"]["win10-x64"])
    try:
        if args.action == "status":
            result = fonts.execute_script(client, windows.windows.execute, status_script(value))
        else:
            result = fonts.execute_script(client, windows.windows.execute, precheck_script(value))
            if args.action == "start":
                validate_precheck(result)
                contract.bind_guest(lab, windows.qmp)
                script = user_script(value)
                contract.exclusive(ATTEMPT, {"time_utc": contract.now(), "request": value, "precheck": result,
                    "operation": "ordinary-user-real-asInvoker-inner-installer-once", "automatic_retry": False})
                windows.windows.execute(client, "New-Item -ItemType Directory -Path '" + value["transport"] + "' -ErrorAction Stop|Out-Null")
                with client.open_sftp() as fs, fs.file(value["transport"].replace("\\", "/") + "/install.ps1", "wx") as stream:
                    stream.write(script.encode("utf-8"))
                result = fonts.execute_script(client, windows.windows.execute, launch_script(value))
        path = OUTPUT / ("wps-ordinary-" + args.action + "-" + uuid.uuid4().hex[:12] + ".json")
        contract.exclusive(path, {"time_utc": contract.now(), "operation": args.action, "result": result, "wps_acceptance_passed": False})
        print(json.dumps({"status": "recorded-only", "operation": args.action, "evidence": str(path),
                          "wps_acceptance_passed": False, "next": "status；如需GUI确认交主控"}, ensure_ascii=False))
    finally:
        client.close()


if __name__ == "__main__":
    main()
