"""Windows 提权参数传递：脚本固定，路径与参数仅通过编码数据传递。"""

from __future__ import annotations

import base64
import json
import subprocess


def elevation_command(executable: str, arguments: list[str]) -> list[str]:
    payload = base64.b64encode(json.dumps({
        "executable": executable,
        "arguments": subprocess.list2cmdline(arguments),
    }, ensure_ascii=False).encode("utf-8")).decode("ascii")
    script = (
        "$ErrorActionPreference='Stop'; "
        "$d=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"
        + payload + "')) | ConvertFrom-Json; "
        "try { $process=Start-Process -FilePath $d.executable "
        "-ArgumentList $d.arguments -Verb RunAs -Wait -PassThru; "
        "if($null -eq $process){throw '管理员进程未创建'} "
        "[Console]::Out.WriteLine('PARTYOPS_ELEVATION_CHILD_EXIT'); exit $process.ExitCode "
        "} catch { $native=0; $e=$_.Exception; "
        "while($null -ne $e){if($e.NativeErrorCode){$native=$e.NativeErrorCode; break}; $e=$e.InnerException}; "
        "if(-not $native){$native=$_.Exception.HResult -band 0xffff}; "
        "[Console]::Error.WriteLine('PARTYOPS_ELEVATION_START_FAILED'); "
        "[Console]::Error.WriteLine($_.Exception.Message); exit $native }"
    )
    return ["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand",
            base64.b64encode(script.encode("utf-16-le")).decode("ascii")]


def run_elevated(executable: str, arguments: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        elevation_command(executable, arguments), check=False, capture_output=True,
        text=True, timeout=180, creationflags=subprocess.CREATE_NO_WINDOW,
    )
