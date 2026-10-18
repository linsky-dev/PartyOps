"""原 Win10 首次主机 QA：prepare/submit 各一次；真实 UAC 与账号创建由操作者完成。"""
from __future__ import annotations

import argparse
import json
import ntpath
import re
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path
from datetime import datetime

import retention_contract as contract

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = Path(__file__).resolve().parent
OLD_PID = 612
OLD_CREATED = "2026-10-17T05:10:53.2190260Z"
PERSONAL_DATA = r"C:\Users\partyopsuser\Documents\PartyOps QA\lifecycle-82dbfdf1894f\中文 空格业务数据"
ROOT_HOST_DATA = r"C:\PartyOps-QA-Host-20261017\主机 数据"


def require(value, code):
    if not value: raise RuntimeError(code)


def load_module(path, name):
    import importlib.util
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def same_wizard(current, expected):
    try:
        first = datetime.fromisoformat(current["created"].replace("Z", "+00:00"))
        second = datetime.fromisoformat(expected["created"].replace("Z", "+00:00"))
        return (first.tzinfo is not None and second.tzinfo is not None and first == second
                and all(current.get(key) == expected.get(key) for key in
                        ("name", "pid", "installed_path_matches", "owner_is_original_user", "session_id")))
    except (KeyError, ValueError, TypeError): return False


def root_recovery_binding(value, binding, attempt_sha):
    require(value.get("candidate") == binding and value.get("attempt_sha256") == attempt_sha
            and value.get("status") == "host-first-run-failed" and value.get("http_status") == 400
            and value.get("bootstrap_admin_page") is False and value.get("public_error_codes") == ["ADMIN_CHILD_FAILED"]
            and value.get("host_status", {}).get("present") is False and value.get("user_mode") == "personal"
            and value.get("system_mode") == "missing", "BOUND_PROFILE_DIRECTORY_FAILURE_REQUIRED")


def approved_existing_binding(attempt, result, rollback, retained, baseline, binding, attempt_sha, preserved):
    require(attempt.get("candidate") == binding and attempt.get("case") == "root-host-directory"
            and attempt.get("qa_host_data_directory") == ROOT_HOST_DATA and attempt.get("preserved_inputs") == preserved
            and result.get("candidate") == binding and result.get("attempt_sha256") == attempt_sha
            and result.get("case") == "root-host-directory" and result.get("qa_host_data_directory") == ROOT_HOST_DATA
            and result.get("status") == "host-first-run-failed" and result.get("http_status") == 500
            and result.get("bootstrap_admin_page") is False
            and result.get("directory_contract") == {"fixed_disk": True, "directory_exists": False,
                "profile_path": False, "reparse_ancestor": False}, "BOUND_NEW_QA_DIRECTORY_ATTEMPT_REQUIRED")
    snapshot = rollback.get("snapshot", {})
    require(rollback.get("candidate") == binding and snapshot.get("pending_mode_switch") is False
            and snapshot.get("user_journal_present") is False and snapshot.get("user_mode") == "personal"
            and snapshot.get("system_mode") == "missing" and "rollback" in snapshot.get("public_outcomes_since_launch", [])
            and rollback.get("host_configuration") == "blocked-awaiting-ca-consent"
            and re.fullmatch(r"[a-f0-9]{64}", str(rollback.get("test_ca_sha256", "")))
            and any(s.get("name") == "PartyOpsHost" and s.get("state") == "Stopped"
                    for s in rollback.get("live", {}).get("services", [])), "COMPLETED_CA_TIMEOUT_ROLLBACK_REQUIRED")
    require(retained.get("status") == "passed" and retained.get("phase") == "after-host-rollback"
            and retained.get("candidate") == binding and retained.get("guest_uuid") == contract.UUID
            and retained.get("disk") == str(contract.DISK.resolve())
            and retained.get("retained") == baseline.get("retained")
            and all(retained.get(key) == baseline.get(key) for key in ("calendar_id", "calendar_sha256")),
            "PASSED_ORIGINAL_DATA_AFTER_ROLLBACK_REQUIRED")
    return {"ca_sha256": rollback["test_ca_sha256"], "created_after": attempt["time_utc"],
            "created_before": result["completed_at"]}


