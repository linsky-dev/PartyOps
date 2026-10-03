"""显式只读检查指定的新本机 run；保留旧证据，部分诊断不代表生命周期通过。"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from lab import fingerprint, load_configuration
from native_diagnostics import read_current, read_record, register_current, summary
from native_install import run_directory, write_new
from provenance import bind_package
from providers import QemuLab


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-directory", type=Path)
    parser.add_argument("--register-current", action="store_true", help="验证后显式登记当前运行，保留原登记和所有历史实测")
    parser.add_argument("--wps-evidence", type=Path, action="append", default=[], help="显式纳入该运行内真实 WPS evidence.json；可重复")
    parser.add_argument("--output", type=Path, help="在该运行内新建摘要；已有文件拒绝覆盖")
    args = parser.parse_args()
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    if args.register_current and args.run_directory is None:
        parser.error("--register-current 必须显式指定 --run-directory")
    run = run_directory(lab, args.run_directory) if args.run_directory else read_current(lab, "win11-x64-native")[0]
    candidate, _ = read_record(run, run / "candidate.json")
    source = fingerprint()
    package = bind_package(lab, candidate, source) if candidate.get("source_fingerprint") == source else {}
    if args.register_current:
        register_current(lab, "win11-x64-native", package, source, run, wps_evidence=args.wps_evidence)
    result = summary(lab, "win11-x64-native", package, source, run_directory=run if args.run_directory else None,
                     wps_evidence=args.wps_evidence)
    if args.output:
        from evidence import safe_child
        output = safe_child(run, args.output)
        if output.parent != run:
            parser.error("--output 必须为当前 run 直接子文件")
        write_new(output, result)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["status"] == "partial" else 2


if __name__ == "__main__":
    raise SystemExit(main())
