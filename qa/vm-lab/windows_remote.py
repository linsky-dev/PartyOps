"""Win7 使用系统自带 WinRM，经本机回环映射与 WMI UUID 校验执行。"""
from __future__ import annotations

import base64
import hashlib
import io
import json
import ntpath
import re
import time
import warnings
from pathlib import Path


def session(lab, target: str, timeout: int):
    """凭据只来自当前已登记 VM，端点只使用本机端口，逐次复核硬件 UUID。"""
    import winrm

    spec = lab.matrix["targets"][target]
    state = lab.state(target)
    credential = json.loads((lab.vm_dir(target) / "guest-credential.local.json").read_text(encoding="utf-8"))
    if credential["uuid"] != state["uuid"] or not lab.live(state):
        raise RuntimeError("WINRM_GUEST_OWNERSHIP_MISMATCH")
    client = winrm.Session(f"http://127.0.0.1:{spec['winrm_port']}/wsman",
                           auth=(credential["username"], credential["password"]), transport="ntlm",
                           message_encryption="always", operation_timeout_sec=max(5, min(timeout, 60)),
                           read_timeout_sec=max(15, min(timeout, 60) + 10))
    identity = client.run_ps(r"""
[Console]::WriteLine((Get-WmiObject Win32_ComputerSystemProduct).UUID)
[Console]::WriteLine((Join-Path (Get-WmiObject Win32_OperatingSystem).WindowsDirectory 'System32\WindowsPowerShell\v1.0\powershell.exe'))
""")
    lines = identity.std_out.decode("utf-8", errors="replace").strip().splitlines()
    if identity.status_code or len(lines) != 2 or lines[0].casefold() != state["uuid"].casefold():
        raise RuntimeError("WINRM_GUEST_HARDWARE_UUID_MISMATCH")
    shell_path = lines[1]
    drive, tail = ntpath.splitdrive(shell_path)
    if (len(drive) != 2 or drive[1] != ":" or not tail.startswith("\\") or shell_path != ntpath.normpath(shell_path)
            or not tail.casefold().endswith(r"\system32\windowspowershell\v1.0\powershell.exe")
            or any(character in shell_path for character in "\"'\r\n")):
        raise RuntimeError("WINRM_GUEST_POWERSHELL_PATH_INVALID")
    # WinRM 2 的 skip_cmd_shell 不搜索 PATH；路径必须来自同一已核验 Guest 的 WindowsDirectory。
    client.partyops_powershell = shell_path
    return client


def transport_error(exc: Exception, stage: str) -> RuntimeError:
    """仅输出阶段和结构化数字错误码；原始 HTTP/XML/异常文本可能携带凭据，不能写入报告。"""
    details = ["stage=" + stage]
    for attribute, label in (("code", "http_status"), ("wsman_fault_code", "wsman_fault"), ("wmierror_code", "wmi_error")):
        value = getattr(exc, attribute, None)
        if isinstance(value, int) and not isinstance(value, bool):
            details.append(label + "=" + str(value) + (f"(0x{value & 0xffffffff:08x})" if attribute != "code" else ""))
    return RuntimeError("WINRM_TRANSPORT_FAILED:" + type(exc).__name__ + "; " + "; ".join(details))


def send_input(protocol, shell: str, command: str, data: bytes, *, end=False):
    """采用 WinRS/Ansible 的可选 End 语义；旧 WinRM 的非末包不发送 End=false 属性。"""
    import xmltodict

    request = {"env:Envelope": protocol.build_wsman_header(
        resource_uri="http://schemas.microsoft.com/wbem/wsman/1/windows/shell/cmd",
        action="http://schemas.microsoft.com/wbem/wsman/1/windows/shell/Send", shell_id=shell)}
    stream = {"@Name": "stdin", "@CommandId": command, "#text": base64.b64encode(data).decode("ascii")}
    if end:
        stream["@End"] = "true"
    request["env:Envelope"]["env:Body"] = {"rsp:Send": {"rsp:Stream": stream}}
    protocol.send_message(xmltodict.unparse(request))


def transfer_path(value: str) -> str:
    """文件传输只面向 Guest 验收目录；不得接受宿主路径或任意远端路径。"""
    path = value.replace("/", "\\")
    root = "C:\\PartyOps-QA\\"
    if (not path.casefold().startswith(root.casefold()) or ".." in path.split("\\")
            or path != ntpath.normpath(path) or "'" in path or ":" in path[2:]):
        raise RuntimeError("WINRM_TRANSFER_OUTSIDE_GUEST_QA_ROOT")
    return path


