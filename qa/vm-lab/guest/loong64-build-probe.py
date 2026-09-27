"""在原版 Loong64 Guest 留存构建前置证据；仓库候选包不代表产品已捆绑。"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import re
import shutil
import struct
import subprocess
import sys
import sysconfig
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import urlsplit

PACKAGES = (
    "build-essential", "gcc", "g++", "binutils", "make", "cmake", "ninja-build", "pkg-config",
    "git", "curl", "ca-certificates", "patchelf", "zstd", "unzip", "autoconf", "automake", "libtool",
    "python3", "python3-dev", "python3-venv", "python3-pip", "python3-build", "python3-wheel",
    "python3-setuptools", "python3-numpy", "python3-onnxruntime", "onnxruntime", "libonnxruntime-dev",
    "python3-tokenizers", "python3-pymupdf", "python3-fitz", "python3-pil", "python3-lxml",
    "python3-cryptography", "python3-pyinstaller", "pyinstaller", "rustc", "cargo", "clang",
    "libssl-dev", "libffi-dev", "libbz2-dev", "liblzma-dev", "libsqlite3-dev", "libreadline-dev",
    "tesseract-ocr", "tesseract-ocr-chi-sim", "tesseract-ocr-eng", "libtesseract-dev", "libleptonica-dev",
    "libpng-dev", "zlib1g-dev", "libopenblas-dev", "libprotobuf-dev", "protobuf-compiler",
    "libreoffice", "libreoffice-core", "libreoffice-writer", "mono-devel", "mono-runtime",
    "libmono-2.0-dev", "wps-office",
)
TOOLS = (
    ("gcc", "--version"), ("g++", "--version"), ("ld", "--version"), ("cmake", "--version"),
    ("ninja", "--version"), ("make", "--version"), ("rustc", "--version"), ("cargo", "--version"),
    ("pkg-config", "--version"), ("patchelf", "--version"), ("python3.11", "--version"),
    ("tesseract", "--version"), ("soffice", "--version"), ("mono", "--version"),
)
MINIMUM_GLIBC = (2, 36)  # 上游首次加入 LoongArch Linux ABI；不改变其他 ISA 的 2.17 基线。
BUILD_TOOLS = (
    "build-essential", "gcc", "g++", "binutils", "make", "cmake", "ninja-build", "pkg-config",
    "git", "curl", "ca-certificates", "patchelf", "zstd", "unzip", "autoconf", "automake", "libtool",
    "python3-dev", "python3-venv", "python3-pip", "python3-build", "python3-wheel", "python3-setuptools",
    "libssl-dev", "libffi-dev", "libbz2-dev", "liblzma-dev", "libsqlite3-dev", "libreadline-dev",
)


def read(path):
    item = Path(path)
    return item.read_text(encoding="utf-8", errors="replace") if item.is_file() else ""


def digest(value):
    return hashlib.sha256(value).hexdigest()


def redact_urls(value):
    """保留仓库来源证据，剔除可能存在的 URL 用户信息和查询参数。"""
    def redact(match):
        parsed = urlsplit(match[0])
        host = parsed.hostname or ""
        if parsed.port:
            host += ":" + str(parsed.port)
        return parsed.scheme + "://" + host + parsed.path
    return re.sub(r"https?://[^\s\]\)]+", redact, value)


def command(args, timeout=90):
    try:
        completed = subprocess.run(args, capture_output=True, text=True,
                                   timeout=timeout, check=False, env={**os.environ, "LC_ALL": "C", "LANG": "C"})
        return {"command": list(args), "exit_code": completed.returncode,
                "stdout": redact_urls(completed.stdout), "stderr": redact_urls(completed.stderr)}
    except FileNotFoundError:
        return {"command": list(args), "exit_code": 127, "stdout": "", "stderr": "COMMAND_NOT_INSTALLED"}
    except subprocess.TimeoutExpired:
        return {"command": list(args), "exit_code": 124, "stdout": "", "stderr": "COMMAND_TIMEOUT"}


def validate_owner(expected_uuid, purpose):
    actual_arch = {"loong64": "loongarch64"}.get(platform.machine(), platform.machine())
    marker = json.loads(read("/etc/partyops-vm-lab.json") or "{}")
    hardware = command(["sudo", "-n", "cat", "/sys/class/dmi/id/product_uuid"])
    if marker != {"uuid": expected_uuid, "purpose": purpose} or hardware["stdout"].strip().lower() != expected_uuid.lower():
        raise RuntimeError("LOONG_BUILD_GUEST_OWNERSHIP_MISMATCH")
    if actual_arch != "loongarch64" or command(["dpkg", "--print-architecture"])["stdout"].strip() != "loong64":
        raise RuntimeError("LOONG_BUILD_GUEST_ISA_MISMATCH")
    if os.getuid() == 0:
        raise RuntimeError("LOONG_BUILD_STANDARD_USER_REQUIRED")


def system_probe():
    libc = command(["getconf", "GNU_LIBC_VERSION"])
    match = re.fullmatch(r"glibc (\d+)\.(\d+)", libc["stdout"].strip())
    actual = tuple(map(int, match.groups())) if match else ()
    libpython = Path(sysconfig.get_config_var("LIBDIR") or "/nonexistent") / (sysconfig.get_config_var("LDLIBRARY") or "missing")
    return {"os_release_raw": read("/etc/os-release"), "os_version_raw": read("/etc/os-version"),
            "deepin_version_raw": read("/etc/deepin-version"), "arch": platform.machine(),
            "boot_id": read("/proc/sys/kernel/random/boot_id").strip(), "kernel": platform.release(),
            "python": {"version": platform.python_version(), "executable": sys.executable,
                       "soabi": sysconfig.get_config_var("SOABI"), "libpython": str(libpython),
                       "shared": bool(sysconfig.get_config_var("Py_ENABLE_SHARED")), "libpython_exists": libpython.is_file()},
            "glibc": libc, "loong64_minimum_glibc": "2.36", "minimum_glibc_met": bool(actual and actual >= MINIMUM_GLIBC),
            "release_build_sysroot_pinned": False,
            "resources": {"memory_raw": read("/proc/meminfo"), "disk": command(["df", "-Pk", str(Path.home())])}}


def official_repository(url):
    host = (urlsplit(url).hostname or "").lower()
    # 原版 25.2.0 安装后的 sources 和签名索引实测包含此应用商店地址。
    # 证据：loong-prep-8b28895573d0/repositories.json；只补充精确主机名。
    return host == "com-store-packages.uniontech.com" or any(
        host == domain or host.endswith("." + domain) for domain in ("deepin.com", "deepin.org"))


def sources_probe():
    paths = [Path("/etc/apt/sources.list")]
    paths.extend(sorted(Path("/etc/apt/sources.list.d").glob("*.list")))
    paths.extend(sorted(Path("/etc/apt/sources.list.d").glob("*.sources")))
    records, uris = [], []
    for path in paths:
        if not path.is_file():
            continue
        raw = read(path)
        records.append({"path": str(path), "sha256": digest(raw.encode()), "content": redact_urls(raw)})
        active = "\n".join(line for line in raw.splitlines() if not line.lstrip().startswith("#"))
        uris.extend(re.findall(r"(?:https?|file|cdrom):[^\s]+", active))
    http = [url for url in uris if url.startswith(("http:", "https:"))]
    # 原版镜像里的其他镜像源需要人工核验；不修改 sources.list 来掩盖来源。
    return {"files": records, "http_origins": [redact_urls(url) for url in http],
            "only_deepin_official_http_origins": bool(http) and all(official_repository(url) for url in http),
            "non_http_sources_present": any(not url.startswith(("http:", "https:")) for url in uris)}


def repositories_probe():
    candidates = {}
    for package in PACKAGES:
        raw = command(["apt-cache", "policy", package])
        match = re.search(r"^\s*Candidate:\s*(\S+)", raw["stdout"], re.MULTILINE)
        candidate = match[1] if match and match[1] != "(none)" else None
        candidates[package] = {"candidate": candidate, "available_in_cached_index": bool(candidate), "raw": raw}
    return {"sources": sources_probe(), "index_policy": command(["apt-cache", "policy"]),
            "index_files": [{"name": path.name, "bytes": path.stat().st_size, "mtime_ns": path.stat().st_mtime_ns}
                            for path in sorted(Path("/var/lib/apt/lists").glob("*")) if path.is_file()],
            "installed": command(["dpkg-query", "-W", "-f=${binary:Package}\t${Architecture}\t${Version}\t${db:Status-Status}\n", *PACKAGES]),
            "candidates": candidates, "bundled_runtime_verified": False}


def toolchain_probe():
    return {"commands": [command(args) for args in TOOLS],
            "gcc_target": command(["gcc", "-dumpmachine"]),
            "python_packages": command([sys.executable, "-m", "pip", "list", "--format=json", "--disable-pip-version-check"]),
            "loaders": command(["sh", "-c", "find /lib /lib64 /usr/lib -name 'ld-linux-loongarch*.so*' -print 2>/dev/null"]),
            "runtime_bundling_verified": False}


def refresh_index(purpose):
    if purpose != "disposable-build":
        raise RuntimeError("APT_REFRESH_REQUIRES_DISPOSABLE_BUILD_CLONE")
    sources = sources_probe()
    if not sources["only_deepin_official_http_origins"] or sources["non_http_sources_present"]:
        raise RuntimeError("APT_REFRESH_REQUIRES_VERIFIED_DEEPIN_HTTP_SOURCES")
    # 禁用隐式重试，超时和限流由控制器保留原始结果，避免无限占用 TCG Guest。
    return command(["sudo", "-n", "apt-get", "-o", "Acquire::Retries=0", "-o", "Acquire::http::Timeout=30",
                    "-o", "Acquire::https::Timeout=30", "update"], timeout=600)


def install_build_tools(purpose):
    """在独立构建盘安装已查明候选版本的工具；记录模拟事务及实际安装结果。"""
    if purpose != "disposable-build":
        raise RuntimeError("BUILD_TOOLS_INSTALL_REQUIRES_DISPOSABLE_BUILD_CLONE")
    if shutil.disk_usage(Path.home()).free < 6 * 1024**3:
        raise RuntimeError("BUILD_TOOLS_INSTALL_REQUIRES_6_GIB_FREE")
    sources = sources_probe()
    if not sources["only_deepin_official_http_origins"] or sources["non_http_sources_present"]:
        raise RuntimeError("BUILD_TOOLS_REQUIRE_VERIFIED_DEEPIN_HTTP_SOURCES")
    index = repositories_probe()
    # 同时拒绝遗留 APT 索引引用的非官方 HTTP 源；不能仅凭新 sources.list 追认旧索引。
    for url in re.findall(r"https?://[^\s]+", index["index_policy"]["stdout"]):
        if not official_repository(url):
            raise RuntimeError("BUILD_TOOLS_INDEX_ORIGIN_NOT_VERIFIED")
    versions = {name: index["candidates"][name]["candidate"] for name in BUILD_TOOLS}
    missing = [name for name, version in versions.items() if not version]
    if missing:
        return {"status": "blocked", "missing": missing, "index": index, "installed": False}
    pins = [name + "=" + version for name, version in versions.items()]
    simulate = command(["apt-get", "--simulate", "--no-install-recommends", "install", *pins], timeout=180)
    if simulate["exit_code"]:
        return {"status": "blocked", "versions": versions, "simulation": simulate, "installed": False}
    installed = command(["sudo", "-n", "env", "DEBIAN_FRONTEND=noninteractive", "apt-get", "--yes",
                         "--no-install-recommends", "-o", "Acquire::Retries=0", "-o", "Acquire::http::Timeout=30",
                         "-o", "Acquire::https::Timeout=30", "install", *pins], timeout=1800)
    return {"status": "installed" if installed["exit_code"] == 0 else "failed", "versions": versions,
            "simulation": simulate, "transaction": installed,
            "installed_packages": command(["dpkg-query", "-W", "-f=${binary:Package}\t${Architecture}\t${Version}\n"]),
            "runtime_bundling_verified": False, "partyops_runtime_passed": False}


def elf_identity(path):
    raw = Path(path).read_bytes()[:64]
    if len(raw) < 64 or raw[:4] != b"\x7fELF" or raw[4:6] != b"\x02\x01":
        raise RuntimeError("LOONG_BUILD_SMOKE_ELF_CLASS_OR_ENDIAN_MISMATCH")
    machine, flags = struct.unpack_from("<H", raw, 18)[0], struct.unpack_from("<I", raw, 48)[0]
    if machine != 258 or flags & 7 != 3:
        raise RuntimeError("LOONG_BUILD_SMOKE_ELF_MACHINE_OR_LP64D_MISMATCH")
    return {"class": 64, "endian": "little", "machine": machine, "flags": flags, "float_abi": "double"}


def smoke(purpose, run_id):
    if purpose != "disposable-build":
        raise RuntimeError("COMPILER_SMOKE_REQUIRES_DISPOSABLE_BUILD_CLONE")
    if not re.fullmatch(r"loong-prep-[a-f0-9]{12}", run_id):
        raise RuntimeError("INVALID_LOONG_BUILD_RUN_ID")
    base = Path.home().resolve() / "partyops-loong64-build"
    root = base / run_id
    if base.is_symlink() or root.is_symlink() or Path.home().name != "partyopsqa":
        raise RuntimeError("LOONG_BUILD_PATH_MISMATCH")
    root.mkdir(parents=True, exist_ok=False)
    result = {"directory": str(root), "commands": [], "status": "failed", "partyops_runtime_passed": False}
    for compiler, suffix, source in (("gcc", "c", '#include <stdio.h>\nint main(void){puts("partyops-loong64-toolchain");return 0;}\n'),
                                     ("g++", "cc", '#include <iostream>\nint main(){std::cout << "partyops-loong64-toolchain\\n";}\n')):
        code, binary = root / ("probe." + suffix), root / ("probe-" + compiler.replace("+", "p"))
        code.write_text(source, encoding="utf-8")
        built = command([compiler, "-O2", str(code), "-o", str(binary)], timeout=180)
        result["commands"].append(built)
        if built["exit_code"]:
            return result
        result[compiler + "_elf"] = elf_identity(binary)
        result[compiler + "_sha256"] = digest(binary.read_bytes())
        result["commands"].append(command([str(binary)]))
        result["commands"].append(command(["readelf", "-h", "-l", "-V", str(binary)]))
    result["commands"].append(command([sys.executable, "-c", "import ssl,sqlite3,ctypes,zlib,lzma,bz2,json; print(json.dumps({'openssl':ssl.OPENSSL_VERSION,'sqlite':sqlite3.sqlite_version,'python_stdlib_imports':True}))"]))
    if all(item["exit_code"] == 0 for item in result["commands"]):
        result["status"] = "passed"
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--uuid", required=True)
    parser.add_argument("--purpose", required=True, choices=("disposable-qa", "disposable-build"))
    parser.add_argument("--phase", required=True, choices=("system", "repositories", "toolchain", "refresh-index", "install-build-tools", "smoke"))
    parser.add_argument("--run-id", required=True)
    args = parser.parse_args()
    validate_owner(args.uuid, args.purpose)
    functions = {"system": system_probe, "repositories": repositories_probe, "toolchain": toolchain_probe,
                 "refresh-index": lambda: refresh_index(args.purpose), "install-build-tools": lambda: install_build_tools(args.purpose),
                 "smoke": lambda: smoke(args.purpose, args.run_id)}
    result = {"schema_version": 1, "at": datetime.now(timezone.utc).isoformat(), "guest_uuid": args.uuid,
              "phase": args.phase, "scope": "loong64-build-preparation", "runtime_environment_passed": False,
              "result": functions[args.phase]()}
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
