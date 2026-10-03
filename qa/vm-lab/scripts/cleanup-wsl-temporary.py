"""清理本地 OracleLinux 构建环境的过期项目临时树；默认只盘点。"""
import argparse
import json
import os
import shutil
import time
from datetime import datetime, timezone
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    if os.name != "posix" or not Path("/opt/manylinux2014-aarch64-rootfs").is_dir():
        raise RuntimeError("仅适用于本机既有 OracleLinux 构建环境")
    for entry in Path("/proc").iterdir():
        if entry.name.isdigit() and int(entry.name) != os.getpid():
            try:
                name = (entry / "comm").read_text().strip()
            except OSError:
                continue
            if name in {"make", "ninja", "rpmbuild", "gcc", "g++", "cc1", "cc1plus", "cargo"}:
                raise RuntimeError("构建仍在运行，暂缓清理")
    roots = [Path("/tmp"), Path("/opt/manylinux2014-aarch64-rootfs/tmp")]
    rows = []
    for root in roots:
        if root.is_symlink():
            raise RuntimeError("临时根目录不能为符号链接")
        for path in sorted(root.glob("partyops*")):
            if (not path.is_dir() or path.is_symlink() or "tools" in path.name
                    or path.stat().st_mtime > time.time() - 12 * 3600):
                continue
            if path.resolve().parent != root.resolve() or os.path.ismount(str(path)):
                raise RuntimeError("临时目标越界或是挂载点")
            size, count = 0, 0
            for directory, subdirs, files in os.walk(str(path), followlinks=False):
                if os.path.ismount(directory):
                    raise RuntimeError("临时树中存在挂载点")
                subdirs[:] = [name for name in subdirs if not Path(directory, name).is_symlink()]
                for name in files:
                    item = Path(directory, name)
                    if not item.is_symlink():
                        size += item.stat().st_size
                        count += 1
            rows.append({"path": str(path), "bytes": size, "files": count, "status": "prepared"})
    report = Path("/mnt/d/PartyOps-VM-Lab/reports/wsl-temporary-retention-20260906.json")
    record = {"at": datetime.now(timezone.utc).isoformat(), "apply": args.apply,
              "scope": "WSL-filesystem-only-host-vhd-not-compacted", "items": rows,
              "logical_bytes": sum(row["bytes"] for row in rows),
              "preserves": ["/opt 工具链", "名称含 tools 的构建工具", "12 小时内修改的目录"]}

    def save():
        report.write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")

    save()
    if args.apply:
        for row in rows:
            # 标准库只移除已核对的临时树，符号链接本身被移除而不跟随。
            shutil.rmtree(row["path"])
            row["status"] = "deleted"
            save()
    print(json.dumps({"directories": len(rows), "logical_gib": round(record["logical_bytes"] / 1024**3, 2),
                      "applied": args.apply, "report": str(report)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
