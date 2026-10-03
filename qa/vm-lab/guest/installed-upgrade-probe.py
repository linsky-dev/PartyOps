"""用 Guest 安装版迁移真实旧库；复用基线生成器，不导入产品源码。"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import socket
import sqlite3
import subprocess
import time
import urllib.error
import urllib.request
import zipfile
from datetime import datetime, timedelta, timezone
from pathlib import Path


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def is_child(path, parent):
    """兼容 UOS 原版 Python 3.7，不放宽合成测试目录边界。"""
    try:
        return bool(path.relative_to(parent).parts)
    except ValueError:
        return False


def inspect_data(data, expected_schema, attachment_sha256):
    """仅只读核对基线中的固定合成账号、附件及 SQLite 完整性。"""
    with sqlite3.connect((data / "partyops.db").as_uri() + "?mode=ro", uri=True) as database:
        revision = database.execute("SELECT version_num FROM alembic_version").fetchone()[0]
        integrity = database.execute("PRAGMA quick_check").fetchone()[0]
        user = database.execute("SELECT display_name FROM users WHERE id=?",
                                ("rc4-native-upgrade-admin",)).fetchone()
    if revision != expected_schema or integrity != "ok" or user != ("原生覆盖升级管理员",):
        raise RuntimeError("旧库迁移版本、完整性或合成账号不一致")
    actual_hash = digest(data / "attachments/preserved.txt")
    if actual_hash != attachment_sha256:
        raise RuntimeError("旧库附件被修改")
    return {"schema": revision, "quick_check": integrity,
            "fixture_user_preserved": True, "attachment_sha256": actual_hash}


def inspect_backups(data, original_schema):
    backups = list((data / "backups").glob("PartyOps-pre-upgrade-*.partyops-backup"))
    if len(backups) != 1:
        raise RuntimeError("必须保留唯一迁移前备份")
    path = backups[0]
    with zipfile.ZipFile(path) as archive:
        manifest = json.loads(archive.read("manifest.json"))
        if manifest.get("schema_version") != original_schema:
            raise RuntimeError("备份不是原数据库版本")
        files = manifest.get("files", [])
        if not files or "database/partyops.db" not in {item["path"] for item in files}:
            raise RuntimeError("备份没有数据库")
        for item in files:
            payload = archive.read(item["path"])
            if hashlib.sha256(payload).hexdigest() != item["sha256"] or len(payload) != item["size"]:
                raise RuntimeError("备份文件内容与清单不一致")
    return {"filename": path.name, "sha256": digest(path),
            "schema": original_schema, "verified_files": len(files)}


def run(args):
    marker = json.loads(Path("/etc/partyops-vm-lab.json").read_text())
    if platform.system() != "Linux" or os.getuid() == 0 or marker != {
        "uuid": args.uuid, "purpose": "disposable-qa"
    }:
        raise RuntimeError("必须在指定 Guest 内由标准用户执行")
    data = args.data.resolve(strict=True)
    output = args.output.resolve()
    for path in (data, output):
        if not is_child(path, Path.home().resolve()):
            raise RuntimeError("只允许 Guest 合成测试目录")
    if data == output or is_child(output, data):
        raise RuntimeError("证据目录不能混入业务数据")
    executable = Path("/opt/partyops/partyops")
    if digest(executable) != args.executable_sha256:
        raise RuntimeError("安装版主程序哈希不一致")
    before = inspect_data(data, args.expected_schema if args.recheck else args.original_schema,
                          args.attachment_sha256)
    output.mkdir(parents=True, exist_ok=False)
    record = {"status": "failed", "scope": "installed-binary-database-upgrade-only",
              "runtime_environment_passed": False, "guest_uuid": args.uuid,
              "boot_id": Path("/proc/sys/kernel/random/boot_id").read_text().strip(),
              "arch": platform.machine(), "uid": os.getuid(), "before": before,
              "recheck": args.recheck, "executable_sha256": digest(executable),
              "app_version": Path("/opt/partyops/VERSION").read_text().strip()}
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 18869))
    environment = {key: value for key, value in os.environ.items()
                   if not key.startswith(("PARTYOPS_", "MONO_", "PYTHON", "LD_LIBRARY_PATH"))}
    environment.update(PARTYOPS_MODE="host", PARTYOPS_ENVIRONMENT="production",
                       PARTYOPS_DATA_DIR=str(data), PARTYOPS_HOST="127.0.0.1",
                       PARTYOPS_BIND_HOST="127.0.0.1", PARTYOPS_PORT="18869",
                       PARTYOPS_TLS_ENABLED="false", PARTYOPS_SEED_DEMO="false")
    process = None
    try:
        with (output / "upgrade-server.log").open("w", encoding="utf-8") as log:
            process = subprocess.Popen([str(executable)], env=environment, cwd=output,
                                       stdout=log, stderr=subprocess.STDOUT)
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
            deadline = time.monotonic() + 300
            while True:
                if process.poll() is not None:
                    raise RuntimeError("安装版在升级完成前退出")
                try:
                    with opener.open("http://127.0.0.1:18869/api/v1/health", timeout=10) as response:
                        health = json.load(response)
                    break
                except urllib.error.URLError:
                    if time.monotonic() >= deadline:
                        raise RuntimeError("安装版升级健康检查超时") from None
                    time.sleep(2)
            if health.get("status") != "ok" or health.get("mode") != "host":
                raise RuntimeError("协同主服务健康状态不符")
            record["health"] = health
            record["after"] = inspect_data(data, args.expected_schema, args.attachment_sha256)
            record["backup"] = inspect_backups(data, args.original_schema)
            record["status"] = "passed"
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=30)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
                record.update(status="failed", shutdown_error="进程没有正常退出")
        record["verified_at"] = datetime.now(timezone(timedelta(hours=8))).isoformat(timespec="seconds")
        (output / "upgrade-evidence.json").write_text(
            json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if record["status"] != "passed":
        raise RuntimeError("升级局部验收失败")
    print(json.dumps(record, ensure_ascii=False, indent=2))


def main():
    parser = argparse.ArgumentParser()
    for name in ("uuid", "executable-sha256", "attachment-sha256", "original-schema", "expected-schema"):
        parser.add_argument("--" + name, required=True)
    for name in ("data", "output"):
        parser.add_argument("--" + name, required=True, type=Path)
    parser.add_argument("--recheck", action="store_true", help="真正 Guest 重启后复核同一数据目录")
    run(parser.parse_args())


if __name__ == "__main__":
    main()