def local_hash(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def file_script(path: str) -> str:
    path = transfer_path(path)
    return "$path='" + path + "'\n" + r"""
$ancestor=$path
while($ancestor) {
  if(Test-Path -LiteralPath $ancestor) {
    if((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {throw 'WINRM_TRANSFER_REPARSE_REJECTED'}
  }
  $ancestor=Split-Path -Path $ancestor -Parent
}
"""


class WinRMFiles:
    """使用 pywinrm 标准 stdin/stdout 流传文件，避免命令行携带大段包内容。"""

    def __init__(self, lab, target):
        self.lab, self.target = lab, target

    def stream(self, script, *, source=None, destination=None, timeout=3600):
        try:
            return self._stream(script, source=source, destination=destination, timeout=timeout)
        except RuntimeError as exc:
            failure = str(exc)
            # 此时仅执行过只读身份检查，尚未发送业务命令/文件数据；其他阶段绝不自动重发。
            before_command = any('; stage=' + stage + ';' in failure
                                 for stage in ('session-hardware-identity', 'open-shell'))
            if not (failure.startswith('WINRM_TRANSPORT_FAILED:') and before_command
                    and '; http_status=500' in failure):
                raise
            warnings.warn('WINRM_PRE_COMMAND_HTTP_500_RETRY_ONCE_AFTER_2S', RuntimeWarning, stacklevel=2)
            time.sleep(2)
            return self._stream(script, source=source, destination=destination, timeout=timeout)

    def _stream(self, script, *, source=None, destination=None, timeout=3600):
        import requests
        from winrm.exceptions import (
            WinRMError,
            WinRMOperationTimeoutError,
            WinRMTransportError,
        )

        protocol = shell = command = None
        stage = "session-hardware-identity"
        output, errors = io.BytesIO(), io.BytesIO()
        deadline = time.monotonic() + timeout
        payload = (script if source is not None else
                   "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=New-Object Text.UTF8Encoding($false); try {\n" + script + "\n} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }")
        encoded = base64.b64encode(payload.encode("utf-16le")).decode("ascii")
        try:
            stage = "session-hardware-identity"
            client = session(self.lab, self.target, min(timeout, 60))
            protocol = client.protocol
            stage = "open-shell"
            shell = protocol.open_shell(codepage=65001)
            stage = "run-command"
            arguments = ["-NoProfile", "-NonInteractive", "-EncodedCommand", encoded]
            command = protocol.run_command(shell, client.partyops_powershell, arguments,
                                           console_mode_stdin=False, skip_cmd_shell=True)
            if source is not None:
                stage = "send-stdin"
                while True:
                    chunk = source.read(65536)
                    if not chunk:
                        break
                    if time.monotonic() >= deadline:
                        raise RuntimeError("WINRM_FILE_TRANSFER_TIMEOUT")
                    # 借鉴 Ansible WinRM 文件传输：每条 pipeline 输入是有限大小的 Base64 文本行。
                    send_input(protocol, shell, command, base64.b64encode(chunk) + b"\r\n")
            # WinRM 2 在 pipe stdin 模式下需要显式 EOF；纯读取命令也不能留下未关闭的输入流。
            stage = "close-stdin"
            send_input(protocol, shell, command, b"", end=True)
            while True:
                stage = "receive-output"
                if time.monotonic() >= deadline:
                    raise RuntimeError("WINRM_COMMAND_TIMEOUT_CHECK_GUEST_BEFORE_RETRY")
                try:
                    stdout, stderr, status, done = protocol.get_command_output_raw(shell, command)
                except WinRMOperationTimeoutError:
                    continue
                (destination if destination is not None else output).write(stdout)
                errors.write(stderr)
                if done:
                    if status:
                        raise RuntimeError("WINRM_COMMAND_FAILED:" + str(status) + "; " + errors.getvalue()[-3000:].decode("utf-8", errors="replace"))
                    return output.getvalue()
        except RuntimeError:
            raise
        except (WinRMError, WinRMTransportError, requests.RequestException, OSError, ValueError) as exc:
            raise transport_error(exc, stage) from None
        finally:
            if protocol is not None:
                if command is not None:
                    try:
                        protocol.cleanup_command(shell, command)
                    except (WinRMError, WinRMTransportError, requests.RequestException, OSError, ValueError) as exc:
                        warnings.warn("WINRM_COMMAND_CLEANUP_FAILED; " + str(transport_error(exc, "cleanup-command")), RuntimeWarning, stacklevel=2)
                if shell is not None:
                    try:
                        protocol.close_shell(shell)
                    except (WinRMError, WinRMTransportError, requests.RequestException, OSError, ValueError) as exc:
                        warnings.warn("WINRM_SHELL_CLEANUP_FAILED; " + str(transport_error(exc, "close-shell")), RuntimeWarning, stacklevel=2)

    def powershell(self, script, timeout=600):
        return self.stream(script, timeout=timeout).decode("utf-8-sig", errors="replace").strip()

    def put(self, local: Path, remote: str, expected: str, timeout=3600):
        if local_hash(local) != expected:
            raise RuntimeError("PACKAGE_CHANGED_BEFORE_WINRM_TRANSFER")
        script = "begin {\n$ErrorActionPreference='Stop'\ntry {\n" + file_script(remote) + r"""
$directory=Split-Path -Path $path -Parent
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$file=New-Object IO.FileStream($path,[IO.FileMode]::Create,[IO.FileAccess]::Write)
}catch{[Console]::Error.WriteLine($_.Exception.Message);exit 1}
}
process {
try{foreach($line in $input){$bytes=[Convert]::FromBase64String($line);$file.Write($bytes,0,$bytes.Length)}}catch{$file.Close();[Console]::Error.WriteLine($_.Exception.Message);exit 1}
}
end {
try {
$file.Close()
$file=[IO.File]::OpenRead($path)
try {$hash=[Security.Cryptography.SHA256]::Create();[Console]::WriteLine([BitConverter]::ToString($hash.ComputeHash($file)).Replace('-','').ToLowerInvariant())} finally {$file.Dispose();$hash.Clear()}
}catch{[Console]::Error.WriteLine($_.Exception.Message);exit 1}
}
"""
        with local.open("rb") as source:
            actual = self.stream(script, source=source, timeout=timeout).decode("utf-8-sig").strip()
        if actual != expected:
            raise RuntimeError("WINDOWS_GUEST_PACKAGE_HASH_MISMATCH")

    def get(self, remote: str, local: Path, timeout=3600):
        script = file_script(remote) + r"""
$file=[IO.File]::OpenRead($path)
$outputStream=[Console]::OpenStandardOutput()
$buffer=New-Object byte[] 65536
try {while(($count=$file.Read($buffer,0,$buffer.Length)) -gt 0) {$outputStream.Write($buffer,0,$count)}} finally {$file.Dispose()}
"""
        with local.open("wb") as output:
            self.stream(script, destination=output, timeout=timeout)
        actual = self.powershell(file_script(remote) + r"""
$file=[IO.File]::OpenRead($path)
try {$hash=[Security.Cryptography.SHA256]::Create(); [BitConverter]::ToString($hash.ComputeHash($file)).Replace('-','').ToLowerInvariant()} finally {$file.Dispose();$hash.Clear()}
""")
        if local_hash(local) != actual:
            raise RuntimeError("WINRM_DOWNLOADED_EVIDENCE_HASH_MISMATCH")


def execute(lab, target: str, command: str, timeout: int, log: Path | None = None) -> str:
    import requests
    from winrm.exceptions import WinRMError, WinRMTransportError

    # 只拆解实验室生成的完整固定格式；不把任意 cmd 文本当作 PowerShell 重写。
    # WinRM 默认 cmd.exe 受 8191 字符限制，完整身份探针须用已核验绝对路径直接执行。
    encoded = re.fullmatch(r"powershell\.exe -NoProfile(?: -NonInteractive)? -EncodedCommand ([A-Za-z0-9+/]+={0,2})", command, re.IGNORECASE)
    if encoded:
        try:
            script = base64.b64decode(encoded.group(1), validate=True).decode("utf-16le")
        except (ValueError, UnicodeError) as exc:
            raise RuntimeError("WINRM_ENCODED_POWERSHELL_INVALID") from exc
        try:
            output = WinRMFiles(lab, target).powershell(script, timeout=timeout)
        except RuntimeError as exc:
            if log:
                log.parent.mkdir(parents=True, exist_ok=True)
                log.write_text(str(exc), encoding="utf-8")
            raise
        if log:
            log.parent.mkdir(parents=True, exist_ok=True)
            log.write_text(output, encoding="utf-8")
        return output
    stage = "session-hardware-identity"
    try:
        client = session(lab, target, timeout)
        stage = "execute-command"
        result = client.run_cmd("cmd.exe", ["/d", "/s", "/c", command])
    except RuntimeError:
        raise
    except (WinRMError, WinRMTransportError, requests.RequestException, OSError, ValueError) as exc:
        raise transport_error(exc, stage) from None
    output = result.std_out.decode("utf-8", errors="replace")
    error = result.std_err.decode("utf-8", errors="replace")
    if log:
        log.parent.mkdir(parents=True, exist_ok=True)
        log.write_text(output + error, encoding="utf-8")
    if result.status_code:
        raise RuntimeError(f"WINRM_COMMAND_FAILED:{result.status_code}; {error[-500:]}")
    return output
