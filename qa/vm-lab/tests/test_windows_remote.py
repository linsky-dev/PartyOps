"""Win7 原版 WinRM 安装传输边界，不连接 Guest，不执行安装器。"""
from __future__ import annotations

import base64
import hashlib
import importlib.util
import io
import json
from pathlib import Path
from types import SimpleNamespace

import pytest
import windows_remote


@pytest.mark.parametrize('stage', ['session-hardware-identity', 'open-shell'])
def test_http_500_retries_only_before_product_command(monkeypatch, stage):
    client = windows_remote.WinRMFiles(None, 'win7')
    calls = []
    waits = []
    def execute(*args, **kwargs):
        calls.append((args, kwargs))
        if len(calls) == 1:
            raise RuntimeError('WINRM_TRANSPORT_FAILED:WSManFaultError; stage=' + stage + '; http_status=500')
        return b'verified'
    monkeypatch.setattr(client, '_stream', execute)
    monkeypatch.setattr(windows_remote.time, 'sleep', waits.append)
    with pytest.warns(RuntimeWarning, match='RETRY_ONCE'):
        assert client.stream('product-command') == b'verified'
    assert len(calls) == 2 and calls[0] == calls[1] and waits == [2]


@pytest.mark.parametrize('stage', ['run-command', 'send-stdin', 'receive-output'])
def test_possible_product_execution_is_never_retried(monkeypatch, stage):
    client = windows_remote.WinRMFiles(None, 'win7')
    calls = []
    def execute(*args, **kwargs):
        calls.append(1)
        raise RuntimeError('WINRM_TRANSPORT_FAILED:WSManFaultError; stage=' + stage + '; http_status=500')
    monkeypatch.setattr(client, '_stream', execute)
    with pytest.raises(RuntimeError, match='TRANSPORT_FAILED'):
        client.stream('business-write')
    assert calls == [1]


def test_second_pre_command_failure_is_returned(monkeypatch):
    client = windows_remote.WinRMFiles(None, 'win7')
    calls = []
    def execute(*args, **kwargs):
        calls.append(1)
        raise RuntimeError('WINRM_TRANSPORT_FAILED:WSManFaultError; stage=open-shell; http_status=500')
    monkeypatch.setattr(client, '_stream', execute)
    monkeypatch.setattr(windows_remote.time, 'sleep', lambda _: None)
    with pytest.warns(RuntimeWarning, match='RETRY_ONCE'), pytest.raises(RuntimeError, match='TRANSPORT_FAILED'):
        client.stream('business-write')
    assert calls == [1, 1]


