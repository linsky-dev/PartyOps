"""本次失败host提交的同ID恢复；不重发旧请求，不改产品或业务。"""
import argparse
import json
import re
import uuid
import time
from pathlib import Path

import controller as c

OUTPUT = Path(__file__).resolve().parent
contract, require = c.contract, c.require
TASK = "PartyOps-QA-Journal-Recovery-20261018"
CANCEL_TASK = "PartyOps-QA-Journal-After-Cancel-20261018"


def helpers():
    host_module = c.load("journal_host_adapter", OUTPUT / "host-personal.py")
    first, host, form, transition = host_module.helpers()
    restore = c.load("journal_restore_primitives", c.OLD / "restore-personal-qa.py")
    return host_module, first, host, transition, restore


def failure(binding):
    attempt = OUTPUT / "first-host-approved-existing-root-submit.attempt.json"
    result = OUTPUT / "first-host-approved-existing-root-submit.result.json"
    value = contract.read(result)
    require(value.get("candidate") == contract.read(attempt).get("candidate") == binding
            and value.get("status") == "failed" and value.get("attempt_sha256") == contract.digest(attempt),
            "BOUND_FAILED_HOST_REQUEST_REQUIRED")
    return {"failed_host_attempt_sha256": contract.digest(attempt), "failed_host_result_sha256": contract.digest(result)}


def metadata_script(host, binding, since, expected_id=None):
    require(expected_id is None or re.fullmatch(r"[A-Za-z0-9_-]{32,128}", expected_id), "TRANSACTION_ID_INVALID")
    return host.prefix(binding, since) + "$expected='" + (expected_id or "") + "'\n" + r"""
$j=Join-Path $userRoot 'host-config-journal.json';$transaction='';$journalHash='';$outcome=''
if(Test-Path -LiteralPath $j) {
 $v=Get-Content -LiteralPath $j -Raw|ConvertFrom-Json
 if($v.format_version -ne 1 -or $v.transaction_id -notmatch '^[A-Za-z0-9_-]{32,128}$') {throw 'JOURNAL_ID_INVALID'}
 $transaction=[string]$v.transaction_id;$journalHash=(Get-FileHash -LiteralPath $j -Algorithm SHA256).Hash.ToLowerInvariant()
}
if($expected -and $transaction -and $expected -cne $transaction) {throw 'JOURNAL_TRANSACTION_CHANGED'}
$id=if($expected){$expected}else{$transaction}
if($id){$p=Join-Path $control ('host-config-outcome-'+$id+'.json');if(Test-Path -LiteralPath $p){$o=Get-Content -LiteralPath $p -Raw|ConvertFrom-Json;if($o.transaction_id -cne $id -or $o.format_version -ne 1 -or $o.outcome -notin @('commit','rollback')){throw 'OUTCOME_INVALID'};$outcome=[string]$o.outcome}}
$all=@(Get-CimInstance Win32_Process)
$w=@($all|Where-Object {$_.Name -eq 'PartyOpsWizard.exe'})
$extraPS=@($all|Where-Object {$_.Name -in @('powershell.exe','pwsh.exe') -and $_.ProcessId -ne $PID})
$consent=@($all|Where-Object {$_.Name -eq 'consent.exe'})
$children=@();if($w.Count -eq 1){$ids=@([int]$w[0].ProcessId);for($n=0;$n -lt 32 -and $ids.Count;$n++){$rows=@($all|Where-Object {$_.ParentProcessId -in $ids});$children+=@($rows|ForEach-Object {@{pid=[int]$_.ProcessId;name=$_.Name}});$ids=@($rows|ForEach-Object {[int]$_.ProcessId})}}
$services=@(Get-CimInstance Win32_Service|Where-Object {$_.Name -in @('PartyOpsHost','PartyOpsUpdateService')}|ForEach-Object {@{name=$_.Name;state=$_.State;start_mode=$_.StartMode}})
$s=New-Object -ComObject 'Schedule.Service';$s.Connect();$tasks=@($s.GetFolder('\').GetTasks(1)|Where-Object {$_.Name -eq 'PartyOps-QA-Journal-Recovery-20261018'})
$resumeTask=$null;if($tasks.Count -eq 1){$d=$tasks[0].Definition;$ownerSid=([Security.Principal.NTAccount]::new($d.Principal.UserId)).Translate([Security.Principal.SecurityIdentifier]).Value;$resumeTask=@{state=[int]$tasks[0].State;last_run=$tasks[0].LastRunTime.ToUniversalTime().ToString('o');original_user_matches=($ownerSid -eq $sid);run_level=[int]$d.Principal.RunLevel;logon_type=[int]$d.Principal.LogonType;actions=@($d.Actions|ForEach-Object {@{path_matches=($_.Path -eq $wizard);arguments=$_.Arguments}})}}
$cancelTasks=@($s.GetFolder('\').GetTasks(1)|Where-Object {$_.Name -eq 'PartyOps-QA-Journal-After-Cancel-20261018'})
@{transaction_id=$id;journal_present=(Test-Path -LiteralPath $j);journal_sha256=$journalHash;pending=$pending;outcome=$outcome;wizard_count=$w.Count;other_powershell_count=$extraPS.Count;consent_count=$consent.Count;wizard_children=$children;services=$services;recovery_task_count=$tasks.Count;resume_task=$resumeTask;cancel_relaunch_task_count=$cancelTasks.Count}|ConvertTo-Json -Depth 7 -Compress
"""


