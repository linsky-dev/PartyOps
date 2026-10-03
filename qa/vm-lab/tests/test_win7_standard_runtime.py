"""Win7 普通用户入口的身份、载荷与恢复反例；不触达 Guest、账户或真实应用。"""
import copy
import json
import os
import re
import shutil
import subprocess
from types import SimpleNamespace

import pytest
import windows_win7_standard as standard
from evidence import sha256, write_json


@pytest.fixture
def context():
    return {"run_id": "win7-standard-123456789abc", "uuid": "406ef8ee-e342-405f-933f-7ddce0a55f1b",
            "sid": "S-1-5-21-1-2-3-1003", "session_id": 1, "boot_id": "original-boot",
            "version": "1.4.5-rc.6", "app_path": standard.INSTALL_DIR + r"\PartyOps.exe", "app_sha256": "a" * 64}


@pytest.fixture
def successful(context):
    process = {"pid": 100, "parent_pid": 30, "sid": context["sid"], "session_id": 1,
               "path": context["app_path"], "created": "20260908080000.000000+480"}
    return {"schema_version": 1, "run_id": context["run_id"], "context_sha256": "b" * 64,
            "runtime_environment_passed": False, "exit_code": 0,
            "token": {"sid": context["sid"], "administrator": False, "session_id": 1},
            "permission_process": {**process, "pid": 90}, "server_process": process,
            "server_process_after_health": dict(process), "listener_pid": 100,
            "permission_exit_code": 0, "permission": {"passed": True, "runtime_readable": True, "user_temp_writable": True},
            "health": {"status": "ok", "app_version": context["version"], "mode": "personal", "sqlite": {"safe_version": True, "fts5": True}},
            "frontend_ready": True, "app_sha256_after": context["app_sha256"], "server_exit_code": -1,
            "diagnostic_stop": "owned-handle-force-stop"}


def test_partial_diagnostic_success_keeps_environment_false(context, successful):
    standard.validate_result(successful, context, "b" * 64, valid_task(context))
    assert successful["runtime_environment_passed"] is False


@pytest.mark.parametrize("path,value,error", [
    ("run_id", "old-run", "BINDING"), ("context_sha256", "old", "BINDING"),
    ("schema_version", True, "BINDING"), ("runtime_environment_passed", True, "BINDING"),
    ("exit_code", True, "PROBE_FAILED"), ("exit_code", 4, "PROBE_FAILED"),
    ("token.sid", "S-1-5-21-1-2-3-500", "TOKEN"), ("token.administrator", True, "TOKEN"),
    ("token.session_id", 0, "TOKEN"), ("token.session_id", True, "TOKEN"),
    ("server_process.sid", "foreign", "PROCESS"), ("server_process.path", r"C:\foreign\PartyOps.exe", "PROCESS"),
    ("server_process.created", "", "PROCESS"), ("server_process.pid", True, "PROCESS"),
    ("server_process_after_health.created", "reused-pid", "LISTENER"), ("listener_pid", 999, "LISTENER"),
    ("permission_exit_code", None, "PERMISSION"), ("permission_exit_code", 3, "PERMISSION"),
    ("permission.user_temp_writable", False, "PERMISSION"),
    ("health.app_version", "1.4.4", "HEALTH"), ("health.mode", "collaboration", "HEALTH"),
    ("health.sqlite.fts5", False, "HEALTH"), ("frontend_ready", False, "HEALTH"),
    ("app_sha256_after", "changed", "HEALTH"), ("server_exit_code", None, "HEALTH"),
    ("diagnostic_stop", "already-exited", "HEALTH"),
])
def test_rejects_forged_or_partial_runtime_evidence(context, successful, path, value, error):
    row = successful
    keys = path.split(".")
    for key in keys[:-1]:
        row = row[key]
    row[keys[-1]] = value
    with pytest.raises(RuntimeError, match=error):
        standard.validate_result(successful, context, "b" * 64, valid_task(context))


@pytest.mark.parametrize("task", [{"exit_code": True, "running": False}, {"exit_code": 1, "running": False}, {"exit_code": 0, "running": True}])
def test_rejects_missing_or_failed_scheduler_exit(context, successful, task):
    with pytest.raises(RuntimeError, match="TASK_FAILED"):
        standard.validate_result(successful, context, "b" * 64, {**valid_task(context), **task})


def valid_task(context):
    return {"exit_code": 0, "running": False, "principal_sid": context["sid"], "logon_type": 3, "run_level": 0, "action_matches": True}


