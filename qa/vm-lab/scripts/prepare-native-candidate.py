"""准备或重验新本机 run 的独立安装前绑定；不执行安装、不操作账户。"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import native_install
from lab import REPO, fingerprint, inventory, load_configuration
from providers import QemuLab


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-directory", type=Path, required=True)
    parser.add_argument("--verify-only", action="store_true")
    args = parser.parse_args()
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    source = fingerprint()
    if args.verify_only:
        result = native_install.verify(lab, args.run_directory, source)
    else:
        packages, errors = inventory(matrix, REPO / "artifacts")
        if "windows_amd64" in errors:
            raise RuntimeError(errors["windows_amd64"])
        result = native_install.prepare(lab, args.run_directory, packages["windows_amd64"], source)
    if fingerprint() != source:
        raise RuntimeError("NATIVE_SOURCE_CHANGED_DURING_BINDING")
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
