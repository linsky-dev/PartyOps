"""正式个人恢复QA：只用产品向导、原目录及真实UAC，prepare/submit各一次。"""
from __future__ import annotations
import argparse
import json
import re
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from datetime import datetime
from pathlib import Path, PureWindowsPath

import retention_contract as contract

OUTPUT = Path(__file__).resolve().parent
ROOT = OUTPUT.parents[2]


def require(value, code):
    if not value: raise RuntimeError(code)


def public_error(error):
    """仅保存工具自身的公开常量码，不保存远端异常正文或HTTP正文。"""
    value = str(error)
    return value if re.fullmatch(r"[A-Z][A-Z0-9_]{1,100}", value) else "RESTORE_QA_OPERATION_FAILED"


def load(path, name):
    import importlib.util
    spec = importlib.util.spec_from_file_location(name, path); module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module); return module


def host_success(value, binding, attempt_sha, first):
    status = value.get("host_status", {})
    require(value.get("candidate") == binding and value.get("attempt_sha256") == attempt_sha
            and value.get("case") == "approved-existing-root" and value.get("status") == "bootstrap-page-observed"
            and value.get("http_status") == 200 and value.get("bootstrap_admin_page") is True
            and value.get("public_error_codes") == [] and status.get("present") is True and status.get("stage") == "ready"
            and status.get("code") == "" and isinstance(status.get("pid"), int) and status["pid"] > 0
            and value.get("user_mode") == value.get("system_mode") == "host"
            and value.get("qa_host_data_directory") == first.ROOT_HOST_DATA
            and value.get("original_personal_restore_directory") == first.PERSONAL_DATA,
            "SUCCESSFUL_BOUND_FIRST_HOST_REQUIRED")


SERVICES = r"""
$ErrorActionPreference='Stop'
$services=@(Get-CimInstance Win32_Service|Where-Object {$_.Name -in @('PartyOpsHost','PartyOpsUpdateService')}|ForEach-Object {
  @{name=$_.Name;state=$_.State;start_mode=$_.StartMode;pid=[int]$_.ProcessId}
})
$hostService=@($services|Where-Object {$_.name -eq 'PartyOpsHost' -and $_.state -eq 'Running'})
$tree=@()
if($hostService.Count -eq 1) {
  $all=@(Get-CimInstance Win32_Process);$ids=@([int]$hostService[0].pid)
  for($level=0;$level -lt 32 -and $ids.Count;$level++) {
    $rows=@($all|Where-Object {$_.ProcessId -in $ids})
    $tree+=@($rows|ForEach-Object {@{pid=[int]$_.ProcessId;parent_pid=[int]$_.ParentProcessId;name=$_.Name;path=$_.ExecutablePath;created=$_.CreationDate.ToUniversalTime().ToString('o')}})
    $seen=@($tree|ForEach-Object {$_.pid})
    $ids=@($all|Where-Object {$_.ParentProcessId -in $ids -and $_.ProcessId -notin $seen}|ForEach-Object {[int]$_.ProcessId})
  }
}
@{services=$services;host_process_tree=$tree}|ConvertTo-Json -Depth 5 -Compress
"""


def services_match(value, state):
    rows = value.get("services", [])
    require(len(rows) == 2 and {row.get("name") for row in rows} == {"PartyOpsHost", "PartyOpsUpdateService"}
            and all(row.get("state") == state for row in rows), "HOST_SERVICES_STATE_MISMATCH")
    if state == "Stopped": require(all(row.get("start_mode") == "Manual" for row in rows), "HOST_AUTOSTART_NOT_DEACTIVATED")


def prepare_script(host, first, transition, binding, since, wizard):
    target = json.dumps(wizard, ensure_ascii=False).replace("'", "''")
    tail = "$scheduler=" + host.launch_script(binding, since).split("$scheduler=", 1)[1]
    tail = tail.replace("--reconfigure --initial-role host", "--reconfigure --initial-role personal")
    tail = tail.replace("$request.task", "'PartyOps-QA-Restore-Personal-20261017'")
    return host.prefix(binding, since) + transition.START_TIME_MATCH + "$target='" + target + "'|ConvertFrom-Json\n" + r"""
if($pending -or $journal) {throw 'PENDING_TRANSACTION_REQUIRES_RECOVERY'}
$rows=@(Get-CimInstance Win32_Process -Filter "Name='PartyOpsWizard.exe'")
if($rows.Count -ne 1) {throw 'SINGLE_QA_HOST_WIZARD_REQUIRED'}
$p=$rows[0];$owner=Invoke-CimMethod -InputObject $p -MethodName GetOwner
if($p.ProcessId -ne $target.pid -or $p.ExecutablePath -ne $wizard -or $p.SessionId -ne $desktop[0].session_id -or $owner.ReturnValue -ne 0 -or $owner.User -ne 'partyopsuser' -or $owner.Domain -ne $env:COMPUTERNAME -or -not (Test-CimCreationInstant $p.CreationDate $target.created)) {throw 'BOUND_HOST_WIZARD_CHANGED'}
function Product-Processes {return (@(Get-CimInstance Win32_Process -Filter "Name='PartyOps.exe'"|Sort-Object ProcessId|ForEach-Object {
  ([string]$_.ProcessId)+'|'+$_.CreationDate.ToUniversalTime().ToString('o')
}) -join ';')}
$before=Product-Processes
$handle=Get-Process -Id $target.pid -ErrorAction Stop
if(-not (Test-CimCreationInstant $handle.StartTime $target.created)) {throw 'QA_WIZARD_HANDLE_CHANGED'}
$closed=$false;$terminated=$false
if($handle.MainWindowHandle -ne 0) {$closed=$handle.CloseMainWindow();if($closed){$null=$handle.WaitForExit(10000)}}
if(-not $handle.HasExited) {$handle.Kill();$terminated=$true;if(-not $handle.WaitForExit(10000)){throw 'QA_WIZARD_DID_NOT_EXIT'}}
if((Product-Processes) -cne $before) {throw 'PRODUCT_PROCESS_CHANGED_DURING_PREPARE'}
if(@(Get-CimInstance Win32_Process -Filter "Name='PartyOpsWizard.exe'").Count) {throw 'OTHER_WIZARD_APPEARED'}
""" + tail.replace("@{launch_requested=$true;", "@{old_qa_wizard_controlled_termination=$terminated;close_main_window_accepted=$closed;launch_requested=$true;")


