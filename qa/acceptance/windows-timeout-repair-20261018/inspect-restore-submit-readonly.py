"""已提交恢复的单次只读观察，区分普通向导和特权helper，不重提。"""
import json
import uuid
from pathlib import Path
import controller as c

OUT = Path(__file__).resolve().parent

def main():
    destination = OUT / 'after-journal-restore-personal-after-submit-failure-inspect.json'
    c.no_attempt(destination)
    source = OUT / 'after-journal-restore-personal-submit.result.json'
    failed = c.contract.read(source)
    c.require(failed.get('status') == 'failed' and failed.get('error_code') == 'SINGLE_WIZARD_REQUIRED', 'BOUND_SUBMITTED_FAILURE_REQUIRED')
    binding = failed['candidate']
    r = c.load('submit_readonly_helpers', OUT / 'recover-host-journal.py')
    module, lab, target = c.environment()
    c.contract.bind_guest(lab, module.qmp)
    h, first, host, transition, restore = r.helpers()
    client = module.windows.connect(lab, target)
    try:
        since = json.loads(module.windows.execute(client, restore.GUEST_CLOCK))['guest_utc']
        def probe(script):
            return host.transfer(client, module.windows.execute, script, 'host-wizard-' + uuid.uuid4().hex[:12])
        snapshot = probe(first.inspection_script(host, binding, since))
        metadata = probe(r.metadata_script(host, binding, since))
        runtime = c.contract.probe(client, module.windows.execute)
        services = json.loads(module.windows.execute(client, restore.SERVICES))
        details = probe(host.prefix(binding, since) + r'''
$rows=@(Get-CimInstance Win32_Process -Filter "Name='PartyOpsWizard.exe'")
$w=@($rows|ForEach-Object {$p=$_;$o=Invoke-CimMethod -InputObject $p -MethodName GetOwner;$h=Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue;@{pid=[int]$p.ProcessId;parent_pid=[int]$p.ParentProcessId;session=[int]$p.SessionId;path_matches=($p.ExecutablePath -eq $wizard);owner_original=($o.ReturnValue -eq 0 -and $o.User -eq 'partyopsuser' -and $o.Domain -eq $env:COMPUTERNAME);owner_system=($o.ReturnValue -eq 0 -and $o.User -eq 'SYSTEM');created=$p.CreationDate.ToUniversalTime().ToString('o');handle_exists=($null -ne $h);has_exited=if($h){$h.HasExited}else{$null};privileged_apply=([string]$p.CommandLine -match '--apply-privileged-host-config');privileged_abort=([string]$p.CommandLine -match '--abort-privileged-host-config')}})
@{wizard_identities=$w;consent_count=@(Get-CimInstance Win32_Process -Filter "Name='consent.exe'").Count;mutation_executed=$false}|ConvertTo-Json -Depth 6 -Compress
''')
        value = {'candidate': binding, 'time_utc': c.contract.now(), 'snapshot': snapshot, 'metadata': metadata,
                 'runtime': runtime, 'services': services, 'details': details,
                 'failed_submit_sha256': c.contract.digest(source), 'scope': 'read-only-not-terminal-http-proof'}
        c.contract.exclusive(destination, value)
        print(json.dumps(value, ensure_ascii=False))
    finally:
        client.close()

if __name__ == '__main__': main()