@pytest.mark.parametrize("field,value", [("principal_sid", "S-1-5-18"), ("logon_type", 1), ("run_level", 1), ("action_matches", False)])
def test_changed_scheduler_principal_or_action_cannot_import_result(context, successful, field, value):
    with pytest.raises(RuntimeError, match="TASK_FAILED"):
        standard.validate_result(successful, context, "b" * 64, {**valid_task(context), field: value})


@pytest.fixture
def installation(tmp_path, monkeypatch):
    target = "win7-x64"
    directory = tmp_path / "reports" / target / "install-new"
    directory.mkdir(parents=True)
    (directory / "install.log").write_text("synthetic installer log", encoding="utf-8")
    state = {"uuid": "original-uuid", "restore_generation": "restore-one"}
    environment = {"vm_uuid": state["uuid"], "baseline_id": "original-cold", "media_sha256": "1" * 64}
    package = {"id": "windows7_amd64", "version": "1.4.5-rc.6", "sha256": "2" * 64,
               "path": "D:/lab/packages/PartyOps_1.4.5-rc.6_windows7_amd64.exe"}
    receipt = {"target": target, "guest_uuid": state["uuid"], "environment": environment, "restore_generation": "restore-one",
               "package": package, "install": {"exit_code": 0, "installer_sha256": package["sha256"]},
               "evidence": {"install_log": {"path": "install.log", "sha256": sha256(directory / "install.log")}}}
    write_json(directory / "installed-probe.json", receipt)
    lab = SimpleNamespace(root=tmp_path, matrix={"targets": {target: {"backend": "qemu", "os": "windows", "arch": "x86_64", "os_release": "7 SP1", "winrm_port": 23171}}}, state=lambda _target: state)
    monkeypatch.setattr(standard, "runtime_binding", lambda *_: environment)
    payload = {"manifest": {"sha256": "3" * 64}, "executables": {"PartyOps.exe": {"sha256": "4" * 64}, "PartyOpsWizard.exe": {"sha256": "5" * 64}}}
    monkeypatch.setattr(standard, "expected_payload", lambda _lab, candidate, source: (candidate, payload, {}))
    return lab, directory, receipt


@pytest.mark.parametrize("field,value", [("target", "win11-x64-native"), ("guest_uuid", "old-vm"),
                                          ("restore_generation", "old-restore"), ("environment", {"baseline_id": "deepin"}),
                                          ("install", {"exit_code": 1}), ("package", {"id": "windows7_x86"})])
def test_installation_binding_rejects_imported_or_failed_receipt(installation, field, value):
    lab, directory, receipt = installation
    receipt[field] = value
    write_json(directory / "installed-probe.json", receipt)
    with pytest.raises(RuntimeError, match="RECEIPT_BINDING"):
        standard.installation_binding(lab, "win7-x64", directory, "current-source")


def test_guest_hash_expectations_come_from_independent_build_receipt(installation):
    lab, directory, _ = installation
    binding = standard.installation_binding(lab, "win7-x64", directory, "current-source")
    script = standard.installed_script(binding)
    assert "4" * 64 in script and "3" * 64 in script and "2" * 64 in script
    assert "windows7_amd64.exe" in script and "WIN7_INSTALLED_REGISTRATION_MISMATCH" in script
    assert "WIN7_INSTALLED_PE_ARCH_MISMATCH" in script and "34404" in script
    (directory / "install.log").write_text("changed", encoding="utf-8")
    with pytest.raises(RuntimeError, match="EVIDENCE_CHANGED"):
        standard.installation_binding(lab, "win7-x64", directory, "current-source")


def test_missing_independent_payload_blocks_before_guest_mutation(installation, monkeypatch):
    lab, directory, _ = installation
    def missing(*_):
        raise RuntimeError("WINDOWS_BUILD_PAYLOAD_RECEIPT_REQUIRED")
    monkeypatch.setattr(standard, "expected_payload", missing)
    with pytest.raises(RuntimeError, match="PAYLOAD_RECEIPT_REQUIRED"):
        standard.installation_binding(lab, "win7-x64", directory, "current-source")


def test_x86_requires_original_x86_os_and_x86_installed_pe(installation):
    lab, old_directory, receipt = installation
    spec = lab.matrix["targets"].pop("win7-x64")
    spec["arch"] = "i686"
    lab.matrix["targets"]["win7-x86"] = spec
    directory = lab.root / "reports/win7-x86/install-new"
    directory.mkdir(parents=True)
    (directory / "install.log").write_bytes((old_directory / "install.log").read_bytes())
    receipt["target"] = "win7-x86"
    receipt["package"]["id"] = "windows7_x86"
    receipt["package"]["path"] = receipt["package"]["path"].replace("amd64", "x86")
    write_json(directory / "installed-probe.json", receipt)
    binding = standard.installation_binding(lab, "win7-x86", directory, "current-source")
    assert binding["pe_machine"] == 0x14C and "$machine -ne 332" in standard.installed_script(binding)
    spec["arch"] = "x86_64"
    with pytest.raises(RuntimeError, match="ORIGINAL_WIN7"):
        standard.installation_binding(lab, "win7-x86", directory, "current-source")