TASK_ABSENT = r"""
$scheduler=New-Object -ComObject 'Schedule.Service';$scheduler.Connect();$folder=$scheduler.GetFolder('\')
if(@($folder.GetTasks(1)|Where-Object {$_.Name -eq 'PartyOps-QA-Restore-Personal-20261017'}).Count) {throw 'RESTORE_TASK_ALREADY_EXISTS'}
"""

GUEST_CLOCK = "@{guest_utc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Compress"
TASK_OBSERVE = r"""
$ErrorActionPreference='Stop'
$s=New-Object -ComObject 'Schedule.Service';$s.Connect()
$t=$s.GetFolder('\').GetTask('PartyOps-QA-Restore-Personal-20261017');$d=$t.Definition
$actualSid=([Security.Principal.NTAccount]::new($d.Principal.UserId)).Translate([Security.Principal.SecurityIdentifier]).Value
$expectedSid=(Get-LocalUser -Name 'partyopsuser').SID.Value
@{name=$t.Name;state=[int]$t.State;last_run=$t.LastRunTime.ToUniversalTime().ToString('o');user=$d.Principal.UserId;original_user_matches=($actualSid -eq $expectedSid);run_level=[int]$d.Principal.RunLevel;logon_type=[int]$d.Principal.LogonType;actions=@($d.Actions|ForEach-Object {@{path=$_.Path;arguments=$_.Arguments;working_directory=$_.WorkingDirectory}})}|ConvertTo-Json -Depth 4 -Compress
"""


def observe_recovery(snapshot, task, expected, host):
    """用同一Guest时域的任务启动/进程创建绑定已运行向导，不拿宿主时钟作门槛。"""
    require(task.get("name") == "PartyOps-QA-Restore-Personal-20261017" and task.get("state") == 4
            and task.get("run_level") == 0 and task.get("logon_type") == 3
            and task.get("original_user_matches") is True, "OBSERVED_TASK_LUA_IDENTITY_REQUIRED")
    actions = task.get("actions", [])
    require(len(actions) == 1 and PureWindowsPath(actions[0].get("path", "")) == PureWindowsPath(contract.INSTALL + r"\PartyOpsWizard.exe")
            and PureWindowsPath(actions[0].get("working_directory", "")) == PureWindowsPath(contract.INSTALL)
            and actions[0].get("arguments") == "--reconfigure --initial-role personal", "OBSERVED_TASK_ACTION_REQUIRED")
    rows = [p for p in snapshot.get("processes", []) if p.get("name") == "PartyOpsWizard.exe"]
    require(len(rows) == 1 and rows[0]["pid"] == expected["ProcessId"]
            and datetime.fromisoformat(rows[0]["created"].replace("Z", "+00:00")) == datetime.fromisoformat(expected["created"].replace("Z", "+00:00")),
            "OBSERVED_EXISTING_WIZARD_CHANGED")
    host.validate_wizard(snapshot, task["last_run"])
    ports = [p for p in snapshot.get("listeners", []) if p.get("owned_by_wizard") and p.get("pid") == rows[0]["pid"]]
    require(len(ports) == 1 and ports[0].get("loopback") is True, "OBSERVED_SINGLE_WIZARD_LISTENER_REQUIRED")
    return rows[0]


def recovery_links(binding):
    """恢复只续接这次失败prepare，保留原回执并显式绑定部分完成证据。"""
    failure = OUTPUT / "restore-personal-prepare.result.json"
    attempt = OUTPUT / "restore-personal-prepare.attempt.json"
    value, intent = contract.read(failure), contract.read(attempt)
    require(value.get("status") == "failed" and value.get("candidate") == intent.get("candidate") == binding
            and value.get("attempt_sha256") == contract.digest(attempt)
            and value.get("original_personal_instance", "missing") is None
            and intent.get("original_personal_instance", "missing") is None,
            "FAILED_PARTIAL_PREPARE_REQUIRED")
    return {"failed_prepare_sha256": contract.digest(failure), "failed_prepare_attempt_sha256": contract.digest(attempt),
            "preflight_sha256": contract.digest(OUTPUT / "restore-personal-preflight-inspect.json"),
            "partial_inspection_sha256": contract.digest(OUTPUT / "restore-personal-after-prepare-failure-inspect.json")}


