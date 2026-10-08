"""调用原排版源码编译出的无窗口宿主。

本模块只负责本机进程协议、取消和结果校验，不包含任何排版规则。各平台
正式包必须携带与当前架构匹配的源码宿主；宿主缺失时宁可明确失败，也不能
静默回退到格式行为不同的实现。
"""

from __future__ import annotations

import json
import os
import platform
import secrets
import subprocess
import sys
import time
import uuid
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

from .official_format import OfficialFormatError, _system_command_environment

ProgressCallback = Callable[[int, str], None]
CancelCallback = Callable[[], bool]
HOST_TIMEOUT_SECONDS = 30 * 60
MAC_CLEANUP_MARKER = ".mac-cleanup-unconfirmed.json"


def _confirm_mac_cleanup(control: Path, task_id: str, response: dict[str, Any]) -> bool:
    """仅当次正式任务回执确认后移动标记；未知/错绑定回执保留工作副本。"""
    state = response.get("mac_task")
    if not isinstance(state, dict) or state.get("task_id") != task_id or any(state.get(key) is not True for key in ("cleanup_confirmed", "lease_released", "registration_owned", "capability_revoked")):
        return False
    marker = control / MAC_CLEANUP_MARKER
    try:
        binding = json.loads(marker.read_text(encoding="utf-8"))
        if binding != {"schema": 1, "task_id": task_id}:
            return False
        confirmed = control / (".mac-cleanup-confirmed-" + task_id + ".json")
        if confirmed.exists():
            return False
        marker.rename(confirmed)
        return True
    except (OSError, ValueError):
        return False
_LINUX_WPS_ROOTS = tuple(Path(value) for value in (
    "/opt/kingsoft/wps-office/office6", "/usr/lib/office6", "/usr/local/lib/office6",
))


def _source_host_environment() -> dict[str, str]:
    """隔离冻结运行库，并发现已安装 WPS 的私有 Qt 库，不修改父进程。"""
    environment = _system_command_environment()
    if sys.platform == "darwin":
        # 仅本次自包含formatter子进程，避免继承Mono配置覆盖已嵌入的真实dllmap。
        environment["MONO_CONFIG"] = "/dev/null"
        environment.pop("MONO_ENV_OPTIONS", None)
        environment.pop("MONO_BUNDLED_OPTIONS", None)
    if sys.platform != "linux":
        return environment
    explicit = environment.get("PARTYOPS_WPS_RPC_LIBRARY", "").strip()
    if explicit:
        library = Path(explicit)
        office = library.parent if library.is_file() else None
    else:
        # 与源码宿主 WpsInstallation.Resolve 的既有安装探测顺序保持一致。
        office = next((root for root in _LINUX_WPS_ROOTS if (root / "wps").is_file()
                       and any((root / name).is_file() for name in
                               ("librpcwpsapi_wpsqt.so", "librpcwpsapi_sysqt5.so"))), None)
    if office is not None:
        paths = [str(path) for path in (office, office / "lib") if path.is_dir()]
        original = environment.get("LD_LIBRARY_PATH", "")
        if original:
            paths.append(original)
        environment["LD_LIBRARY_PATH"] = ":".join(paths)
    return environment


@dataclass(frozen=True)
class SourceHostOutput:
    path: Path
    host_display_name: str
    message: str


def _executable_names() -> tuple[str, ...]:
    if sys.platform == "darwin":
        return ("partyops-document-formatter-host",)
    if os.name != "nt":
        return ("partyops-document-formatter-host",)
    machine = platform.machine().lower()
    if machine in {"x86", "i386", "i686"} or sys.maxsize <= 2**32:
        return ("PartyOps.DocumentFormatter.Host-x86.exe", "PartyOps.DocumentFormatter.Host.exe")
    return (
        "PartyOps.DocumentFormatter.Host-x64.exe",
        "PartyOps.DocumentFormatter.Host.exe",
        "PartyOps.DocumentFormatter.Host-x86.exe",
    )


