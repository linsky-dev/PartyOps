"""原版 Win7 双位数安装版普通用户诊断入口；不自动启动 Guest。"""
import argparse
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from lab import load_configuration
from providers import QemuLab
from windows_win7_standard import exercise


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", choices=["win7-x64", "win7-x86"])
    parser.add_argument("phase", choices=["prepare", "probe"])
    parser.add_argument("--install-report", type=Path, required=True)
    parser.add_argument("--resume", type=Path)
    parser.add_argument("--timeout", type=int, default=420)
    args = parser.parse_args()
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    try:
        result = exercise(lab, args.target, args.phase, args.install_report, resume=args.resume, timeout=args.timeout)
    except (RuntimeError, OSError, KeyError, ValueError) as exc:
        result = {"status": "blocked", "error": str(exc), "exit_code": 1, "runtime_environment_passed": False}
    print(json.dumps(result, ensure_ascii=False))
    return result["exit_code"]


if __name__ == "__main__":
    raise SystemExit(main())
