"""观测新 Windows 兼容目标中已经启动的实际普通用户 PartyOps 进程，不启动任何实例。"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from lab import fingerprint, inventory, load_configuration  # noqa: E402
from provenance import bind_package  # noqa: E402
from providers import QemuLab  # noqa: E402
from windows_execution import capture  # noqa: E402


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target")
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--executable", required=True)
    parser.add_argument("--report-directory", type=Path, required=True)
    parser.add_argument("--artifacts", type=Path, default=Path(__file__).resolve().parents[3] / "artifacts")
    args = parser.parse_args()
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    try:
        package_id = next(key for key, spec in matrix["packages"].items() if args.target in spec["required_targets"])
        packages, errors = inventory(matrix, args.artifacts)
        if package_id in errors:
            raise RuntimeError(errors[package_id])
        package = bind_package(lab, packages[package_id], fingerprint())
        result = capture(lab, args.target, package, args.pid, args.executable, args.report_directory)
        print(json.dumps(result, ensure_ascii=False))
        return 0
    except (RuntimeError, ValueError, OSError, KeyError, StopIteration) as exc:
        print(json.dumps({"status": "blocked", "error": str(exc), "runtime_environment_passed": False}, ensure_ascii=False))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