def source_host_candidates() -> tuple[Path, ...]:
    """按正式包、开发环境的顺序返回候选，不搜索 PATH。"""

    configured = os.environ.get("PARTYOPS_DOCUMENT_FORMATTER_HOST", "").strip()
    roots: list[Path] = []
    if configured:
        roots.append(Path(configured).expanduser())

    executable_root = Path(sys.executable).resolve().parent
    module_root = Path(__file__).resolve().parents[2]
    for name in _executable_names():
        roots.extend(
            (
                executable_root / "formatter-host" / name,
                executable_root / name,
                executable_root.parent / "Resources" / "formatter-host" / name,
                module_root / "packaging" / "windows" / "formatter-host" / "bin" / "x64" / "Release" / name,
                module_root / "packaging" / "windows" / "formatter-host" / "bin" / "x86" / "Release" / name,
                module_root / "packaging" / "linux" / "formatter-host" / name,
                module_root / "packaging" / "macos" / "formatter-host" / name,
            )
        )
    return tuple(dict.fromkeys(path.resolve() for path in roots))


def resolve_source_host() -> Path | None:
    return next((candidate for candidate in source_host_candidates() if candidate.is_file()), None)


def _read_progress(path: Path, consumed: int, callback: ProgressCallback) -> int:
    if not path.is_file():
        return consumed
    with path.open("rb") as stream:
        stream.seek(consumed)
        chunk = stream.read()
        consumed = stream.tell()
    for raw_line in chunk.splitlines():
        try:
            event = json.loads(raw_line.decode("utf-8"))
            percent = max(0, min(100, int(event.get("percent", 0))))
            message = str(event.get("message", "正在处理文档"))[:500]
        except (UnicodeDecodeError, json.JSONDecodeError, TypeError, ValueError):
            continue
        callback(percent, message)
    return consumed


def _content_type(path: Path) -> str:
    return {
        ".docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".doc": "application/msword",
        ".pdf": "application/pdf",
        ".txt": "text/plain; charset=utf-8",
        ".png": "image/png",
        ".jpg": "image/jpeg",
        ".jpeg": "image/jpeg",
    }.get(path.suffix.lower(), "application/octet-stream")


def _windows_path_units(path: Path) -> int:
    return len(str(path).encode("utf-16-le")) // 2


def _check_windows_source_paths(source: Path, control: Path, output: Path, suffix: str) -> None:
    """兼容 Win7/.NET/WPS 的传统路径；计入原源码事务临时名，不依赖系统长路径开关。"""
    if sys.platform != "win32":
        return
    # 与锁定原源码 AtomicFileService 的 .<名称>.<32位GUID>.tmp.<扩展名> 一致。
    # 每份文档使用全新的作业目录，不会触发已有文件的递增重名后缀。
    temporary = output / ("." + source.stem + suffix + "." + "0" * 32 + ".tmp.docx")
    longest = max(_windows_path_units(path) for path in (source.resolve(), control / "progress.jsonl", temporary))
    if longest > 259:
        raise OfficialFormatError(
            "SOURCE_FORMATTER_PATH_TOO_LONG",
            "文档临时路径过长",
            f"WPS/Word 兼容工作路径需不超过 259 个 UTF-16 单位（当前 {longest}）。"
            "请将当前用户的 TEMP/TMP 设为同一磁盘内较短且可写的目录，"
            "完全退出并重启 PartyOps 后再试；原文件未改变。",
        )