def recovery_script(host, transition, binding, since, runtime, service_state):
    """只启动尚未创建的LUA任务；无关闭进程、配置提交或提权。"""
    target = runtime["processes"][0]
    service = next(s for s in service_state["services"] if s["name"] == "PartyOpsHost")
    payload = json.dumps({"pid": target["pid"], "created": target["created"], "service_pid": service["pid"]}).replace("'", "''")
    tail = "$scheduler=" + host.launch_script(binding, since).split("$scheduler=", 1)[1]
    tail = tail.replace("--reconfigure --initial-role host", "--reconfigure --initial-role personal").replace("$request.task", "'PartyOps-QA-Restore-Personal-20261017'")
    # 分段公开码用于定位COM失败；不输出异常消息、命令行或用户数据。
    tail = tail.replace("$definition=$scheduler.NewTask(0)", "$stage='RESTORE_QA_TASK_DEFINITION_FAILED';$definition=$scheduler.NewTask(0)")
    tail = tail.replace("$registered=$folder.RegisterTaskDefinition", "$stage='RESTORE_QA_TASK_REGISTER_FAILED';$registered=$folder.RegisterTaskDefinition")
    tail = tail.replace("$registered.Run($null)", "$stage='RESTORE_QA_TASK_RUN_FAILED';$registered.Run($null)")
    return host.prefix(binding, since) + transition.START_TIME_MATCH + "$target='" + payload + "'|ConvertFrom-Json\n" + r"""
$stage='RESTORE_QA_RECOVERY_GUARD_FAILED'
try {
if($pending -or $journal) {throw 'PENDING_TRANSACTION_REQUIRES_RECOVERY'}
if(@(Get-CimInstance Win32_Process -Filter "Name='PartyOpsWizard.exe'").Count) {throw 'RECOVERY_REQUIRES_NO_WIZARD'}
$p=@(Get-CimInstance Win32_Process -Filter "Name='PartyOps.exe'")
$service=Get-CimInstance Win32_Service -Filter "Name='PartyOpsHost'"
if($p.Count -ne 1 -or $p[0].ProcessId -ne $target.pid -or $p[0].SessionId -ne 0 -or $p[0].ParentProcessId -ne $target.service_pid -or -not (Test-CimCreationInstant $p[0].CreationDate $target.created) -or $service.State -ne 'Running' -or $service.ProcessId -ne $target.service_pid) {throw 'RECOVERY_HOST_PROCESS_CHANGED'}
if(@(Get-NetTCPConnection -State Listen -LocalPort 18775 -ErrorAction SilentlyContinue).Count) {throw 'RECOVERY_PERSONAL_LISTENER_APPEARED'}
""" + TASK_ABSENT + tail + r"""
} catch {
  $code=$stage
  if($_.Exception.Message -cmatch '^[A-Z][A-Z0-9_]{1,100}$') {$code=$_.Exception.Message}
  @{launch_requested=$false;error_code=$code;hresult=[int]$_.Exception.HResult}|ConvertTo-Json -Compress
}
"""


def personal_csrf(page, base):
    class PersonalForm(base):
        def handle_starttag(self, tag, attrs):
            if tag == "form":
                values = dict(attrs)
                attrs = [(key, value) for key, value in attrs if key != "id"] + [("id", "host-form" if values.get("id") == "personal-form" else "ignored")]
            super().handle_starttag(tag, attrs)
    parsed = PersonalForm(); parsed.feed(page)
    require(parsed.title.strip() == "党建智办 · 重新配置运行角色" and parsed.mode == "personal"
            and isinstance(parsed.csrf, str) and re.fullmatch(r"[A-Za-z0-9_-]{16,128}", parsed.csrf), "PERSONAL_RECONFIGURATION_FORM_REQUIRED")
    return parsed.csrf


