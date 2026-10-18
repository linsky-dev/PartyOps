"""本次原 Win10 的一次性 QA 运行态切换；不代表 GUI 退出或 UAC 验收。"""
from __future__ import annotations

import argparse
import importlib.util
import json
import sys
import time
from pathlib import Path
from types import SimpleNamespace

import retention_contract as contract

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = Path(__file__).resolve().parent
LAB_ROOT = Path("D:/PartyOps-VM-Lab")
OLD_MANIFEST = "1c5842c8bd7ba8cfe9b76d2d87e6d0554f7cb8d627cebc89a3f0fcc333a4ce8d"
sys.path.insert(0, str(ROOT / "qa/vm-lab"))

# CIM CreationDate 只有微秒精度；Process.StartTime 保留 100ns，必须截到同一精度。
# 使用 Int64 tick 取余，避免浮点转换损失高位时间精度；仅忽略最后一个 100ns 位。
START_TIME_MATCH = r"""
function Test-CimCreationInstant([DateTime]$ProcessStart, [string]$CimCreated) {
  [long]$actual=$ProcessStart.ToUniversalTime().Ticks
  [long]$expected=([DateTimeOffset]::Parse($CimCreated, [Globalization.CultureInfo]::InvariantCulture)).UtcDateTime.Ticks
  return (($actual - ($actual % 10)) -eq ($expected - ($expected % 10)))
}
"""

# 不取命令行/环境/配置内容；仅收集精确进程身份及安装器是否正在运行。
INVENTORY = r"""
$ErrorActionPreference='Stop'
$marker=Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw|ConvertFrom-Json
if($marker.uuid -ne 'f47cf265-72a2-4d51-9cf1-b1f5ae960cb2' -or $marker.purpose -ne 'disposable-qa' -or (Get-CimInstance Win32_ComputerSystemProduct).UUID -ne $marker.uuid) {throw 'QA_IDENTITY_REQUIRED'}
$all=@(Get-CimInstance Win32_Process)
$products=@($all|Where-Object {$_.Name -like 'PartyOps*'}|ForEach-Object {
  $owner=Invoke-CimMethod -InputObject $_ -MethodName GetOwner
  if($owner.ReturnValue -ne 0) {throw 'PROCESS_OWNER_UNAVAILABLE'}
  @{name=$_.Name;pid=[int]$_.ProcessId;path=$_.ExecutablePath;created=$_.CreationDate.ToUniversalTime().ToString('o');owner=$owner.User;domain=$owner.Domain;local_owner=($owner.Domain -eq $env:COMPUTERNAME);session_id=[int]$_.SessionId}
})
$installers=@($all|Where-Object {$_.Name -like '*setup*' -or $_.Name -eq 'msiexec.exe' -or $_.Name -like 'PartyOps_*.exe' -or $_.Name -like 'is-*.tmp'})
@{products=$products;installer_count=$installers.Count}|ConvertTo-Json -Depth 5 -Compress
"""


def require(condition, error):
    if not condition:
        raise RuntimeError(error)


def preserved_inputs():
    """只核对既有回执与备份，绝不创建或覆盖基线。"""
    baseline_file = OUTPUT / "retention-baseline.json"
    boot_file = OUTPUT / "retention-boot-before.json"
    baseline, boot = contract.read(baseline_file), contract.read(boot_file)
    require(baseline.get("guest_uuid") == contract.UUID and baseline.get("status") == "passed"
            and baseline.get("installed_manifest_sha256") == OLD_MANIFEST, "ORIGINAL_PASSED_BASELINE_REQUIRED")
    require(boot.get("guest_uuid") == contract.UUID and boot.get("disk") == str(contract.DISK.resolve())
            and boot.get("baseline_sha256") == contract.digest(baseline_file), "UNCHANGED_BOOT_BASELINE_REQUIRED")
    backup = ROOT / ".qa/windows-retention/before-overlay.partyops-backup"
    require(baseline.get("backup", {}).get("verified") is True
            and contract.digest(backup) == baseline["backup"]["sha256"]
            and backup.stat().st_size == baseline["backup"]["size"], "VERIFIED_ORIGINAL_BACKUP_REQUIRED")
    return {"baseline_sha256": contract.digest(baseline_file), "boot_before_sha256": contract.digest(boot_file),
            "backup_sha256": baseline["backup"]["sha256"]}, boot


