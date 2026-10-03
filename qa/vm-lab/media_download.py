"""官方介质下载：断点续传、实际长度余量检查，临时令牌只留内存。"""
from __future__ import annotations

import json
import os
import re
import subprocess
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from collections import deque
from pathlib import Path

from evidence import now, sha256, write_json


def download_aria2(item: dict, part: Path, check_space, binary: Path) -> None:
    """调用官方 aria2 分段续传；签名 URL 通过 stdin 传入，不进入命令行和报告。"""
    for attempt in range(2):
        url = source_url(item)
        with urllib.request.urlopen(urllib.request.Request(url, method="HEAD"), timeout=30) as response:
            total = int(response.headers.get("Content-Length", "0"))
        if total <= 0 or (item.get("size_bytes") and item["size_bytes"] != total):
            raise RuntimeError("MEDIA_SIZE_MISMATCH")
        existing = part.stat().st_size if part.exists() else 0
        check_space(part.parent, (max(0, total - existing) + 1024**3 - 1) // 1024**3)
        args = [str(binary), "--input-file=-", "--continue=true", "--max-connection-per-server=8",
                "--split=8", "--min-split-size=4M", "--file-allocation=none", "--max-tries=1",
                "--auto-file-renaming=false", "--allow-overwrite=false", "--check-integrity=true",
                "--checksum=sha-256=" + item["sha256"], "--summary-interval=0", "--console-log-level=warn",
                "--download-result=hide", "--show-console-readout=false", "--enable-color=false",
                "--timeout=30", "--connect-timeout=15", "--dir=" + str(part.parent)]
        process = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                   creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        # --input-file 模式的 out 必须写在下载项下；全局 --out 不生效。
        process.stdin.write((url + "\n  out=" + part.name + "\n").encode())
        process.stdin.close()
        messages = deque(maxlen=16)
        def drain_output(process=process, messages=messages):
            # aria2 的告警也可能填满管道，持续读取但不输出含令牌的 URL。
            while chunk := process.stdout.read1(4096):
                messages.append(chunk)
        reader = threading.Thread(target=drain_output, daemon=True)
        reader.start()
        last_notice = 0.0
        try:
            while process.poll() is None:
                check_space(part.parent)
                if time.monotonic() - last_notice >= 30:
                    print(json.dumps({"media": item["filename"], "downloader": "aria2",
                                      "allocated_bytes": part.stat().st_size if part.exists() else 0,
                                      "total_bytes": total}, ensure_ascii=False), flush=True)
                    last_notice = time.monotonic()
                time.sleep(2)
        except BaseException:
            process.terminate()
            process.wait(timeout=20)
            raise
        reader.join(timeout=5)
        output = b"".join(messages).decode(errors="replace")
        if process.returncode == 0:
            if part.stat().st_size != total:
                raise RuntimeError("MEDIA_DOWNLOAD_TRUNCATED")
            return
        status = re.search(r"status=(\d{3})", output)
        redacted = re.sub(r'https?://[^\s]+', '[download-url-redacted]', output)
        write_json(part.with_suffix(part.suffix + ".download-error.json"),
                   {"at": now(), "exit_code": process.returncode, "media": item["filename"],
                    "diagnostic": redacted[-4000:]})
        code = int(status[1]) if status else None
        if not attempt and (code == 429 or (code and code >= 500) or "timeout" in output.lower()):
            time.sleep(20 if code == 429 else 2)
            continue
        details = re.findall(r"(?:errorCode|error code)[=: ]+(\d+)", output, re.IGNORECASE)
        raise RuntimeError(f"MEDIA_DOWNLOAD_FAILED: {item['filename']}; aria2={process.returncode}; HTTP={code}; details={details[-8:]}")


def source_url(item: dict) -> str:
    if item.get("download_api"):
        if item["download_api"] != "https://www.chinauos.com/api/v1/download/os":
            raise RuntimeError("UNSUPPORTED_MEDIA_API")
        query = urllib.parse.urlencode({"arch": "arm64" if item["arch"] == "aarch64" else "amd64",
                                       "iso_name": item["remote_file"], "file_name": item["remote_file"], "flag": 1})
        with urllib.request.urlopen(item["download_api"] + "?" + query, timeout=30) as response:
            value = response.read().decode().strip()
        if value.startswith('"'):
            value = json.loads(value)
        parsed = urllib.parse.urlparse(value)
        if parsed.scheme != "https" or parsed.hostname != "cdimage-download.chinauos.com":
            raise RuntimeError("UOS_DOWNLOAD_URL_INVALID")
        return value
    url = item.get("url", "")
    if not url.startswith("https://"):
        raise RuntimeError("MEDIA_HTTPS_REQUIRED")
    return url


def download(item: dict, part: Path, check_space) -> None:
    """重试只针对限流、超时和服务错误；不把 HTML 错误页当成 ISO。"""
    for attempt in range(2):
        try:
            offset = part.stat().st_size if part.exists() else 0
            if offset and sha256(part) == item["sha256"]:
                return
            request = urllib.request.Request(source_url(item), headers={"Range": f"bytes={offset}-"} if offset else {})
            with urllib.request.urlopen(request, timeout=30) as response:
                resume = offset > 0 and response.status == 206
                if resume and not response.headers.get("Content-Range", "").startswith(f"bytes {offset}-"):
                    raise RuntimeError("MEDIA_CONTENT_RANGE_MISMATCH")
                length = int(response.headers.get("Content-Length", "0"))
                if length <= 0:
                    raise RuntimeError("MEDIA_DOWNLOAD_LENGTH_REQUIRED")
                if "text/html" in response.headers.get("Content-Type", ""):
                    raise RuntimeError("MEDIA_RESPONSE_IS_HTML")
                check_space(part.parent, (length + 1024**3 - 1) // 1024**3)
                total = length + (offset if resume else 0)
                if item.get("size_bytes") and item["size_bytes"] != total:
                    raise RuntimeError("MEDIA_SIZE_MISMATCH")
                written = offset if resume else 0
                last_notice = 0.0
                with part.open("ab" if resume else "wb") as output:
                    while chunk := response.read(4 * 1024 * 1024):
                        check_space(part.parent)
                        output.write(chunk)
                        written += len(chunk)
                        if time.monotonic() - last_notice >= 30:
                            print(json.dumps({"media": item["filename"], "downloaded_bytes": written,
                                              "total_bytes": total}, ensure_ascii=False), flush=True)
                            last_notice = time.monotonic()
                if written != total:
                    raise RuntimeError("MEDIA_DOWNLOAD_TRUNCATED")
            return
        except (urllib.error.URLError, TimeoutError) as exc:
            code = getattr(exc, "code", None)
            if attempt or (code is not None and code != 429 and code < 500):
                raise RuntimeError(f"MEDIA_DOWNLOAD_FAILED: {item['filename']}; HTTP={code}") from None
            time.sleep(20 if code == 429 else 2)
