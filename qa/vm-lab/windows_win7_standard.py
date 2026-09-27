"""WinRM/PS2 原版 Win7 安装版普通用户启动诊断；不生成完整生命周期通过。"""
from __future__ import annotations

import base64
import json
import re
import time
import uuid
from pathlib import Path

from evidence import now, safe_child, sha256, write_json
from identity import probe, runtime_binding
from windows_build_payload import expected_payload
from windows_remote import WinRMFiles, file_script

HERE = Path(__file__).resolve().parent
INSTALL_DIR = r"C:\PartyOps QA\中文 程序"
JSON_PS = "[void][Reflection.Assembly]::LoadWithPartialName('System.Web.Extensions'); $json=New-Object Web.Script.Serialization.JavaScriptSerializer\n"


def ps(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def installation_binding(lab, target: str, install_report: Path, source: str) -> dict:
    """旧 VM、旧安装回执、旧源码和无构建载荷封存均不可导入新诊断。"""
    spec = lab.matrix["targets"][target]
    architecture = {"win7-x64": "x86_64", "win7-x86": "i686"}.get(target)
    if (architecture is None or spec.get("backend") != "qemu" or spec.get("os") != "windows"
            or spec.get("arch") != architecture or spec.get("os_release") != "7 SP1" or not spec.get("winrm_port")):
        raise RuntimeError("ORIGINAL_WIN7_WINRM_TARGET_REQUIRED")
    path = safe_child(lab.root / "reports" / target, install_report / "installed-probe.json")
    receipt = read_json(path)
    state = lab.state(target)
    environment = runtime_binding(lab, target)
    package_id = "windows7_amd64" if target == "win7-x64" else "windows7_x86"
    if (receipt.get("target") != target or receipt.get("guest_uuid") != state["uuid"]
            or receipt.get("environment") != environment or receipt.get("restore_generation") != state.get("restore_generation")
            or receipt.get("package", {}).get("id") != package_id or receipt.get("install", {}).get("exit_code") != 0):
        raise RuntimeError("WIN7_INSTALLATION_RECEIPT_BINDING_MISMATCH")
    package, payload, _ = expected_payload(lab, receipt["package"], source)
    evidence = receipt.get("evidence", {}).get("install_log", {})
    log = safe_child(path.parent, path.parent / evidence["path"])
    if sha256(log) != evidence["sha256"] or receipt["install"].get("installer_sha256") != package["sha256"]:
        raise RuntimeError("WIN7_INSTALLATION_EVIDENCE_CHANGED")
    return {"target": target, "uuid": state["uuid"], "environment": environment,
            "restore_generation": state.get("restore_generation"), "package": package,
            "source_fingerprint": source, "install_report": {"path": str(path), "sha256": sha256(path)},
            "install_log": {"path": str(log), "sha256": sha256(log)},
            "manifest_sha256": payload["manifest"]["sha256"],
            "app_sha256": payload["executables"]["PartyOps.exe"]["sha256"],
            "wizard_sha256": payload["executables"]["PartyOpsWizard.exe"]["sha256"],
            "version": package["version"], "app_path": INSTALL_DIR + r"\PartyOps.exe",
            "pe_machine": 0x8664 if target == "win7-x64" else 0x14C,
            "scripts": {name: sha256(HERE / "guest" / name) for name in ("windows-standard-user.ps1", "windows-win7-probe.ps1")},
            "controller_sha256": sha256(Path(__file__))}


def hash_assertions(files: dict[str, str]) -> str:
    script = ""
    for path, digest in files.items():
        script += "$path=" + ps(path) + "\n" + r"""
$ancestor=$path
while($ancestor) {
  if((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {throw 'WIN7_INSTALLED_REPARSE_REJECTED'}
  $ancestor=Split-Path -Path $ancestor -Parent
}
$stream=[IO.File]::OpenRead($path); $algorithm=[Security.Cryptography.SHA256]::Create()
try {$actual=[BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}
finally {$stream.Dispose();$algorithm.Clear()}
""" + "if($actual -ne " + ps(digest) + ") {throw 'WIN7_EXPECTED_FILE_HASH_MISMATCH'}\n"
    return script


def installed_script(binding: dict) -> str:
    """Guest 内再次校验实际安装包、独立封存的 EXE/清单与卸载登记，不信安装后自报哈希。"""
    files = {
        binding["app_path"]: binding["app_sha256"], INSTALL_DIR + r"\PartyOpsWizard.exe": binding["wizard_sha256"],
        INSTALL_DIR + r"\release-manifest.json": binding["manifest_sha256"],
        "C:\\PartyOps-QA\\incoming\\" + Path(binding["package"]["path"]).name: binding["package"]["sha256"],
    }
    script = JSON_PS + hash_assertions(files)
    script += "$exe=" + ps(binding["app_path"]) + ";$expectedVersion=" + ps(binding["version"]) + "\n" + r"""
$stream=[IO.File]::OpenRead($exe);$reader=New-Object IO.BinaryReader($stream)
try {
 if($reader.ReadUInt16() -ne 0x5a4d){throw 'WIN7_INSTALLED_PE_INVALID'}
 $stream.Position=0x3c;$offset=$reader.ReadInt32();$stream.Position=$offset
 if($reader.ReadUInt32() -ne 0x4550){throw 'WIN7_INSTALLED_PE_INVALID'}
 $machine=$reader.ReadUInt16()
} finally {$reader.Close();$stream.Dispose()}
$rows=@()
foreach($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')) {
 $key=$root+'\{1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A}_is1'
 if(Test-Path -LiteralPath $key) {$entry=Get-ItemProperty -LiteralPath $key; $rows+=@{version=[string]$entry.DisplayVersion; path=[string]$entry.InstallLocation}}
}
if($rows.Count -ne 1 -or $rows[0]['version'] -ne $expectedVersion -or $rows[0]['path'].TrimEnd('\') -ne (Split-Path $exe -Parent)) {throw 'WIN7_INSTALLED_REGISTRATION_MISMATCH'}
""" + f"if($machine -ne {binding['pe_machine']}) {{throw 'WIN7_INSTALLED_PE_ARCH_MISMATCH'}}\n"
    return script + "[Console]::WriteLine($json.Serialize(@{verified=$true;pe_machine=[int]$machine;version=$expectedVersion}))"


def validate_account(account: dict, binding: dict, *, desktop: bool) -> None:
    if (account.get("uuid", "").casefold() != binding["uuid"].casefold() or account.get("administrator") is not False
            or account.get("username") != "partyopsuser" or not re.fullmatch(r"S-1-5-21-\d+-\d+-\d+-\d+", str(account.get("sid", "")))):
        raise RuntimeError("WIN7_STANDARD_ACCOUNT_IDENTITY_MISMATCH")
    rows = account.get("desktop", [])
    if desktop and (len(rows) != 1 or type(rows[0].get("session_id")) is not int or rows[0]["session_id"] <= 0):
        raise RuntimeError("WIN7_STANDARD_USER_INTERACTIVE_DESKTOP_REQUIRED_REBOOT_GUEST")


def validate_result(result: dict, context: dict, context_sha256: str, task: dict) -> None:
    """从主机检查普通令牌、真实进程、健康端点和每一个退出码；局部成功始终不是完整通过。"""
    if (result.get("run_id") != context["run_id"] or result.get("context_sha256") != context_sha256
            or result.get("runtime_environment_passed") is not False or type(result.get("schema_version")) is not int or result["schema_version"] != 1):
        raise RuntimeError("WIN7_STANDARD_RESULT_BINDING_MISMATCH")
    if (type(task.get("exit_code")) is not int or task["exit_code"] != 0 or task.get("running") is not False
            or task.get("principal_sid") != context["sid"] or type(task.get("logon_type")) is not int or task["logon_type"] != 3
            or type(task.get("run_level")) is not int or task["run_level"] != 0 or task.get("action_matches") is not True):
        raise RuntimeError("WIN7_STANDARD_TASK_FAILED_OR_STILL_RUNNING")
    if type(result.get("exit_code")) is not int or result["exit_code"] != 0 or result.get("error"):
        raise RuntimeError("WIN7_STANDARD_PROBE_FAILED:" + str(result.get("error", "exit_code")))
    token = result.get("token", {})
    if (token.get("sid") != context["sid"] or token.get("administrator") is not False
            or type(token.get("session_id")) is not int or token.get("session_id") != context["session_id"] or token.get("session_id", 0) <= 0):
        raise RuntimeError("WIN7_STANDARD_RESULT_TOKEN_MISMATCH")
    for key in ("permission_process", "server_process", "server_process_after_health"):
        row = result.get(key, {})
        if (row.get("sid") != context["sid"] or row.get("session_id") != context["session_id"] or row.get("path") != context["app_path"]
                or type(row.get("pid")) is not int or row["pid"] <= 0 or not row.get("created") or not row.get("parent_pid")):
            raise RuntimeError("WIN7_STANDARD_RESULT_PROCESS_MISMATCH")
    server = result["server_process"]
    if (server != result["server_process_after_health"] or result.get("listener_pid") != server["pid"]
            or result["permission_process"]["pid"] == server["pid"] or result["permission_process"]["parent_pid"] != server["parent_pid"]):
        raise RuntimeError("WIN7_STANDARD_RESULT_LISTENER_MISMATCH")
    permission = result.get("permission", {})
    if (type(result.get("permission_exit_code")) is not int or result["permission_exit_code"] != 0
            or any(permission.get(key) is not True for key in ("passed", "runtime_readable", "user_temp_writable"))):
        raise RuntimeError("WIN7_STANDARD_RESULT_PERMISSION_FAILED")
    health = result.get("health", {})
    if (health.get("status") != "ok" or health.get("app_version") != context["version"] or health.get("mode") != "personal"
            or any(health.get("sqlite", {}).get(key) is not True for key in ("safe_version", "fts5"))
            or result.get("frontend_ready") is not True or result.get("app_sha256_after") != context["app_sha256"]
            or type(result.get("server_exit_code")) is not int or result.get("diagnostic_stop") != "owned-handle-force-stop"):
        raise RuntimeError("WIN7_STANDARD_RESULT_HEALTH_FAILED")


def task_command(remote: str) -> str:
    # PS2 直接 -File 会将无 BOM 的 UTF-8 中文读成 ANSI；显式解码后执行。
    command = "& ([scriptblock]::Create([IO.File]::ReadAllText(" + ps(remote + r"\windows-win7-probe.ps1") + ",[Text.Encoding]::UTF8))) -ContextPath " + ps(remote + r"\context.json")
    # 函数外的JSON序列化/入口错误同样留痕；不得把已退出任务空等到超时。
    command = "$ErrorActionPreference='Stop';try {" + command + "} catch {[IO.File]::WriteAllText(" + ps(remote + r"\output\entry-error.txt") + ",([string]$_.Exception.Message+' at '+[string]$_.InvocationInfo.PositionMessage),(New-Object Text.UTF8Encoding($false)));exit 1}"
    return base64.b64encode(command.encode("utf-16le")).decode("ascii")


def task_status_script(remote: str, name: str) -> str:
    return JSON_PS + file_script(remote + r"\output\result.json") + "\n$name=" + ps(name) + ";$encoded=" + ps(task_command(remote)) + ";$directory=" + ps(remote) + r"""
$scheduler=New-Object -ComObject 'Schedule.Service';$scheduler.Connect();$folder=$scheduler.GetFolder('\')
$task=$folder.GetTask($name)
$definition=$task.Definition;$actions=$definition.Actions
$principalSid=[string]$definition.Principal.UserId
if($principalSid -notmatch '^S-1-') {$principalSid=(New-Object Security.Principal.NTAccount($principalSid)).Translate([Security.Principal.SecurityIdentifier]).Value}
$shellPath=Join-Path (Get-WmiObject Win32_OperatingSystem).WindowsDirectory 'System32\WindowsPowerShell\v1.0\powershell.exe'
$matches=$actions.Count -eq 1
if($matches){$action=$actions.Item(1);$matches=$action.Path -eq $shellPath -and $action.Arguments -eq ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand '+$encoded) -and $action.WorkingDirectory -eq $directory}
[Console]::WriteLine($json.Serialize(@{running=([int]$task.State -eq 4);state=[int]$task.State;exit_code=[int]$task.LastTaskResult;result_exists=[IO.File]::Exists($path);
 principal_sid=[string]$principalSid;logon_type=[int]$definition.Principal.LogonType;run_level=[int]$definition.Principal.RunLevel;action_matches=[bool]$matches}))
"""


def start_task_script(remote: str, context: dict) -> str:
    encoded = task_command(remote)
    return JSON_PS + "$name=" + ps("PartyOps-QA-" + context["run_id"]) + ";$sid=" + ps(context["sid"]) + ";$directory=" + ps(remote) + ";$encoded=" + ps(encoded) + r"""
$active=@(Get-WmiObject Win32_Process -Filter "Name LIKE 'PartyOps%'" | Where-Object {$_.GetOwnerSid().Sid -eq $sid})
if($active.Count -ne 0){throw 'WIN7_STANDARD_USER_APP_ALREADY_RUNNING_CHECK_BEFORE_RETRY'}
$scheduler=New-Object -ComObject 'Schedule.Service';$scheduler.Connect();$task=$scheduler.NewTask(0)
$task.RegistrationInfo.Description='PartyOps original Win7 ordinary-user installed diagnostic'
$task.Principal.UserId=$sid;$task.Principal.LogonType=3;$task.Principal.RunLevel=0
$task.Settings.Enabled=$true;$task.Settings.AllowDemandStart=$true;$task.Settings.MultipleInstances=2
$task.Settings.Priority=5
$task.Settings.ExecutionTimeLimit='PT10M'
$action=$task.Actions.Create(0)
$action.Path=Join-Path (Get-WmiObject Win32_OperatingSystem).WindowsDirectory 'System32\WindowsPowerShell\v1.0\powershell.exe'
$action.Arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand '+$encoded
$action.WorkingDirectory=$directory
$folder=$scheduler.GetFolder('\')
# TASK_CREATE=2，已有任务不覆盖、不重复执行；断线后只通过 resume 收集原结果。
$registered=$folder.RegisterTaskDefinition($name,$task,2,$sid,$null,3,$null)
$registered.Run($null) | Out-Null
[Console]::WriteLine($json.Serialize(@{scheduled=$true;task_name=$name;sid=$sid;logon_type=3;run_level=0}))
"""


def exercise(lab, target: str, phase: str, install_report: Path, *, resume: Path | None = None, timeout: int = 420) -> dict:
    from lab import fingerprint

    binding = installation_binding(lab, target, install_report, fingerprint())
    system = probe(lab, target)
    if system.get("installed_package") is not True:
        raise RuntimeError("WIN7_REGISTERED_INSTALLED_PACKAGE_REQUIRED")
    client = WinRMFiles(lab, target)
    # 每次续跑重新哈希安装字节并核验身份，旧 JSON 不能代替当前系统。
    installed = json.loads(client.powershell(installed_script(binding), timeout=600))
    if phase not in {"prepare", "probe"} or (resume is not None and phase != "probe"):
        raise RuntimeError("WIN7_STANDARD_PHASE_OR_RESUME_INVALID")
    if resume:
        directory = safe_child(lab.root / "reports" / target, resume)
        saved = read_json(directory / "binding.json")
        if saved != binding:
            raise RuntimeError("WIN7_STANDARD_RESUME_BINDING_CHANGED")
        context = read_json(directory / "context.json")
        if context["boot_id"] != system["boot_id"]:
            raise RuntimeError("WIN7_STANDARD_RESUME_BOOT_CHANGED")
        run_id = context["run_id"]
        if not re.fullmatch(r"win7-standard-[a-f0-9]{12}", run_id):
            raise RuntimeError("WIN7_STANDARD_RUN_ID_INVALID")
        remote = "C:\\PartyOps-QA\\" + run_id
    else:
        run_id = "win7-standard-" + uuid.uuid4().hex[:12]
        directory = lab.root / "reports" / target / run_id
        directory.mkdir(parents=True, exist_ok=False)
        remote = "C:\\PartyOps-QA\\" + run_id
        write_json(directory / "binding.json", binding)
        write_json(directory / "guest-identity.json", system)
        write_json(directory / "installed-files.json", installed)
        client.powershell(file_script(remote + r"\context.json") + "\n$root=" + ps(remote) + r"""
if(Test-Path -LiteralPath $root){throw 'WIN7_STANDARD_REMOTE_RUN_ALREADY_EXISTS'}
[void][IO.Directory]::CreateDirectory($root)
& icacls.exe $root /inheritance:r /grant '*S-1-5-18:(OI)(CI)F' /grant '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if($LASTEXITCODE -ne 0){throw 'WIN7_STANDARD_RUN_ACL_FAILED'}
""")
        for name in binding["scripts"]:
            client.put(HERE / "guest" / name, remote + "\\" + name, binding["scripts"][name])
        action = "Prepare" if phase == "prepare" else "Inspect"
        account = json.loads(client.powershell("& ([scriptblock]::Create([IO.File]::ReadAllText(" + ps(remote + r"\windows-standard-user.ps1") + ",[Text.Encoding]::UTF8))) -ExpectedUuid " + ps(binding["uuid"]) + " -Action " + action))
        account.pop("wizard_url", None)  # 普通用户入口可能有一次性票据；本诊断不需要，也不保存。
        write_json(directory / "account.json", account)
        validate_account(account, binding, desktop=phase == "probe")
        if phase == "prepare":
            result = {"status": "prepared", "report_path": str(directory), "exit_code": 0,
                      "next": "正常重启 Guest，核验专用普通用户 Explorer 后执行 probe；此命令不自动重启。",
                      "runtime_environment_passed": False}
            write_json(directory / "prepare-result.json", result)
            return result
        context = {**binding, "run_id": run_id, "boot_id": system["boot_id"], "sid": account["sid"],
                   "session_id": account["desktop"][0]["session_id"]}
        write_json(directory / "context.json", context)
        client.put(directory / "context.json", remote + r"\context.json", sha256(directory / "context.json"))
        client.powershell("$root=" + ps(remote) + ";$sid=" + ps(account["sid"]) + r"""
[void][IO.Directory]::CreateDirectory((Join-Path $root 'output'))
& icacls.exe $root /grant ('*'+$sid+':(OI)(CI)RX') | Out-Null
if($LASTEXITCODE -ne 0){throw 'WIN7_STANDARD_CONTEXT_ACL_FAILED'}
& icacls.exe (Join-Path $root 'output') /grant ('*'+$sid+':(OI)(CI)M') | Out-Null
if($LASTEXITCODE -ne 0){throw 'WIN7_STANDARD_OUTPUT_ACL_FAILED'}
""")
        write_json(directory / "launch-request.json", {"at": now(), "context_sha256": sha256(directory / "context.json")})
        # 先写主机请求再发起唯一任务；网络中断不自动重复创建/运行。
        try:
            launch = json.loads(client.powershell(start_task_script(remote, context)))
        except RuntimeError as exc:
            raise RuntimeError("WIN7_STANDARD_LAUNCH_UNCERTAIN_USE_RESUME:" + str(directory) + ";" + str(exc)) from exc
        write_json(directory / "launch-result.json", launch)
    request = read_json(directory / "launch-request.json")
    if request.get("context_sha256") != sha256(directory / "context.json"):
        raise RuntimeError("WIN7_STANDARD_CONTEXT_CHANGED")
    remote_hashes = {remote + "\\" + name: digest for name, digest in binding["scripts"].items()}
    remote_hashes[remote + r"\context.json"] = request["context_sha256"]
    client.powershell(hash_assertions(remote_hashes))
    deadline = time.monotonic() + timeout
    while True:
        task = json.loads(client.powershell(task_status_script(remote, "PartyOps-QA-" + run_id)))
        if task.get("result_exists") and task.get("running") is False:
            break
        if task.get("running") is False and task.get("exit_code") == 1 and not task.get("result_exists"):
            write_json(directory / "entry-failed-task.json", task)
            error_path = remote + r"\output\entry-error.txt"
            exists = client.powershell("[Console]::WriteLine([IO.File]::Exists(" + ps(error_path) + "))").strip()
            if exists.casefold() == "true":
                client.get(error_path, directory / "entry-error.txt")
            raise RuntimeError("WIN7_STANDARD_ENTRY_FAILED_INSPECT_REPORT:" + str(directory))
        if time.monotonic() >= deadline:
            write_json(directory / ("pending-" + uuid.uuid4().hex[:8] + ".json"), {"at": now(), "task": task})
            raise RuntimeError("WIN7_STANDARD_PENDING_USE_RESUME_NO_RELAUNCH:" + str(directory))
        time.sleep(5)
    collection = directory / ("collection-" + uuid.uuid4().hex[:8])
    collection.mkdir(exist_ok=False)
    write_json(collection / "task.json", task)
    client.get(remote + r"\output\result.json", collection / "result.json")
    result = read_json(collection / "result.json")
    for name in ("permission.stdout", "permission.stderr", "server.stdout", "server.stderr"):
        exists = client.powershell("[Console]::WriteLine([IO.File]::Exists(" + ps(remote + "\\output\\" + name) + "))").strip()
        if exists.casefold() == "true":
            client.get(remote + "\\output\\" + name, collection / name)
    error = None
    try:
        validate_result(result, context, request["context_sha256"], task)
    except RuntimeError as exc:
        error = str(exc)
    try:
        installed_after = json.loads(client.powershell(installed_script(binding), timeout=600))
        write_json(collection / "installed-files-after.json", installed_after)
        client.powershell(hash_assertions(remote_hashes))
    except RuntimeError as exc:
        error = str(exc)
    # 完成后再次绑定真实系统和包，不能在执行中替换源码或恢复快照。
    if installation_binding(lab, target, install_report, fingerprint()) != binding or probe(lab, target)["boot_id"] != context["boot_id"]:
        error = "WIN7_STANDARD_BINDING_CHANGED_DURING_EXECUTION"
    summary = {"status": "blocked" if error else "partial", "exit_code": 1 if error else 0,
               "report_path": str(collection), "run_directory": str(directory), "error": error,
               "scope": "ordinary-interactive-token-installed-permission-health-only", "runtime_environment_passed": False,
               "remaining": "首次配置、桌面交互、业务、OCR/模型、真实重启、升级及卸载仍须独立执行。"}
    write_json(collection / "summary.json", summary)
    return summary