def standard_desktop(info):
    # WMI 输出大写 UUID，QMP/既有登记为小写；比较同一标识而非显示大小写。
    require(str(info.get("uuid", "")).lower() == contract.UUID and info.get("username") == "partyopsuser"
            and info.get("administrator") is False and len(info.get("desktop", [])) == 1
            and info["desktop"][0].get("owner") == "partyopsuser"
            and info["desktop"][0].get("session_id", 0) != 0, "ORIGINAL_STANDARD_USER_DESKTOP_REQUIRED")


def old_target(actual, inventory, desktop):
    standard_desktop(desktop)
    require(inventory.get("installer_count") == 0, "OTHER_INSTALLER_RUNNING")
    products = inventory.get("products", [])
    require(len(products) == 1, "SINGLE_ORIGINAL_QA_PROCESS_REQUIRED")
    process = products[0]
    require(process.get("name") == "PartyOps.exe"
            and process.get("path", "").casefold() == (contract.INSTALL + r"\PartyOps.exe").casefold()
            and process.get("owner") == "partyopsuser" and process.get("local_owner") is True
            and process.get("session_id") == desktop["desktop"][0]["session_id"]
            and process.get("created"), "ORIGINAL_PROCESS_IDENTITY_REQUIRED")
    require(actual.get("manifest_sha256") == OLD_MANIFEST, "ORIGINAL_MANIFEST_REQUIRED")
    # 复用运行态契约，检查唯一 PartyOps.exe、创建时间及 HTTP 18775 PID 归属。
    contract.runtime_matches(actual, actual)
    require(actual["processes"][0]["pid"] == process["pid"]
            and actual["processes"][0]["created"] == process["created"], "PROCESS_CHANGED_DURING_INSPECTION")
    return process


def controlled_stop_script(process, allow_termination):
    # 将盘点表达式作为变量捕获，保持远端唯一 JSON 回执。
    inventory = INVENTORY.replace("@{products=$products;installer_count=$installers.Count}|ConvertTo-Json -Depth 5 -Compress",
                                  "$snapshot=@{products=$products;installer_count=$installers.Count}")
    request = json.dumps({"target": process, "allow_termination": allow_termination, "manifest": OLD_MANIFEST})
    return "$request='" + request.replace("'", "''") + "'|ConvertFrom-Json\n" + START_TIME_MATCH + inventory + r"""
if($snapshot.installer_count -ne 0 -or $snapshot.products.Count -ne 1) {throw 'PROCESS_SET_CHANGED'}
$p=$snapshot.products[0]; $t=$request.target
foreach($key in @('name','pid','path','created','owner','domain','session_id','local_owner')) {
  if($p[$key] -cne $t.$key) {throw 'PROCESS_IDENTITY_CHANGED'}
}
$manifest=Join-Path 'C:\PartyOps QA\中文 程序' 'release-manifest.json'
if((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant() -ne $request.manifest) {throw 'OLD_MANIFEST_CHANGED'}
$owners=@(Get-NetTCPConnection -State Listen -LocalPort 18775 -ErrorAction SilentlyContinue|Select-Object -ExpandProperty OwningProcess -Unique)
if($owners.Count -ne 1 -or $owners[0] -ne $t.pid) {throw 'HTTP_OWNER_CHANGED'}
$handle=Get-Process -Id $t.pid -ErrorAction Stop
if(-not (Test-CimCreationInstant $handle.StartTime $t.created)) {throw 'PROCESS_START_CHANGED'}
$closeAttempted=$false; $closeAccepted=$false; $terminated=$false
if($handle.MainWindowHandle -ne 0) {
  $closeAttempted=$true; $closeAccepted=$handle.CloseMainWindow()
  if($closeAccepted) {$null=$handle.WaitForExit(15000)}
}
if(-not $handle.HasExited) {
  if(-not $request.allow_termination) {throw 'EXPLICIT_QA_PROCESS_TERMINATION_REQUIRED'}
  # 同一 Process 对象持有原进程句柄；不使用名称批量停止，不删除任何数据。
  $handle.Kill(); $terminated=$true
  if(-not $handle.WaitForExit(15000)) {throw 'TARGET_PROCESS_DID_NOT_EXIT'}
}
$remaining=@(Get-CimInstance Win32_Process|Where-Object {$_.Name -like 'PartyOps*'})
$listeners=@(Get-NetTCPConnection -State Listen -LocalPort 18775 -ErrorAction SilentlyContinue)
if($remaining.Count -ne 0 -or $listeners.Count -ne 0) {throw 'PRODUCT_OR_LISTENER_REMAINS'}
@{status='completed';target_pid=[int]$t.pid;target_created=$t.created;close_main_window_attempted=$closeAttempted;close_main_window_accepted=$closeAccepted;controlled_qa_process_termination=$terminated;exit_kind=$(if($terminated){'controlled-qa-process-termination'}else{'close-main-window'});gui_normal_exit_acceptance=$false;uac_acceptance=$false}|ConvertTo-Json -Compress
"""