def personal_instance(snapshot, actual, binding, host_services=None, expected_host_pid=None):
    """绑定候选、普通桌面身份及18775归属；创建时刻按UTC瞬时值比较。"""
    owners = actual.get("http_owners", [])
    def windows_path(value): return str(PureWindowsPath(value)).casefold()
    require(isinstance(owners, list) and len(owners) <= 1, "SINGLE_PERSONAL_HTTP_OWNER_REQUIRED")
    processes = actual.get("processes", [])
    selected = [p for p in processes if owners and p.get("pid") == owners[0]]
    require(len(selected) == len(owners), "PERSONAL_HTTP_PROCESS_REQUIRED")
    process = selected[0] if selected else None
    extras = [p for p in processes if not process or p.get("pid") != process["pid"]]
    if process:
        contract.runtime_matches({**actual, "processes": selected}, binding)
    else:
        # 正常主机状态可已停止旧个人实例；只接受成功Host服务进程且18775无人。
        require(host_services is not None and len(extras) == 1, "BOUND_HOST_WITHOUT_PERSONAL_REQUIRED")
        require(all(actual.get(key) == binding.get(key) for key in
                    ("manifest_sha256", "executable_sha256", "wizard_sha256")), "HOST_CANDIDATE_IDENTITY_REQUIRED")
    if extras:
        require(host_services is not None and len(extras) == 1 and extras[0].get("pid") == expected_host_pid,
                "UNBOUND_EXTRA_PARTYOPS_PROCESS")
        services_match(host_services, "Running")
        service = next(s for s in host_services["services"] if s["name"] == "PartyOpsHost")
        tree = host_services.get("host_process_tree", [])
        require(len({p.get("pid") for p in tree}) == len(tree), "HOST_PROCESS_TREE_INVALID")
        by_pid = {p["pid"]: p for p in tree}; current = extras[0]; visited = set()
        while True:
            node = by_pid.get(current["pid"])
            require(node is not None and node["pid"] not in visited
                    and windows_path(node.get("path", "")).startswith(windows_path(contract.INSTALL) + "\\")
                    and datetime.fromisoformat(node["created"].replace("Z", "+00:00")) == datetime.fromisoformat(current["created"].replace("Z", "+00:00")),
                    "HOST_SERVICE_PROCESS_IDENTITY_REQUIRED")
            visited.add(node["pid"])
            if node["pid"] == service["pid"]:
                require(windows_path(node["path"]) == windows_path(contract.INSTALL + r"\PartyOpsService.exe"),
                        "HOST_SERVICE_WRAPPER_REQUIRED")
                break
            parent = by_pid.get(node["parent_pid"])
            require(parent is not None and datetime.fromisoformat(parent["created"].replace("Z", "+00:00"))
                    <= datetime.fromisoformat(node["created"].replace("Z", "+00:00")), "HOST_SERVICE_ANCESTRY_REQUIRED")
            current = parent
        require(windows_path(extras[0]["path"]) == windows_path(contract.INSTALL + r"\PartyOps.exe"), "HOST_CANDIDATE_PATH_REQUIRED")
    observed_processes = [p for p in snapshot.get("processes", []) if p.get("name") == "PartyOps.exe"]
    require({p["pid"] for p in observed_processes} == {p["pid"] for p in processes}, "PARTYOPS_SNAPSHOT_CHANGED")
    if process is None:
        row = observed_processes[0]
        require(row.get("installed_path_matches") is True and row.get("session_id") == 0
                and row.get("owner_is_original_user") is False
                and datetime.fromisoformat(row["created"].replace("Z", "+00:00"))
                == datetime.fromisoformat(extras[0]["created"].replace("Z", "+00:00")), "BOUND_HOST_SNAPSHOT_REQUIRED")
        return None
    rows = [p for p in observed_processes if p.get("pid") == process["pid"]]
    require(len(rows) == 1 and rows[0].get("pid") == process["pid"]
            and rows[0].get("installed_path_matches") is True and rows[0].get("owner_is_original_user") is True
            and rows[0].get("session_id") == snapshot.get("session_id"), "PERSONAL_HTTP_OWNER_REQUIRED")
    created = datetime.fromisoformat(process["created"].replace("Z", "+00:00"))
    observed = datetime.fromisoformat(rows[0]["created"].replace("Z", "+00:00"))
    require(created.tzinfo is not None and observed == created, "PERSONAL_PROCESS_CREATION_CHANGED")
    return {"candidate": binding, "pid": process["pid"], "created": created.isoformat(),
            "path": process["path"].casefold(), "session_id": rows[0]["session_id"],
            "owner_is_original_user": True, "http_owners": actual["http_owners"]}


def runtime_ready(snapshot, services, actual, binding, started, original_instance=None):
    require(snapshot.get("user_mode") == snapshot.get("system_mode") == "personal" and snapshot.get("pending_mode_switch") is False
            and snapshot.get("user_journal_present") is False, "PERSONAL_MODE_CLEANUP_REQUIRED")
    services_match(services, "Stopped")
    current = personal_instance(snapshot, actual, binding)
    # 产品可复用原目录的健康实例；只有执行前完整绑定的同一实例可以早于动作。
    if original_instance is None or current != original_instance:
        contract.runtime_matches(actual, binding, started)


def finish(client, record, destination):
    """收尾失败必须落盘failed并抛错；调用者只能在本函数成功后构造返回。"""
    close_failed = False
    try: client.close()
    except Exception:
        close_failed = True; record.update(status="failed", cleanup_error="SSH_CLOSE_FAILED")
    record["completed_at"] = contract.now(); contract.exclusive(destination, record)
    if close_failed: raise RuntimeError("SSH_CLOSE_FAILED")


