"""静态验证 Windows 7 Legacy 冻结目录的 PE 架构、子系统和导入 API。"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

import pefile

MACHINES = {"amd64": 0x8664, "x86": 0x014C}

# ECMA-335 / CorHdr.h 的 CLR 映像标志。AnyCPU 托管程序集仍使用 I386
# Machine 值，但只含 IL，且不能要求或偏好 32 位，也不能带原生入口点。
COMIMAGE_FLAGS_ILONLY = 0x00000001
COMIMAGE_FLAGS_32BITREQUIRED = 0x00000002
COMIMAGE_FLAGS_NATIVE_ENTRYPOINT = 0x00000010
COMIMAGE_FLAGS_32BITPREFERRED = 0x00020000

# 下列 DLL/API 最早随 Windows 8/10 提供。Win7 制品出现任一项都说明冻结运行时
# 使用了错误的 SDK/工具链；KB2533623 提供的 AddDllDirectory 等不在禁止列表中。
FORBIDDEN_DLL_PREFIXES = (
    "api-ms-win-core-path-",
    "api-ms-win-core-winrt-",
    "api-ms-win-core-realtime-",
)
FORBIDDEN_IMPORTS = {
    "kernel32.dll": {
        "CopyFile2",
        "CreateFile2",
        "CreateFileMappingFromApp",
        "GetCurrentPackageFamilyName",
        "GetCurrentPackageFullName",
        "GetCurrentPackageId",
        "GetProcessMitigationPolicy",
        "GetSystemTimePreciseAsFileTime",
        "MapViewOfFileFromApp",
        "SetProcessMitigationPolicy",
        "SetThreadDescription",
        "WaitOnAddress",
        "WakeByAddressAll",
        "WakeByAddressSingle",
    },
    "shcore.dll": {"GetDpiForMonitor", "SetProcessDpiAwareness"},
    "bcryptprimitives.dll": {"ProcessPrng"},
    # 微软Win7兼容UCRT也通过该APISet导入Sleep，不能禁止整个DLL。
    "api-ms-win-core-synch-l1-2-0.dll": {"WaitOnAddress", "WakeByAddressAll", "WakeByAddressSingle"},
    "user32.dll": {
        "EnableNonClientDpiScaling",
        "GetDpiForSystem",
        "GetDpiForWindow",
        "GetSystemMetricsForDpi",
        "SetProcessDpiAwarenessContext",
    },
}


def _decode(value: bytes | None) -> str:
    return (value or b"").decode("ascii", errors="replace")


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _managed_corflags(image: pefile.PE) -> int | None:
    """读取 CLR 头标志；非托管或结构损坏时返回 None。"""

    directories = image.OPTIONAL_HEADER.DATA_DIRECTORY
    com_index = pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR"]
    if len(directories) <= com_index:
        return None
    descriptor = directories[com_index]
    if descriptor.VirtualAddress == 0 or descriptor.Size < 20:
        return None
    header = image.get_data(descriptor.VirtualAddress, 20)
    if len(header) < 20:
        return None
    size, major, _minor, metadata_rva, metadata_size, flags = struct.unpack_from(
        "<IHHIII", header
    )
    if size < 0x48 or major < 2 or metadata_rva == 0 or metadata_size == 0:
        return None
    return flags


def _is_managed_anycpu(image: pefile.PE) -> bool:
    """仅认可纯 IL、未绑定 32 位且没有原生入口点的 AnyCPU 程序集。"""

    if image.FILE_HEADER.Machine != MACHINES["x86"]:
        return False
    flags = _managed_corflags(image)
    if flags is None or not flags & COMIMAGE_FLAGS_ILONLY:
        return False
    disallowed = (
        COMIMAGE_FLAGS_32BITREQUIRED
        | COMIMAGE_FLAGS_32BITPREFERRED
        | COMIMAGE_FLAGS_NATIVE_ENTRYPOINT
    )
    return flags & disallowed == 0


def validate_pe(
    path: Path,
    architecture: str,
    verified_ucrt_hashes: dict[str, str],
    verified_universal_hashes: dict[Path, str] | None = None,
) -> list[str]:
    errors: list[str] = []
    try:
        image = pefile.PE(str(path), fast_load=False)
    except pefile.PEFormatError as exc:
        return [f"{path.name}: PE 文件无效：{exc}"]
    try:
        machine = image.FILE_HEADER.Machine
        universal_hashes = verified_universal_hashes or {}
        is_verified_universal = (
            path.resolve() in universal_hashes
            and _sha256(path) == universal_hashes[path.resolve()]
        )
        is_managed_anycpu = architecture == "amd64" and _is_managed_anycpu(image)
        if (
            machine != MACHINES[architecture]
            and not is_managed_anycpu
            and not is_verified_universal
        ):
            errors.append(
                f"{path.name}: 架构为 0x{machine:04x}，期望 {architecture}"
            )
        subsystem = (
            image.OPTIONAL_HEADER.MajorSubsystemVersion,
            image.OPTIONAL_HEADER.MinorSubsystemVersion,
        )
        # Microsoft 官方 app-local UCRT 的 APISet 转发器按设计支持 Win7，
        # 但 PE 子系统字段仍为 10.0。只对已由嵌入来源清单逐文件校验的
        # 转发器放行；其他 EXE/DLL/PYD 仍必须明确以 Win7 6.1 为上限。
        is_verified_ucrt_forwarder = (
            path.name.lower().startswith("api-ms-win-")
            and _sha256(path) == verified_ucrt_hashes.get(path.name.lower())
        )
        if subsystem > (6, 1) and not is_verified_ucrt_forwarder:
            errors.append(
                f"{path.name}: PE 子系统 {subsystem[0]}.{subsystem[1]} 高于 Win7 6.1"
            )
        for entry in getattr(image, "DIRECTORY_ENTRY_IMPORT", []):
            dll = _decode(entry.dll).lower()
            if any(dll.startswith(prefix) for prefix in FORBIDDEN_DLL_PREFIXES):
                errors.append(f"{path.name}: 导入 Win7 不支持的 DLL {dll}")
            forbidden = FORBIDDEN_IMPORTS.get(dll, set())
            for imported in entry.imports:
                name = _decode(imported.name)
                if name in forbidden:
                    errors.append(f"{path.name}: 导入 Win7 不支持的 API {dll}!{name}")
    finally:
        image.close()
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--architecture", choices=tuple(MACHINES), required=True)
    args = parser.parse_args()
    root = args.root.resolve()
    if not root.is_dir():
        print(f"Win7 PE 门禁失败：目录不存在：{root}", file=sys.stderr)
        return 2
    source_path = root / "ucrt-source.json"
    if not source_path.is_file():
        print("Win7 PE 门禁失败：缺少 UCRT 来源与哈希清单", file=sys.stderr)
        return 2
    try:
        source = json.loads(source_path.read_text(encoding="utf-8"))
        ucrt_hashes = {
            name.lower(): str(value).lower()
            for name, value in source["files"].items()
        }
    except (OSError, ValueError, KeyError, AttributeError) as exc:
        print(f"Win7 PE 门禁失败：UCRT 来源清单无效：{exc}", file=sys.stderr)
        return 2
    expected_ucrt_arch = "x64" if args.architecture == "amd64" else "x86"
    if source.get("version") != "10.0.19041.0" or source.get(
        "architecture"
    ) != expected_ucrt_arch:
        print("Win7 PE 门禁失败：UCRT 版本或架构与候选不匹配", file=sys.stderr)
        return 2
    for directory in (root, root / "_internal"):
        for name, expected_hash in ucrt_hashes.items():
            candidate = directory / name
            if not candidate.is_file() or _sha256(candidate) != expected_hash:
                print(
                    f"Win7 PE 门禁失败：UCRT 文件缺失或哈希不匹配：{candidate}",
                    file=sys.stderr,
                )
                return 2

    # .NET Framework 4.8 官方离线包是微软的 x86 引导程序，但其负载同时支持
    # x86/x64。它不进入 PartyOps 程序目录，只由 Inno 临时释放。必须以固定
    # 路径、版本、大小和 SHA-256 完整确证，不能按文件名宽松放行其它 x86 EXE。
    prerequisite_source_path = root / "prerequisites" / "SOURCE.json"
    prerequisite_installer = (
        root / "prerequisites" / "ndp48-x86-x64-allos-enu.exe"
    )
    if not prerequisite_source_path.is_file() or not prerequisite_installer.is_file():
        print("Win7 PE 门禁失败：缺少 .NET Framework 4.8 前置包或来源清单", file=sys.stderr)
        return 2
    try:
        prerequisite_source = json.loads(
            prerequisite_source_path.read_text(encoding="utf-8")
        )
    except (OSError, ValueError) as exc:
        print(f"Win7 PE 门禁失败：.NET 4.8 来源清单无效：{exc}", file=sys.stderr)
        return 2
    expected_dotnet_hash = (
        "0a3a390c47e639d0f7fc65b21195fee6b7f65b066f80f70c60fab191d14b7e40"
    )
    if (
        prerequisite_source.get("version") != "4.8"
        or prerequisite_source.get("filename") != prerequisite_installer.name
        or prerequisite_source.get("architectures") != ["x86", "x64"]
        or str(prerequisite_source.get("sha256", "")).lower()
        != expected_dotnet_hash
        or prerequisite_source.get("size") != 121346568
        or _sha256(prerequisite_installer) != expected_dotnet_hash
    ):
        print("Win7 PE 门禁失败：.NET 4.8 前置包来源、架构、大小或哈希不一致", file=sys.stderr)
        return 2
    verified_universal_hashes = {
        prerequisite_installer.resolve(): expected_dotnet_hash
    }

    vc_source_path = root / "vc-runtime-source.json"
    if not vc_source_path.is_file():
        print("Win7 PE 门禁失败：缺少 VC142 来源与哈希清单", file=sys.stderr)
        return 2
    try:
        vc_source = json.loads(vc_source_path.read_text(encoding="utf-8"))
        vc_hashes = {
            name.lower(): str(value).lower()
            for name, value in vc_source["files"].items()
        }
    except (OSError, ValueError, KeyError, AttributeError) as exc:
        print(f"Win7 PE 门禁失败：VC142 来源清单无效：{exc}", file=sys.stderr)
        return 2
    if vc_source.get("version") != "14.29.30157.0" or vc_source.get(
        "architecture"
    ) != expected_ucrt_arch:
        print("Win7 PE 门禁失败：VC142 版本或架构与候选不匹配", file=sys.stderr)
        return 2
    for directory in (root, root / "_internal"):
        for name, expected_hash in vc_hashes.items():
            candidate = directory / name
            if not candidate.is_file() or _sha256(candidate) != expected_hash:
                print(
                    f"Win7 PE 门禁失败：VC142 文件缺失或哈希不匹配：{candidate}",
                    file=sys.stderr,
                )
                return 2

    files = sorted(
        path
        for path in root.rglob("*")
        if path.is_file() and path.suffix.lower() in {".exe", ".dll", ".pyd"}
    )
    if not files:
        print("Win7 PE 门禁失败：冻结目录没有 EXE/DLL/PYD", file=sys.stderr)
        return 2
    errors = [
        error
        for path in files
        for error in validate_pe(
            path,
            args.architecture,
            ucrt_hashes,
            verified_universal_hashes,
        )
    ]
    if errors:
        print("Win7 PE 门禁失败：", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 2
    print(f"Win7 {args.architecture} PE 门禁通过：已检查 {len(files)} 个二进制文件。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
