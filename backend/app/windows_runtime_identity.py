"""Windows 安装包身份：分别保留宿主 ISA、进程 ISA 和发布线，不把仿真当原生。"""
from __future__ import annotations

import ctypes
import hashlib
import json
import re
from ctypes import wintypes
from pathlib import Path

PE_MACHINES = {0x014C: "x86", 0x8664: "amd64", 0xAA64: "arm64", 0xA641: "arm64ec"}
CORE_CAPABILITIES = ("host", "collaboration", "database", "files", "archives", "backup", "ocr")
# 只列已经存在的包线。ARM64 原生包尚未建立，不因为宿主 ISA 为 ARM64 而添加。
PACKAGE_PROFILES = {
    ("windows", "amd64", "full"): (*CORE_CAPABILITIES, "semantic_rerank", "local_llm"),
    ("windows7", "amd64", "legacy-smart"): (*CORE_CAPABILITIES, "semantic_rerank"),
    ("windows7", "x86", "legacy-core"): CORE_CAPABILITIES,
}


def executable_architecture(path: Path) -> str:
    """读取实际可执行文件 PE Machine；不依赖会在仿真中变化的环境变量。"""
    with path.open("rb") as stream:
        header = stream.read(64)
        if len(header) != 64 or header[:2] != b"MZ":
            raise ValueError("WINDOWS_EXECUTABLE_PE_INVALID")
        offset = int.from_bytes(header[60:64], "little")
        if offset < 64 or offset > 1024 * 1024:
            raise ValueError("WINDOWS_EXECUTABLE_PE_INVALID")
        stream.seek(offset)
        pe = stream.read(6)
    if len(pe) != 6 or pe[:4] != b"PE\0\0":
        raise ValueError("WINDOWS_EXECUTABLE_PE_INVALID")
    machine = PE_MACHINES.get(int.from_bytes(pe[4:6], "little"))
    if machine is None:
        raise ValueError("WINDOWS_EXECUTABLE_ISA_UNKNOWN")
    return machine


def query_machine_types(executable: Path) -> tuple[str, str]:
    """IsWow64Process2 明确区分宿主和进程；Win7 回退仅支持真实 x86/x64 OS。"""
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    current_process = kernel.GetCurrentProcess
    current_process.restype = wintypes.HANDLE
    try:
        query = kernel.IsWow64Process2
    except AttributeError:
        # Win7 尚无 IsWow64Process2；该系统没有 Windows ARM64 仿真层。
        class SystemInfo(ctypes.Structure):
            _fields_ = [("architecture", wintypes.WORD), ("reserved", wintypes.WORD),
                        ("page_size", wintypes.DWORD), ("minimum", ctypes.c_void_p),
                        ("maximum", ctypes.c_void_p), ("mask", ctypes.c_size_t),
                        ("processors", wintypes.DWORD), ("processor_type", wintypes.DWORD),
                        ("granularity", wintypes.DWORD), ("level", wintypes.WORD), ("revision", wintypes.WORD)]
        query_native = kernel.GetNativeSystemInfo
        query_native.argtypes = [ctypes.POINTER(SystemInfo)]
        query_native.restype = None
        system = SystemInfo()
        query_native(ctypes.byref(system))
        legacy_native_name = {0: "x86", 9: "amd64"}.get(system.architecture)
        if legacy_native_name is None:
            raise ValueError("WINDOWS_NATIVE_ISA_UNPROVEN")
        return executable_architecture(executable), legacy_native_name
    query.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.USHORT), ctypes.POINTER(wintypes.USHORT)]
    query.restype = wintypes.BOOL
    process, native = wintypes.USHORT(), wintypes.USHORT()
    if not query(current_process(), ctypes.byref(process), ctypes.byref(native)):
        raise ValueError("WINDOWS_MACHINE_TYPES_QUERY_FAILED")
    process_name = PE_MACHINES.get(process.value or native.value)
    native_name = PE_MACHINES.get(native.value)
    if process_name is None or native_name is None:
        raise ValueError("WINDOWS_NATIVE_ISA_UNPROVEN")
    return process_name, native_name


def execution_kind(process: str, native: str, version: str) -> str:
    if process == native and process in {"x86", "amd64", "arm64"}:
        return "native"
    if process == "x86" and native == "amd64":
        return "wow64"
    parts = version.split(".")
    if native == "arm64" and len(parts) >= 3 and all(value.isdigit() for value in parts[:3]):
        major, _minor, build = map(int, parts[:3])
        if major >= 10 and process == "x86":
            return "windows-x86-emulation"
        if major >= 10 and build >= 22000 and process == "amd64":
            return "windows-x64-emulation"
    raise ValueError("WINDOWS_PROCESS_OS_COMBINATION_UNSUPPORTED")


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("WINDOWS_RELEASE_MANIFEST_DUPLICATE_KEY")
        result[key] = value
    return result


