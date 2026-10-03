"""只读收取公共目录WPS安装及普通用户COM登记，不启动安装器或产品。"""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from lab import load_configuration
from providers import QemuLab
from windows_remote import WinRMFiles

matrix, media = load_configuration()
lab = QemuLab(matrix, Path(matrix['defaults']['primary_root']), media)
result = WinRMFiles(lab, 'win7-x64').powershell(r'''
Get-ChildItem -LiteralPath 'C:\Program Files\Kingsoft' | Select-Object Name,LastWriteTime | Format-Table -AutoSize | Out-String
Get-WmiObject Win32_Process | Where-Object {$_.Name -match '^(wps|setup|ksomisc).*\.exe$'} | Select-Object Name,ProcessId,SessionId,ExecutablePath | Format-Table -AutoSize | Out-String
$sid='S-1-5-21-1355956109-3807972244-525148999-1001'
$s=New-Object -ComObject Schedule.Service;$s.Connect();$t=$s.GetFolder('\').GetTask('PartyOps-QA-font-20260912')
[Console]::WriteLine('FONT_TASK_STATE='+[int]$t.State+' RESULT='+[int]$t.LastTaskResult)
$font=[Microsoft.Win32.Registry]::Users.OpenSubKey($sid+'\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts')
if($font){[Console]::WriteLine('FONT_PATH='+[string]$font.GetValue('方正小标宋简体 (TrueType)'));$font.Close()}
foreach($root in @([Microsoft.Win32.Registry]::Users.OpenSubKey($sid+'\Software\Classes'),[Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('Software\Classes'))){
  if($root){$key=$root.OpenSubKey('KWPS.Application\CLSID');if($key){$id=[string]$key.GetValue('');$key.Close();[Console]::WriteLine('CLSID='+$id);$key=$root.OpenSubKey('CLSID\'+$id+'\LocalServer32');if($key){[Console]::WriteLine('SERVER='+[string]$key.GetValue(''));$key.Close()}};$root.Close()}
}
''', timeout=180)
(lab.root / 'reports/win7-x64/wps-public-inspect-20260912.txt').write_text(result, encoding='utf-8')
print(result)