def can_reopen(before):
    value, snap = before["metadata"], before["snapshot"]
    require(value.get("journal_present") is True and value.get("pending") is False and value.get("outcome") == ""
            and re.fullmatch(r"[A-Za-z0-9_-]{32,128}", value.get("transaction_id", ""))
            and value.get("wizard_count") == 1 and value.get("other_powershell_count") == 0
            and value.get("consent_count") == 0
            and value.get("wizard_children") == [] and snap.get("user_mode") == snap.get("system_mode") == "personal"
            and len(value.get("services", [])) == 2 and all(s.get("state") == "Stopped" for s in value["services"]),
            "NO_WRITER_AND_BOUND_PERSONAL_JOURNAL_REQUIRED")


def can_resume(value, before):
    m, original = value["metadata"], before["metadata"]
    require(m.get("transaction_id") == original["transaction_id"] and m.get("journal_sha256") == original["journal_sha256"]
            and m.get("journal_present") is True and m.get("pending") is False and m.get("outcome") == ""
            and m.get("wizard_count") == m.get("other_powershell_count") == m.get("consent_count") == m.get("recovery_task_count") == 0
            and m.get("wizard_children") == [] and value["snapshot"].get("user_mode") == value["snapshot"].get("system_mode") == "personal"
            and len(m.get("services", [])) == 2 and all(s.get("state") == "Stopped" for s in m["services"]),
            "RESUME_REQUIRES_NO_TASK_OR_WRITER_AND_UNCHANGED_JOURNAL")


def can_cancelled_relaunch(value, original):
    can_reopen(value)
    m = value["metadata"]
    task = m.get("resume_task") or {}
    wizards = [p for p in value["snapshot"]["processes"] if p["name"] == "PartyOpsWizard.exe"]
    require(m.get("transaction_id") == original["metadata"]["transaction_id"]
            and m.get("journal_sha256") == original["metadata"]["journal_sha256"]
            and m.get("recovery_task_count") == 1 and m.get("cancel_relaunch_task_count") == 0
            and task.get("state") == 4 and task.get("original_user_matches") is True
            and task.get("run_level") == 0 and task.get("logon_type") == 3
            and task.get("actions") == [{"path_matches": True, "arguments": "--reconfigure --initial-role personal"}]
            and len(wizards) == 1 and wizards[0].get("installed_path_matches") is True
            and wizards[0].get("owner_is_original_user") is True
            and wizards[0].get("session_id") == value["snapshot"].get("session_id"),
            "BOUND_CANCELLED_RESUME_WIZARD_REQUIRED")
    from datetime import datetime
    parse = lambda x: datetime.fromisoformat(x.replace("Z", "+00:00"))
    require(parse(task["last_run"]) <= parse(wizards[0]["created"]), "RESUME_TASK_START_AFTER_WIZARD_REJECTED")


