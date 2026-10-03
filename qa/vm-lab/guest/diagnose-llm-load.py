"""复现安装版子进程参数并测量模型加载时间；独立诊断绝不替代产品推理。"""
import argparse
import json
import os
import resource
import subprocess
import time
import urllib.error
import urllib.request
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--observation", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if os.getuid() == 0 or args.output.exists():
        raise RuntimeError("NEW_STANDARD_USER_DIAGNOSTIC_DIRECTORY_REQUIRED")
    observation = json.loads(args.observation.read_text())
    child = next(row["children"][0] for row in observation["result"]["samples"] if row["children"])
    command = [value for value in child["command"] if value]
    if Path(command[0]).resolve() != Path("/opt/partyops/llama-server"):
        raise RuntimeError("OBSERVATION_MUST_IDENTIFY_INSTALLED_RUNTIME")
    command[command.index("--port") + 1] = "18769"
    args.output.mkdir()
    def limits():
        os.nice(10)
        resource.setrlimit(resource.RLIMIT_AS, (3584 * 1024**2, 3584 * 1024**2))
    result = {"scope": "diagnostic-only-not-product-inference", "command": command,
              "loader_environment": child["loader_environment"], "polls": []}
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with (args.output / "llama-server.log").open("w") as log:
        # 此独立诊断进程不创建线程，复用实际产品的 POSIX 子进程限制。
        process = subprocess.Popen(command, env={**os.environ, **child["loader_environment"]},
                                   preexec_fn=limits, stdout=log, stderr=subprocess.STDOUT)  # noqa: PLW1509
        started = time.monotonic()
        try:
            while time.monotonic() - started < 120 and process.poll() is None:
                try:
                    with opener.open("http://127.0.0.1:18769/health", timeout=1) as response:
                        status = response.status
                except urllib.error.HTTPError as exc:
                    status = exc.code
                except (OSError, urllib.error.URLError):
                    status = 0
                elapsed = round(time.monotonic() - started, 3)
                result["polls"].append({"elapsed": elapsed, "health": status})
                if status == 200:
                    result["ready_seconds"] = elapsed
                    break
                time.sleep(0.5)
            result["exit_code_before_stop"] = process.poll()
        finally:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=20)
            (args.output / "diagnostic.json").write_text(json.dumps(result))
    print(json.dumps(result))


if __name__ == "__main__":
    main()