def existing_root_script(provenance):
    require(re.fullmatch(r"[a-f0-9]{64}", str(provenance.get("ca_sha256", ""))), "BOUND_PUBLIC_CA_SHA_REQUIRED")
    # 仅元数据、SQLite固定文件头与公开CA摘要；不读取表、正文、环境或私钥。
    request = {"root": ROOT_HOST_DATA, **provenance}
    return "$owned='" + json.dumps(request, ensure_ascii=False).replace("'", "''") + "'|ConvertFrom-Json\n" + r"""
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($owned.root)
if($root -ne 'C:\PartyOps-QA-Host-20261017\主机 数据' -or -not(Test-Path -LiteralPath $root -PathType Container)) {throw 'OWNED_QA_HOST_DIRECTORY_REQUIRED'}
$disk=Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
if($disk.DriveType -ne 3) {throw 'FIXED_QA_DISK_REQUIRED'}
$ancestor=$root
while($ancestor) {
  if((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {throw 'QA_REPARSE_REJECTED'}
  $ancestor=Split-Path -Path $ancestor -Parent
}
$queue=New-Object 'Collections.Generic.Queue[string]';$queue.Enqueue($root);$entries=0;$databases=@()
while($queue.Count) {
  foreach($entry in @(Get-ChildItem -LiteralPath $queue.Dequeue() -Force)) {
    $entries++;if($entries -gt 10000) {throw 'QA_DIRECTORY_INVENTORY_LIMIT'}
    if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {throw 'QA_TREE_REPARSE_REJECTED'}
    if($entry.PSIsContainer) {$queue.Enqueue($entry.FullName)}
    elseif($entry.Extension -eq '.db') {$databases+= $entry}
  }
}
$db=Join-Path $root 'partyops.db'
if($databases.Count -ne 1 -or $databases[0].FullName -ne $db) {throw 'SINGLE_OWNED_QA_DATABASE_REQUIRED'}
$created=$databases[0].CreationTimeUtc
if($created -lt [DateTimeOffset]::Parse($owned.created_after).UtcDateTime -or $created -gt [DateTimeOffset]::Parse($owned.created_before).UtcDateTime) {throw 'QA_DATABASE_CREATION_NOT_BOUND_TO_PRIOR_ATTEMPT'}
$links=@(& fsutil.exe hardlink list $db 2>$null|Where-Object {$_.Trim()})
if($LASTEXITCODE -ne 0 -or $links.Count -ne 1 -or $links[0].Trim() -ne $db.Substring(2)) {throw 'QA_DATABASE_HARDLINK_OWNERSHIP_UNVERIFIED'}
$stream=[IO.File]::OpenRead($db)
try {$header=New-Object byte[] 16;if($stream.Read($header,0,16) -ne 16 -or [Text.Encoding]::ASCII.GetString($header) -cne "SQLite format 3`0") {throw 'QA_DATABASE_HEADER_INVALID'}}finally {$stream.Dispose()}
$ca=Join-Path $root 'secrets\pki\ca.pem';$public='C:\ProgramData\PartyOps\public-host-ca.pem'
foreach($path in @($ca,$public)) {
  if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {throw 'QA_PUBLIC_CA_PROVENANCE_MISMATCH'}
  # 既有回执保存的是证书DER指纹，不是PEM文本文件摘要；两份公开证书均须匹配。
  $pem=[IO.File]::ReadAllText($path)
  $der=[Convert]::FromBase64String(($pem -replace '-----BEGIN CERTIFICATE-----|-----END CERTIFICATE-----|\s',''))
  $cert=New-Object Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList (,$der)
  $hasher=[Security.Cryptography.SHA256]::Create()
  try {$fingerprint=([BitConverter]::ToString($hasher.ComputeHash($cert.RawData))).Replace('-','').ToLowerInvariant()}finally {$hasher.Dispose();$cert.Dispose()}
  if($fingerprint -ne $owned.ca_sha256) {throw 'QA_PUBLIC_CA_PROVENANCE_MISMATCH'}
}
$services=@(Get-CimInstance Win32_Service|Where-Object {$_.Name -in @('PartyOpsHost','PartyOpsUpdateService')})
if(@($services|Where-Object {$_.State -ne 'Stopped'}).Count) {throw 'HOST_SERVICES_NOT_ROLLED_BACK'}
@{fixed_disk=$true;directory_exists=$true;profile_path=$false;reparse_ancestor=$false;tree_reparse=$false;single_qa_database=$true;database_hardlinks=1;database_header_valid=$true;database_creation_bound=$true;ca_sha256=$owned.ca_sha256;database_sha256=(Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash.ToLowerInvariant();host_services_stopped=$true}|ConvertTo-Json -Compress
"""


