"""复用Guest已登记的鼠标辅助代码，在管理员桌面精确点击一次。"""
import argparse
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from lab import load_configuration
from providers import QemuLab
from windows_remote import WinRMFiles

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('x', type=int, choices=range(800))
parser.add_argument('y', type=int, choices=range(600))
args = parser.parse_args()
matrix, media = load_configuration()
lab = QemuLab(matrix, Path(matrix['defaults']['primary_root']), media)
script = r'''
$s=New-Object -ComObject Schedule.Service;$s.Connect();$folder=$s.GetFolder('\')
$old=$folder.GetTask('PartyOps-QA-WPS-Accept-20260910');$definition=$old.Definition
$action=@($definition.Actions)[0]
$code=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String(($action.Arguments -split ' ')[-1]))
if(-not $code.Contains('[QAInput]::SetCursorPos(68,494)')){throw 'REGISTERED_POINTER_CHANGED'}
$code=$code.Replace('[QAInput]::SetCursorPos(68,494)','[QAInput]::SetCursorPos(COORDINATES)')
$action.Arguments='-NoProfile -NonInteractive -EncodedCommand '+[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))
$definition.Principal.UserId='S-1-5-21-1355956109-3807972244-525148999-1000'
$definition.Principal.RunLevel=1;$definition.Principal.LogonType=3
$task=$folder.RegisterTaskDefinition('PartyOps-QA-WPS-Pointer-Admin-20260912',$definition,6,$definition.Principal.UserId,$null,3,$null)
$task.Run($null)|Out-Null
[Console]::WriteLine('CLICK_REQUESTED COORDINATES')
'''.replace('COORDINATES', f'{args.x},{args.y}')
print(WinRMFiles(lab, 'win7-x64').powershell(script, timeout=180))
