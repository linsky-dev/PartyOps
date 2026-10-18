"""本次取消后回收失败的一次只读诊断；不执行变更脚本。"""
import json
import uuid
from pathlib import Path
import controller as c

OUT = Path(__file__).resolve().parent

def main():
    destination = OUT / 'host-journal-cancelled-relaunch-readonly-diagnosis.json'
    c.no_attempt(destination)
    r = c.load('cancel_diagnosis_helpers', OUT / 'recover-host-journal.py')
    before = c.contract.read(OUT / 'host-journal-cancelled-before.json')
    failed_path = OUT / 'host-journal-cancelled-relaunch.result.json'
    failed = c.contract.read(failed_path)
    binding = before['candidate']
    c.require(failed['candidate'] == binding and failed['operation'].get('error_id') == 'OTHER_LIVE_WIZARD_APPEARED', 'BOUND_RECYCLE_FAILURE_REQUIRED')
    module, lab, target = c.environment()
    c.contract.bind_guest(lab, module.qmp)
    h, first, host, transition, restore = r.helpers()
    client = module.windows.connect(lab, target)
    try:
        since = json.loads(module.windows.execute(client, restore.GUEST_CLOCK))['guest_utc']
        def read_probe(script):
            return host.transfer(client, module.windows.execute, script, 'host-wizard-' + uuid.uuid4().hex[:12])
        snapshot = read_probe(first.inspection_script(host, binding, since))
        metadata = read_probe(r.metadata_script(host, binding, since, before['metadata']['transaction_id']))
        runtime = c.contract.probe(client, module.windows.execute)
        old_wizard = next(p for p in before['snapshot']['processes'] if p['name'] == 'PartyOpsWizard.exe')
        script = host.prefix(binding, since) + '$oldPid=' + str(int(old_wizard['pid'])) + '\n' + r'''
$rows=@(Get-CimInstance Win32_Process|Where-Object {$_.Name -in @('PartyOpsWizard.exe','PartyOps.exe','consent.exe') -or ($_.Name -in @('powershell.exe','pwsh.exe') -and $_.ProcessId -ne $PID)})
$ids=@($oldPid)+@($rows|ForEach-Object {[int]$_.ProcessId})
$handles=@($ids|Select-Object -Unique|ForEach-Object {$id=$_;$p=Get-Process -Id $id -ErrorAction SilentlyContinue;if($p){$exited=$p.HasExited;$start='';try{$start=$p.StartTime.ToUniversalTime().ToString('o')}catch{};@{pid=$id;exists=$true;has_exited=$exited;created=$start}}else{@{pid=$id;exists=$false;has_exited=$null;created=''}}})
$listeners=@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue|Where-Object {$_.OwningProcess -in $ids}|ForEach-Object {@{pid=[int]$_.OwningProcess;port=[int]$_.LocalPort}})
$s=New-Object -ComObject 'Schedule.Service';$s.Connect();$tasks=@($s.GetFolder('\').GetTasks(1)|Where-Object {$_.Name -in @('PartyOps-QA-Journal-Recovery-20261018','PartyOps-QA-Journal-After-Cancel-20261018')}|ForEach-Object {@{name=$_.Name;state=[int]$_.State;last_result=[int]$_.LastTaskResult;last_run=$_.LastRunTime.ToUniversalTime().ToString('o')}})
@{old_wizard_pid=$oldPid;handles=$handles;listeners=$listeners;tasks=$tasks;processes=@($rows|ForEach-Object {@{pid=[int]$_.ProcessId;parent_pid=[int]$_.ParentProcessId;name=$_.Name;path=$_.ExecutablePath;created=$_.CreationDate.ToUniversalTime().ToString('o')}});mutation_executed=$false}|ConvertTo-Json -Depth 8 -Compress
'''
        details = read_probe(script)
        verify = c.load('cancel_diagnosis_personal', c.OLD / 'verify-retained-business.py')
        verify.same_restored_runtime(runtime, before['runtime'], binding)
        value = {'candidate': binding, 'time_utc': c.contract.now(), 'snapshot': snapshot, 'metadata': metadata,
                 'runtime': runtime, 'details': details, 'personal_same_instance': True,
                 'cancelled_before_sha256': c.contract.digest(OUT / 'host-journal-cancelled-before.json'),
                 'failed_result_sha256': c.contract.digest(failed_path), 'scope': 'read-only-not-completion'}
        c.contract.exclusive(destination, value)
        print(json.dumps(value, ensure_ascii=False))
    finally:
        client.close()

if __name__ == '__main__':
    main()