@pytest.fixture
def installer():
    path = Path(__file__).resolve().parents[1] / "scripts/exercise-windows-install.py"
    spec = importlib.util.spec_from_file_location("win7_installer_test", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@pytest.fixture
def lab(tmp_path):
    directory = tmp_path / "vm"
    directory.mkdir()
    (directory / "guest-credential.local.json").write_text(json.dumps({"uuid": "owned-uuid", "username": "qa", "password": "fixture-private"}))
    return SimpleNamespace(matrix={"targets": {"win7": {"winrm_port": 23171}}},
                           state=lambda _: {"uuid": "owned-uuid"}, live=lambda _: True,
                           vm_dir=lambda _: directory)


def test_winrm_session_uses_encrypted_loopback_and_checks_hardware_before_commands(lab, monkeypatch):
    import winrm

    calls = []
    client = SimpleNamespace(run_ps=lambda command: calls.append(command) or
                             SimpleNamespace(status_code=0, std_out=b"owned-uuid\r\nC:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\r\n"))
    monkeypatch.setattr(winrm, "Session", lambda endpoint, **kwargs: calls.append((endpoint, kwargs)) or client)
    assert windows_remote.session(lab, "win7", 20) is client
    assert calls[0][0] == "http://127.0.0.1:23171/wsman"
    assert calls[0][1]["transport"] == "ntlm" and calls[0][1]["message_encryption"] == "always"
    assert "Win32_ComputerSystemProduct" in calls[1]
    assert client.partyops_powershell == r"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
    client.run_ps = lambda _: SimpleNamespace(status_code=0, std_out=b"other-vm")
    with pytest.raises(RuntimeError, match="WINRM_GUEST_HARDWARE_UUID_MISMATCH"):
        windows_remote.session(lab, "win7", 20)


def test_winrm_does_not_load_credentials_for_another_vm(lab, monkeypatch):
    import winrm

    lab.state = lambda _: {"uuid": "different-uuid"}
    monkeypatch.setattr(winrm, "Session", lambda *args, **kwargs: pytest.fail("不得连接错误的Guest"))
    with pytest.raises(RuntimeError, match="WINRM_GUEST_OWNERSHIP_MISMATCH"):
        windows_remote.session(lab, "win7", 30)


@pytest.mark.parametrize("path", [r"C:\Windows\example.exe", r"D:\PartyOps-QA\install.exe",
                                   r"C:\PartyOps-QA\..\Windows\file", r"C:\PartyOps-QA\x:stream",
                                   r"C:\PartyOps-QA\x'bad", r"\\host\share\file"])
def test_winrm_transfer_rejects_external_paths(path):
    with pytest.raises(RuntimeError, match="WINRM_TRANSFER_OUTSIDE_GUEST_QA_ROOT"):
        windows_remote.transfer_path(path)


def test_winrm_package_uses_bounded_binary_stdin_not_command_arguments(monkeypatch):
    payload = bytes(range(256)) * 1024
    inputs, commands, cleanup = [], [], []
    protocol = SimpleNamespace(
        open_shell=lambda **kwargs: "shell",
        run_command=lambda shell, executable, arguments, **kwargs: commands.append((executable, arguments, kwargs)) or "command",
        send_command_input=lambda shell, command, chunk, **kwargs: inputs.append((chunk, kwargs)),
        get_command_output_raw=lambda *args: (b"remote-result", b"", 0, True),
        cleanup_command=lambda *args: cleanup.append("command"),
        close_shell=lambda *args: cleanup.append("shell"),
    )
    monkeypatch.setattr(windows_remote, "session", lambda *args: SimpleNamespace(protocol=protocol, partyops_powershell=r"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"))
    monkeypatch.setattr(windows_remote, "send_input", lambda protocol, shell, command, data, **kwargs: protocol.send_command_input(shell, command, data, **kwargs))
    result = windows_remote.WinRMFiles(None, "win7").stream("# fixture stream", source=io.BytesIO(payload))
    assert result == b"remote-result"
    assert b"".join(base64.b64decode(item[0]) for item in inputs if item[0]) == payload
    assert max(len(item[0]) for item in inputs) <= 87386
    assert inputs[-1] == (b"", {"end": True})
    assert commands[0][0] == r"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
    assert commands[0][2]["skip_cmd_shell"] is True
    assert commands[0][1][-2] == "-EncodedCommand"
    assert len(commands[0][1][-1]) < 4096
    assert cleanup == ["command", "shell"]


def test_winrm_readonly_pipe_command_closes_stdin_before_waiting_for_exit(monkeypatch):
    calls = []

    def receive(*args):
        assert calls == [(b"", {"end": True})]
        return b"readonly", b"", 0, True

    protocol = SimpleNamespace(
        open_shell=lambda **kwargs: "shell",
        run_command=lambda *args, **kwargs: "command",
        send_command_input=lambda shell, command, chunk, **kwargs: calls.append((chunk, kwargs)),
        get_command_output_raw=receive, cleanup_command=lambda *args: None, close_shell=lambda *args: None,
    )
    monkeypatch.setattr(windows_remote, "session", lambda *args: SimpleNamespace(
        protocol=protocol, partyops_powershell=r"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"))
    monkeypatch.setattr(windows_remote, "send_input", lambda protocol, shell, command, data, **kwargs: protocol.send_command_input(shell, command, data, **kwargs))
    assert windows_remote.WinRMFiles(None, "win7").powershell("[Console]::WriteLine('readonly')") == "readonly"


def test_winrm_put_rejects_guest_hash_mismatch_and_never_reads_entire_package(tmp_path, monkeypatch):
    path = tmp_path / "package.exe"
    path.write_bytes(b"fixture installer")
    client = windows_remote.WinRMFiles(None, "win7")
    monkeypatch.setattr(Path, "read_bytes", lambda _: pytest.fail("大文件不得整包载入内存"))
    monkeypatch.setattr(client, "stream", lambda *args, **kwargs: b"0" * 64)
    with pytest.raises(RuntimeError, match="WINDOWS_GUEST_PACKAGE_HASH_MISMATCH"):
        client.put(path, r"C:\PartyOps-QA\incoming\package.exe", hashlib.sha256(b"fixture installer").hexdigest())


@pytest.mark.parametrize("version,path,message", [
    ("1.4.5-rc.5", "C:\\PartyOps QA\\中文 程序\\", "WINDOWS_INSTALLED_VERSION_MISMATCH"),
    ("1.4.5-rc.6", "C:\\OtherApp", "WINDOWS_INSTALLED_DIRECTORY_MISMATCH"),
])
def test_legacy_install_requires_exact_registry_version_and_directory(installer, version, path, message):
    with pytest.raises(RuntimeError, match=message):
        installer.verify_legacy_install({"registrations": [{"version": version, "install_dir": path}]}, "1.4.5-rc.6", "windows7_amd64", "a" * 64)


def test_legacy_scripts_do_not_require_modern_powershell(installer):
    script = installer.legacy_install_script("PartyOps_1.4.5-rc.6_windows7_amd64.exe", "a" * 64)
    for unsupported in ("Get-CimInstance", "Get-FileHash", "ConvertFrom-Json", "ConvertTo-Json", "[ordered]", " -Raw", ".StartType", "$hash.Dispose()"):
        assert unsupported not in script
    assert "Get-WmiObject" in script and "JavaScriptSerializer" in script
    assert "standard_user_first_start_verified=$false" in script
    encoded = base64.b64encode(script.encode("utf-16le"))
    assert len(encoded) < 16000
    with pytest.raises(RuntimeError, match="WIN7_PACKAGE_FILENAME_INVALID"):
        installer.legacy_install_script("PartyOps.exe'; Write-Output injected", "a" * 64)


def test_direct_win7_install_stops_before_transfer_when_transport_preparation_fails(installer, tmp_path, monkeypatch):
    def blocked(*args):
        raise RuntimeError("WINRM_PREPARATION_INSTALLER_ACTIVE_OR_UNKNOWN")

    monkeypatch.setattr(installer, "prepare_win7_transport", blocked)
    monkeypatch.setattr(installer, "WinRMFiles", lambda *args: pytest.fail("准备失败后不得连接安装或上传载荷"))
    with pytest.raises(RuntimeError, match="INSTALLER_ACTIVE"):
        installer.install_legacy(None, "win7-x64", {}, {}, tmp_path)


@pytest.mark.parametrize("field,value,message", [("installed_pe_machine", 0x14C, "WINDOWS_INSTALLED_PE_ARCHITECTURE_MISMATCH"),
                                                  ("installer_sha256", "b" * 64, "WINDOWS_GUEST_PACKAGE_HASH_MISMATCH")])
def test_win7_registry_version_cannot_hide_wrong_pe_or_hash(installer, field, value, message):
    result = {"registrations": [{"version": "1.4.5-rc.6", "install_dir": "C:\\PartyOps QA\\中文 程序\\"}],
              "installed_pe_machine": 0x8664, "installer_sha256": "a" * 64}
    installer.verify_legacy_install(result, "1.4.5-rc.6", "windows7_amd64", "a" * 64)
    result[field] = value
    with pytest.raises(RuntimeError, match=message):
        installer.verify_legacy_install(result, "1.4.5-rc.6", "windows7_amd64", "a" * 64)


def test_winrm_log_download_requires_guest_hash(tmp_path, monkeypatch):
    path = tmp_path / "install.log"
    client = windows_remote.WinRMFiles(None, "win7")
    monkeypatch.setattr(client, "stream", lambda script, destination, timeout: destination.write(b"received log"))
    monkeypatch.setattr(client, "powershell", lambda script: "a" * 64)
    with pytest.raises(RuntimeError, match="WINRM_DOWNLOADED_EVIDENCE_HASH_MISMATCH"):
        client.get(r"C:\PartyOps-QA\install.log", path)


def test_winrm_transport_error_does_not_expose_credentials(lab, monkeypatch):
    import requests

    def fail_session(*args):
        raise requests.ConnectionError("fixture-private must not enter evidence")
    monkeypatch.setattr(windows_remote, "session", fail_session)
    with pytest.raises(RuntimeError, match="^WINRM_TRANSPORT_FAILED:ConnectionError; stage=session-hardware-identity$"):
        windows_remote.WinRMFiles(lab, "win7").powershell("Write-Output fixture")


def test_wsman_error_retains_numeric_fault_and_stage_but_never_raw_response_or_reason():
    from winrm.exceptions import WSManFaultError

    error = WSManFaultError(500, "fixture-private", "secret HTTP response", "fixture-private reason",
                           wsman_fault_code=2147942402, wmierror_code=8)
    formatted = str(windows_remote.transport_error(error, "run-command"))
    assert "stage=run-command" in formatted
    assert "http_status=500" in formatted
    assert "wsman_fault=2147942402(0x80070002)" in formatted
    assert "wmi_error=8(0x00000008)" in formatted
    assert "fixture-private" not in formatted and "secret" not in formatted


def test_non_winrmerror_http_exception_is_also_sanitized(lab, monkeypatch):
    from winrm.exceptions import WinRMTransportError

    def fail(*args):
        raise WinRMTransportError("http", 401, "fixture-private response body")

    monkeypatch.setattr(windows_remote, "session", fail)
    with pytest.raises(RuntimeError) as error:
        windows_remote.WinRMFiles(lab, "win7").powershell("Write-Output fixture")
    assert str(error.value) == "WINRM_TRANSPORT_FAILED:WinRMTransportError; stage=session-hardware-identity; http_status=401"


@pytest.mark.parametrize("extra_flags", ["", " -NonInteractive"])
def test_long_encoded_powershell_uses_direct_stream_without_rewriting_script(monkeypatch, tmp_path, extra_flags):
    script = "# 中文长身份探针\n" * 500 + "[Console]::WriteLine('identity')"
    command = "powershell.exe -NoProfile" + extra_flags + " -EncodedCommand " + base64.b64encode(script.encode("utf-16le")).decode("ascii")
    assert len(command) > 8191
    calls = []
    monkeypatch.setattr(windows_remote.WinRMFiles, "powershell", lambda self, actual, timeout: calls.append((self.target, actual, timeout)) or "identity")
    monkeypatch.setattr(windows_remote, "session", lambda *args: pytest.fail("长命令不得走 cmd.exe"))
    log = tmp_path / "probe.log"
    assert windows_remote.execute(None, "win7", command, 45, log) == "identity"
    assert calls == [("win7", script, 45)]
    assert log.read_text(encoding="utf-8") == "identity"


@pytest.mark.parametrize("encoded", ["A", "YQ=="])
def test_invalid_encoded_powershell_rejected_before_connect(monkeypatch, encoded):
    monkeypatch.setattr(windows_remote, "session", lambda *args: pytest.fail("编码无效不得连接"))
    with pytest.raises(RuntimeError, match="WINRM_ENCODED_POWERSHELL_INVALID"):
        windows_remote.execute(None, "win7", "powershell.exe -NoProfile -EncodedCommand " + encoded, 45)


def test_cmd_suffix_is_not_silently_dropped_by_powershell_fast_path(monkeypatch):
    original = "powershell.exe -NoProfile -EncodedCommand YQA= & echo fixture"
    calls = []
    client = SimpleNamespace(run_cmd=lambda executable, arguments: calls.append((executable, arguments)) or SimpleNamespace(std_out=b"fixture", std_err=b"", status_code=0))
    monkeypatch.setattr(windows_remote, "session", lambda *args: client)
    monkeypatch.setattr(windows_remote.WinRMFiles, "powershell", lambda *args, **kwargs: pytest.fail("不得截断 cmd 尾部并重写语义"))
    assert windows_remote.execute(None, "win7", original, 45) == "fixture"
    assert calls == [("cmd.exe", ["/d", "/s", "/c", original])]


@pytest.mark.parametrize("shell_path", ["powershell.exe", r"\\host\share\powershell.exe",
                                       r"C:\Windows\..\System32\WindowsPowerShell\v1.0\powershell.exe",
                                       r"C:\Windows\System32\other.exe"])
def test_winrm_rejects_unbound_or_relative_powershell_paths(lab, monkeypatch, shell_path):
    import winrm

    client = SimpleNamespace(run_ps=lambda command: SimpleNamespace(
        status_code=0, std_out=("owned-uuid\n" + shell_path + "\n").encode("utf-8")))
    monkeypatch.setattr(winrm, "Session", lambda *args, **kwargs: client)
    with pytest.raises(RuntimeError, match="WINRM_GUEST_POWERSHELL_PATH_INVALID"):
        windows_remote.session(lab, "win7", 20)


def test_win7_install_script_parses_without_executing_installer(installer, tmp_path):
    import os
    import subprocess

    if os.name != "nt":
        pytest.skip("只在 Windows 宿主调用 PowerShell 官方语法解析器")
    path = tmp_path / "legacy-install.ps1"
    path.write_text(installer.legacy_install_script("PartyOps_1.4.5-rc.6_windows7_x86.exe", "a" * 64), encoding="utf-8-sig")
    escaped = str(path).replace("'", "''")
    command = "$tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile('" + escaped + "',[ref]$tokens,[ref]$errors);if($errors){$errors|Out-String|Write-Output;exit 1}"
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                             base64.b64encode(command.encode("utf-16le")).decode("ascii")],
                            capture_output=True, text=True, timeout=30, check=False)
    assert result.returncode == 0, result.stdout + result.stderr