def test_resume_only_collects_original_task_without_relaunch(tmp_path, monkeypatch, context, successful):
    import lab as lab_module
    binding = {"source_fingerprint": "source", "uuid": context["uuid"], "scripts": {"windows-win7-probe.ps1": "c" * 64}}
    directory = tmp_path / "reports/win7-x64" / context["run_id"]
    directory.mkdir(parents=True)
    write_json(directory / "binding.json", binding)
    write_json(directory / "context.json", context)
    digest = sha256(directory / "context.json")
    write_json(directory / "launch-request.json", {"context_sha256": digest})
    successful["context_sha256"] = digest
    calls = []
    class Files:
        def __init__(self, *_):
            pass
        def powershell(self, script, **_):
            calls.append(script)
            if "GetTask($name)" in script:
                return json.dumps({**valid_task(context), "result_exists": True})
            if "File]::Exists" in script:
                return "False"
            if "WIN7_EXPECTED_FILE_HASH_MISMATCH" in script:
                return ""
            assert script == "READ-INSTALLED"
            return '{"verified":true}'
        def put(self, *_):
            pytest.fail("续跑不得覆盖 Guest 脚本或凭据")
        def get(self, remote, local):
            assert remote.endswith(r"output\result.json")
            write_json(local, successful)
    monkeypatch.setattr(lab_module, "fingerprint", lambda: "source")
    monkeypatch.setattr(standard, "installation_binding", lambda *_: binding)
    monkeypatch.setattr(standard, "probe", lambda *_: {"installed_package": True, "boot_id": context["boot_id"]})
    monkeypatch.setattr(standard, "installed_script", lambda _: "READ-INSTALLED")
    monkeypatch.setattr(standard, "WinRMFiles", Files)
    result = standard.exercise(SimpleNamespace(root=tmp_path), "win7-x64", "probe", tmp_path, resume=directory)
    assert result["exit_code"] == 0 and result["runtime_environment_passed"] is False
    assert not any("RegisterTask" in call or ".Run(" in call for call in calls)
    original = copy.deepcopy(context)
    context["boot_id"] = "other-boot"
    write_json(directory / "context.json", context)
    monkeypatch.setattr(standard, "probe", lambda *_: {"installed_package": True, "boot_id": original["boot_id"]})
    with pytest.raises(RuntimeError, match="RESUME_BOOT_CHANGED"):
        standard.exercise(SimpleNamespace(root=tmp_path), "win7-x64", "probe", tmp_path, resume=directory)


def test_ps2_task_uses_original_interactive_limited_token_and_utf8(context):
    script = standard.start_task_script(r"C:\PartyOps-QA\win7-standard-123456789abc", context)
    assert "Principal.LogonType=3" in script and "Principal.RunLevel=0" in script
    assert "RegisterTaskDefinition($name,$task,2,$sid,$null,3,$null)" in script
    import base64
    encoded = re.search(r"\$encoded='([^']+)'", script)[1]
    decoded = base64.b64decode(encoded).decode("utf-16le")
    assert "[Text.Encoding]::UTF8" in decoded and "[scriptblock]::Create" in decoded
    source = (standard.HERE / "guest/windows-win7-probe.ps1").read_text(encoding="utf-8")
    for newer in ("ConvertFrom-Json", "ConvertTo-Json", "Get-CimInstance", "Get-FileHash", "Get-LocalUser", "Get-NetTCPConnection", "::new(", "[ordered]"):
        assert newer not in source


POWERSHELL = shutil.which("powershell.exe")