def validate_existing_root(value, provenance):
    require(value.get("ca_sha256") == provenance["ca_sha256"] and re.fullmatch(r"[a-f0-9]{64}", str(value.get("database_sha256", "")))
            and value.get("directory_exists") is True and value.get("database_hardlinks") == 1
            and all(value.get(key) is True for key in ("fixed_disk", "single_qa_database", "database_header_valid",
                "database_creation_bound", "host_services_stopped"))
            and all(value.get(key) is False for key in ("profile_path", "reparse_ancestor", "tree_reparse")),
            "EXISTING_QA_DIRECTORY_OWNERSHIP_UNVERIFIED_READ_ONLY_CHECK_REQUIRED")


def root_path_script(path):
    require(ntpath.normcase(ntpath.normpath(path)) == ntpath.normcase(ROOT_HOST_DATA)
            and not ntpath.normcase(path).startswith("c:\\users\\"), "FIXED_NON_PROFILE_HOST_QA_DIRECTORY_REQUIRED")
    return "$path='" + path.replace("'", "''") + "'\n" + r"""
$ErrorActionPreference='Stop'
$full=[IO.Path]::GetFullPath($path)
if($full -ne 'C:\PartyOps-QA-Host-20261017\主机 数据' -or $full.StartsWith('C:\Users\',[StringComparison]::OrdinalIgnoreCase)) {throw 'QA_HOST_PROFILE_PATH_REJECTED'}
$disk=Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
if($disk.DriveType -ne 3 -or (Test-Path -LiteralPath $full)) {throw 'NEW_FIXED_DISK_HOST_DIRECTORY_REQUIRED'}
$ancestor=Split-Path -Path $full -Parent
while($ancestor) {
  if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {throw 'QA_HOST_REPARSE_REJECTED'}
  $ancestor=Split-Path -Path $ancestor -Parent
}
@{fixed_disk=$true;directory_exists=$false;profile_path=$false;reparse_ancestor=$false}|ConvertTo-Json -Compress
"""


def inspection_script(host_qa, binding, since):
    # 只替换该 QA 探针的固定 host_data 参数，不修改产品或旧回执。
    script = host_qa.inspect_script(binding, since)
    old = '"host_data": ' + json.dumps(host_qa.HOST_DATA, ensure_ascii=False)
    require(old in script, "INSPECT_HOST_DATA_PARAMETER_REQUIRED")
    return script.replace(old, '"host_data": ' + json.dumps(ROOT_HOST_DATA, ensure_ascii=False), 1)


