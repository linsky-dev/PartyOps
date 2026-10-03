#!/usr/bin/env python3
"""在真实 WPS 中验证 PartyOps 静默 JSAPI 桥接能力。

该探针只验证跨平台适配的最小硬前提，不声称六项排版功能已经移植完成。
它复制输入 DOCX、通过 WPS 加载项把目标段落首行缩进设为 2 字符，再从保存
后的 OOXML 验证 ``w:firstLineChars=200``。Linux/macOS 的安装包门禁只接受
目标平台自身的证据；Windows 入口仅用于在开发机验证同一 JSAPI 协议链。
任何一步无法证明都会失败闭锁。
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import secrets
import shutil
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from datetime import datetime
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from xml.etree import ElementTree
from zoneinfo import ZoneInfo

PLUGIN_NAME = "PartyOpsDocumentFormatterBridge"
WPS_RELAY = "http://127.0.0.1:58890"
WORD_NS = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"


def _json_bytes(value: Any) -> bytes:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _post(path: str, body: bytes, timeout: float) -> str:
    request = urllib.request.Request(
        WPS_RELAY + path,
        data=body,
        headers={"Content-Type": "application/json; charset=utf-8"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=timeout) as response:
        if response.status != 200:
            raise RuntimeError(f"[WPS_RELAY_HTTP_FAILED] HTTP {response.status}")
        return response.read(4 * 1024 * 1024).decode("utf-8", errors="strict")


def _network_error_summary(error: BaseException) -> str:
    if isinstance(error, urllib.error.HTTPError):
        try:
            body = error.read(2048).decode("utf-8", errors="replace")
        except OSError:
            body = ""
        compact = " ".join(body.split())[:500]
        return f"HTTP {error.code}" + (f" {compact}" if compact else "")
    if isinstance(error, urllib.error.URLError):
        return f"URLError {error.reason}"
    return type(error).__name__


def _relay_version() -> str:
    try:
        return _post("/version", b"{}", 2.0).strip()
    except (OSError, TimeoutError, UnicodeError, urllib.error.URLError):
        return ""


def _start_relay(platform_name: str) -> None:
    uri = "ksoWPSCloudSvr://start=RelayHttpServer"
    if platform_name == "windows":
        startfile = getattr(os, "startfile", None)
        if startfile is None:
            raise RuntimeError("[WPS_RELAY_START_FAILED] 当前 Python 不支持 Windows URI 启动。")
        try:
            startfile(uri)
            return
        except OSError as exc:
            raise RuntimeError("[WPS_RELAY_START_FAILED] 无法启动 WPS 本机桥接服务。") from exc
    if platform_name == "macos":
        command = ["open", "-g", uri]
    else:
        command = ["xdg-open", uri]
    try:
        subprocess.run(
            command,
            check=False,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=10,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise RuntimeError("[WPS_RELAY_START_FAILED] 无法启动 WPS 本机桥接服务。") from exc


def wait_for_relay(platform_name: str, *, start: bool = True) -> str:
    version = _relay_version()
    if not version and start:
        _start_relay(platform_name)
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            time.sleep(0.25)
            version = _relay_version()
            if version:
                break
    if not version:
        raise RuntimeError(
            "[WPS_RELAY_UNAVAILABLE] 未发现支持加载项调用的 WPS 本机服务；"
            "目标系统必须安装并启动兼容版本 WPS。"
        )
    return version


class _PluginHandler(SimpleHTTPRequestHandler):
    server_version = "PartyOpsWpsProbe/1"

    def __init__(
        self,
        *args: Any,
        directory: str,
        config: dict[str, Any],
        **kwargs: Any,
    ) -> None:
        self._config = config
        super().__init__(*args, directory=directory, **kwargs)

    def log_message(self, _format: str, *_args: Any) -> None:
        return

    def _record_request(self) -> None:
        path = urllib.parse.urlsplit(self.path).path[:500]
        requests = getattr(self.server, "partyops_requests", None)
        lock = getattr(self.server, "partyops_requests_lock", None)
        if isinstance(requests, list) and lock is not None:
            with lock:
                requests.append(path)

    def end_headers(self) -> None:
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        super().end_headers()

    def do_GET(self) -> None:
        self._record_request()
        if self.path == "/jsplugins.xml":
            base = f"http://127.0.0.1:{self.server.server_port}"
            payload = (
                '<?xml version="1.0" encoding="UTF-8"?>\n'
                '<jsplugins><jspluginonline name="'
                + PLUGIN_NAME
                + '" type="wps" url="'
                + base
                + '/"/></jsplugins>\n'
            ).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/xml; charset=utf-8")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
            return
        if self.path == "/config.js":
            payload = (
                "this.PARTYOPS_WPS_BRIDGE_CONFIG="
                + json.dumps(self._config, ensure_ascii=False, separators=(",", ":"))
                + ";\n"
            ).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "text/javascript; charset=utf-8")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
            return
        super().do_GET()


def start_plugin_server(
    plugin_root: Path,
    token: str,
    output_path: Path,
    paragraph_index: int,
) -> ThreadingHTTPServer:
    if not (plugin_root / "index.html").is_file() or not (plugin_root / "main.js").is_file():
        raise RuntimeError("[WPS_PLUGIN_INCOMPLETE] WPS 桥接加载项文件不完整。")

    def handler(*args: Any, **kwargs: Any) -> _PluginHandler:
        return _PluginHandler(
            *args,
            directory=str(plugin_root),
            config={
                "token": token,
                "output_path": str(output_path.resolve()),
                "paragraph_index": paragraph_index,
            },
            **kwargs,
        )

    server = ThreadingHTTPServer(("127.0.0.1", 0), handler)
    server.partyops_requests = []  # type: ignore[attr-defined]
    server.partyops_requests_lock = threading.Lock()  # type: ignore[attr-defined]
    threading.Thread(target=server.serve_forever, name="partyops-wps-probe", daemon=True).start()
    return server


def build_invocation(
    *,
    token: str,
    output_path: Path,
    paragraph_index: int,
    jsplugins_url: str,
    timeout_ms: int = 60_000,
) -> tuple[str, bytes]:
    command_id = secrets.token_hex(16)
    info = {
        "token": token,
        "output_path": str(output_path.resolve()),
        "paragraph_index": paragraph_index,
    }
    echo_url = WPS_RELAY + "/transferEcho/runParams"
    function = "var res = partyOpsProbe"
    callback = (
        "var xhr=new XMLHttpRequest();xhr.open('POST','"
        + echo_url
        + "');xhr.send(JSON.stringify({id:'"
        + command_id
        + "',response:res}));"
    )
    start_info = {
        "name": PLUGIN_NAME,
        "function": function,
        "info": json.dumps(info, ensure_ascii=False, separators=(",", ":"))
        + ");"
        + callback
        + "void(0",
        "showToFront": False,
        "jsPluginsXml": jsplugins_url,
    }
    encoded = base64.b64encode(_json_bytes(start_info)).decode("ascii")
    wrapper = {
        "id": command_id,
        "app": "wps",
        "data": "ksowebstartupwps://" + encoded,
        "mode": True,
        "timeout": timeout_ms,
    }
    return command_id, _json_bytes(wrapper)


def parse_probe_response(raw: str, token: str) -> dict[str, Any]:
    try:
        value: Any = json.loads(raw)
        if isinstance(value, dict) and "response" in value:
            value = value["response"]
        if isinstance(value, dict) and "data" in value:
            value = value["data"]
        if isinstance(value, str):
            value = json.loads(value)
    except (json.JSONDecodeError, TypeError) as exc:
        raise RuntimeError("[WPS_PROBE_RESPONSE_INVALID] WPS 返回内容无法校验。") from exc
    if not isinstance(value, dict):
        raise TypeError("[WPS_PROBE_RESPONSE_INVALID] WPS 返回内容不是对象。")
    if (
        value.get("schema") != 1
        or value.get("passed") is not True
        or value.get("provider") != "wps"
        or not secrets.compare_digest(str(value.get("token", "")), token)
        or abs(float(value.get("character_unit_first_line_indent", -1)) - 2.0) > 0.001
        or value.get("visible") is not False
    ):
        raise RuntimeError("[WPS_PROBE_CONTRACT_FAILED] WPS 静默字符缩进契约未通过。")
    return value


def verify_saved_character_indent(path: Path, paragraph_index: int) -> None:
    try:
        with zipfile.ZipFile(path) as package:
            document = ElementTree.fromstring(package.read("word/document.xml"))
            styles = ElementTree.fromstring(package.read("word/styles.xml"))
    except (OSError, KeyError, zipfile.BadZipFile, ElementTree.ParseError) as exc:
        raise RuntimeError("[WPS_PROBE_OUTPUT_INVALID] WPS 未生成有效 DOCX。") from exc
    paragraphs = document.findall(f".//{{{WORD_NS}}}body/{{{WORD_NS}}}p")
    if paragraph_index > len(paragraphs):
        raise RuntimeError("[WPS_PROBE_OUTPUT_INVALID] 保存后的目标段落不存在。")
    paragraph = paragraphs[paragraph_index - 1]
    ppr = paragraph.find(f"{{{WORD_NS}}}pPr")
    indent = ppr.find(f"{{{WORD_NS}}}ind") if ppr is not None else None
    if indent is not None and indent.get(f"{{{WORD_NS}}}firstLineChars") == "200":
        return
    style_ref = ppr.find(f"{{{WORD_NS}}}pStyle") if ppr is not None else None
    style_id = style_ref.get(f"{{{WORD_NS}}}val") if style_ref is not None else None
    for style in styles.findall(f"{{{WORD_NS}}}style"):
        if style.get(f"{{{WORD_NS}}}styleId") != style_id:
            continue
        style_indent = style.find(
            f"{{{WORD_NS}}}pPr/{{{WORD_NS}}}ind"
        )
        if (
            style_indent is not None
            and style_indent.get(f"{{{WORD_NS}}}firstLineChars") == "200"
        ):
            return
    raise RuntimeError(
        "[WPS_PROBE_INDENT_NOT_PERSISTED] 保存后的 OOXML 未保留 2 字符首行缩进。"
    )


def run_probe(args: argparse.Namespace) -> dict[str, Any]:
    plugin_root = args.plugin_root.resolve()
    source = args.input.resolve()
    output = args.output.resolve()
    if not source.is_file() or source.suffix.lower() != ".docx":
        raise RuntimeError("[WPS_PROBE_INPUT_INVALID] 探针输入必须是现有 DOCX。")
    if args.paragraph_index < 1:
        raise RuntimeError("[WPS_PROBE_PARAGRAPH_INVALID] 段落序号必须大于零。")
    output.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, output)
    original_hash = _sha256(output)
    token = secrets.token_urlsafe(48)
    server = start_plugin_server(
        plugin_root,
        token,
        output,
        args.paragraph_index,
    )
    try:
        version = wait_for_relay(args.platform, start=not args.no_start_relay)
        plugins_url = f"http://127.0.0.1:{server.server_port}/jsplugins.xml"
        _command_id, body = build_invocation(
            token=token,
            output_path=output,
            paragraph_index=args.paragraph_index,
            jsplugins_url=plugins_url,
            timeout_ms=int(args.timeout_seconds * 1000),
        )
        try:
            raw = _post("/transfer/runParams", body, args.timeout_seconds + 30.0)
        except (OSError, TimeoutError, UnicodeError, urllib.error.URLError) as exc:
            requested = list(getattr(server, "partyops_requests", []))
            request_summary = ",".join(dict.fromkeys(requested)) or "none"
            raise RuntimeError(
                "[WPS_PROBE_INVOKE_FAILED] WPS 加载项调用失败；"
                f"中继={_network_error_summary(exc)}；加载项服务请求={request_summary}。"
            ) from exc
        response = parse_probe_response(raw, token)
    finally:
        server.shutdown()
        server.server_close()
    verify_saved_character_indent(output, args.paragraph_index)
    output_hash = _sha256(output)
    if output_hash == original_hash:
        raise RuntimeError("[WPS_PROBE_OUTPUT_UNCHANGED] WPS 未持久化探针修改。")
    return {
        "schema": 1,
        "passed": True,
        "timezone": "Asia/Shanghai",
        "verified_at": datetime.now(ZoneInfo("Asia/Shanghai")).isoformat(
            timespec="seconds"
        ),
        "platform": args.platform,
        "architecture": args.architecture,
        "provider": "wps",
        "adapter": "wps-jsapi-source-bridge",
        "relay_version": version,
        "application": str(response.get("application", "WPS Office")),
        "silent": True,
        "character_unit_first_line_indent": 2,
        "input_sha256": _sha256(source),
        "output_sha256": output_hash,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--platform", choices=("windows", "linux", "macos"), required=True
    )
    parser.add_argument("--architecture", required=True)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--paragraph-index", type=int, default=3)
    parser.add_argument("--timeout-seconds", type=float, default=60.0)
    parser.add_argument(
        "--plugin-root",
        type=Path,
        default=Path(__file__).resolve().parents[1]
        / "packaging"
        / "wps-formatter-adapter"
        / "plugin",
    )
    parser.add_argument("--evidence", type=Path)
    parser.add_argument("--no-start-relay", action="store_true")
    args = parser.parse_args()
    if not 5.0 <= args.timeout_seconds <= 180.0:
        parser.error("--timeout-seconds 必须在 5 到 180 之间。")
    try:
        result = run_probe(args)
        rendered = json.dumps(result, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
        if args.evidence:
            args.evidence.parent.mkdir(parents=True, exist_ok=True)
            args.evidence.write_text(rendered, encoding="utf-8")
        print(rendered, end="")
        return 0
    except (RuntimeError, ValueError) as exc:
        print(str(exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