@pytest.mark.parametrize("expected_exit", [0, 7])
def test_legacy_install_process_owns_handle_and_keeps_real_exit_code(installer, tmp_path, expected_exit):
    """只提取进程等待片段运行系统 cmd，绝不运行安装器或其余安装脚本。"""
    import os
    import subprocess

    if os.name != "nt":
        pytest.skip("需要 Windows 系统 cmd 的真实退出码")
    script = installer.legacy_install_script("PartyOps_1.4.5-rc.6_windows7_x86.exe", "a" * 64)
    start = script.index("$info=New-Object Diagnostics.ProcessStartInfo")
    finish = script.index("$services=@", start)
    process_script = script[start:finish].replace("WaitForExit(6600000)", "WaitForExit(10000)")
    command = "$ErrorActionPreference='Stop';$installer=Join-Path $env:SystemRoot 'System32\\cmd.exe';$arguments='/d /c exit " + str(expected_exit) + "'\n" + process_script + "\n[Console]::WriteLine($exitCode)"
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                             base64.b64encode(command.encode("utf-16le")).decode("ascii")],
                            capture_output=True, text=True, timeout=30, check=False)
    assert result.returncode == 0, result.stdout + result.stderr
    assert result.stdout.strip() == str(expected_exit)


def test_winrm_send_end_attribute_is_absent_until_last_message():
    import xmltodict

    messages = []
    protocol = SimpleNamespace(build_wsman_header=lambda **kwargs: {}, send_message=messages.append)
    windows_remote.send_input(protocol, "shell", "command", b"fixture")
    windows_remote.send_input(protocol, "shell", "command", b"", end=True)
    first = xmltodict.parse(messages[0])["env:Envelope"]["env:Body"]["rsp:Send"]["rsp:Stream"]
    last = xmltodict.parse(messages[1])["env:Envelope"]["env:Body"]["rsp:Send"]["rsp:Stream"]
    assert "@End" not in first
    assert base64.b64decode(first["#text"]) == b"fixture"
    assert last["@End"] == "true"


def test_utf8_bom_hash_output_is_normalized_but_empty_file_hash_is_rejected(tmp_path, monkeypatch):
    path = tmp_path / "fixture.bin"
    content = b"not empty"
    path.write_bytes(content)
    expected = hashlib.sha256(content).hexdigest()
    client = windows_remote.WinRMFiles(None, "win7")
    monkeypatch.setattr(client, "stream", lambda *args, **kwargs: b"\xef\xbb\xbf" + expected.encode("ascii") + b"\r\n")
    client.put(path, r"C:\PartyOps-QA\fixture.bin", expected)
    monkeypatch.setattr(client, "stream", lambda *args, **kwargs: b"\xef\xbb\xbf" + hashlib.sha256(b"").hexdigest().encode("ascii") + b"\r\n")
    with pytest.raises(RuntimeError, match="WINDOWS_GUEST_PACKAGE_HASH_MISMATCH"):
        client.put(path, r"C:\PartyOps-QA\fixture.bin", expected)
