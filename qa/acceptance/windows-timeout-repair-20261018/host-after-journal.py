"""同ID恢复已证实结束后，独立验证新host请求；原failed请求永不重放。"""
import json
from pathlib import Path

import controller as c

OUTPUT = Path(__file__).resolve().parent
PREFIX = "after-journal-"
contract, require = c.contract, c.require


def recovery(binding):
    source = OUTPUT / "host-journal-recovery-verified.json"
    value = contract.read(source)
    before_file = OUTPUT / "host-journal-recovery-before.json"
    before = contract.read(before_file)
    launch_source = value.get("launch_source", "host-journal-reopen")
    require(launch_source in {"host-journal-reopen", "host-journal-resume-launch", "host-journal-cancelled-relaunch", "host-journal-cancelled-start"}, "RECOVERY_LAUNCH_SOURCE_REQUIRED")
    launch = OUTPUT / (launch_source + ".attempt.json")
    result_file = OUTPUT / (launch_source + ".result.json")
    result = contract.read(result_file)
    intent = contract.read(launch)
    if launch_source == "host-journal-cancelled-start":
        diagnosis_file = OUTPUT / "host-journal-cancelled-relaunch-readonly-diagnosis.json"
        cancelled_file = OUTPUT / "host-journal-cancelled-before.json"
        recycle_file = OUTPUT / "host-journal-cancelled-relaunch.result.json"
        recycle_attempt = OUTPUT / "host-journal-cancelled-relaunch.attempt.json"
        diagnosis, cancelled, recycled = [contract.read(p) for p in (diagnosis_file, cancelled_file, recycle_file)]
        require(diagnosis.get("candidate") == cancelled.get("candidate") == recycled.get("candidate") == binding
                and intent.get("diagnosis_sha256") == contract.digest(diagnosis_file)
                and intent.get("cancelled_before_sha256") == diagnosis.get("cancelled_before_sha256") == contract.digest(cancelled_file)
                and intent.get("failed_result_sha256") == diagnosis.get("failed_result_sha256") == contract.digest(recycle_file)
                and recycled.get("attempt_sha256") == contract.digest(recycle_attempt)
                and recycled.get("operation", {}).get("phase") == "recycle"
                and recycled.get("operation", {}).get("error_id") == "OTHER_LIVE_WIZARD_APPEARED"
                and recycled.get("operation", {}).get("launch_requested") is False
                and diagnosis.get("details", {}).get("mutation_executed") is False,
                "START_ONLY_FAILED_RECYCLE_SOURCE_REQUIRED")
        starter = c.load("start_only_source_validation", OUTPUT / "resume-cancelled-start.py")
        starter.ready(diagnosis, cancelled)
        starter.ready(intent["before"], cancelled)
        retained_runtime = c.load("start_only_original_instance", c.OLD / "verify-retained-business.py")
        for runtime in (diagnosis["runtime"], cancelled["runtime"], intent["before"]["runtime"]):
            contract.runtime_matches(runtime, binding)
            retained_runtime.same_restored_runtime(runtime, before["runtime"], binding)
        # 以下沿用取消前已绑定的来源链，保持真正启动intent独立。
        source_intent = contract.read(recycle_attempt)
        require(source_intent.get("candidate") == binding, "FAILED_RECYCLE_CANDIDATE_REQUIRED")
    else:
        source_intent = intent
    failed_attempt = OUTPUT / "first-host-approved-existing-root-submit.attempt.json"
    failed_file = OUTPUT / "first-host-approved-existing-root-submit.result.json"
    failed = contract.read(failed_file)
    require(value.get("candidate") == before.get("candidate") == result.get("candidate") == intent.get("candidate") == failed.get("candidate") == binding
            and value.get("status") == "rollback-personal-retained" and failed.get("status") == "failed"
            and failed.get("attempt_sha256") == contract.digest(failed_attempt)
            and value.get("failed_host_attempt_sha256") == before.get("failed_host_attempt_sha256") == contract.digest(failed_attempt)
            and value.get("failed_host_result_sha256") == before.get("failed_host_result_sha256") == contract.digest(failed_file)
            and value.get("before_sha256") == contract.digest(before_file)
            and value.get("reopen_attempt_sha256") == result.get("attempt_sha256") == contract.digest(launch)
            and source_intent.get("before_sha256") == contract.digest(before_file)
            and source_intent.get("transaction_id") == before["metadata"].get("transaction_id")
            and value.get("reopen_result_sha256") == contract.digest(result_file)
            and result.get("operation", {}).get("launch_requested") is True
            and value.get("transaction_id") == before["metadata"].get("transaction_id") == value["metadata"].get("transaction_id")
            and value["metadata"].get("outcome") == "rollback" and value["metadata"].get("journal_present") is False
            and value["metadata"].get("pending") is False and value["metadata"].get("consent_count") == 0
            and value["metadata"].get("wizard_count") == 1 and value["metadata"].get("other_powershell_count") == 0
            and value["metadata"].get("wizard_children") == []
            and len(value["metadata"].get("services", [])) == 2
            and {s.get("name") for s in value["metadata"]["services"]} == {"PartyOpsHost", "PartyOpsUpdateService"}
            and all(s.get("state") == "Stopped" for s in value["metadata"]["services"])
            and value["snapshot"].get("user_mode") == value["snapshot"].get("system_mode") == "personal"
            and value.get("inputs") == c.preserved_inputs(), "CONFIRMED_FAILED_TRANSACTION_RECOVERY_REQUIRED")
    baseline = contract.read(c.OLD / "retention-baseline.json")
    require(all(value.get(key) == baseline[key] for key in ("retained", "calendar_id", "calendar_sha256")),
            "RECOVERED_ORIGINAL_BUSINESS_REQUIRED")
    contract.runtime_matches(value["runtime"], binding)
    contract.runtime_matches(before["runtime"], binding)
    runtime_validator = c.load("recovery_retained_instance", c.OLD / "verify-retained-business.py")
    runtime_validator.same_restored_runtime(value["runtime"], before["runtime"], binding)
    if launch_source in {"host-journal-resume-launch", "host-journal-cancelled-relaunch", "host-journal-cancelled-start"}:
        resume_attempt = OUTPUT / "host-journal-resume-launch.attempt.json"
        resume_result = OUTPUT / "host-journal-resume-launch.result.json"
        resumed = contract.read(resume_attempt)
        resumed_result = contract.read(resume_result)
        diagnosis = contract.read(OUTPUT / "host-journal-reopen-readonly-diagnosis.json")
        require(resumed.get("candidate") == resumed_result.get("candidate") == binding
                and resumed_result.get("attempt_sha256") == contract.digest(resume_attempt)
                and resumed_result.get("operation", {}).get("launch_requested") is True
                and resumed.get("partial_reopen_attempt_sha256") == contract.digest(OUTPUT / "host-journal-reopen.attempt.json")
                and resumed.get("readonly_diagnosis_sha256") == contract.digest(OUTPUT / "host-journal-reopen-readonly-diagnosis.json")
                and diagnosis.get("mutation_script_executed") is False and diagnosis.get("tasks") == []
                and diagnosis.get("failed_script_local") == "host-journal-reopen-failed-script.ps1"
                and diagnosis.get("failed_script_sha256") == contract.digest(OUTPUT / "host-journal-reopen-failed-script.ps1"),
                "RESUME_PARTIAL_REOPEN_CHAIN_REQUIRED")
        if launch_source in {"host-journal-cancelled-relaunch", "host-journal-cancelled-start"}:
            cancelled_file = OUTPUT / "host-journal-cancelled-before.json"
            cancelled = contract.read(cancelled_file)
            require(cancelled.get("candidate") == binding and source_intent.get("cancelled_before_sha256") == contract.digest(cancelled_file)
                    and source_intent.get("resume_attempt_sha256") == cancelled.get("resume_attempt_sha256") == contract.digest(resume_attempt)
                    and source_intent.get("resume_result_sha256") == cancelled.get("resume_result_sha256") == contract.digest(resume_result)
                    and cancelled.get("before_sha256") == contract.digest(before_file), "CANCELLED_RELAUNCH_SOURCE_CHAIN_REQUIRED")
            validator = c.load("cancelled_relaunch_validation", OUTPUT / "recover-host-journal.py")
            validator.can_cancelled_relaunch(cancelled, before)
            runtime_validator.same_restored_runtime(cancelled["runtime"], before["runtime"], binding)
    return value


