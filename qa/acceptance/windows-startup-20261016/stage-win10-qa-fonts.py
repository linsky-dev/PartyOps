"""固定三份宿主字体仅用于原 Win10 QA 用户；不进入安装包，不读取凭据正文。"""
from __future__ import annotations
import argparse
import importlib.util
import json
import sys
import time
import uuid
from pathlib import Path
from types import SimpleNamespace

import retention_contract as contract

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = Path(__file__).resolve().parent
LAB_ROOT = Path("D:/PartyOps-VM-Lab")
SOURCE = Path("C:/Users/Administrator/AppData/Local/Microsoft/Windows/Fonts")
USER = "partyopsuser"
FONT_DIR = r"C:\Users\partyopsuser\AppData\Local\Microsoft\Windows\Fonts"
FONTS = (("仿宋_GB2312.ttf", "仿宋_GB2312 (TrueType)"), ("楷体_GB2312.ttf", "楷体_GB2312 (TrueType)"),
         ("方正小标宋简.TTF", "方正小标宋简体 (TrueType)"))
sys.path.insert(0, str(ROOT / "qa/vm-lab"))


def require(value, message):
    if not value:
        raise RuntimeError(message)


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def local_fonts():
    """输入固定到已授权的三文件，不接受额外路径或通配符。"""
    result = []
    for name, registry in FONTS:
        path = (SOURCE / name).resolve(strict=True)
        require(path.parent == SOURCE.resolve() and path.is_file(), "FIXED_HOST_FONT_REQUIRED")
        result.append({"filename": name, "registry_name": registry, "sha256": contract.digest(path), "bytes": path.stat().st_size})
    return result


def request(fonts, transport):
    require([f["filename"] for f in fonts] == [f[0] for f in FONTS], "ONLY_THREE_FIXED_QA_FONTS_ALLOWED")
    require(len(transport) == 12 and all(c in "0123456789abcdef" for c in transport), "OWNED_FONT_TRANSPORT_REQUIRED")
    return {"uuid": contract.UUID, "username": USER, "font_dir": FONT_DIR, "fonts": fonts,
        "transport": r"C:\Users\partyopsuser\AppData\Local\PartyOps-QA\font-stage-" + transport,
        "task": "PartyOps-QA-Fonts-20261017-" + transport}


def prefix(value):
    return "$request='" + json.dumps(value, ensure_ascii=False).replace("'", "''") + "'|ConvertFrom-Json\n" + r"""
$ErrorActionPreference='Stop'
$marker=Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw|ConvertFrom-Json
if($marker.uuid -ne $request.uuid -or $marker.purpose -ne 'disposable-qa' -or (Get-CimInstance Win32_ComputerSystemProduct).UUID -ne $request.uuid){throw 'ORIGINAL_QA_GUEST_REQUIRED'}
$account=Get-LocalUser -Name 'partyopsuser';$sid=$account.SID.Value
if(-not $account.Enabled -or @(Get-LocalGroupMember -SID 'S-1-5-32-544'|Where-Object {$_.SID.Value -eq $sid}).Count){throw 'ORIGINAL_STANDARD_ACCOUNT_REQUIRED'}
$desktop=@(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'"|ForEach-Object {
  $owner=Invoke-CimMethod -InputObject $_ -MethodName GetOwner
  if($owner.ReturnValue -eq 0 -and $owner.User -eq 'partyopsuser' -and $owner.Domain -eq $env:COMPUTERNAME){[int]$_.SessionId}
})
if($desktop.Count -ne 1 -or $desktop[0] -le 0){throw 'ORIGINAL_INTERACTIVE_DESKTOP_REQUIRED'}
"""


