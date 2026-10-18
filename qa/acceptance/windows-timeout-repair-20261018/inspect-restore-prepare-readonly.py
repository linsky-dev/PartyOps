"""本轮restore prepare失败的一次只读诊断；不重放动作脚本。"""
import hashlib
import json
import re
import uuid
from pathlib import Path
import controller as c

OUT = Path(__file__).resolve().parent

def main():
    destination = OUT / 'after-journal-restore-personal-after-prepare-failure-inspect.json'
    c.no_attempt(destination)
    failed_path = OUT / 'after-journal-restore-personal-prepare.result.json'
    failed = c.contract.read(failed_path)
    binding = failed['candidate']
    prepared = c.contract.read(OUT / 'after-journal-first-host-prepare.result.json')
    old_pid = int(prepared['wizard']['pid'])
    r = c.load('restore_diagnosis_helpers', OUT / 'recover-host-journal.py')
    module, lab, target = c.environment()
    c.contract.bind_guest(lab, module.qmp)
    h, first, host, transition, restore = r.helpers()
    client = module.windows.connect(lab, target)
    try:
        base = '/C:/ProgramData/PartyOps-VM-Lab'
        scripts = []
        with client.open_sftp() as fs:
            for entry in fs.listdir_attr(base):
                if not re.fullmatch(r'host-wizard-[a-f0-9]{12}', entry.filename): continue
                path = base + '/' + entry.filename + '/operation.ps1'
                try:
                    with fs.file(path, 'rb') as stream: data = stream.read()
                except OSError: continue
                if b'OTHER_LIVE_WIZARD_APPEARED' in data and b'PartyOps-QA-After-Journal-Personal-20261018' in data and b'JOURNAL_CHANGED_BEFORE_RECYCLE' not in data:
                    scripts.append((entry.st_mtime, path, data))
        c.require(len(scripts) == 1, 'ONE_ACTUAL_RESTORE_PREPARE_SCRIPT_REQUIRED')
        _, remote, data = scripts[0]
        local = OUT / 'after-journal-restore-personal-failed-prepare.ps1'
        with local.open('xb') as stream: stream.write(data)
        since = json.loads(module.windows.execute(client, restore.GUEST_CLOCK))['guest_utc']
        def probe(script):
            return host.transfer(client, module.windows.execute, script, 'host-wizard-' + uuid.uuid4().hex[:12])
        snapshot = probe(first.inspection_script(host, binding, since))
        metadata = probe(r.metadata_script(host, binding, since))
        runtime = c.contract.probe(client, module.windows.execute)
        services = json.loads(module.windows.execute(client, restore.SERVICES))
        script = host.prefix(binding, since) + '$oldPid=' + str(old_pid) + "\n$failedScript='" + remote.lstrip('/') + "'\n" + r'''
$all=@(Get-CimInstance Win32_Process)
$rows=@($all|Where-Object {$_.Name -in @('PartyOpsWizard.exe','PartyOps.exe','consent.exe') -or ($_.Name -in @('powershell.exe','pwsh.exe') -and $_.ProcessId -ne $PID)})
$ids=@($oldPid)+@($rows|ForEach-Object {[int]$_.ProcessId})
$handles=@($ids|Select-Object -Unique|ForEach-Object {$id=$_;$p=Get-Process -Id $id -ErrorAction SilentlyContinue;if($p){$created='';try{$created=$p.StartTime.ToUniversalTime().ToString('o')}catch{};@{pid=$id;exists=$true;has_exited=$p.HasExited;created=$created}}else{@{pid=$id;exists=$false;has_exited=$null;created=''}}})
$listeners=@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue|Where-Object {$_.OwningProcess -in $ids}|ForEach-Object {@{pid=[int]$_.OwningProcess;port=[int]$_.LocalPort}})
$s=New-Object -ComObject 'Schedule.Service';$s.Connect();$tasks=@($s.GetFolder('\').GetTasks(1)|Where-Object {$_.Name -like 'PartyOps-QA-After-Journal-*'}|ForEach-Object {@{name=$_.Name;state=[int]$_.State;last_result=[int]$_.LastTaskResult}})
$tokens=$null;$errors=$null;$source=[IO.File]::ReadAllText($failedScript,[Text.Encoding]::UTF8);$null=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
@{handles=$handles;listeners=$listeners;tasks=$tasks;parser_errors=@($errors|ForEach-Object {@{id=$_.ErrorId;line=$_.Extent.StartLineNumber}});processes=@($rows|ForEach-Object {@{pid=[int]$_.ProcessId;parent_pid=[int]$_.ParentProcessId;name=$_.Name;created=$_.CreationDate.ToUniversalTime().ToString('o')}});mutation_script_executed=$false;original_stderr_persisted=$false}|ConvertTo-Json -Depth 8 -Compress
'''
        details = probe(script)
        value = {'candidate': binding, 'time_utc': c.contract.now(), 'snapshot': snapshot, 'metadata': metadata,
                 'runtime': runtime, 'services': services, 'details': details, 'old_host_wizard_pid': old_pid,
                 'failed_result_sha256': c.contract.digest(failed_path), 'failed_script_source': remote,
                 'failed_script_local': local.name, 'failed_script_sha256': hashlib.sha256(data).hexdigest(),
                 'scope': 'read-only-not-recovery-completion'}
        c.contract.exclusive(destination, value)
        print(json.dumps(value, ensure_ascii=False))
    finally:
        client.close()

if __name__ == '__main__': main()