def run(args):
    first = load(OUTPUT / "first-host-qa.py", "restore_first_helpers")
    host = load(OUTPUT / "win10-host-wizard-qa.py", "restore_host_helpers")
    form = load(OUTPUT / "win10-host-form-qa.py", "restore_form_helpers")
    transition = load(OUTPUT / "runtime-transition.py", "restore_retention_helpers")
    windows = load(ROOT / "qa/vm-lab/scripts/exercise-windows-business.py", "restore_windows")
    matrix, media = windows.load_configuration(); matrix["defaults"].update(primary_root=str(host.LAB_ROOT), fallback_root=str(host.LAB_ROOT))
    lab = windows.QemuLab(matrix, host.LAB_ROOT, media)
    binding = contract.candidate(lab, ROOT, args.package, args.sha256); contract.completed_install(OUTPUT / "overlay-install", binding)
    source = OUTPUT / "first-host-approved-existing-root-submit.result.json"
    host_receipt = contract.read(source)
    host_success(host_receipt, binding, contract.digest(OUTPUT / "first-host-approved-existing-root-submit.attempt.json"), first)
    original = contract.read(OUTPUT / "first-host-prepare.result.json")
    require(original.get("candidate") == binding and original.get("status") == "prepared"
            and original.get("attempt_sha256") == contract.digest(OUTPUT / "first-host-prepare.attempt.json"), "BOUND_HOST_WIZARD_REQUIRED")
    attempt = OUTPUT / ("restore-personal-" + args.action + ".attempt.json")
    require(not attempt.exists(), "RESTORE_ACTION_ALREADY_ATTEMPTED_INSPECT_ONLY")
    preserved = transition.preserved_inputs()[0]
    observed = args.action == "observe-recovery" or getattr(args, "use_observed_recovery", False)
    recovered = args.action == "recover-prepare" or getattr(args, "use_recovered_prepare", False) or observed
    require(not getattr(args, "use_recovered_prepare", False) or args.action == "submit", "RECOVERED_PREPARE_ONLY_FOR_SUBMIT")
    require(not getattr(args, "use_observed_recovery", False) or args.action == "submit", "OBSERVED_RECOVERY_ONLY_FOR_SUBMIT")
    links = recovery_links(binding) if recovered else {}
    if observed:
        recovery_attempt = OUTPUT / "restore-personal-recover-prepare.attempt.json"
        recovery_result = OUTPUT / "restore-personal-recover-prepare.result.json"
        failed_recovery = contract.read(recovery_result)
        require(failed_recovery.get("candidate") == binding and failed_recovery.get("status") == "failed"
                and failed_recovery.get("attempt_sha256") == contract.digest(recovery_attempt)
                and failed_recovery.get("operation", {}).get("launch_requested") is True, "LAUNCHED_RECOVERY_FAILURE_REQUIRED")
        links.update(recovery_attempt_sha256=contract.digest(recovery_attempt), recovery_failed_sha256=contract.digest(recovery_result),
                     running_task_inspection_sha256=contract.digest(OUTPUT / "restore-personal-task-after-timeout.json"))
    prepare_name = "observe-recovery" if observed else ("recover-prepare" if recovered else "prepare")
    prepare_attempt = OUTPUT / ("restore-personal-" + prepare_name + ".attempt.json")
    prepared = contract.read(OUTPUT / ("restore-personal-" + prepare_name + ".result.json")) if args.action == "submit" else None
    if prepared:
        require(prepared.get("status") == ("observed-prepared" if observed else "prepared") and prepared.get("candidate") == binding
                and prepared.get("attempt_sha256") == contract.digest(prepare_attempt)
                and prepared.get("original_personal_instance") == contract.read(prepare_attempt).get("original_personal_instance")
                and "original_personal_instance" in prepared
                and "original_personal_instance" in contract.read(prepare_attempt)
                and (not recovered or prepared.get("recovery_links") == contract.read(prepare_attempt).get("recovery_links") == links),
                "SUCCESSFUL_RESTORE_PREPARE_REQUIRED")
    since = prepared["since"] if prepared else contract.now()
    contract.bind_guest(lab, windows.qmp)
    client = windows.windows.connect(lab, matrix["targets"]["win10-x64"]); record = None
    destination = OUTPUT / ("restore-personal-" + args.action + ".result.json")
    try:
        name = "host-wizard-" + uuid.uuid4().hex[:12]
        snapshot = host.transfer(client, windows.windows.execute, first.inspection_script(host, binding, original["since"]), name)
        command = "& ([scriptblock]::Create([IO.File]::ReadAllText('C:/ProgramData/PartyOps-VM-Lab/" + name + "/operation.ps1',[Text.Encoding]::UTF8)))"
        def inspect():
            contract.bind_guest(lab, windows.qmp)
            return json.loads(windows.windows.execute(client, command, timeout=120))
        def services(): return json.loads(windows.windows.execute(client, SERVICES))
        def guest_clock():
            start = contract.now(); guest = json.loads(windows.windows.execute(client, GUEST_CLOCK))["guest_utc"]; end = contract.now()
            instant = datetime.fromisoformat(guest.replace("Z", "+00:00"))
            require(instant.tzinfo is not None, "GUEST_UTC_REQUIRED")
            return {"host_before": start, "guest_utc": guest, "host_after": end,
                    "guest_minus_host_seconds": (instant - datetime.fromisoformat(start)).total_seconds()}
        clock = guest_clock()
        if not prepared: since = clock["guest_utc"]
        if args.action not in {"recover-prepare", "observe-recovery"}: host.validate_wizard(snapshot, prepared["wizard"]["created"] if prepared else original["wizard"]["created"])
        elif args.action == "observe-recovery": pass
        else:
            require(snapshot.get("username") == "partyopsuser" and snapshot.get("administrator") is False
                    and snapshot.get("session_id", 0) > 0 and snapshot.get("wizard_exists") is False
                    and not any(p.get("name") == "PartyOpsWizard.exe" for p in snapshot.get("processes", [])),
                    "RECOVERY_REQUIRES_NO_WIZARD_AND_ORDINARY_DESKTOP")
        require(not snapshot["pending_mode_switch"] and not snapshot["user_journal_present"], "NO_PENDING_HOST_TRANSACTION_REQUIRED")
        before_runtime, before_services = contract.probe(client, windows.windows.execute), services()
        original_instance = personal_instance(snapshot, before_runtime, binding, before_services, host_receipt["host_status"]["pid"])
        if prepared:
            require(original_instance == prepared.get("original_personal_instance"), "BOUND_ORIGINAL_PERSONAL_INSTANCE_CHANGED")
        if args.action == "observe-recovery":
            require(snapshot["user_mode"] == snapshot["system_mode"] == "host" and original_instance is None, "OBSERVATION_REQUIRES_HOST_ONLY")
            prior = contract.read(OUTPUT / "restore-personal-task-after-timeout.json")
            expected = next(p for p in prior["processes"] if p["Name"] == "PartyOpsWizard.exe")
            task = json.loads(windows.windows.execute(client, TASK_OBSERVE))
            # Cmdlet记录调度请求时间，COM记录实际开始时间；两者均须早于同一已绑定进程创建。
            require(datetime.fromisoformat(prior["tasks"][0]["last_run"].replace("Z", "+00:00")) <= datetime.fromisoformat(task["last_run"].replace("Z", "+00:00")) <= datetime.fromisoformat(expected["created"].replace("Z", "+00:00")), "OBSERVED_TASK_RERUN_REJECTED")
            wizard = observe_recovery(snapshot, task, expected, host)
            require(datetime.fromisoformat(wizard["created"].replace("Z", "+00:00")) <= datetime.fromisoformat(clock["guest_utc"].replace("Z", "+00:00")), "GUEST_CLOCK_BEFORE_WIZARD")
        elif args.action == "recover-prepare":
            require(snapshot["user_mode"] == snapshot["system_mode"] == "host" and original_instance is None,
                    "RECOVERY_REQUIRES_UNCHANGED_HOST_ONLY")
            previous = contract.read(OUTPUT / "restore-personal-preflight-inspect.json")["runtime"]["processes"][0]
            current = before_runtime["processes"][0]
            require(current["pid"] == previous["pid"] and datetime.fromisoformat(current["created"].replace("Z", "+00:00"))
                    == datetime.fromisoformat(previous["created"].replace("Z", "+00:00")), "RECOVERY_HOST_PROCESS_CHANGED")
            # 在写intent前只读确认同名任务不存在；动作脚本再次检查防止竞态。
            windows.windows.execute(client, "$ErrorActionPreference='Stop'\n" + TASK_ABSENT)
            script = recovery_script(host, transition, binding, since, before_runtime, before_services)
        elif args.action == "prepare":
            require(snapshot["user_mode"] == snapshot["system_mode"] == "host", "CURRENT_HOST_ROLE_REQUIRED")
            services_match(services(), "Running")
            wizard = [p for p in snapshot["processes"] if p["name"] == "PartyOpsWizard.exe"][0]
            require(first.same_wizard(wizard, original["wizard"]), "ORIGINAL_FIRST_HOST_WIZARD_CHANGED")
            script = prepare_script(host, first, transition, binding, since, wizard)
        else:
            wizard = [p for p in snapshot["processes"] if p["name"] == "PartyOpsWizard.exe"][0]
            require(first.same_wizard(wizard, prepared["wizard"]), "PREPARED_RESTORE_WIZARD_CHANGED")
            ports = {p["port"] for p in snapshot["listeners"] if p["owned_by_wizard"] and p["loopback"]}
            require(len(ports) == 1, "SINGLE_RESTORE_WIZARD_LISTENER_REQUIRED"); port = ports.pop()
            origin = "http://127.0.0.1:" + str(port)
            class RejectRedirect(urllib.request.HTTPRedirectHandler):
                def redirect_request(self, *_a, **_kw): raise RuntimeError("RESTORE_REDIRECT_REJECTED")
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), windows.GuestHTTPHandler(client.get_transport()), RejectRedirect())
            def request(path="/", body=None):
                actual = inspect(); host.validate_wizard(actual, wizard["created"])
                rows = [p for p in actual["processes"] if p["name"] == "PartyOpsWizard.exe"]
                require(len(rows) == 1 and first.same_wizard(rows[0], wizard)
                        and any(p["port"] == port and p["pid"] == wizard["pid"] and p["owned_by_wizard"] and p["loopback"]
                                for p in actual["listeners"]), "RESTORE_WIZARD_PID_PORT_CHANGED")
                require(path == "/" or re.fullmatch(r"/transactions/[A-Za-z0-9_-]{24}", path), "RESTORE_PATH_INVALID")
                headers = {"Origin": origin}
                if body is not None: headers["Content-Type"] = "application/x-www-form-urlencoded"
                try:
                    with opener.open(urllib.request.Request(origin + path, data=body, headers=headers), timeout=600) as response:
                        return response.status, response.read(2 * 1024**2)
                except Exception: raise RuntimeError("RESTORE_HTTP_FAILED_NO_RESUBMIT") from None
            code, page = request(); require(code == 200, "RESTORE_FORM_GET_FAILED")
            csrf = personal_csrf(page.decode("utf-8"), form.HostForm); del page
            body = urllib.parse.urlencode({"csrf": csrf, "mode": "personal", "port": "18775", "data_dir": first.PERSONAL_DATA}).encode(); del csrf
        require(transition.preserved_inputs()[0] == preserved, "ORIGINAL_BASELINE_CHANGED")
        action_clock = guest_clock(); action_started = action_clock["guest_utc"]
        contract.exclusive(attempt, {"time_utc": action_started, "since": since, "candidate": binding, "action": args.action,
            "host_success_sha256": contract.digest(source), "preserved_inputs": preserved,
            "original_personal_instance": original_instance,
            "recovery_links": links, "prepared_source": prepare_name,
            "clock_observation": action_clock, "host_time_utc": contract.now(),
            "original_personal_data_directory": first.PERSONAL_DATA, "configuration_route": "product-reconfigure-personal",
            "automatic_uac_acceptance": False, "account_creation": "not-attempted"})
        record = {"status": "failed", "candidate": binding, "since": since, "attempt_sha256": contract.digest(attempt),
            "runtime_action_started_at": action_started,
            "original_personal_instance": original_instance,
            "recovery_links": links, "prepared_source": prepare_name,
            "clock_observation": action_clock,
            "account_creation": "not-attempted", "uac_acceptance": "not-verified", "original_personal_data_directory": first.PERSONAL_DATA}
        if args.action == "observe-recovery":
            record.update(status="observed-prepared", wizard=wizard, task_observation=task, read_only=True)
            require(json.loads(windows.windows.execute(client, TASK_OBSERVE)) == task, "OBSERVED_TASK_CHANGED")
            observe_recovery(inspect(), task, expected, host)
        elif args.action in {"prepare", "recover-prepare"}:
            record["operation"] = host.transfer(client, windows.windows.execute, script, "host-wizard-" + uuid.uuid4().hex[:12])
            require(record["operation"].get("launch_requested") is True,
                    record["operation"].get("error_code", "RESTORE_QA_LAUNCH_FAILED"))
            deadline = time.monotonic() + 45
            while True:
                snapshot = inspect()
                try: host.validate_wizard(snapshot, since); break
                except RuntimeError:
                    require(time.monotonic() < deadline, "RESTORE_WIZARD_TIMEOUT_NO_RELAUNCH"); time.sleep(2)
            record.update(status="prepared", wizard=[p for p in snapshot["processes"] if p["name"] == "PartyOpsWizard.exe"][0])
        else:
            code, raw = request(body=body); del body
            require(code == 202, "RESTORE_TRANSACTION_REQUIRED"); path = form.transaction_path(json.loads(raw)); del raw
            record["states"] = []; deadline = time.monotonic() + 600
            while True:
                code, raw = request(path); require(code == 200, "RESTORE_POLL_FAILED")
                value = json.loads(raw); del raw
                require(value.get("transaction_id") == path.rsplit("/", 1)[1], "RESTORE_TRANSACTION_CHANGED")
                state = form.public_status(value); del value; record["states"].append(state)
                contract.exclusive(OUTPUT / ("restore-personal-observation-" + uuid.uuid4().hex[:12] + ".json"), state)
                if state["status"] in {"ready", "failed"}: break
                require(time.monotonic() < deadline, "RESTORE_PENDING_NO_RESUBMIT"); time.sleep(2)
            require(state == {"status": "ready", "code": "RUNTIME_READY"}, "PRODUCT_PERSONAL_RESTORE_FAILED")
            snapshot, stopped = inspect(), services()
            runtime = contract.probe(client, windows.windows.execute)
            runtime_ready(snapshot, stopped, runtime, binding, action_started, original_instance)
            record.update(status="restored-ready", runtime=runtime, services=stopped["services"],
                          user_mode=snapshot["user_mode"], pending_mode_switch=False, user_journal_present=False)
        require(transition.preserved_inputs()[0] == preserved, "ORIGINAL_BASELINE_CHANGED_AFTER_RESTORE")
        if recovered: require(all(links[k] == v for k, v in recovery_links(binding).items()), "PARTIAL_PREPARE_EVIDENCE_CHANGED")
        contract.bind_guest(lab, windows.qmp)
    except Exception as error:
        if record is not None: record.update(status="failed", error_type=type(error).__name__, error_code=public_error(error))
        raise
    finally:
        if record is not None: finish(client, record, destination)
        else: client.close()
    return {"status": record["status"], "evidence": str(destination), "account_creation": "not-attempted"}


