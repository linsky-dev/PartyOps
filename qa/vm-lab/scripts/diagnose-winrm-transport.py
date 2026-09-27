"""仅诊断登记 Guest 的 WinRM 生命周期；不安装产品、不改变 VM 或基础盘。"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import sys
import time
import uuid
from pathlib import Path

import requests
from winrm.exceptions import WinRMError, WinRMOperationTimeoutError, WinRMTransportError

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from evidence import now, write_json
from lab import load_configuration
from providers import QemuLab
from windows_remote import WinRMFiles, file_script, session


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--transfer-fixture", action="store_true", help="只往登记 C:/PartyOps-QA 传输随机命名的 256 KiB 二进制夹具并下载核验")
    parser.add_argument("--exit-code-fixture", action="store_true", help="只运行 Guest cmd exit 0/7，核验真实退出码采集")
    parser.add_argument("--identity-probe", action="store_true", help="执行既有完整只读 identity.probe，验证长命令直连执行路径")
    parser.add_argument("--install-state", action="store_true", help="只读安装包、安装登记及进程，回收现有安装日志；不启动安装器")
    args = parser.parse_args()
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    target = "win7-x64"
    state = lab.state(target)
    if state["uuid"] != "406ef8ee-e342-405f-933f-7ddce0a55f1b" or not lab.live(state):
        raise RuntimeError("DIAGNOSTIC_REQUIRES_REGISTERED_RUNNING_WIN7")
    run_id = uuid.uuid4().hex[:12]
    record = {"scope": "winrm-transport-fixture" if args.transfer_fixture else "winrm-readonly-transport-diagnostic", "generated_at": now(),
              "target": target, "uuid": state["uuid"], "pid": state.get("pid"), "runtime_environment_passed": False}
    directory = lab.root / "reports" / target / ("winrm-transport-" + run_id)
    directory.mkdir()
    destination = directory / "diagnostic.json"
    print(json.dumps(record, ensure_ascii=False), flush=True)
    protocol = shell = command = None
    stage = "session-hardware-identity"
    try:
        client = session(lab, target, 60)
        protocol = client.protocol
        record["powershell_path"] = client.partyops_powershell
        stage = "open-shell-65001"
        shell = protocol.open_shell(codepage=65001)
        stage = "run-command-skip-shell"
        script = "[Console]::WriteLine('winrm-readonly-ok')"
        encoded = base64.b64encode(script.encode("utf-16le")).decode("ascii")
        command = protocol.run_command(shell, client.partyops_powershell, ["-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
                                       console_mode_stdin=False, skip_cmd_shell=True)
        stage = "close-stdin"
        protocol.send_command_input(shell, command, b"", end=True)
        stage = "receive-output"
        deadline = time.monotonic() + 90
        stdout, stderr = b"", b""
        while True:
            if time.monotonic() > deadline:
                raise RuntimeError("DIAGNOSTIC_COMMAND_TIMEOUT")
            try:
                out, err, code, done = protocol.get_command_output_raw(shell, command)
            except WinRMOperationTimeoutError:
                continue
            stdout += out
            stderr += err
            if done:
                break
        record.update(status="observed", exit_code=code, stdout=stdout.decode("utf-8", errors="replace"),
                      stderr=stderr.decode("utf-8", errors="replace"))
        if args.identity_probe and code == 0:
            from identity import probe
            stage = "full-identity-probe"
            record["system_identity"] = probe(lab, target)
        if args.install_state and code == 0:
            stage = "readonly-install-state"
            files = WinRMFiles(lab, target)
            script = r"""
Add-Type -AssemblyName System.Web.Extensions
$json=New-Object Web.Script.Serialization.JavaScriptSerializer
$incoming='C:\PartyOps-QA\incoming\PartyOps_1.4.5-rc.6_windows7_amd64.exe'
$app='C:\PartyOps QA\中文 程序\PartyOps.exe'
$log='C:\PartyOps-QA\install.log'
$result=@{incoming_exists=[bool](Test-Path -LiteralPath $incoming);incoming_bytes=[long]0;app_exists=[bool](Test-Path -LiteralPath $app);log_exists=[bool](Test-Path -LiteralPath $log);log_bytes=[long]0;powershell_version=[string]$PSVersionTable.PSVersion.ToString()}
if($result.incoming_exists){$result.incoming_bytes=[long](Get-Item -LiteralPath $incoming).Length}
if($result.log_exists){$result.log_bytes=[long](Get-Item -LiteralPath $log).Length}
$result.processes=@(Get-WmiObject Win32_Process|Where-Object {$_.Name -like 'PartyOps*' -or $_.Name -like 'setup*'}|ForEach-Object {@{pid=[int]$_.ProcessId;name=[string]$_.Name;executable_path=[string]$_.ExecutablePath;command_line=[string]$_.CommandLine}})
$keys=@('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A}_is1','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A}_is1')
$result.registrations=@($keys|Where-Object {Test-Path -LiteralPath $_}|ForEach-Object {$item=Get-ItemProperty -LiteralPath $_;@{version=[string]$item.DisplayVersion;install_dir=[string]$item.InstallLocation}})
$result.layout=@(Get-ChildItem -LiteralPath 'C:\PartyOps QA\中文 程序' -ErrorAction SilentlyContinue|ForEach-Object {@{name=[string]$_.Name;is_directory=[bool]$_.PSIsContainer}})
[Console]::WriteLine($json.Serialize($result))
"""
            record["installation_observation"] = json.loads(files.powershell(script, timeout=90))
            if record["installation_observation"]["log_exists"]:
                stage = "readonly-install-log-download"
                files.get(r"C:\PartyOps-QA\install.log", directory / "observed-install.log", timeout=120)
                record["installation_log"] = {"path": str(directory / "observed-install.log"),
                                              "sha256": hashlib.sha256((directory / "observed-install.log").read_bytes()).hexdigest()}
        if args.exit_code_fixture and code == 0:
            stage = "exit-code-fixture"
            outcomes = []
            files = WinRMFiles(lab, target)
            for expected_exit in (0, 7):
                check = r"""