def read_installed_manifest(executable: Path) -> dict:
    manifest_path = executable.parent / "release-manifest.json"
    if manifest_path.is_symlink() or executable.is_symlink():
        raise ValueError("WINDOWS_RELEASE_MANIFEST_LINK_REJECTED")
    with manifest_path.open("rb") as stream:
        raw = stream.read(8 * 1024 * 1024 + 1)
    if len(raw) > 8 * 1024 * 1024:
        raise ValueError("WINDOWS_RELEASE_MANIFEST_TOO_LARGE")
    manifest = json.loads(raw.decode("utf-8-sig"), object_pairs_hook=_unique_object)
    if (not isinstance(manifest, dict) or type(manifest.get("schema_version")) is not int
            or manifest.get("schema_version") != 1
            or manifest.get("product") != "PartyOps" or not isinstance(manifest.get("files"), list)):
        raise ValueError("WINDOWS_RELEASE_MANIFEST_INVALID")
    candidates = [entry for entry in manifest["files"] if isinstance(entry, dict)
                  and str(entry.get("path", "")).replace("\\", "/").casefold() == executable.name.casefold()]
    if len(candidates) != 1 or not re.fullmatch(r"[0-9a-f]{64}", str(candidates[0].get("sha256", ""))):
        raise ValueError("WINDOWS_CURRENT_EXECUTABLE_NOT_IN_MANIFEST")
    digest = hashlib.sha256()
    size = 0
    with executable.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            size += len(chunk)
            digest.update(chunk)
    if digest.hexdigest() != candidates[0]["sha256"] or size != candidates[0].get("size"):
        raise ValueError("WINDOWS_CURRENT_EXECUTABLE_MANIFEST_MISMATCH")
    return manifest


def installed_identity(executable: Path, version: str) -> dict[str, object]:
    """本地清单/当前 PE 的绑定不等同发布签名或完整功能验收。未知结果不提供更新线。"""
    result: dict[str, object] = {
        "architecture": "", "os_architecture": "", "process_architecture": "",
        "binary_architecture": "", "execution_kind": "unknown", "package_platform": "",
        "runtime_profile": "unsupported", "capabilities": [],
        "package_identity_status": "unsupported", "package_identity_reason": "WINDOWS_PACKAGE_IDENTITY_UNPROVEN",
    }
    try:
        binary = executable_architecture(executable)
        process, native = query_machine_types(executable)
        result.update(architecture=process, process_architecture=process, os_architecture=native,
                      binary_architecture=binary)
        if binary != process:
            raise ValueError("WINDOWS_PE_PROCESS_ISA_MISMATCH")
        kind = execution_kind(process, native, version)
        result["execution_kind"] = kind
        manifest = read_installed_manifest(executable)
        if manifest.get("architecture") != process:
            raise ValueError("WINDOWS_PACKAGE_PROCESS_ISA_MISMATCH")
        package_key = (manifest.get("platform"), process, manifest.get("runtime_profile"))
        if package_key not in PACKAGE_PROFILES:
            raise ValueError("WINDOWS_PACKAGE_CHANNEL_UNSUPPORTED")
        # 与安装器 MinVersion 一致；未知 OS 版本不能作为已支持运行环境。
        parts = version.split(".")
        if len(parts) < 3 or not all(value.isdigit() for value in parts[:3]):
            raise ValueError("WINDOWS_OS_VERSION_UNPROVEN")
        os_version = tuple(map(int, parts[:3]))
        minimum = (10, 0, 0) if manifest["platform"] == "windows" else (6, 1, 7601)
        if os_version < minimum:
            raise ValueError("WINDOWS_PACKAGE_OS_VERSION_UNSUPPORTED")
        result.update(package_platform=manifest["platform"], runtime_profile=manifest["runtime_profile"],
                      capabilities=list(PACKAGE_PROFILES[package_key]), package_identity_status="verified",
                      package_identity_reason="")
    except (OSError, ValueError, TypeError, AttributeError) as exc:
        reason = str(exc)
        result["package_identity_reason"] = reason if re.fullmatch(r"[A-Z0-9_]+", reason) else "WINDOWS_PACKAGE_IDENTITY_UNPROVEN"
    return result