def completion_sources(binding):
    """只承接已实际提交但HTTP观察中断的这次事务，不提供重提入口。"""
    files = {"submit_attempt": "restore-personal-submit.attempt.json", "failed_submit": "restore-personal-submit.result.json",
             "observed_prepare": "restore-personal-observe-recovery.result.json", "host_success": "first-host-approved-existing-root-submit.result.json"}
    values = {key: contract.read(OUTPUT / name) for key, name in files.items()}
    links = {key + "_sha256": contract.digest(OUTPUT / name) for key, name in files.items()}
    failed, intent, prepared = values["failed_submit"], values["submit_attempt"], values["observed_prepare"]
    require(all(value.get("candidate") == binding for value in values.values()) and failed.get("status") == "failed"
            and failed.get("error_code") == "SINGLE_WIZARD_REQUIRED" and failed.get("attempt_sha256") == links["submit_attempt_sha256"]
            and failed.get("states") and failed["states"][-1] == {"status": "running", "code": "RUNTIME_CONFIGURING"}
            and intent.get("action") == "submit" and intent.get("prepared_source") == "observe-recovery"
            and intent.get("host_success_sha256") == links["host_success_sha256"]
            and prepared.get("status") == "observed-prepared"
            and prepared.get("attempt_sha256") == contract.digest(OUTPUT / "restore-personal-observe-recovery.attempt.json")
            and prepared.get("recovery_links") == intent.get("recovery_links") == failed.get("recovery_links")
            and prepared.get("original_personal_data_directory") == intent.get("original_personal_data_directory") == failed.get("original_personal_data_directory")
            and intent.get("original_personal_instance", "missing") is None
            and failed.get("runtime_action_started_at") == intent.get("clock_observation", {}).get("guest_utc"),
            "BOUND_SUBMITTED_OBSERVATION_FAILURE_REQUIRED")
    return values, links