def run_source_host(
    feature_id: str,
    source: Path,
    workspace: Path,
    options: dict[str, Any],
    *,
    progress: ProgressCallback,
    cancelled: CancelCallback,
) -> tuple[SourceHostOutput, ...]:
    """执行源码宿主并只接收任务目录内、真实存在的输出。"""

    host = resolve_source_host()
    if host is None:
        raise OfficialFormatError(
            "SOURCE_FORMATTER_HOST_MISSING",
            "原排版引擎未就绪",
            "安装包缺少与当前系统匹配的原排版源码宿主；为防止格式偏差，已停止处理。请修复安装 PartyOps。",
        )

    workspace = workspace.resolve()
    control = workspace / ".source-host"
    request_path = control / "request.json"
    response_path = control / "response.json"
    progress_path = control / "progress.jsonl"
    cancel_path = control / "cancel.flag"
    output_directory = workspace / "o"

    compatibility = str(options.get("compatibility_mode", "auto")).strip().lower()
    host_preference = {
        "auto": "wps-preferred",
        "wps": "wps",
        "word": "word",
    }.get(compatibility)
    if host_preference is None:
        raise OfficialFormatError(
            "FORMAT_COMPATIBILITY_INVALID",
            "兼容模式无效",
            "兼容模式只能选择自动、WPS 或 Word。",
        )

    target_format = str(options.get("target_format", "docx")).lower()
    direct_convert = feature_id in {"convert", "pdf-to-word"}
    payload = {
        "source_paths": [str(source.resolve())],
        "output_directory": str(output_directory),
        "feature_id": feature_id,
        "feature_display_name": {
            "format": "一键排版",
            "replace": "一键替换",
            "redheader": "一键套红",
            "rename": "一键命名",
            "convert": "一键转换",
            "pdf-to-word": "PDF 转 Word",
        }[feature_id],
        "output_suffix": {
            "format": "_已排版",
            "replace": "_已替换",
            "redheader": "_已套红",
            "rename": "_已命名",
            "convert": "_已转换",
            "pdf-to-word": "_已转换",
        }[feature_id],
        "host_preference": host_preference,
        "export_docx": direct_convert or target_format == "docx" or feature_id not in {"convert", "pdf-to-word"},
        "export_pdf": not direct_convert and target_format == "pdf",
        "export_txt": not direct_convert and target_format == "txt",
        "options": options,
    }
    _check_windows_source_paths(source, control, output_directory, str(payload["output_suffix"]))
    control.mkdir(parents=True, exist_ok=True)
    output_directory.mkdir(parents=True, exist_ok=True)
    if sys.platform == "darwin":
        # 绑定既有六入口；逐项原生验收另记，不接受任意工具或 QA 模式。
        if feature_id not in {"format", "replace", "redheader", "rename", "convert", "pdf-to-word"}:
            raise OfficialFormatError("MAC_FORMAT_FEATURE_NOT_ACCEPTED", "Mac 功能入口无效", "Intel Mac 仅接受既有六个格式工具。")
        mac_state = control / "mac-state"
        mac_state.mkdir(exist_ok=False)
        payload["mac_task"] = {
            "schema": 1,
            "nonce": secrets.token_urlsafe(32),
            "state_directory": str(mac_state.resolve()),
            "resources": str((host.parent / "wps-formatter-plugin").resolve()),
            "task_id": uuid.uuid4().hex,
        }
        payload["host_preference"] = "wps"
        with (control / MAC_CLEANUP_MARKER).open("x", encoding="utf-8") as stream:
            json.dump({"schema": 1, "task_id": payload["mac_task"]["task_id"]}, stream)
    # Windows 7/x86 随包运行时仍为 Python 3.8；Path.write_text 在该版本
    # 不支持 newline 参数，使用 Path.open 保证请求文件始终采用 LF。
    with request_path.open("w", encoding="utf-8", newline="\n") as stream:
        stream.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")))

    command = [
        str(host),
        "--request",
        str(request_path),
        "--response",
        str(response_path),
        "--progress",
        str(progress_path),
        "--cancel",
        str(cancel_path),
    ]
    creation_flags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
    # 冻结主服务自带旧版 libstdc++，不能泄漏给外部 WPS 引擎。
    child_environment = _source_host_environment()
    # mkbundle 的 AppDomain.BaseDirectory 在部分 Mono 版本中会跟随 cwd。
    # 处理任务必须在私有控制目录运行，因此显式把 WPS 槽位表锁到随包宿主
    # 相邻位置，避免安装后因当前目录不同而出现“本机测试通过、用户失败”。
    if sys.platform == "darwin":
        child_environment.pop("PARTYOPS_WPS_VTABLE_MAP", None)
        child_environment["XDG_DATA_HOME"] = payload["mac_task"]["state_directory"]
        child_environment["PARTYOPS_MAC_QUOTE_FONT"] = "Times New Roman"
    else:
        child_environment["PARTYOPS_WPS_VTABLE_MAP"] = str(host.parent / "word-vtable-map.json")
    process = subprocess.Popen(
        command,
        cwd=str(control),
        env=child_environment,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=creation_flags,
    )
    started = time.monotonic()
    consumed = 0
    cancel_written = False
    timeout_cancelled_at = None
    while process.poll() is None:
        consumed = _read_progress(progress_path, consumed, progress)
        if cancelled() and not cancel_written:
            cancel_path.touch(exist_ok=True)
            cancel_written = True
        if time.monotonic() - started > HOST_TIMEOUT_SECONDS:
            if sys.platform == "darwin" and timeout_cancelled_at is None:
                cancel_path.touch(exist_ok=True)
                cancel_written = True
                timeout_cancelled_at = time.monotonic()
            if sys.platform == "darwin" and time.monotonic() - timeout_cancelled_at < 120:
                time.sleep(0.1)
                continue
            process.kill()
            process.wait(timeout=10)
            raise OfficialFormatError(
                "SOURCE_FORMATTER_TIMEOUT",
                "原排版引擎处理超时",
                "文档引擎处理超时；已停止本次宿主。Mac 任务的文档清理状态未知，请在 WPS 中核对己方工作副本。" if sys.platform == "darwin" else "文档引擎在 30 分钟内未完成；已停止本次任务，源文件未改变。",
            )
        time.sleep(0.1)
    _read_progress(progress_path, consumed, progress)

    if not response_path.is_file():
        raise OfficialFormatError(
            "SOURCE_FORMATTER_NO_RESPONSE",
            "原排版引擎未返回结果",
            f"文档宿主异常退出（代码 {process.returncode}）；源文件未改变。",
        )
    try:
        response = json.loads(response_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise OfficialFormatError(
            "SOURCE_FORMATTER_RESPONSE_INVALID",
            "原排版引擎结果无效",
            "文档宿主返回了无法校验的结果；源文件未改变。",
        ) from exc
    mac_clean = sys.platform != "darwin" or _confirm_mac_cleanup(control, payload["mac_task"]["task_id"], response)
    if response.get("fatal"):
        raise OfficialFormatError(
            "SOURCE_FORMATTER_FATAL",
            "原排版引擎无法启动",
            str(response.get("message", "文档宿主发生未知错误。"))[:1000] + (" Mac 清理未确认，工作副本已保留，请在 WPS 中人工核对。" if not mac_clean else ""),
        )
    if sys.platform == "darwin":
        if not mac_clean:
            raise OfficialFormatError("MAC_FORMAT_CLEANUP_UNCONFIRMED", "Mac 任务清理未确认", "请保留本次工作副本并在 WPS 中核对；宿主未确认文档与加载项条目已释放。")
    jobs = response.get("jobs")
    if not isinstance(jobs, list) or len(jobs) != 1 or not jobs[0].get("success"):
        job = jobs[0] if isinstance(jobs, list) and jobs else {}
        code = "FORMAT_JOB_CANCELLED" if job.get("cancelled") or cancel_written else "SOURCE_FORMATTER_FAILED"
        raise OfficialFormatError(
            code,
            "任务已取消" if code == "FORMAT_JOB_CANCELLED" else "原排版引擎处理失败",
            str(job.get("message", "文档引擎未完成处理。"))[:2000],
        )

    outputs: list[SourceHostOutput] = []
    output_root = output_directory.resolve()
    for raw_path in jobs[0].get("output_paths", []):
        path = Path(str(raw_path)).resolve()
        if output_root not in path.parents or not path.is_file() or path.stat().st_size <= 0:
            raise OfficialFormatError(
                "SOURCE_FORMATTER_OUTPUT_INVALID",
                "原排版引擎输出无效",
                "文档宿主返回的文件不在私有任务目录内或文件为空。",
            )
        outputs.append(
            SourceHostOutput(
                path=path,
                host_display_name=str(jobs[0].get("host_display_name", ""))[:500],
                message=str(jobs[0].get("message", "处理完成"))[:1000],
            )
        )
    if not outputs:
        raise OfficialFormatError(
            "SOURCE_FORMATTER_OUTPUT_MISSING",
            "原排版引擎没有生成文件",
            "文档引擎报告成功但未生成可下载结果；源文件未改变。",
        )
    return tuple(outputs)


__all__ = [
    "SourceHostOutput",
    "_content_type",
    "resolve_source_host",
    "run_source_host",
    "source_host_candidates",
]