def configure():
    h = c.load("confirmed_journal_host", OUTPUT / "host-personal.py")
    h.RECORD_PREFIX = PREFIX
    h.HOST_TASK = "PartyOps-QA-After-Journal-Host-20261018"
    h.RESTORE_TASK = "PartyOps-QA-After-Journal-Personal-20261018"
    original_inputs = h.current_inputs
    def inputs(binding):
        result = original_inputs(binding)
        recovery(binding)
        return result
    h.current_inputs = inputs
    def source_link(binding):
        recovery(binding)
        return {"recovery_verified_sha256": contract.digest(OUTPUT / "host-journal-recovery-verified.json")}
    h.RECOVERY_SOURCE_LINK = source_link
    def prepare(first, host, transition, binding, since, snapshot, actual):
        verified = recovery(binding)
        require(snapshot.get("user_mode") == snapshot.get("system_mode") == "personal"
                and not snapshot.get("pending_mode_switch") and not snapshot.get("user_journal_present"),
                "RECOVERED_PERSONAL_ROLE_REQUIRED")
        current = [p for p in snapshot["processes"] if p["name"] == "PartyOpsWizard.exe"]
        expected = [p for p in verified["snapshot"]["processes"] if p["name"] == "PartyOpsWizard.exe"]
        require(len(current) == len(expected) == 1 and first.same_wizard(current[0], expected[0]), "RECOVERY_WIZARD_CHANGED")
        validate = c.load("confirmed_journal_runtime", c.OLD / "verify-retained-business.py")
        validate.same_restored_runtime(actual, verified["runtime"], binding)
        first.OLD_PID, first.OLD_CREATED = current[0]["pid"], current[0]["created"]
        script = first.prepare_script(host, transition, binding, since, actual["processes"][0])
        script = script.replace("$p.CreationDate.ToUniversalTime().ToString('o') -cne $target.created",
            "(-not (Test-CimCreationInstant $p.CreationDate $target.created))")
        for variable in ("before", "after"):
            script = script.replace("$" + variable + "[0].created -cne $target.personal.created",
                "(-not (Test-CimCreationInstant ([DateTimeOffset]::Parse($" + variable + "[0].created).UtcDateTime) $target.personal.created))")
        script = script.replace("PartyOps-QA-First-Host-20261017", h.HOST_TASK)
        guard = "if($pending -or $journal) {throw 'PENDING_MODE_SWITCH_REQUIRES_RECOVERY'}"
        require(guard in script, "EXPECTED_PREPARE_GUARD_REQUIRED")
        extra = r"""
if(@(Get-CimInstance Win32_Process -Filter "Name='consent.exe'").Count) {throw 'CONSENT_STILL_ACTIVE'}
if(@(Get-CimInstance Win32_Process|Where-Object {$_.Name -in @('powershell.exe','pwsh.exe') -and $_.ProcessId -ne $PID}).Count) {throw 'OTHER_POWERSHELL_STILL_ACTIVE'}
$all=@(Get-CimInstance Win32_Process);$w=@($all|Where-Object {$_.Name -eq 'PartyOpsWizard.exe'})
if($w.Count -ne 1 -or @($all|Where-Object {$_.ParentProcessId -eq $w[0].ProcessId}).Count) {throw 'WIZARD_HELPER_OR_CHILD_ACTIVE'}
$s=New-Object -ComObject 'Schedule.Service';$s.Connect();if(@($s.GetFolder('\').GetTasks(1)|Where-Object {$_.Name -eq 'TASKNAME'}).Count){throw 'NEW_HOST_TASK_ALREADY_EXISTS'}
""".replace("TASKNAME", h.HOST_TASK)
        return script.replace(guard, guard + "\n" + extra, 1)
    h.HOST_PREPARE_HOOK = prepare
    return h


if __name__ == "__main__": configure().main()