def inspect_script(value):
    return prefix(value) + r"""
$key=[Microsoft.Win32.Registry]::Users.OpenSubKey($sid+'\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts')
$fonts=@($request.fonts|ForEach-Object {
  $dest=Join-Path $request.font_dir $_.filename
  $hash=if(Test-Path -LiteralPath $dest){(Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash.ToLowerInvariant()}else{$null}
  $registered=if($key){$key.GetValue($_.registry_name)}else{$null}
  @{filename=$_.filename;sha256=$hash;registry_matches=($registered -eq $dest)}
})
if($key){$key.Close()}
@{guest_uuid=$marker.uuid;username=$request.username;session_id=$desktop[0];standard_account=$true;fonts=$fonts}|ConvertTo-Json -Depth 5 -Compress
"""


def pending_fonts(snapshot, fonts):
    require(snapshot.get("guest_uuid", "").lower() == contract.UUID and snapshot.get("username") == USER
            and snapshot.get("standard_account") is True and snapshot.get("session_id", 0) > 0, "ORIGINAL_STANDARD_FONT_SESSION_REQUIRED")
    rows = snapshot.get("fonts", [])
    require(len(rows) == 3 and {r["filename"] for r in rows} == {f["filename"] for f in fonts}, "THREE_FONT_INSPECTION_REQUIRED")
    by_name = {r["filename"]: r for r in rows}
    pending = []
    for font in fonts:
        row = by_name[font["filename"]]
        require(row.get("sha256") in (None, font["sha256"]), "EXISTING_FONT_HASH_CONFLICT:" + font["filename"])
        if row.get("sha256") is None or row.get("registry_matches") is not True:
            pending.append(font)
    return pending


def user_script(value):
    # 参考 install-win7-test-font.py；增加真实当前用户/SID/交互会话与摘要核对。
    return prefix(value) + r"""
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($identity)
$session=[Diagnostics.Process]::GetCurrentProcess().SessionId
if($identity.User.Value -ne $sid -or $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or $session -ne $desktop[0]){throw 'INTERACTIVE_LUA_FONT_TASK_REQUIRED'}
if((Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Fonts') -ne $request.font_dir){throw 'ORIGINAL_USER_FONT_DIRECTORY_REQUIRED'}
$receipt=Join-Path $request.transport 'result.json'
if(Test-Path -LiteralPath $receipt){throw 'FONT_TASK_RESULT_EXISTS_INSPECT_ONLY'}
try {
  # 全部预检完成后才写任一文件，遇同名不同摘要绝不覆盖。
  foreach($font in $request.fonts){
    $dest=Join-Path $request.font_dir $font.filename
    if(Test-Path -LiteralPath $dest){
      if((Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash.ToLowerInvariant() -ne $font.sha256){throw 'FONT_DESTINATION_CONFLICT'}
    }else{
      $source=Join-Path $request.transport $font.filename
      if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $font.sha256){throw 'STAGED_FONT_HASH_MISMATCH'}
    }
  }
  [IO.Directory]::CreateDirectory($request.font_dir)|Out-Null
  $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts')
  Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public class QAUserFont{[DllImport("gdi32.dll",CharSet=CharSet.Unicode)]public static extern int AddFontResourceEx(string p,uint f,IntPtr r);[DllImport("user32.dll")]public static extern bool PostMessage(IntPtr w,uint m,IntPtr a,IntPtr b);}'
  $fonts=@(foreach($font in $request.fonts){
    $dest=Join-Path $request.font_dir $font.filename
    $copied=$false;$registered=$false
    if(-not (Test-Path -LiteralPath $dest)){
      # Copy的overwrite=false保证竞态下也不覆盖既有字体。
      [IO.File]::Copy((Join-Path $request.transport $font.filename),$dest,$false);$copied=$true
    }
    if((Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash.ToLowerInvariant() -ne $font.sha256){throw 'FINAL_FONT_HASH_MISMATCH'}
    if($key.GetValue($font.registry_name) -ne $dest -or $copied){
      if([QAUserFont]::AddFontResourceEx($dest,0,[IntPtr]::Zero) -le 0){throw 'FONT_REGISTER_FAILED'}
      $key.SetValue($font.registry_name,$dest)
      $registered=$true
    }
    @{filename=$font.filename;sha256=$font.sha256;registry_matches=($key.GetValue($font.registry_name) -eq $dest);copied=$copied;registered=$registered}
  })
  $key.Close()
  if(-not [QAUserFont]::PostMessage([IntPtr]65535,0x001d,[IntPtr]::Zero,[IntPtr]::Zero)){throw 'WM_FONTCHANGE_FAILED'}
  $result=@{status='completed';guest_uuid=$marker.uuid;username=$env:USERNAME;sid=$identity.User.Value;administrator=$false;session_id=$session;fonts=$fonts;scope='HKCU-and-original-QA-user-font-directory';packaged_in_product=$false}
}catch{$result=@{status='failed';error_type=$_.Exception.GetType().FullName;packaged_in_product=$false}}
$json=$result|ConvertTo-Json -Depth 5 -Compress
$stream=New-Object IO.FileStream($receipt,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
try{$bytes=[Text.Encoding]::UTF8.GetBytes($json);$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Close()}
"""