@pytest.mark.skipif(not POWERSHELL, reason="需要Windows PowerShell执行原生进程日志检查")
def test_hidden_capture_drains_both_pipes_and_keeps_real_exit_code(tmp_path):
    harness = tmp_path / 'capture.ps1'
    harness.write_text(r'''
$ErrorActionPreference='Stop'
$tokens=$null;$failures=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($env:UNIT_SCRIPT,[ref]$tokens,[ref]$failures)
$node=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Start-ProbeCapturedProcess'},$false)
. ([scriptblock]::Create($node.Extent.Text))
$body="[Console]::Out.WriteLine(('O'*10000));[Console]::Error.WriteLine(('E'*10000));exit 7"
$args='-NoProfile -NonInteractive -EncodedCommand '+[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($body))
$out=Join-Path $env:UNIT_DIRECTORY 'stdout.txt';$err=Join-Path $env:UNIT_DIRECTORY 'stderr.txt'
$child=Start-ProbeCapturedProcess (Join-Path $PSHOME 'powershell.exe') $args $env:UNIT_DIRECTORY $out $err
if(-not $child.WaitForExit(15000)){throw 'CAPTURE_PIPE_DEADLOCK'}
$child.WaitForExit()
if($child.ExitCode -ne 7){throw 'CAPTURE_LOST_EXIT_CODE'}
if([IO.File]::ReadAllText($out).Trim() -ne ('O'*10000)){throw 'STDOUT_TRUNCATED'}
if([IO.File]::ReadAllText($err).Trim() -ne ('E'*10000)){throw 'STDERR_TRUNCATED'}
if($child.StartInfo.UseShellExecute -or -not $child.StartInfo.CreateNoWindow){throw 'CAPTURE_MUST_STAY_HIDDEN'}
''', encoding='utf-8')
    completed = subprocess.run([POWERSHELL, '-NoProfile', '-NonInteractive', '-File', str(harness)],
                               env={**os.environ, 'UNIT_SCRIPT': str(standard.HERE / 'guest/windows-win7-probe.ps1'),
                                    'UNIT_DIRECTORY': str(tmp_path)}, capture_output=True, timeout=25)
    assert completed.returncode == 0, completed.stderr.decode(errors='replace')


@pytest.mark.skipif(not POWERSHELL, reason="需要 Windows PowerShell；不替代 Guest PS2 真机")
@pytest.mark.parametrize("scenario,expected", [("valid", None), ("foreign-owner", "STANDARD_CHILD_IDENTITY_MISMATCH"),
                                              ("session-zero", "STANDARD_CHILD_IDENTITY_MISMATCH"), ("wrong-parent", "STANDARD_CHILD_IDENTITY_MISMATCH"),
                                              ("wrong-port-owner", "STANDARD_LISTENER_OWNER_MISMATCH"), ("not-listening", None)])
def test_guest_wmi_process_and_listener_reject_foreign_instances_without_real_execution(tmp_path, scenario, expected):
    harness = tmp_path / "ps2-contract.ps1"
    harness.write_text(r"""
$ErrorActionPreference='Stop'
$tokens=$null;$failures=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($env:UNIT_SCRIPT,[ref]$tokens,[ref]$failures)
if($failures.Count){throw ($failures|Out-String)}
foreach($node in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){. ([scriptblock]::Create($node.Extent.Text))}
$scenario=$env:UNIT_SCENARIO
function Get-WmiObject {
 $row=New-Object PSObject -Property @{ProcessId=10;ExecutablePath='C:\fixture\PartyOps.exe';SessionId=1;ParentProcessId=$PID;CreationDate='actual-start'}
 if($scenario -eq 'session-zero'){$row.SessionId=0};if($scenario -eq 'wrong-parent'){$row.ParentProcessId=987}
 $row|Add-Member ScriptMethod GetOwnerSid {return @{ReturnValue=0;Sid=$(if($scenario -eq 'foreign-owner'){'foreign'}else{'ordinary'})}}
 return $row
}
function Get-ProbeNetstat {
 if($scenario -eq 'not-listening'){return 'TCP 127.0.0.1:19999 0.0.0.0:0 LISTENING 10'}
 if($scenario -eq 'wrong-port-owner'){return 'TCP 127.0.0.1:18825 0.0.0.0:0 LISTENING 999'}
 return 'TCP 127.0.0.1:18825 0.0.0.0:0 LISTENING 10'
}
try {
 $row=Get-ProbeProcess @{Id=10} 'ordinary' 'C:\fixture\PartyOps.exe' 1
 $listening=Test-ProbeListener 18825 10
 $result=@{error=$null;listening=$listening;process=$row}
}catch{$result=@{error=$_.Exception.Message}}
[void][Reflection.Assembly]::LoadWithPartialName('System.Web.Extensions')
$json=New-Object Web.Script.Serialization.JavaScriptSerializer
[Console]::WriteLine($json.Serialize($result))
""", encoding="utf-8")
    completed = subprocess.run([POWERSHELL, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(harness)],
                               env={**os.environ, "UNIT_SCRIPT": str(standard.HERE / "guest/windows-win7-probe.ps1"), "UNIT_SCENARIO": scenario},
                               capture_output=True, timeout=30, check=False)
    assert completed.returncode == 0, completed.stderr.decode(errors="replace")
    output = json.loads(completed.stdout)
    assert output["error"] == expected
    if not expected:
        assert output["listening"] is (scenario != "not-listening")