def resume_script(host, binding, since, before):
    host.TASK = TASK
    transition = c.load("resume_process_instant", c.OLD / "runtime-transition.py")
    prefix = host.prefix(binding, since) + transition.START_TIME_MATCH
    tail = "$scheduler=" + host.launch_script(binding, since).split("$scheduler=", 1)[1]
    tail = tail.replace("--reconfigure --initial-role host", "--reconfigure --initial-role personal")
    tail = tail[:tail.rfind("@{launch_requested=")]
    tail = tail.replace("$registered=$folder.RegisterTaskDefinition", "$phase='register'\n$registered=$folder.RegisterTaskDefinition")
    tail = tail.replace("$registered.Run($null)", "$phase='run'\n$registered.Run($null)")
    request = json.dumps({"transaction": before["metadata"]["transaction_id"], "hash": before["metadata"]["journal_sha256"],
                          "personal": before["runtime"]["processes"][0]}).replace("'", "''")
    return "$phase='identity'\ntry {\n" + prefix + "$expected='" + request + "'|ConvertFrom-Json\n" + r"""
$phase='preflight'
if($pending -or -not $journal){throw 'BOUND_JOURNAL_REQUIRED'}
$j=Join-Path $userRoot 'host-config-journal.json';$v=Get-Content -LiteralPath $j -Raw|ConvertFrom-Json
if($v.transaction_id -cne $expected.transaction -or (Get-FileHash -LiteralPath $j -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected.hash){throw 'JOURNAL_CHANGED'}
$all=@(Get-CimInstance Win32_Process)
if(@($all|Where-Object {$_.Name -in @('PartyOpsWizard.exe','consent.exe') -or ($_.Name -in @('powershell.exe','pwsh.exe') -and $_.ProcessId -ne $PID)}).Count){throw 'WRITER_OR_CONSENT_APPEARED'}
$p=@($all|Where-Object {$_.Name -eq 'PartyOps.exe'})
if($p.Count -ne 1 -or $p[0].ProcessId -ne $expected.personal.pid -or -not (Test-CimCreationInstant $p[0].CreationDate $expected.personal.created)){throw 'PERSONAL_PROCESS_CHANGED'}
$s=New-Object -ComObject 'Schedule.Service';$s.Connect();if(@($s.GetFolder('\').GetTasks(1)|Where-Object {$_.Name -eq $request.task}).Count){throw 'RECOVERY_TASK_EXISTS'}
""" + tail + r"""
@{status='launch-requested';phase=$phase;launch_requested=$true;gui_normal_exit_verified=$false;recycled_wizard_again=$false}|ConvertTo-Json -Compress
} catch {
$id=[string]$_.FullyQualifiedErrorId;if($id -notmatch '^[A-Za-z0-9_.,-]{1,100}$'){$id='REDACTED'}
@{status='failed';phase=$phase;launch_requested=$false;error_type=$_.Exception.GetType().FullName;error_id=$id;script_line=$_.InvocationInfo.ScriptLineNumber}|ConvertTo-Json -Compress
}
"""


def captured_execute(client, windows, script, timeout=120):
    _stdin, stdout, _stderr = client.exec_command(windows.encoded_powershell(script), timeout=timeout)
    channel = stdout.channel; out, err = [], []; deadline = time.monotonic() + timeout
    while True:
        while channel.recv_ready(): out.append(channel.recv(65536))
        while channel.recv_stderr_ready(): err.append(channel.recv_stderr(65536))
        if channel.exit_status_ready() and not channel.recv_ready() and not channel.recv_stderr_ready(): break
        require(time.monotonic() < deadline, "RESUME_TRANSPORT_TIMEOUT_INSPECT_ONLY")
        time.sleep(.05)
    raw, errors = b"".join(out), b"".join(err)
    value = json.loads(raw.decode("utf-8").strip())
    value["transport"] = {"exit_code": channel.recv_exit_status(), "stdout_bytes": len(raw), "stderr_bytes": len(errors),
                          "stderr_content_saved": False, "stdout_saved_as_public_json": True}
    return json.dumps(value)