def launch_script(value):
    return prefix(value) + r"""
$scheduler=New-Object -ComObject 'Schedule.Service';$scheduler.Connect();$folder=$scheduler.GetFolder('\')
$definition=$scheduler.NewTask(0)
$definition.Principal.UserId=$sid;$definition.Principal.LogonType=3;$definition.Principal.RunLevel=0
$definition.Settings.Enabled=$true;$definition.Settings.AllowDemandStart=$true;$definition.Settings.MultipleInstances=2
$action=$definition.Actions.Create(0);$action.Path='C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
$script=Join-Path $request.transport 'fonts.ps1'
$action.Arguments='-NoProfile -NonInteractive -Command "& ([scriptblock]::Create([IO.File]::ReadAllText('''+$script+''',[Text.Encoding]::UTF8)))"'
# TASK_CREATE=2，不覆盖或复用已存在任务；只申请普通交互令牌，不接受UAC。
$task=$folder.RegisterTaskDefinition($request.task,$definition,2,$sid,$null,3,$null)
$task.Run($null)|Out-Null
@{requested=$true;run_level=0;logon_type=3}|ConvertTo-Json -Compress
"""


def execute_script(client, execute, script):
    """沿用host-wizard的UTF-8 SFTP脚本和短命令传输，避免Windows命令行截断。"""
    remote = "C:/ProgramData/PartyOps-VM-Lab/qa-fonts-" + uuid.uuid4().hex[:12]
    with client.open_sftp() as fs:
        fs.mkdir(remote)
        with fs.file(remote + "/operation.ps1", "wx") as stream:
            stream.write(script.encode("utf-8"))
    command = "& ([scriptblock]::Create([IO.File]::ReadAllText('" + remote + "/operation.ps1',[Text.Encoding]::UTF8)))"
    return json.loads(execute(client, command, timeout=120))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["prepare", "inspect", "stage"])
    parser.add_argument("--execute-stage", action="store_true", help="明确一次传入并登记三份QA字体；断线不重发")
    args = parser.parse_args()
    require(args.execute_stage == (args.action == "stage"), "EXPLICIT_STAGE_ONLY_FLAG_REQUIRED")
    fonts = local_fonts()
    value = request(fonts, uuid.uuid4().hex[:12])
    if args.action == "prepare":
        contract.exclusive(OUTPUT / "win10-qa-fonts-local-plan.json", {"guest_uuid": contract.UUID, "username": USER,
            "fonts": fonts, "scope": "QA-user-fonts-only", "packaged_in_product": False, "guest_called": False})
        print("固定三字体本地准备记录已生成；未连接Guest。")
        return
    windows = load(ROOT / "qa/vm-lab/scripts/exercise-windows-business.py", "font_qa_windows")
    transition = load(OUTPUT / "runtime-transition.py", "font_qa_transition")
    matrix, media = windows.load_configuration()
    matrix["defaults"].update(primary_root=str(LAB_ROOT), fallback_root=str(LAB_ROOT))
    lab = windows.QemuLab(matrix, LAB_ROOT, media)
    contract.bind_guest(lab, windows.qmp)
    client = windows.windows.connect(lab, matrix["targets"]["win10-x64"])
    try:
        runner = object.__new__(windows.WindowsRun)
        runner.client, runner.args = client, SimpleNamespace(uuid=contract.UUID)
        transition.standard_desktop(runner.desktop("Inspect"))
        before = execute_script(client, windows.windows.execute, inspect_script(value))
        pending = pending_fonts(before, fonts)
        if args.action == "inspect" or not pending:
            contract.exclusive(OUTPUT / ("win10-qa-fonts-inspect-" + uuid.uuid4().hex[:12] + ".json"),
                {"scope": "QA-user-fonts-only", "fonts": fonts, "snapshot": before, "pending_count": len(pending),
                 "fonts_changed": False, "qa_transport_script_written": True})
            print(json.dumps({"status": "already-present" if not pending else "inspected", "pending_fonts": len(pending), "fonts_changed": False}, ensure_ascii=False))
            return
        contract.bind_guest(lab, windows.qmp)
        contract.exclusive(OUTPUT / "win10-qa-fonts-stage.attempt.json", {"time_utc": contract.now(), "guest_uuid": contract.UUID,
            "username": USER, "fonts": fonts, "request": value, "packaged_in_product": False, "automatic_retry": False})
        # 独占意图落盘后只传缺失字体；同摘要字体只补HKCU登记，不再次传输。
        remote = value["transport"].replace("\\", "/")
        windows.windows.execute(client, "New-Item -ItemType Directory -Path '" + value["transport"] + "' -ErrorAction Stop|Out-Null")
        by_name = {r["filename"]: r for r in before["fonts"]}
        with client.open_sftp() as fs:
            for font in pending:
                if by_name[font["filename"]]["sha256"] is None:
                    with (SOURCE / font["filename"]).open("rb") as source, fs.file(remote + "/" + font["filename"], "wx") as dest:
                        while chunk := source.read(1024 * 1024): dest.write(chunk)
            with fs.file(remote + "/fonts.ps1", "wx") as dest: dest.write(user_script(value).encode("utf-8"))
        queued = execute_script(client, windows.windows.execute, launch_script(value))
        require(queued == {"requested": True, "run_level": 0, "logon_type": 3}, "ORDINARY_FONT_TASK_REQUEST_REQUIRED")
        deadline = time.monotonic() + 60
        while True:
            try:
                with client.open_sftp() as fs, fs.file(remote + "/result.json") as stream: result = json.load(stream)
                break
            except FileNotFoundError:
                require(time.monotonic() < deadline, "FONT_TASK_TIMEOUT_INSPECT_WITHOUT_RELAUNCH")
                time.sleep(2)
        require(result.get("status") == "completed" and result.get("administrator") is False, "STANDARD_USER_FONT_TASK_NOT_COMPLETED")
        require(not pending_fonts({**result, "standard_account": True}, fonts), "FONT_RESULT_INCOMPLETE")
        after = execute_script(client, windows.windows.execute, inspect_script(value))
        require(not pending_fonts(after, fonts), "FONT_INDEPENDENT_RECHECK_FAILED")
        contract.bind_guest(lab, windows.qmp)
        contract.exclusive(OUTPUT / "win10-qa-fonts-stage.completed.json", {"time_utc": contract.now(),
            "attempt_sha256": contract.digest(OUTPUT / "win10-qa-fonts-stage.attempt.json"), "scope": "QA-user-fonts-only",
            "fonts": fonts, "result": result, "after": after, "packaged_in_product": False, "wps_acceptance": "not-verified"})
        print("原QA普通用户三字体已核对登记；WPS排版验收尚未完成。")
    finally:
        client.close()


if __name__ == "__main__":
    main()
