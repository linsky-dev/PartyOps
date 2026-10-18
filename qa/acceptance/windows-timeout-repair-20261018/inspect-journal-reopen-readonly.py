"""只读诊断已失败reopen：复制暂存脚本、核语法和前置，不运行变更脚本。"""
import hashlib
import json
import re
import uuid
from pathlib import Path

import controller as c

OUTPUT = Path(__file__).resolve().parent
contract = c.contract


def main():
    module, lab, target = c.environment()
    contract.bind_guest(lab, module.qmp)
    before = contract.read(OUTPUT / "host-journal-recovery-before.json")
    first = c.load("failed_reopen_first", c.OLD / "first-host-qa.py")
    host = c.load("failed_reopen_host", c.OLD / "win10-host-wizard-qa.py")
    transition = c.load("failed_reopen_transition", c.OLD / "runtime-transition.py")
    wizard = next(p for p in before["snapshot"]["processes"] if p["name"] == "PartyOpsWizard.exe")
    client = module.windows.connect(lab, target)
    try:
        base = "/C:/ProgramData/PartyOps-VM-Lab"
        scripts = []
        with client.open_sftp() as fs:
            for entry in fs.listdir_attr(base):
                if not re.fullmatch(r"host-wizard-[a-f0-9]{12}", entry.filename): continue
                path = base + "/" + entry.filename + "/operation.ps1"
                try:
                    with fs.file(path, "rb") as stream: data = stream.read()
                except OSError: continue
                if b"JOURNAL_CHANGED_BEFORE_RECYCLE" not in data: continue
                scripts.append((entry.st_mtime, path, data))
        c.require(len(scripts) == 1, "ONE_FAILED_REOPEN_SCRIPT_REQUIRED")
        _mtime, remote_script, content = scripts[0]
        local_script = OUTPUT / "host-journal-reopen-failed-script.ps1"
        with local_script.open("xb") as stream: stream.write(content)
        request = json.dumps(wizard, ensure_ascii=False).replace("'", "''")
        txn = before["metadata"]["transaction_id"]
        script = host.prefix(before["candidate"], before["time_utc"]) + transition.START_TIME_MATCH
        script += "$target='" + request + "'|ConvertFrom-Json\n$txn='" + txn + "'\n$failedScript='" + remote_script.lstrip("/") + "'\n"
        script += r"""
$checks=[ordered]@{}
function Check-ReadOnly([string]$name,[scriptblock]$body){try{$checks[$name]=@{value=(& $body);error_type='';error_id=''}}catch{$id=[string]$_.FullyQualifiedErrorId;if($id -notmatch '^[A-Za-z0-9_.,-]{1,100}$'){$id='REDACTED'};$checks[$name]=@{value=$null;error_type=$_.Exception.GetType().FullName;error_id=$id}}}
$tokens=$null;$parseErrors=$null;$source=[IO.File]::ReadAllText($failedScript,[Text.Encoding]::UTF8)
$null=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$parseErrors)
$parse=@($parseErrors|ForEach-Object {@{id=$_.ErrorId;line=$_.Extent.StartLineNumber}})
$rows=@(Get-CimInstance Win32_Process -Filter "Name='PartyOpsWizard.exe'")
$p=if($rows.Count -eq 1){$rows[0]}else{$null}
$owner=if($p){Invoke-CimMethod -InputObject $p -MethodName GetOwner}else{$null}
Check-ReadOnly 'single_wizard' {$rows.Count -eq 1}
Check-ReadOnly 'pid' {$p.ProcessId -eq $target.pid}
Check-ReadOnly 'path' {$p.ExecutablePath -eq $wizard}
Check-ReadOnly 'session' {$p.SessionId -eq $desktop[0].session_id}
Check-ReadOnly 'owner' {$owner.ReturnValue -eq 0 -and $owner.User -eq 'partyopsuser' -and $owner.Domain -eq $env:COMPUTERNAME}
Check-ReadOnly 'cim_creation_match' {Test-CimCreationInstant $p.CreationDate $target.created}
Check-ReadOnly 'handle_start_match' {$h=Get-Process -Id $target.pid -ErrorAction Stop;Test-CimCreationInstant $h.StartTime $target.created}
Check-ReadOnly 'no_consent' {@(Get-CimInstance Win32_Process -Filter "Name='consent.exe'").Count -eq 0}
Check-ReadOnly 'no_other_powershell' {@(Get-CimInstance Win32_Process|Where-Object {$_.Name -in @('powershell.exe','pwsh.exe') -and $_.ProcessId -ne $PID}).Count -eq 0}
Check-ReadOnly 'no_wizard_children' {@(Get-CimInstance Win32_Process|Where-Object {$_.ParentProcessId -eq $target.pid}).Count -eq 0}
Check-ReadOnly 'journal_present' {$journal}
Check-ReadOnly 'pending_absent' {-not $pending}
Check-ReadOnly 'journal_same_id' {$v=Get-Content -LiteralPath (Join-Path $userRoot 'host-config-journal.json') -Raw|ConvertFrom-Json;$v.transaction_id -ceq $txn}
$s=New-Object -ComObject 'Schedule.Service';$s.Connect();$tasks=@($s.GetFolder('\').GetTasks(1)|Where-Object {$_.Name -eq 'PartyOps-QA-Journal-Recovery-20261018'}|ForEach-Object {@{name=$_.Name;state=[int]$_.State;last_result=[int]$_.LastTaskResult}})
$products=@(Get-CimInstance Win32_Process|Where-Object {$_.Name -in @('PartyOps.exe','PartyOpsWizard.exe','consent.exe')}|ForEach-Object {@{name=$_.Name;pid=[int]$_.ProcessId;parent_pid=[int]$_.ParentProcessId;path=$_.ExecutablePath;created=$_.CreationDate.ToUniversalTime().ToString('o')}})
@{checks=$checks;parser_errors=$parse;tasks=$tasks;products=$products;mutation_script_executed=$false;original_stderr_persisted=$false}|ConvertTo-Json -Depth 7 -Compress
"""
        # 只暂存和执行上面的只读诊断；不执行failedScript。
        remote = base + "/readonly-journal-" + uuid.uuid4().hex[:12]
        with client.open_sftp() as fs:
            fs.mkdir(remote)
            with fs.file(remote + "/probe.ps1", "wx") as stream: stream.write(script.encode("utf-8"))
        command = "& ([scriptblock]::Create([IO.File]::ReadAllText('" + remote.lstrip("/") + "/probe.ps1',[Text.Encoding]::UTF8)))"
        value = json.loads(module.windows.execute(client, command, timeout=120))
        value.update(failed_script_sha256=hashlib.sha256(content).hexdigest(), failed_script_source=remote_script,
                     failed_script_local=local_script.name, time_utc=contract.now())
        contract.exclusive(OUTPUT / "host-journal-reopen-readonly-diagnosis.json", value)
        print(json.dumps(value, ensure_ascii=False))
    finally:
        client.close()


if __name__ == "__main__": main()