def execute_stop_script(client, execute, script):
    """长脚本放入固定 QA 目录，避免 SSH/cmd 的命令行长度上限。"""
    import hashlib

    content = script.encode("utf-8")
    sha256 = hashlib.sha256(content).hexdigest()
    remote = "/C:/ProgramData/PartyOps-VM-Lab/runtime-stop-" + sha256 + ".ps1"
    with client.open_sftp() as fs:
        with fs.file(remote, "wx") as stream:
            stream.write(content)
        with fs.file(remote, "rb") as stream:
            require(hashlib.sha256(stream.read()).hexdigest() == sha256, "STOP_SCRIPT_TRANSFER_MISMATCH")
    windows_path = remote.removeprefix("/").replace("/", "\\")
    # PS5 默认编码不是 UTF-8；显式读取，保留中文安装路径。
    invocation = "& ([scriptblock]::Create([IO.File]::ReadAllText('" + windows_path + "',[Text.Encoding]::UTF8)))"
    return execute(client, invocation)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["stop-old", "launch-candidate"])
    parser.add_argument("--allow-qa-process-termination", action="store_true")
    parser.add_argument("--package", type=Path)
    parser.add_argument("--sha256")
    args = parser.parse_args()
    require(not args.allow_qa_process_termination or args.action == "stop-old", "TERMINATION_FLAG_ONLY_FOR_STOP")
    require(args.action != "launch-candidate" or (args.package and args.sha256), "OFFICIAL_PACKAGE_SHA_REQUIRED")
    inputs, boot = preserved_inputs()
    # 延迟加载现有控制器；导入及静态测试不会连接 Guest。
    spec = importlib.util.spec_from_file_location("transition_windows_business", ROOT / "qa/vm-lab/scripts/exercise-windows-business.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    matrix, media = module.load_configuration()
    matrix["defaults"].update(primary_root=str(LAB_ROOT), fallback_root=str(LAB_ROOT))
    lab = module.QemuLab(matrix, LAB_ROOT, media)
    binding = None
    if args.action == "launch-candidate":
        binding = contract.candidate(lab, ROOT, args.package, args.sha256)
        contract.completed_install(OUTPUT / "overlay-install", binding)
    contract.bind_guest(lab, module.qmp)
    # 独占意图文件提前检查，已有任何尝试时不再连接或动作。
    attempt_file = OUTPUT / ("runtime-" + args.action + ".attempt.json")
    require(not attempt_file.exists(), "OPERATION_ALREADY_ATTEMPTED_INSPECT_RECEIPT")
    client = module.windows.connect(lab, matrix["targets"]["win10-x64"])
    try:
        # 仅复用 desktop 方法，避免 __init__ 读取业务私有状态/凭据。
        runner = object.__new__(module.WindowsRun)
        runner.client, runner.args = client, SimpleNamespace(uuid=contract.UUID)
        desktop = runner.desktop("Inspect")
        standard_desktop(desktop)
        inventory = json.loads(module.windows.execute(client, INVENTORY))
        actual = contract.probe(client, module.windows.execute)
        require(actual["boot_id"] == boot["boot_id"], "BOOT_CHANGED_BEFORE_RUNTIME_TRANSITION")
        if args.action == "stop-old":
            process = old_target(actual, inventory, desktop)
        else:
            require(inventory.get("installer_count") == 0 and inventory.get("products") == []
                    and actual.get("processes") == [] and actual.get("http_owners") == [], "NO_PRODUCT_INSTANCE_OR_INSTALLER_REQUIRED")
            for key in ("manifest_sha256", "executable_sha256", "wizard_sha256"):
                require(actual.get(key) == binding[key], "INSTALLED_CANDIDATE_MISMATCH:" + key)
        require(preserved_inputs()[0] == inputs, "PRESERVED_INPUTS_CHANGED")
        if binding:
            require(contract.digest(args.package) == args.sha256.lower(), "CANDIDATE_CHANGED_BEFORE_LAUNCH")
            contract.completed_install(OUTPUT / "overlay-install", binding)
        contract.bind_guest(lab, module.qmp)
        started = contract.now()
        attempt = {"action": args.action, "time_utc": started, "guest_uuid": contract.UUID,
                   "disk": str(contract.DISK.resolve()), "inputs": inputs, "candidate": binding,
                   "allow_qa_process_termination": args.allow_qa_process_termination,
                   "scope": "qa-runtime-transition-only-not-gui-exit-or-uac-acceptance"}
        if args.action == "stop-old":
            attempt["target"] = process
        else:
            attempt["install_completed_sha256"] = contract.digest(OUTPUT / "overlay-install/completed.json")
        contract.exclusive(attempt_file, attempt)
        try:
            if args.action == "stop-old":
                result = json.loads(execute_stop_script(client, module.windows.execute,
                    controlled_stop_script(process, args.allow_qa_process_termination)))
            else:
                # 再次盘点后只调用一次既有普通用户交互任务，不执行 Prepare/配置。
                last = json.loads(module.windows.execute(client, INVENTORY))
                require(last.get("products") == [] and last.get("installer_count") == 0, "PROCESS_APPEARED_BEFORE_LAUNCH")
                launched = runner.desktop("Launch")
                standard_desktop(launched)
                deadline = time.monotonic() + 120
                while True:
                    current = contract.probe(client, module.windows.execute)
                    try:
                        contract.runtime_matches(current, binding, started)
                        owner = runner.desktop("Inspect")
                        standard_desktop(owner)
                        module.WindowsRun.require_configuration_owner(owner)
                        require(any(p.get("pid") == current["processes"][0]["pid"] and p.get("owner") == "partyopsuser"
                                    for p in owner["processes"]), "HTTP_OWNER_NOT_ORIGINAL_STANDARD_USER")
                        break
                    except RuntimeError:
                        require(time.monotonic() < deadline, "CANDIDATE_START_TIMEOUT_INSPECT_WITHOUT_RELAUNCH")
                        time.sleep(2)
                result = {"status": "completed", "runtime": current, "username": owner["username"],
                          "administrator": owner["administrator"], "launch_count": 1,
                          "gui_normal_exit_acceptance": False, "uac_acceptance": False}
            require(preserved_inputs()[0] == inputs, "PRESERVED_INPUTS_CHANGED_AFTER_ACTION")
            contract.bind_guest(lab, module.qmp)
            contract.exclusive(OUTPUT / ("runtime-" + args.action + ".result.json"), {
                "status": "completed", "completed_at": contract.now(), "attempt_sha256": contract.digest(attempt_file), "result": result})
        except Exception as error:
            # 不记录异常文本（传输异常可能含敏感上下文）；不重发任何停止/启动。
            contract.exclusive(OUTPUT / ("runtime-" + args.action + ".result.json"), {
                "status": "failed-inspect-without-retry", "error_type": type(error).__name__,
                "attempt_sha256": contract.digest(attempt_file), "completed_at": contract.now()})
            raise
    finally:
        client.close()
    print("QA 运行态切换回执已保存；不计 GUI 正常退出或 UAC 验收通过。")


if __name__ == "__main__":
    main()