def negative_binding(attempt, result, binding, attempt_sha):
    require(attempt.get("candidate") == binding and attempt.get("wizard_pid") == OLD_PID
            and attempt.get("wizard_created") == OLD_CREATED and result.get("attempt_sha256") == attempt_sha
            and result.get("status") == "recorded" and result.get("states", [])[-1:] ==
            [{"status": "failed", "code": "RUNTIME_CONFIGURATION_INVALID"}]
            and result.get("unexpected_ready") is False, "BOUND_TERMINAL_NEGATIVE_CASE_REQUIRED")


def prepare_script(host_qa, transition, binding, since, personal):
    request = json.dumps({"pid": OLD_PID, "created": OLD_CREATED, "personal": personal}, ensure_ascii=False).replace("'", "''")
    first_launch = host_qa.launch_script(binding, since).split("$scheduler=", 1)[1]
    first_launch = "$scheduler=" + first_launch
    first_launch = first_launch.replace("--reconfigure --initial-role host", "--initial-role host")
    first_launch = first_launch.replace("$request.task", "'PartyOps-QA-First-Host-20261017'")
    # 与原任务一样使用普通交互令牌；仅前置增加原 QA 向导精确关闭，不触碰个人进程。
    return host_qa.prefix(binding, since) + transition.START_TIME_MATCH + "$target='" + request + "'|ConvertFrom-Json\n" + r"""
if($pending -or $journal) {throw 'PENDING_MODE_SWITCH_REQUIRES_RECOVERY'}
$rows=@(Get-CimInstance Win32_Process -Filter "Name='PartyOpsWizard.exe'")
if($rows.Count -ne 1) {throw 'SINGLE_ORIGINAL_QA_WIZARD_REQUIRED'}
$p=$rows[0];$owner=Invoke-CimMethod -InputObject $p -MethodName GetOwner
if($p.ProcessId -ne $target.pid -or $p.CreationDate.ToUniversalTime().ToString('o') -cne $target.created -or $p.ExecutablePath -ne $wizard -or $p.SessionId -ne $desktop[0].session_id -or $owner.ReturnValue -ne 0 -or $owner.User -ne 'partyopsuser' -or $owner.Domain -ne $env:COMPUTERNAME) {throw 'ORIGINAL_QA_WIZARD_CHANGED'}
$before=@(Get-CimInstance Win32_Process -Filter "Name='PartyOps.exe'"|ForEach-Object {@{pid=[int]$_.ProcessId;created=$_.CreationDate.ToUniversalTime().ToString('o')}})
if($before.Count -ne 1 -or $before[0].pid -ne $target.personal.pid -or $before[0].created -cne $target.personal.created) {throw 'ORIGINAL_PERSONAL_PROCESS_CHANGED'}
$handle=Get-Process -Id $target.pid -ErrorAction Stop
if(-not (Test-CimCreationInstant $handle.StartTime $target.created)) {throw 'QA_WIZARD_HANDLE_CHANGED'}
$closeAttempted=$false;$terminated=$false
if($handle.MainWindowHandle -ne 0) {$closeAttempted=$handle.CloseMainWindow();if($closeAttempted){$null=$handle.WaitForExit(10000)}}
if(-not $handle.HasExited) {$handle.Kill();$terminated=$true;if(-not $handle.WaitForExit(10000)){throw 'QA_WIZARD_DID_NOT_EXIT'}}
$after=@(Get-CimInstance Win32_Process -Filter "Name='PartyOps.exe'"|ForEach-Object {@{pid=[int]$_.ProcessId;created=$_.CreationDate.ToUniversalTime().ToString('o')}})
if($after.Count -ne 1 -or $after[0].pid -ne $target.personal.pid -or $after[0].created -cne $target.personal.created) {throw 'PERSONAL_PROCESS_CHANGED_DURING_PREPARE'}
""" + first_launch.replace("@{launch_requested=$true;", "@{old_qa_wizard_controlled_termination=$terminated;old_qa_wizard_close_attempted=$closeAttempted;launch_requested=$true;")


