"""只读观测实际安装版启动的 llama 子进程；不代启模型，不记作推理通过。"""
import json
import os
import time
import urllib.error
import urllib.request
from pathlib import Path


def observe():
    started = time.monotonic()
    result = {"scope": "diagnostic-only-actual-installed-child", "samples": []}
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    found = False
    while time.monotonic() - started < 100:
        children = []
        for proc in Path("/proc").iterdir():
            if not proc.name.isdigit():
                continue
            try:
                executable = os.readlink(str(proc / "exe"))
                if executable not in {"/opt/partyops/bin/llama-server", "/opt/partyops/llama-server"}:
                    continue
                raw = (proc / "environ").read_bytes().split(b"\0")
                environment = dict(item.split(b"=", 1) for item in raw if b"=" in item)
                # 只回收加载器相关变量，禁止把产品环境中的凭据写入报告。
                public = {key.decode(): environment[key].decode(errors="replace")
                          for key in (b"LD_LIBRARY_PATH", b"LD_LIBRARY_PATH_ORIG", b"OMP_NUM_THREADS") if key in environment}
                children.append({"pid": int(proc.name), "executable": executable,
                                 "command": (proc / "cmdline").read_bytes().decode().split("\0"),
                                 "loader_environment": public, "limits": (proc / "limits").read_text(),
                                 "status": (proc / "status").read_text()})
            except (OSError, ValueError):
                continue
        status = None
        if children:
            found = True
            try:
                with opener.open("http://127.0.0.1:18767/health", timeout=1) as response:
                    status = response.status
            except urllib.error.HTTPError as exc:
                status = exc.code
            except (OSError, urllib.error.URLError):
                status = 0
        result["samples"].append({"elapsed": round(time.monotonic() - started, 3), "children": children, "health": status})
        if found and (not children or status == 200):
            break
        time.sleep(1)
    result["observed_actual_child"] = found
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    observe()