$info=New-Object Diagnostics.ProcessStartInfo
$info.FileName=Join-Path $env:SystemRoot 'System32\cmd.exe'
$info.Arguments='/d /c exit __EXIT_CODE__'
$info.UseShellExecute=$false
$info.CreateNoWindow=$true
$process=New-Object Diagnostics.Process
$process.StartInfo=$info
if(-not $process.Start()){throw 'FIXTURE_START_FAILED'}
$handle=$process.Handle
if($handle -eq [IntPtr]::Zero){throw 'FIXTURE_HANDLE_UNAVAILABLE'}
if(-not $process.WaitForExit(10000)){throw 'FIXTURE_TIMEOUT'}
if($null -eq $process.ExitCode){throw 'FIXTURE_EXIT_CODE_NULL'}
[Console]::WriteLine($process.ExitCode)
$process.Dispose()
""".replace("__EXIT_CODE__", str(expected_exit))
                observed_exit = int(files.powershell(check, timeout=30))
                if observed_exit != expected_exit:
                    raise RuntimeError("DIAGNOSTIC_EXIT_CODE_MISMATCH")
                outcomes.append({"expected": expected_exit, "actual": observed_exit})
            record["exit_code_fixture"] = outcomes
        if args.transfer_fixture and code == 0:
            # 本次专属夹具只使用 WinRMFiles 的登记 QA 路径；不接收用户可选的任意远端路径。
            content = bytes(range(256)) * 1024
            digest = hashlib.sha256(content).hexdigest()
            local = directory / "fixture.bin"
            local.write_bytes(content)
            remote = r"C:\PartyOps-QA\transport-fixture-" + run_id + ".bin"
            files = WinRMFiles(lab, target)
            stage = "fixture-put"
            files.put(local, remote, digest, timeout=120)
            stage = "fixture-get"
            received = directory / "received.bin"
            files.get(remote, received, timeout=120)
            downloaded_digest = hashlib.sha256(received.read_bytes()).hexdigest()
            if downloaded_digest != digest:
                raise RuntimeError("DIAGNOSTIC_ROUNDTRIP_HASH_MISMATCH")
            record["fixture"] = {"remote_path": remote, "bytes": len(content), "sha256": digest,
                                 "download_sha256": downloaded_digest}
    except (RuntimeError, WinRMError, WinRMTransportError, WinRMOperationTimeoutError, requests.RequestException, OSError, ValueError) as exc:
        # 不记录原始 HTTP/XML；只读命令/固定夹具错误和 WSMan reason 经脱敏后用于本地诊断。
        fault = {"exception": type(exc).__name__, "stage": stage}
        if isinstance(exc, UnicodeDecodeError):
            # 这里只处理固定二进制夹具的哈希回应；十六进制前后缀用于定位 BOM/编码，不记录业务内容。
            fault["fixture_hash_output_bytes"] = len(exc.object)
            fault["fixture_hash_output_prefix_hex"] = exc.object[:96].hex()
            fault["fixture_hash_output_suffix_hex"] = exc.object[-96:].hex()
        if isinstance(exc, RuntimeError) and str(exc).startswith("WINRM_TRANSPORT_FAILED:"):
            fault["transport_error"] = str(exc)
        for field in ("code", "wsman_fault_code", "wmierror_code"):
            value = getattr(exc, field, None)
            if isinstance(value, int):
                fault[field] = value
        reason = getattr(exc, "reason", None)
        if isinstance(exc, RuntimeError) and str(exc).startswith(("WINRM_COMMAND_FAILED:", "DIAGNOSTIC_", "WINRM_FILE_TRANSFER_", "WINDOWS_GUEST_PACKAGE_HASH_")):
            reason = str(exc)
        if isinstance(reason, str):
            private = json.loads((lab.vm_dir(target) / "guest-credential.local.json").read_text(encoding="utf-8"))
            for key in ("password", "username"):
                if private.get(key):
                    reason = reason.replace(private[key], "[redacted]")
            fault["reason"] = reason[:1500]
        if args.transfer_fixture and stage == "fixture-put":
            try:
                fault["remote_fixture_bytes"] = int(files.powershell(file_script(remote) + "\n[Console]::WriteLine((Get-Item -LiteralPath $path).Length)", timeout=30))
            except (RuntimeError, WinRMError, WinRMTransportError, requests.RequestException, OSError, ValueError):
                fault["remote_fixture_bytes"] = None
        record.update(status="failed", fault=fault)
    finally:
        if protocol is not None:
            if command is not None:
                try:
                    protocol.cleanup_command(shell, command)
                except (WinRMError, WinRMTransportError, requests.RequestException, OSError, ValueError) as exc:
                    record["command_cleanup_error"] = type(exc).__name__
            if shell is not None:
                try:
                    protocol.close_shell(shell)
                except (WinRMError, WinRMTransportError, requests.RequestException, OSError, ValueError) as exc:
                    record["shell_cleanup_error"] = type(exc).__name__
        write_json(destination, record)
    print(json.dumps(record, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