def first_form_parser(base_parser):
    class FirstForm(base_parser):
        def __init__(self):
            super().__init__(); self.host_select = False; self.addresses = []; self.selected = None
            self.bootstrap_admin = False; self.diagnostic = False; self.codes = []

        def handle_starttag(self, tag, attrs):
            values = dict(attrs)
            super().handle_starttag(tag, attrs)
            if tag == "select" and self.host_form and values.get("name") == "host": self.host_select = True
            if tag == "option" and self.host_select and "disabled" not in values and values.get("value"):
                self.addresses.append(values["value"])
                if "selected" in values: self.selected = values["value"]
            if tag == "form" and values.get("id") == "admin-form": self.bootstrap_admin = True
            if tag == "span" and values.get("id") == "diagnostic-text": self.diagnostic = True

        def handle_endtag(self, tag):
            super().handle_endtag(tag)
            if tag == "select": self.host_select = False
            if tag == "span": self.diagnostic = False

        def handle_data(self, data):
            super().handle_data(data)
            if self.diagnostic: self.codes.extend(re.findall(r"\[([A-Z][A-Z0-9_]{0,63})\]", data))
    return FirstForm


def choose_first_form(page, requested, parser):
    form = parser(); form.feed(page)
    require(form.title.strip() == "党建智办 · 首次配置" and form.mode == "host" and not form.bootstrap_admin,
            "FIRST_CONFIGURATION_HOST_FORM_REQUIRED")
    require(isinstance(form.csrf, str) and re.fullmatch(r"[A-Za-z0-9_-]{16,128}", form.csrf), "FIRST_FORM_CSRF_INVALID")
    selected = (form.selected or (form.addresses[0] if form.addresses else None)) if requested == "default" else requested
    require(selected in form.addresses, "ADDRESS_MUST_BE_IN_ACTUAL_FORM")
    require(selected == "10.0.2.15" or requested == "default", "ONLY_OBSERVED_ADDRESS_OR_FORM_DEFAULT_ALLOWED")
    return form.csrf, selected


