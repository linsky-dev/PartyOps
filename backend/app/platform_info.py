"""跨平台运行环境探测与旧 1.4.x 平台字段兼容。"""

from __future__ import annotations

import platform
import sys
from functools import lru_cache
from pathlib import Path

from .linux_runtime_identity import installed_linux_identity
from .windows_runtime_identity import PACKAGE_PROFILES, installed_identity

CORE_CAPABILITIES = (
    "host",
    "collaboration",
    "database",
    "files",
    "archives",
    "backup",
    "ocr",
)
AI_CAPABILITIES = ("semantic_rerank", "local_llm")


@lru_cache(maxsize=4)
def _frozen_linux_identity(executable: str, architecture: str) -> dict[str, object]:
    # 冻结入口在进程生命周期内不变；避免每次查询本地能力时重算大型 ELF 哈希。
    return installed_linux_identity(Path(executable), architecture)


def normalize_architecture(value: str | None = None) -> str:
    """把操作系统架构名称收敛为发布清单使用的名称。"""

    raw = (value or platform.machine()).strip().lower()
    return {
        "x86_64": "amd64",
        "amd64": "amd64",
        "aarch64": "arm64",
        "arm64": "arm64",
        "loongarch64": "loong64",
        "loong64": "loong64",
        "x86": "x86",
        "i386": "x86",
        "i486": "x86",
        "i586": "x86",
        "i686": "x86",
    }.get(raw, raw[:16])


def read_os_release(path: Path = Path("/etc/os-release")) -> dict[str, str]:
    """读取 freedesktop os-release；损坏或缺失时返回空字典。"""

    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return {}
    values: dict[str, str] = {}
    for line in lines:
        stripped = line.strip()
        if not stripped or stripped.startswith("#") or "=" not in stripped:
            continue
        key, raw = stripped.split("=", 1)
        key = key.strip().upper()
        if not key.replace("_", "").isalnum():
            continue
        value = raw.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in {'"', "'"}:
            value = value[1:-1]
        values[key] = value[:160]
    return values


def _linux_package_format(values: dict[str, str]) -> str:
    tokens = {
        item
        for item in " ".join(
            (values.get("ID", ""), values.get("ID_LIKE", ""))
        ).lower().replace(",", " ").split()
        if item
    }
    if tokens & {"openeuler", "rhel", "fedora", "centos", "suse", "opensuse"}:
        return "rpm"
    if tokens & {"debian", "ubuntu", "deepin", "uos", "kylin", "neokylin"}:
        return "deb"
    return ""

def detect_platform_info(*, os_release_path: Path = Path("/etc/os-release")) -> dict[str, object]:
    """返回心跳、安装器和更新选包共享的稳定平台契约。"""

    architecture = normalize_architecture()
    if sys.platform == "win32":
        release, version, _csd, _ptype = platform.win32_ver()
        is_windows7 = release == "7" or version.startswith("6.1")
        distribution = "windows7" if is_windows7 else "windows"
        if getattr(sys, "frozen", False):
            # 包能力和更新通道属于实际安装包；OS 仅说明运行环境，不能把兼容包升级成 full。
            return {
                "platform_family": "windows", "distribution": distribution,
                "distribution_version": release or version, "package_format": "exe", "platform": "windows",
                **installed_identity(Path(sys.executable).resolve(), version),
            }
        runtime_profile = (
            "legacy-core" if is_windows7 and architecture == "x86"
            else "legacy-smart" if is_windows7 and architecture == "amd64"
            else "unsupported" if architecture not in {"amd64", "x86"}
            else "full"
        )
        capabilities = list(CORE_CAPABILITIES) if runtime_profile != "unsupported" else []
        if runtime_profile == "full":
            capabilities.extend(AI_CAPABILITIES)
        elif runtime_profile == "legacy-smart":
            capabilities.append("semantic_rerank")
        return {
            "platform_family": "windows",
            "distribution": distribution,
            "distribution_version": release or version,
            "package_format": "exe",
            "architecture": architecture,
            "runtime_profile": runtime_profile,
            "capabilities": capabilities,
            "platform": "windows",
        }
    if sys.platform.startswith("linux"):
        values = read_os_release(os_release_path)
        distribution = values.get("ID", "linux").strip().lower() or "linux"
        result: dict[str, object] = {
            "platform_family": "linux",
            "distribution": distribution[:40],
            "distribution_version": values.get("VERSION_ID", "")[:40],
            "package_format": _linux_package_format(values),
            "architecture": architecture,
            "runtime_profile": "full",
            "capabilities": [*CORE_CAPABILITIES, *AI_CAPABILITIES],
            # 1.4.x 旧服务只认识 windows/uos；精确发行版由新字段承载。
            "platform": "uos",
        }
        if architecture == "loong64" and getattr(sys, "frozen", False):
            # 龙芯 core 只能由已安装清单及当前 ELF 共同证明，不能由 CPU 推断。
            identity = _frozen_linux_identity(sys.executable, architecture)
            result.update(identity)
            result["capabilities"] = list(identity["capabilities"])
        return result
    if sys.platform == "darwin":
        macos_version = platform.mac_ver()[0]
        capabilities = [*CORE_CAPABILITIES, *AI_CAPABILITIES]
        return {
            "platform_family": "macos",
            "distribution": "macos",
            "distribution_version": macos_version[:40],
            "package_format": "pkg",
            "architecture": architecture,
            "runtime_profile": "full",
            "capabilities": capabilities,
            "platform": "macos",
        }
    return {
        "platform_family": sys.platform[:24],
        "distribution": sys.platform[:40],
        "distribution_version": "",
        "package_format": "",
        "architecture": architecture,
        "runtime_profile": "unsupported",
        "capabilities": [],
        "platform": sys.platform[:40],
    }


def update_platform_key(info: dict[str, object]) -> str:
    """把平台信息映射为更新清单 format v3 的制品键。"""

    family = str(info.get("platform_family", "")).lower()
    distribution = str(info.get("distribution", "")).lower()
    package_format = str(info.get("package_format", "")).lower()
    if family == "windows":
        if "package_identity_status" in info:
            if info.get("package_identity_status") != "verified":
                return ""
            package = str(info.get("package_platform", ""))
            architecture = str(info.get("process_architecture", ""))
            profile = str(info.get("runtime_profile", ""))
            if info.get("architecture") != architecture or (package, architecture, profile) not in PACKAGE_PROFILES:
                return ""
            return package
        if info.get("runtime_profile") == "unsupported":
            return ""
        return "windows7" if distribution == "windows7" else "windows"
    if family == "linux" and package_format in {"deb", "rpm"}:
        if info.get("architecture") == "loong64" and "package_identity_status" in info:
            if (info.get("package_identity_status") != "verified"
                    or info.get("runtime_profile") != "core" or package_format != "deb"):
                return ""
        return f"linux-{package_format}"
    if family == "macos" and package_format == "pkg":
        return "macos"
    return ""
