"""仅在验收Guest普通用户安装宿主已有字体，不把字体放入PartyOps安装包。"""
import base64
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from evidence import now, sha256, write_json
from lab import load_configuration
from providers import QemuLab
from windows_remote import WinRMFiles

matrix, media = load_configuration()
lab = QemuLab(matrix, Path(matrix['defaults']['primary_root']), media)
source = Path('C:/Users/Administrator/AppData/Local/Microsoft/Windows/Fonts/方正小标宋简.TTF')
digest = sha256(source)
client = WinRMFiles(lab, 'win7-x64')
remote = r'C:\PartyOps-QA\wps-font-20260912\FZXBS.TTF'
client.put(source, remote, digest)
code = r'''
$ErrorActionPreference='Stop'
$dir=Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Fonts'
[IO.Directory]::CreateDirectory($dir)|Out-Null
$dest=Join-Path $dir 'FZXBS.TTF'
Copy-Item -LiteralPath 'C:\PartyOps-QA\wps-font-20260912\FZXBS.TTF' -Destination $dest
$key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts')
$key.SetValue('方正小标宋简体 (TrueType)',$dest);$key.Close()
Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public class QAFont{[DllImport("gdi32.dll",CharSet=CharSet.Unicode)]public static extern int AddFontResourceEx(string p,uint f,IntPtr r);[DllImport("user32.dll")]public static extern bool PostMessage(IntPtr w,uint m,IntPtr a,IntPtr b);}'
if([QAFont]::AddFontResourceEx($dest,0,[IntPtr]::Zero) -le 0){throw 'FONT_REGISTER_FAILED'}
[QAFont]::PostMessage([IntPtr]65535,0x001d,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null
'''
encoded = base64.b64encode(code.encode('utf-16le')).decode()
script = r'''
$s=New-Object -ComObject Schedule.Service;$s.Connect();$folder=$s.GetFolder('\')
$d=$s.NewTask(0);$sid='S-1-5-21-1355956109-3807972244-525148999-1001'
$d.Principal.UserId=$sid;$d.Principal.RunLevel=0;$d.Principal.LogonType=3
$d.Settings.Enabled=$true;$d.Settings.AllowDemandStart=$true
$a=$d.Actions.Create(0);$a.Path='C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
$a.Arguments='-NoProfile -NonInteractive -EncodedCommand ENCODED'
$task=$folder.RegisterTaskDefinition('PartyOps-QA-font-20260912',$d,2,$sid,$null,3,$null)
$task.Run($null)|Out-Null
[Console]::WriteLine('FONT_INSTALL_REQUESTED_ONCE')
'''.replace('ENCODED', encoded)
result = client.powershell(script, timeout=180)
write_json(lab.root / 'reports/win7-x64/font-install-started-20260912.json',
           {'at': now(), 'source': str(source), 'sha256': digest, 'result': result,
            'packaged_in_product': False, 'completed': False})
print(result)