def main_run(args):
    host_qa = load_module(OUTPUT / "win10-host-wizard-qa.py", "first_host_identity")
    form_qa = load_module(OUTPUT / "win10-host-form-qa.py", "first_host_form_helpers")
    transition = load_module(OUTPUT / "runtime-transition.py", "first_host_retention")
    windows = load_module(ROOT / "qa/vm-lab/scripts/exercise-windows-business.py", "first_host_windows")
    matrix, media = windows.load_configuration(); matrix["defaults"].update(primary_root=str(host_qa.LAB_ROOT), fallback_root=str(host_qa.LAB_ROOT))
    lab = windows.QemuLab(matrix, host_qa.LAB_ROOT, media)
    binding = contract.candidate(lab, ROOT, args.package, args.sha256); contract.completed_install(OUTPUT / "overlay-install", binding)
    preserved = transition.preserved_inputs()[0]
    negative_attempt = OUTPUT / "host-form-invalid-address.attempt.json"
    negative_binding(contract.read(negative_attempt), contract.read(OUTPUT / "host-form-invalid-address.result.json"), binding, contract.digest(negative_attempt))
    case = getattr(args, "case", "original")
    require(case == "original" or args.action == "submit", "ROOT_DIRECTORY_RECOVERY_REUSES_EXISTING_WIZARD_ONLY")
    prefix = {"original": "", "root-host-directory": "root-directory-", "approved-existing-root": "approved-existing-root-"}[case]
    operation = "first-host-" + prefix + args.action
    attempt_file = OUTPUT / (operation + ".attempt.json")
    require(not attempt_file.exists(), "FIRST_HOST_ACTION_ALREADY_ATTEMPTED_NO_AUTOMATIC_REPEAT")
    failed_result_sha = None
    existing_provenance = None
    source_evidence = None
    if case == "root-host-directory":
        failed_file = OUTPUT / "first-host-submit.result.json"
        root_recovery_binding(contract.read(failed_file), binding, contract.digest(OUTPUT / "first-host-submit.attempt.json"))
        failed_result_sha = contract.digest(failed_file)
    if case == "approved-existing-root":
        sources = {key: OUTPUT / filename for key, filename in {
            "root_attempt": "first-host-root-directory-submit.attempt.json", "root_result": "first-host-root-directory-submit.result.json",
            "rollback": "host-rollback-final-state.json", "retention": "retention-after-host-rollback.json"}.items()}
        existing_provenance = approved_existing_binding(*(contract.read(sources[key]) for key in
            ("root_attempt", "root_result", "rollback", "retention")), contract.read(OUTPUT / "retention-baseline.json"),
            binding, contract.digest(sources["root_attempt"]), preserved)
        source_evidence = {key + "_sha256": contract.digest(path) for key, path in sources.items()}
    prepared = None
    if args.action == "submit":
        prepared = contract.read(OUTPUT / "first-host-prepare.result.json")
        require(prepared.get("status") == "prepared" and prepared.get("candidate") == binding
                and prepared.get("attempt_sha256") == contract.digest(OUTPUT / "first-host-prepare.attempt.json"), "BOUND_FIRST_HOST_PREPARE_REQUIRED")
    contract.bind_guest(lab, windows.qmp)
    client = windows.windows.connect(lab, matrix["targets"]["win10-x64"])
    record = None
    try:
        with client.open_sftp() as fs, fs.file("C:/ProgramData/PartyOps-VM-Lab/private/lifecycle-82dbfdf1894f.json") as stream: state = json.load(stream)
        require(state.get("business_verified") is True and ntpath.normcase(state.get("data_dir", "")) == ntpath.normcase(PERSONAL_DATA), "ORIGINAL_PERSONAL_RESTORE_DIRECTORY_REQUIRED")
        del state
        since = contract.now() if args.action == "prepare" else prepared["since"]
        probe_name = "host-wizard-" + uuid.uuid4().hex[:12]
        snapshot = host_qa.transfer(client, windows.windows.execute, inspection_script(host_qa, binding, since), probe_name)
        probe_command = "& ([scriptblock]::Create([IO.File]::ReadAllText('C:/ProgramData/PartyOps-VM-Lab/" + probe_name + "/operation.ps1',[Text.Encoding]::UTF8)))"
        def inspect():
            contract.bind_guest(lab, windows.qmp)
            return json.loads(windows.windows.execute(client, probe_command, timeout=120))
        require(not snapshot["pending_mode_switch"] and not snapshot["user_journal_present"], "PENDING_TRANSACTION_REQUIRES_RECOVERY")
        bound_personal = None
        if case in {"root-host-directory", "approved-existing-root"}:
            require(snapshot["user_mode"] == "personal" and snapshot["system_mode"] == "missing", "FAILED_CASE_MUST_HAVE_ROLLED_BACK")
            actual = contract.runtime_matches(contract.probe(client, windows.windows.execute), binding,
                contract.read(OUTPUT / "overlay-install/attempt.json")["time_utc"])
            bound_personal = actual["processes"][0]
            require(any(p["name"] == "PartyOps.exe" and p["pid"] == bound_personal["pid"] and p["owner_is_original_user"]
                        and datetime.fromisoformat(p["created"].replace("Z", "+00:00")) == datetime.fromisoformat(bound_personal["created"].replace("Z", "+00:00"))
                        and p["session_id"] == snapshot["session_id"] for p in snapshot["processes"]), "ORIGINAL_PERSONAL_OWNER_CHANGED_BEFORE_RECOVERY")
        if args.action == "prepare":
            old = [p for p in snapshot["processes"] if p["name"] == "PartyOpsWizard.exe"]
            require(len(old) == 1 and old[0]["pid"] == OLD_PID and old[0]["created"] == OLD_CREATED
                    and old[0]["installed_path_matches"] and old[0]["owner_is_original_user"]
                    and old[0]["session_id"] == snapshot["session_id"], "EXACT_IDLE_RECONFIGURATION_WIZARD_REQUIRED")
            actual = contract.runtime_matches(contract.probe(client, windows.windows.execute), binding,
                contract.read(OUTPUT / "overlay-install/attempt.json")["time_utc"])
            personal = actual["processes"][0]
            require(any(p["name"] == "PartyOps.exe" and p["pid"] == personal["pid"] and p["owner_is_original_user"]
                        and p["session_id"] == snapshot["session_id"] for p in snapshot["processes"]), "ORIGINAL_PERSONAL_OWNER_REQUIRED")
            body_script = prepare_script(host_qa, transition, binding, since, personal)
        else:
            host_qa.validate_wizard(snapshot, since)
            current = [p for p in snapshot["processes"] if p["name"] == "PartyOpsWizard.exe"][0]
            require(same_wizard(current, prepared["wizard"]), "PREPARED_WIZARD_CHANGED")
            ports = {row["port"] for row in snapshot["listeners"] if row["owned_by_wizard"] and row["loopback"]}
            require(len(ports) == 1, "SINGLE_FIRST_WIZARD_PORT_REQUIRED")
            wizard_port = ports.pop()
            origin = "http://127.0.0.1:" + str(wizard_port)
            class RejectRedirect(urllib.request.HTTPRedirectHandler):
                def redirect_request(self, *_args, **_kwargs): raise RuntimeError("FIRST_HOST_REDIRECT_REJECTED")
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), windows.GuestHTTPHandler(client.get_transport()), RejectRedirect())
            def request(data=None):
                current_snapshot = inspect(); host_qa.validate_wizard(current_snapshot, since)
                wizard_rows = [p for p in current_snapshot["processes"] if p["name"] == "PartyOpsWizard.exe"]
                require(len(wizard_rows) == 1 and same_wizard(wizard_rows[0], prepared["wizard"]), "FIRST_WIZARD_CHANGED_BEFORE_HTTP")
                require(any(row["port"] == wizard_port and row["pid"] == prepared["wizard"]["pid"]
                            and row["loopback"] and row["owned_by_wizard"] for row in current_snapshot["listeners"]), "FIRST_WIZARD_HTTP_LISTENER_CHANGED")
                headers = {"Origin": origin}
                if data is not None: headers["Content-Type"] = "application/x-www-form-urlencoded"
                try:
                    with opener.open(urllib.request.Request(origin + "/", data=data, headers=headers), timeout=600) as response:
                        return response.status, response.read(2 * 1024**2).decode("utf-8")
                except urllib.error.HTTPError as error:
                    status = error.code; content = error.read(2 * 1024**2).decode("utf-8", errors="replace"); error.close()
                    return status, content
                except Exception: raise RuntimeError("FIRST_HOST_HTTP_FAILED_NO_RESUBMIT") from None
            status, page = request(); require(status == 200, "FIRST_HOST_PAGE_GET_FAILED")
            parser = first_form_parser(form_qa.HostForm)
            csrf, address = choose_first_form(page, args.address, parser); del page
            body = urllib.parse.urlencode({"csrf": csrf, "mode": "host", "host": address,
                "port": "18765", "data_dir": ROOT_HOST_DATA}).encode("utf-8")
            del csrf
            directory_proof = host_qa.transfer(client, windows.windows.execute,
                existing_root_script(existing_provenance) if existing_provenance else root_path_script(ROOT_HOST_DATA),
                                               "host-wizard-" + uuid.uuid4().hex[:12])
            if existing_provenance: validate_existing_root(directory_proof, existing_provenance)
            else:
                require(directory_proof == {"fixed_disk": True, "directory_exists": False,
                        "profile_path": False, "reparse_ancestor": False}, "ROOT_HOST_DIRECTORY_CONTRACT_FAILED")
        require(transition.preserved_inputs()[0] == preserved, "PRESERVED_BASELINE_CHANGED")
        contract.exclusive(attempt_file, {"time_utc": contract.now(), "since": since, "candidate": binding,
            "action": args.action, "case": case, "qa_host_data_directory": ROOT_HOST_DATA, "original_personal_restore_directory": PERSONAL_DATA,
            "preserved_inputs": preserved, "prior_profile_directory_failure_sha256": failed_result_sha,
            "existing_root_provenance": existing_provenance, "source_evidence": source_evidence,
            "bound_personal_runtime": bound_personal, "automatic_uac_acceptance": False})
        record = {"status": "failed", "candidate": binding, "since": since, "attempt_sha256": contract.digest(attempt_file),
                  "case": case, "qa_host_data_directory": ROOT_HOST_DATA,
                  "uac_acceptance": "not-verified", "account_creation": "not-attempted", "original_personal_restore_directory": PERSONAL_DATA}
        if args.action == "submit": record["directory_contract"] = directory_proof
        if args.action == "prepare":
            record["operation"] = host_qa.transfer(client, windows.windows.execute, body_script, "host-wizard-" + uuid.uuid4().hex[:12])
            deadline = time.monotonic() + 45
            while True:
                snapshot = inspect()
                try: host_qa.validate_wizard(snapshot, since); break
                except RuntimeError:
                    require(time.monotonic() < deadline, "FIRST_WIZARD_START_TIMEOUT_NO_RELAUNCH"); time.sleep(2)
            record.update(status="prepared", wizard=[p for p in snapshot["processes"] if p["name"] == "PartyOpsWizard.exe"][0])
        else:
            status, page = request(body); del body
            parsed = parser(); parsed.feed(page); del page
            snapshot = inspect()
            record.update(http_status=status, bootstrap_admin_page=parsed.bootstrap_admin,
                public_error_codes=list(dict.fromkeys(parsed.codes)), selected_address=address,
                host_status=snapshot["fixed_qa_host_status"], user_mode=snapshot["user_mode"], system_mode=snapshot["system_mode"])
            record["status"] = "bootstrap-page-observed" if status == 200 and parsed.bootstrap_admin and not parsed.codes else "host-first-run-failed"
        require(transition.preserved_inputs()[0] == preserved, "PRESERVED_BASELINE_CHANGED_AFTER_ACTION")
    except Exception as error:
        if record is not None: record.update(status="failed", error_type=type(error).__name__)
        raise
    finally:
        close_failed = False
        try: client.close()
        except Exception:
            close_failed = True
            if record is not None: record.update(status="failed", cleanup_error="SSH_CLOSE_FAILED")
        if record is not None:
            record["completed_at"] = contract.now()
            contract.exclusive(OUTPUT / (operation + ".result.json"), record)
        if close_failed: raise RuntimeError("SSH_CLOSE_FAILED")
    # 到这里表示最终保留校验、连接关闭及结果写入均已完成，才能构造成功返回。
    return {"status": record["status"], "evidence": str(OUTPUT / (operation + ".result.json")),
            "uac_acceptance": "not-verified", "account_creation": "not-attempted"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["prepare", "submit"])
    parser.add_argument("--package", type=Path, required=True); parser.add_argument("--sha256", required=True)
    parser.add_argument("--address", choices=["10.0.2.15", "default"], default="10.0.2.15")
    parser.add_argument("--case", choices=["original", "root-host-directory", "approved-existing-root"], default="original",
                        help="root-host-directory 使用新根目录；approved-existing-root 在已绑定证书超时回滚后复用原QA目录；各独立一次")
    try:
        result = main_run(parser.parse_args()); print(json.dumps(result, ensure_ascii=False))
        return 0 if result["status"] in {"prepared", "bootstrap-page-observed"} else 2
    except Exception as error:
        print(json.dumps({"status": "failed", "error_type": type(error).__name__, "retry": "inspect-only"})); return 2


if __name__ == "__main__": raise SystemExit(main())
