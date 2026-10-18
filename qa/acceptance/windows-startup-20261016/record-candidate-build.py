"""将正式构建回执控制器限定到当前 C 工作区和既有 D 实验室。"""
from __future__ import annotations
import argparse
import importlib.util
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "qa/vm-lab"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if not command or ROOT.drive.upper() != "C:":
        raise RuntimeError("CURRENT_C_WORKSPACE_BUILD_COMMAND_REQUIRED")
    spec = importlib.util.spec_from_file_location("official_record_build", ROOT / "qa/vm-lab/scripts/record-build.py")
    official = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(official)
    original = official.load_configuration

    def fixed_configuration():
        matrix, media = original()
        matrix["defaults"].update(primary_root="D:/PartyOps-VM-Lab", fallback_root="D:/PartyOps-VM-Lab")
        # 只登记此次 Windows 包，不扫描其它平台的旧候选。
        matrix["packages"] = {"windows_amd64": matrix["packages"]["windows_amd64"]}
        return matrix, media

    official.load_configuration = fixed_configuration
    result = official.build(["windows_amd64"], command)
    print("正式构建回执状态：" + result["status"])
    return 0 if result["status"] == "completed" else 2


if __name__ == "__main__":
    raise SystemExit(main())