def verify_completion(args):
    first = load(OUTPUT / "first-host-qa.py", "completion_first")
    host = load(OUTPUT / "win10-host-wizard-qa.py", "completion_host")
    transition = load(OUTPUT / "runtime-transition.py", "completion_retention")
    windows = load(ROOT / "qa/vm-lab/scripts/exercise-windows-business.py", "completion_windows")
    matrix, media = windows.load_configuration(); matrix["defaults"].update(primary_root=str(host.LAB_ROOT), fallback_root=str(host.LAB_ROOT))
    lab = windows.QemuLab(matrix, host.LAB_ROOT, media)
    binding = contract.candidate(lab, ROOT, args.package, args.sha256); contract.completed_install(OUTPUT / "overlay-install", binding)
    values, links = completion_sources(binding)
    host_success(values["host_success"], binding, contract.digest(OUTPUT / "first-host-approved-existing-root-submit.attempt.json"), first)
    preserved = transition.preserved_inputs()[0]
    require(preserved == values["submit_attempt"]["preserved_inputs"], "ORIGINAL_BASELINE_CHANGED")
    attempt = OUTPUT / "restore-personal-verify-completion.attempt.json"
    destination = OUTPUT / "restore-personal-observed-completion.json"
    require(not attempt.exists() and not destination.exists(), "COMPLETION_ALREADY_OBSERVED")
    contract.bind_guest(lab, windows.qmp)
    client = windows.windows.connect(lab, matrix["targets"]["win10-x64"]); record = None
    try:
        contract.exclusive(attempt, {"candidate": binding, "time_utc": contract.now(), "operation": "read-only-verify-completion", **links})
        record = {"status": "failed", "candidate": binding, "attempt_sha256": contract.digest(attempt), **links,
                  "completion_kind": "read-only-observed-after-submit", "http_terminal_observed": False,
                  "original_personal_data_directory": first.PERSONAL_DATA, "account_creation": "not-attempted"}
        snapshot = host.transfer(client, windows.windows.execute, first.inspection_script(host, binding, values["observed_prepare"]["since"]),
                                 "host-wizard-" + uuid.uuid4().hex[:12])
        services = json.loads(windows.windows.execute(client, SERVICES)); runtime = contract.probe(client, windows.windows.execute)
        # 不对提权子向导数量做全机门槛；只核实际个人PID的普通桌面UID/会话/监听者。
        require(snapshot.get("username") == "partyopsuser" and snapshot.get("administrator") is False
                and snapshot.get("session_id", 0) > 0, "ORDINARY_USER_SNAPSHOT_REQUIRED")
        runtime_ready(snapshot, services, runtime, binding, values["failed_submit"]["runtime_action_started_at"], None)
        user_root = "C:/Users/partyopsuser/AppData/Local/PartyOps/"
        with client.open_sftp() as fs:
            mode = json.loads(fs.file(user_root + "mode.json").read())
            private_env = fs.file(user_root + "personal.env").read().decode("utf-8-sig")
        config = mode.get("config_path") or user_root + "personal.env"
        rows = [line.split("=", 1)[1].strip().strip('"').strip("'") for line in private_env.splitlines()
                if line.strip().startswith("PARTYOPS_DATA_DIR=")]
        del private_env
        require(PureWindowsPath(config) == PureWindowsPath(user_root + "personal.env") and len(rows) == 1
                and PureWindowsPath(rows[0]) == PureWindowsPath(first.PERSONAL_DATA), "ORIGINAL_PERSONAL_DIRECTORY_REQUIRED")
        del rows, mode
        require(completion_sources(binding)[1] == links and transition.preserved_inputs()[0] == preserved, "COMPLETION_INPUTS_CHANGED")
        contract.bind_guest(lab, windows.qmp)
        record.update(status="restored-ready", runtime=runtime, services=services["services"], user_mode="personal", system_mode="personal",
                      pending_mode_switch=False, user_journal_present=False, original_data_directory_verified=True,
                      guest_clock=json.loads(windows.windows.execute(client, GUEST_CLOCK)))
    except Exception as error:
        if record is not None: record.update(status="failed", error_type=type(error).__name__, error_code=public_error(error))
        raise
    finally:
        if record is not None: finish(client, record, destination)
        else: client.close()
    return {"status": record["status"], "evidence": str(destination), "http_terminal_observed": False, "read_only": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["prepare", "recover-prepare", "observe-recovery", "submit", "verify-completion"])
    parser.add_argument("--use-recovered-prepare", action="store_true", help="submit显式绑定成功recover-prepare及原失败证据")
    parser.add_argument("--use-observed-recovery", action="store_true", help="submit绑定只读observed-prepared回执，不重新启动向导")
    parser.add_argument("--package", type=Path, required=True); parser.add_argument("--sha256", required=True)
    try:
        args = parser.parse_args()
        value = verify_completion(args) if args.action == "verify-completion" else run(args)
        print(json.dumps(value, ensure_ascii=False)); return 0
    except Exception as error:
        print(json.dumps({"status": "failed", "error_type": type(error).__name__, "error_code": public_error(error), "retry": "inspect-only"})); return 2


if __name__ == "__main__": raise SystemExit(main())
