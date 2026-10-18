"""PowerShell5.1静态语法及拒绝门槛；不执行清理主入口或删除。"""
from pathlib import Path
import subprocess
import unittest

SCRIPT = Path(__file__).with_name('prepare-local-cleanup.ps1').resolve()

class CleanupSafetyTests(unittest.TestCase):
    def test_ps51_parser_and_real_functions_reject_unsafe_inputs(self):
        command = r'''
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile('__SCRIPT__',[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'PS51_PARSE_ERROR'}
foreach($f in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){. ([scriptblock]::Create($f.Extent.Text))}
$Workspace='C:\PartyOps-VM-Lab\windows-startup-repair-20261016'
$GithubReadback=''
$caught=0
foreach($path in @('D:\PartyOps-VM-Lab\x','C:\PartyOps-VM-Lab\windows-startup-repair-20261016\.test-temp\x','C:\PartyOps-VM-Lab\windows-startup-repair-20261016\artifacts\..\README.md')){
 try{Assert-Path $path}catch{$caught++}
}
if($caught -ne 3){throw 'UNSAFE_PATH_ACCEPTED'}
try{Assert-ProductionProof;throw 'MISSING_PROOF_ACCEPTED'}catch{if($_.Exception.Message -ne 'EXPLICIT_CURRENT_GITHUB_READBACK_REQUIRED'){throw}}
function Get-CimInstance { [pscustomobject]@{ProcessId=2147483000;Name='cl.exe';CommandLine='compiler'} }
function Get-Process { param($Id,$ErrorAction) [pscustomobject]@{HasExited=$false} }
try{Assert-NoBuild;throw 'ACTIVE_BUILD_ACCEPTED'}catch{if($_.Exception.Message -ne 'ACTIVE_BUILD_PROCESS_BLOCKS_CLEANUP'){throw}}
$deletes=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Remove-Item'},$true))
if($deletes.Count -ne 1 -or $deletes[0].Extent.Text -notmatch '-LiteralPath'){throw 'DELETE_SCOPE_CHANGED'}
Write-Output 'PS51_SAFETY_CHECKS_PASSED_NO_DELETION'
'''.replace('__SCRIPT__', str(SCRIPT).replace("'", "''"))
        result = subprocess.run(['powershell.exe','-NoProfile','-NonInteractive','-Command',command], capture_output=True, timeout=20)
        self.assertEqual(result.returncode, 0, result.stderr.decode('utf-8',errors='replace'))
        self.assertIn(b'PS51_SAFETY_CHECKS_PASSED_NO_DELETION', result.stdout)

if __name__ == '__main__': unittest.main()
