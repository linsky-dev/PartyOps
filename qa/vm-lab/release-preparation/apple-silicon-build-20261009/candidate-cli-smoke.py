"""仅执行安装后的有界 CLI 自检，不冒充 GUI 或 WPS 操作。"""
import argparse
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--app", type=Path, required=True)
parser.add_argument("--workspace", type=Path, required=True)
args = parser.parse_args()
workspace = args.workspace.resolve()
home, temporary = workspace / "cli-home", workspace / "cli-tmp"
home.mkdir()
temporary.mkdir()
env = {"HOME": str(home), "TMPDIR": str(temporary), "PATH": "/usr/bin:/bin:/usr/sbin:/sbin", "LANG": "en_US.UTF-8"}
receipt = {"schema": 1, "gui_wps_validated": False, "publication_authorized": False, "cli_checks": []}
checks = (("partyops-desktop", "--self-test"), ("partyops-desktop", "--launch-services-self-test"),
          ("partyops-launch-agent", "--mode", "personal", "--self-test"), ("partyops", "--package-self-test"))
try:
    for index, command in enumerate(checks):
        executable = args.app / "Contents/MacOS" / command[0]
        result = subprocess.run([str(executable), *command[1:]], env=env, cwd=workspace,
                                stdin=subprocess.DEVNULL, capture_output=True, timeout=180)
        output = (result.stdout + result.stderr).decode("utf-8", "replace")
        output = output.replace(str(workspace), "$CLI_WORKSPACE")
        (workspace / f"cli-{index}.log").write_text(output[:32000], encoding="utf-8")
        receipt["cli_checks"].append({"command": list(command), "returncode": result.returncode, "passed": result.returncode == 0})
        if result.returncode:
            raise RuntimeError("安装后 CLI 自检失败：" + command[0])
except (OSError, subprocess.TimeoutExpired, RuntimeError) as error:
    receipt["error"] = type(error).__name__ + ": " + str(error).replace(str(workspace), "$CLI_WORKSPACE")
finally:
    (workspace / "cli-smoke.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
if "error" in receipt:
    raise SystemExit(2)