def reopen_script(host, first, transition, restore, binding, since, before, task_name=TASK):
    can_reopen(before)
    wizard = next(p for p in before["snapshot"]["processes"] if p["name"] == "PartyOpsWizard.exe")
    script = restore.prepare_script(host, first, transition, binding, since, wizard)
    guard = "if($pending -or $journal) {throw 'PENDING_TRANSACTION_REQUIRES_RECOVERY'}"
    require(guard in script, "EXPECTED_CONTROLLED_WIZARD_TEMPLATE_REQUIRED")
    transaction = before["metadata"]["transaction_id"]
    expected_hash = before["metadata"]["journal_sha256"]
    preflight = r"""
if($pending -or -not $journal) {throw 'BOUND_USER_JOURNAL_REQUIRED'}
$j=Join-Path $userRoot 'host-config-journal.json'
$v=Get-Content -LiteralPath $j -Raw|ConvertFrom-Json
if($v.transaction_id -cne '__TRANSACTION__' -or (Get-FileHash -LiteralPath $j -Algorithm SHA256).Hash.ToLowerInvariant() -ne '__JOURNALHASH__') {throw 'JOURNAL_CHANGED_BEFORE_RECYCLE'}
if(@(Get-CimInstance Win32_Process|Where-Object {$_.Name -in @('powershell.exe','pwsh.exe') -and $_.ProcessId -ne $PID}).Count) {throw 'OTHER_POWERSHELL_STILL_ACTIVE'}
if(@(Get-CimInstance Win32_Process -Filter "Name='consent.exe'").Count) {throw 'CONSENT_STILL_ACTIVE'}
$all=@(Get-CimInstance Win32_Process);$w=@($all|Where-Object {$_.Name -eq 'PartyOpsWizard.exe'})
if($w.Count -ne 1 -or @($all|Where-Object {$_.ParentProcessId -eq $w[0].ProcessId}).Count) {throw 'WIZARD_HELPER_OR_CHILD_ACTIVE'}
$s=New-Object -ComObject 'Schedule.Service';$s.Connect();if(@($s.GetFolder('\').GetTasks(1)|Where-Object {$_.Name -eq '__TASK__'}).Count){throw 'RECOVERY_TASK_EXISTS_INSPECT_ONLY'}
""".replace("__TRANSACTION__", transaction).replace("__JOURNALHASH__", expected_hash).replace("__TASK__", task_name)
    return script.replace(guard, preflight, 1).replace("PartyOps-QA-Restore-Personal-20261017", task_name)


