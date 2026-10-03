"""核查/下载本地 WSL Windows ARM 组件；构建低并发、可恢复，始终不创建或启动 Guest。"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

import psutil

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from evidence import now, sha256, write_json

CONFIG = HERE / "config/windows-arm64-stack.json"
BUILD_SCRIPT = HERE / "scripts/build-windows-arm64-stack-wsl.sh"


def wsl(config, arguments, *, timeout=120):
    result = subprocess.run(["wsl.exe", "-d", config["wsl_distribution"], "--exec", *arguments],
                            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout, check=False)
    if result.returncode:
        raise RuntimeError("WSL_COMPONENT_COMMAND_FAILED:" + result.stderr.strip()[-1500:])
    return result.stdout.strip()


def guard(config, *, build=False):
    for drive, reserve, extra in (("D:/", config["primary_reserve_gib"], 0), ("E:/", config["fallback_reserve_gib"], 12 if build else 2)):
        if shutil.disk_usage(drive).free < (reserve + extra) * 1024**3:
            raise RuntimeError("WINDOWS_ARM_STACK_STORAGE_RESERVE:" + drive)
    if build:
        if psutil.virtual_memory().available < config["build_memory_gib"] * 1024**3:
            raise RuntimeError("WINDOWS_ARM_STACK_BUILD_MEMORY_RESERVE")
        for process in psutil.process_iter(["name"]):
            name = (process.info.get("name") or "").lower()
            if name.startswith("qemu-system-") or name == "vmware-vmx.exe":
                raise RuntimeError("WINDOWS_ARM_STACK_BUILD_REQUIRES_STOPPED_GUESTS")


def inspect(config):
    guard(config)
    code = r'''
import json, os, platform, shutil, subprocess
from pathlib import Path
requested=json.loads(__PACKAGES__)
values={}
for line in Path('/etc/os-release').read_text().splitlines():
 if '=' in line:
  k,v=line.split('=',1); values[k]=v.strip('"')
packages={}
for name in requested:
 r=subprocess.run(['dpkg-query','-W','-f=${Status} ${Version}',name],capture_output=True,text=True)
 packages[name]=r.stdout.strip() if r.returncode==0 else 'not-installed'
print(json.dumps(dict(distribution=values,architecture=platform.machine(),uid=os.getuid(),
 free_bytes={p:shutil.disk_usage(p).free for p in ['/mnt/d','/mnt/e']},packages=packages,
 mounts=[line for line in Path('/proc/mounts').read_text().splitlines() if any(p in line for p in [' / ',' /mnt/d ',' /mnt/e '])]),ensure_ascii=False))
'''.replace("__PACKAGES__", repr(json.dumps(config["build_dependencies"])))
    record = json.loads(wsl(config, ["python3", "-c", code]))
    if (record["distribution"].get("ID") != config["expected_distribution_id"]
            or record["distribution"].get("VERSION_ID") != config["expected_distribution_version"]
            or record["architecture"] != config["expected_host_arch"]):
        raise RuntimeError("WSL_DEBIAN_IDENTITY_MISMATCH")
    record.update(generated_at=now(), source_config_sha256=sha256(CONFIG), runtime_environment_passed=False)
    record["missing_dependencies"] = [key for key, value in record["packages"].items() if not value.startswith("install ok installed ")]
    write_json(Path(config["windows_root"]) / "state/preflight.json", record)
    return record


def fetch_component(config, name):
    guard(config)
    source = config["sources"][name]
    commit = source["commit"]
    if not re.fullmatch(r"[0-9a-f]{40}", commit) or not source["url"].startswith("https://github.com/"):
        raise RuntimeError("WINDOWS_ARM_STACK_SOURCE_PIN_INVALID")
    # 下载留在 Windows 进程中，低内存时不会为 Git 操作唤醒整个 WSL2 VM。
    directory = Path(config["windows_root"]) / "sources" / name
    directory.mkdir(parents=True, exist_ok=True)
    if not (directory / ".git").is_dir():
        if any(directory.iterdir()):
            raise RuntimeError("WINDOWS_ARM_STACK_SOURCE_DIRECTORY_NOT_EMPTY")
        git(directory, ["init", "-q"])
        git(directory, ["remote", "add", "origin", source["url"]])
    if git(directory, ["remote", "get-url", "origin"]).stdout.strip() != source["url"]:
        raise RuntimeError("WINDOWS_ARM_STACK_SOURCE_ORIGIN_CHANGED")
    if git(directory, ["status", "--porcelain"]).stdout.strip():
        raise RuntimeError("WINDOWS_ARM_STACK_SOURCE_WORKTREE_CHANGED")
    if git(directory, ["cat-file", "-e", commit + "^{commit}"], check=False).returncode:
        git(directory, ["fetch", "--depth", "1", "origin", commit], timeout=900)
    git(directory, ["checkout", "--detach", "-q", commit])
    actual = git(directory, ["rev-parse", "HEAD"]).stdout.strip()
    if actual != commit:
        raise RuntimeError("WINDOWS_ARM_STACK_SOURCE_COMMIT_MISMATCH")
    record = {"name": name, **source, "path": str(directory), "fetched_at": now(), "verified_commit": actual,
              "submodules_status": "not_fetched", "build_status": "not_run"}
    if name == "secureboot_objects":
        certificates = []
        for certificate in config["certificates"]:
            local_path = Path(config["windows_root"]) / "sources/secureboot_objects" / certificate["path"]
            if sha256(local_path) != certificate["sha256"]:
                raise RuntimeError("MICROSOFT_SECURE_BOOT_CERTIFICATE_HASH_MISMATCH")
            certificates.append(certificate)
        record["certificates"] = certificates
    write_json(Path(config["windows_root"]) / "state/sources" / (name + ".json"), record)
    return record


def git(directory, arguments, *, check=True, timeout=120):
    environment = dict(os.environ, GIT_TERMINAL_PROMPT="0")
    # 单次配置；不改变全局 Git 设置，也不依赖用户的凭据助手。
    result = subprocess.run(["git", "-c", "credential.helper=", "-c", "core.autocrlf=false",
                             "-c", "core.filemode=false", "-c", "core.symlinks=true",
                             "-c", "safe.directory=" + directory.as_posix(), "-C", str(directory), *arguments],
                            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout,
                            env=environment, check=False)
    if check and result.returncode:
        raise RuntimeError("WINDOWS_ARM_STACK_GIT_FAILED:" + result.stderr.strip()[-1500:])
    return result


def build_component(config, name):
    guard(config, build=True)
    preflight = inspect(config)
    if preflight["missing_dependencies"]:
        raise RuntimeError("WINDOWS_ARM_STACK_BUILD_DEPENDENCIES_MISSING:" + ",".join(preflight["missing_dependencies"]))
    source = Path(config["windows_root"]) / "state/sources" / (name + ".json")
    if not source.is_file() or json.loads(source.read_text())["verified_commit"] != config["sources"][name]["commit"]:
        raise RuntimeError("WINDOWS_ARM_STACK_SOURCE_RECEIPT_MISSING_OR_CHANGED")
    source_path = config["wsl_root"] + "/sources/" + name
    actual = wsl(config, ["git", "-C", source_path, "rev-parse", "HEAD"])
    if actual != config["sources"][name]["commit"] or wsl(config, ["git", "-C", source_path, "diff", "--name-only", "HEAD"]):
        raise RuntimeError("WINDOWS_ARM_STACK_SOURCE_CHANGED")
    context = hashlib.sha256((sha256(CONFIG) + sha256(BUILD_SCRIPT)).encode()).hexdigest()
    destination = Path(config["windows_root"]) / "state/builds" / name
    context_file = destination / "context.json"
    if context_file.is_file() and json.loads(context_file.read_text())["context_sha256"] != context:
        raise RuntimeError("WINDOWS_ARM_STACK_BUILD_CONTEXT_CHANGED")
    write_json(context_file, {"context_sha256": context, "started_at": now()})
    receipt = destination / "result.json"
    if receipt.is_file():
        previous = json.loads(receipt.read_text())
        if previous.get("exit_code") == 0 and all(Path(item["path"]).is_file() and sha256(Path(item["path"])) == item["sha256"] for item in previous.get("outputs", [])) and previous.get("outputs"):
            return previous
    script_path = "/mnt/e/codex/PartyOps/.publish-github/qa/vm-lab/scripts/build-windows-arm64-stack-wsl.sh"
    log = destination / "build.log"
    environment = dict(os.environ)
    try:
        with log.open("w", encoding="utf-8") as output:
            process = subprocess.run(["wsl.exe", "-d", config["wsl_distribution"], "--exec", "bash", script_path, name, str(config["build_jobs"])],
                                     stdout=output, stderr=subprocess.STDOUT, timeout=7200, env=environment, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        write_json(receipt, {"component": name, "context_sha256": context, "exit_code": None,
                            "completed_at": now(), "error": type(exc).__name__,
                            "log": {"path": str(log), "sha256": sha256(log)}, "outputs": [],
                            "runtime_environment_passed": False})
        raise
    outputs_by_name = {"libtpms": ["prefix/lib/libtpms.so.0"], "swtpm": ["prefix/bin/swtpm"],
                       "qemu": ["prefix/bin/qemu-system-aarch64", "prefix/bin/qemu-img"],
                       "edk2": ["firmware/QEMU_EFI.fd", "firmware/QEMU_VARS.fd"]}
    # Linux 共享库是相对符号链接，Windows 侧不直接解析；用 WSL 报告实路径及哈希。
    output_code = r'''
import hashlib,json,sys
from pathlib import Path
root=Path(sys.argv[1]); windows_root=sys.argv[2]; result=[]
for relative in json.loads(sys.argv[3]):
 path=(root/relative).resolve()
 if path.is_file() and path.is_relative_to(root):
  result.append(dict(path=windows_root+'/'+path.relative_to(root).as_posix(),sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
print(json.dumps(result))
'''
    outputs = json.loads(wsl(config, ["python3", "-c", output_code, config["wsl_root"], config["windows_root"], json.dumps(outputs_by_name[name])]))
    record = {"component": name, "context_sha256": context, "exit_code": process.returncode,
              "completed_at": now(), "log": {"path": str(log), "sha256": sha256(log)}, "outputs": outputs,
              "runtime_environment_passed": False}
    write_json(receipt, record)
    if process.returncode or len(outputs) != len(outputs_by_name[name]):
        raise RuntimeError("WINDOWS_ARM_STACK_COMPONENT_BUILD_FAILED:" + name)
    return record


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("inspect", "fetch", "build"))
    parser.add_argument("--component", choices=("all", "libtpms", "swtpm", "qemu", "edk2", "secureboot_objects"), default="all")
    args = parser.parse_args()
    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    try:
        if args.action == "inspect":
            info = inspect(config)
            print(json.dumps({"status": "inspected", "missing_dependencies": info["missing_dependencies"], "runtime_environment_passed": False}, ensure_ascii=False))
            return 0
        names = list(config["sources"]) if args.component == "all" else [args.component]
        for name in names:
            if args.action == "build" and name == "secureboot_objects":
                continue
            print(json.dumps({"component": name, "action": args.action, "stage": "starting"}), flush=True)
            result = fetch_component(config, name) if args.action == "fetch" else build_component(config, name)
            print(json.dumps({"component": name, "action": args.action, "stage": "completed", "commit": result.get("verified_commit"), "runtime_environment_passed": False}), flush=True)
        return 0
    except (RuntimeError, OSError, ValueError, KeyError, subprocess.TimeoutExpired) as exc:
        print(json.dumps({"status": "blocked", "error": str(exc), "runtime_environment_passed": False}, ensure_ascii=False))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