def cancelled_script(host, first, transition, restore, binding, since, before):
    script = reopen_script(host, first, transition, restore, binding, since, before, CANCEL_TASK)
    script = script.replace('if(@(Get-CimInstance Win32_Process -Filter "Name=\'PartyOpsWizard.exe\'").Count) {throw \'OTHER_WIZARD_APPEARED\'}',
        '$liveW=@(Get-CimInstance Win32_Process -Filter "Name=\'PartyOpsWizard.exe\'"|Where-Object {Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue});if($liveW.Count) {throw \'OTHER_LIVE_WIZARD_APPEARED\'}')
    script = script.replace("$handle=Get-Process", "$phase='recycle'\n$handle=Get-Process", 1)
    script = script.replace("$scheduler=", "$phase='task-definition'\n$scheduler=", 1)
    script = script.replace("$registered=$folder.RegisterTaskDefinition", "$phase='register'\n$registered=$folder.RegisterTaskDefinition", 1)
    script = script.replace("$registered.Run($null)", "$phase='run'\n$registered.Run($null)", 1)
    script = script.replace("@{old_qa_wizard_controlled_termination=", "@{phase=$phase;gui_normal_exit_verified=$false;old_qa_wizard_controlled_termination=", 1)
    return "$phase='preflight'\ntry {\n" + script + r"""
} catch {
$id=[string]$_.FullyQualifiedErrorId;if($id -notmatch '^[A-Za-z0-9_.,-]{1,100}$'){$id='REDACTED'}
@{status='failed';phase=$phase;launch_requested=$false;error_type=$_.Exception.GetType().FullName;error_id=$id;script_line=$_.InvocationInfo.ScriptLineNumber}|ConvertTo-Json -Compress
}
"""


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", nargs="?", default="check", choices=["check", "inspect", "reopen", "resume-launch", "cancelled-inspect", "cancelled-relaunch", "verify"])
    parser.add_argument("--package", required=True, type=Path); parser.add_argument("--sha256", required=True)
    parser.add_argument("--execute", action="store_true")
    args = parser.parse_args(argv)
    require(args.execute == (args.action != "check"), "EXPLICIT_RECOVERY_EXECUTE_REQUIRED")
    if args.action == "reopen":
        c.no_attempt(OUTPUT / "host-journal-reopen.attempt.json")
        c.no_attempt(OUTPUT / "host-journal-reopen.result.json")
    if args.action == "resume-launch":
        c.no_attempt(OUTPUT / "host-journal-resume-launch.attempt.json")
        c.no_attempt(OUTPUT / "host-journal-resume-launch.result.json")
    if args.action == "cancelled-relaunch":
        c.no_attempt(OUTPUT / "host-journal-cancelled-relaunch.attempt.json")
        c.no_attempt(OUTPUT / "host-journal-cancelled-relaunch.result.json")
    module, lab, target = c.environment()
    binding = contract.candidate(lab, c.ROOT, args.package, args.sha256)
    h, first, host, transition, restore = helpers()
    inputs = h.current_inputs(binding)
    links = failure(binding)
    if args.action == "check":
        print(json.dumps({"status": "checked-offline", "candidate": binding, **links})); return
    contract.bind_guest(lab, module.qmp)
    client = module.windows.connect(lab, target)
    try:
        since = json.loads(module.windows.execute(client, restore.GUEST_CLOCK))["guest_utc"]
        before_path = OUTPUT / "host-journal-recovery-before.json"
        before = contract.read(before_path) if args.action in {"reopen", "resume-launch", "cancelled-inspect", "cancelled-relaunch", "verify"} else None
        expected = before["metadata"]["transaction_id"] if before else None
        def transfer(script):
            return host.transfer(client, module.windows.execute, script, "host-wizard-" + uuid.uuid4().hex[:12])
        observed = transfer(first.inspection_script(host, binding, since))
        metadata = transfer(metadata_script(host, binding, since, expected))
        actual = contract.probe(client, module.windows.execute)
        contract.runtime_matches(actual, binding)
        value = {"candidate": binding, "inputs": inputs, "snapshot": observed, "metadata": metadata, "runtime": actual,
                 "time_utc": contract.now(), **links}
        if args.action == "inspect":
            can_reopen(value)
            prepared = contract.read(OUTPUT / "first-host-prepare.result.json")
            wizard = next(p for p in observed["processes"] if p["name"] == "PartyOpsWizard.exe")
            require(first.same_wizard(wizard, prepared["wizard"]), "ORIGINAL_FAILED_WIZARD_CHANGED")
            contract.exclusive(before_path, value)
            print("原journal及无活动helper状态已独占记录；下一步reopen仅回收绑定QA向导并触发同ID恢复。")
            return
        require(before.get("candidate") == binding and all(before.get(k) == v for k, v in links.items()), "RECOVERY_BEFORE_SOURCE_CHANGED")
        old_verify = c.load("journal_personal_runtime", c.OLD / "verify-retained-business.py")
        old_verify.same_restored_runtime(actual, before["runtime"], binding)
        if args.action in {"cancelled-inspect", "cancelled-relaunch"}:
            can_cancelled_relaunch(value, before)
            resume_attempt = OUTPUT / "host-journal-resume-launch.attempt.json"
            resume_result = OUTPUT / "host-journal-resume-launch.result.json"
            resumed = contract.read(resume_result)
            require(resumed.get("candidate") == binding and resumed.get("operation", {}).get("launch_requested") is True
                    and resumed.get("attempt_sha256") == contract.digest(resume_attempt), "BOUND_PRIOR_RESUME_REQUEST_REQUIRED")
            cancellation_links = {"resume_attempt_sha256": contract.digest(resume_attempt), "resume_result_sha256": contract.digest(resume_result),
                "before_sha256": contract.digest(before_path), "cancellation_source": "主控本会话明确UAC取消；不代表产品恢复成功"}
            cancelled_before = OUTPUT / "host-journal-cancelled-before.json"
            if args.action == "cancelled-inspect":
                contract.exclusive(cancelled_before, {**value, **cancellation_links})
                print("取消后同journal/个人实例、无活动helper及原普通用户恢复任务已绑定；未重启任何进程。")
                return
            checked = contract.read(cancelled_before)
            require(checked.get("candidate") == binding and all(checked.get(k) == v for k, v in cancellation_links.items())
                    and checked["metadata"] == metadata and first.same_wizard(
                        next(p for p in checked["snapshot"]["processes"] if p["name"] == "PartyOpsWizard.exe"),
                        next(p for p in observed["processes"] if p["name"] == "PartyOpsWizard.exe")), "CANCELLED_RECOVERY_INSPECTION_CHANGED")
            attempt = OUTPUT / "host-journal-cancelled-relaunch.attempt.json"
            contract.exclusive(attempt, {"candidate": binding, "inputs": inputs, "transaction_id": expected,
                "cancelled_before_sha256": contract.digest(cancelled_before), "time_utc": contract.now(), **cancellation_links, **links})
            script = cancelled_script(host, first, transition, restore, binding, since, checked)
            result = host.transfer(client, lambda cli, command, timeout=120: captured_execute(cli, module.windows, command, timeout),
                                   script, "host-wizard-" + uuid.uuid4().hex[:12])
            contract.exclusive(OUTPUT / "host-journal-cancelled-relaunch.result.json", {"candidate": binding, "operation": result,
                "status": "launch-requested-not-recovered" if result.get("launch_requested") else "failed",
                "attempt_sha256": contract.digest(attempt), "time_utc": contract.now(), "gui_normal_exit_verified": False})
            require(result.get("launch_requested") is True and result.get("transport", {}).get("exit_code") == 0,
                    "CANCELLED_RELAUNCH_FAILED_INSPECT_NO_REPLAY")
            print("取消后独立重开已请求；只处理正常恢复UAC，成功后verify，不提交新业务配置。")
            return
        if args.action == "resume-launch":
            can_resume(value, before)
            diagnosis_file = OUTPUT / "host-journal-reopen-readonly-diagnosis.json"
            diagnosis = contract.read(diagnosis_file)
            require((OUTPUT / "host-journal-reopen.attempt.json").exists() and not (OUTPUT / "host-journal-reopen.result.json").exists()
                    and diagnosis.get("mutation_script_executed") is False and diagnosis.get("tasks") == []
                    and all(p.get("name") == "PartyOps.exe" for p in diagnosis.get("products", [])), "CONFIRMED_PARTIAL_RECYCLE_WITHOUT_LAUNCH_REQUIRED")
            attempt = OUTPUT / "host-journal-resume-launch.attempt.json"
            contract.exclusive(attempt, {"candidate": binding, "inputs": inputs, "transaction_id": expected,
                "before_sha256": contract.digest(before_path), "partial_reopen_attempt_sha256": contract.digest(OUTPUT / "host-journal-reopen.attempt.json"),
                "readonly_diagnosis_sha256": contract.digest(diagnosis_file), "time_utc": contract.now(), **links})
            script = resume_script(host, binding, since, before)
            result = host.transfer(client, lambda cli, command, timeout=120: captured_execute(cli, module.windows, command, timeout),
                                   script, "host-wizard-" + uuid.uuid4().hex[:12])
            contract.exclusive(OUTPUT / "host-journal-resume-launch.result.json", {"candidate": binding, "operation": result,
                "status": result.get("status"), "attempt_sha256": contract.digest(attempt), "time_utc": contract.now()})
            require(result.get("launch_requested") is True and result.get("transport", {}).get("exit_code") == 0,
                    "RESUME_LAUNCH_FAILED_INSPECT_PHASE_NO_REPLAY")
            print("唯一resume-launch已请求；未再次回收任何进程。主控处理同ID恢复UAC后verify。")
            return
        if args.action == "reopen":
            can_reopen(value)
            require(metadata == before["metadata"] and first.same_wizard(
                next(p for p in observed["processes"] if p["name"] == "PartyOpsWizard.exe"),
                next(p for p in before["snapshot"]["processes"] if p["name"] == "PartyOpsWizard.exe")), "RECOVERY_PRECONDITIONS_CHANGED")
            script = reopen_script(host, first, transition, restore, binding, since, before)
            attempt = OUTPUT / "host-journal-reopen.attempt.json"
            contract.exclusive(attempt, {"candidate": binding, "inputs": inputs, "transaction_id": expected,
                "before_sha256": contract.digest(before_path), "time_utc": contract.now(), **links})
            result = transfer(script)
            contract.exclusive(OUTPUT / "host-journal-reopen.result.json", {"candidate": binding, "operation": result,
                "status": "launch-requested-not-recovered", "attempt_sha256": contract.digest(attempt),
                "controlled_qa_wizard_recycle": True, "gui_normal_exit_verified": False, "time_utc": contract.now()})
            require(result.get("launch_requested") is True, "RECOVERY_LAUNCH_NOT_CONFIRMED")
            print("已请求正常个人重新配置入口；主控处理其同ID恢复UAC，勿提交新配置，之后verify。")
            return
        require(metadata.get("transaction_id") == expected and metadata.get("outcome") == "rollback"
                and metadata.get("journal_present") is False and metadata.get("pending") is False
                and metadata.get("wizard_count") == 1 and metadata.get("other_powershell_count") == 0
                and metadata.get("consent_count") == 0
                and metadata.get("wizard_children") == []
                and observed.get("user_mode") == observed.get("system_mode") == "personal"
                and len(metadata.get("services", [])) == 2 and all(s.get("state") == "Stopped" for s in metadata["services"]),
                "SAME_ID_ROLLBACK_AND_CLEARED_JOURNAL_REQUIRED")
        launch_prefix = ("host-journal-cancelled-start" if (OUTPUT / "host-journal-cancelled-start.result.json").exists()
                         else "host-journal-cancelled-relaunch" if (OUTPUT / "host-journal-cancelled-relaunch.result.json").exists()
                         else "host-journal-resume-launch" if (OUTPUT / "host-journal-resume-launch.result.json").exists() else "host-journal-reopen")
        launch_attempt = OUTPUT / (launch_prefix + ".attempt.json")
        launch_result_file = OUTPUT / (launch_prefix + ".result.json")
        launch_result = contract.read(launch_result_file)
        require(launch_result.get("candidate") == binding and launch_result.get("operation", {}).get("launch_requested") is True
                and launch_result.get("attempt_sha256") == contract.digest(launch_attempt), "BOUND_RECOVERY_REOPEN_REQUIRED")
        retained = c.retention(module, lab, client, "after-journal-recovery", actual, binding)
        contract.exclusive(OUTPUT / "host-journal-recovery-verified.json", {**value, **retained, "status": "rollback-personal-retained",
            "transaction_id": expected, "before_sha256": contract.digest(before_path),
            "reopen_attempt_sha256": contract.digest(launch_attempt),
            "reopen_result_sha256": contract.digest(launch_result_file), "launch_source": launch_prefix})
        print("同ID rollback、journal/pending清除及原个人实例/数据保留已验证；不是新主机配置成功。")
    finally:
        client.close()


if __name__ == "__main__": main()
