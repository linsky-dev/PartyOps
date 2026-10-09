#!/usr/bin/env python3
"""验证 Linux/macOS 原源码排版适配宿主的来源清单与离线自检。"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import tempfile
from datetime import datetime, timedelta
from pathlib import Path, PurePosixPath
from typing import Any

FEATURES = ["format", "replace", "redheader", "rename", "convert", "pdf-to-word"]
SOURCE_SNAPSHOT_SHA256 = "7ae0eb67a0cb6a2d4a332cde74adf8977d93ae73541f864f01df214d39fefdf2"
SOURCE_SNAPSHOT_FILES = 898
WORD_VTABLE_MAP_SHA256 = "871fa605d294620b273f19eff20c587e742af6d46903a16216c69913675b5f41"
WPS_SDK_HEADER_SHA256 = "4d0529c076f8f36ce49301982e0c2bb46cdcc4087c3e9945a9b57d649fe26791"
MAC_OBJECT_ADAPTER = "wps-macos-object-source-adapter"
MAC_SOURCE_SHA256 = "ac8466edf2513ea8e3fe9e61d3b86fb8d7a72ceb6cce366f2d19b59d9c9be171"
MAC_RULES_SHA256 = "2cae1d25146334e66f57a98663dcd6574335f720be6b6aacc53e0dbad165d57b"
MAC_PLUGIN_FILES = frozenset({"bootstrap-carrier.docx", "main.js", "ribbon.xml", "task-lease.js", "product-index.html", "product-main.js", "product-ribbon.xml"})
MAC_LIMITATIONS = ["manual-output-review-required", "wps-window-may-appear", "native-document-cycle-unproven", "rollback-unverified", "strict-golden-parity-not-accepted"]
BUNDLED_MSCORLIB_RECORD = re.compile(
    r"^Mono: Assembly Loader loaded assembly from bundle: '([^'\r\n]+)'\.$"
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_json(path: Path, label: str) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise RuntimeError(f"[{label}_INVALID] {path}") from exc
    if not isinstance(value, dict):
        raise TypeError(f"[{label}_INVALID] {path}")
    return value


def has_bundled_mscorlib(trace: str) -> bool:
    """逐行解析 Mono bundle 记录，只接受裸文件名或 Unix 绝对路径。"""
    for line in trace.splitlines():
        match = BUNDLED_MSCORLIB_RECORD.fullmatch(line)
        if match is None:
            continue
        assembly_path = match.group(1)
        if assembly_path == "mscorlib.dll" or (
            assembly_path.startswith("/")
            and PurePosixPath(assembly_path).name == "mscorlib.dll"
        ):
            return True
    return False


def validate_arm_sidecars(runtime: Path, record: dict[str, Any], native: dict[str, Any], profile: Any) -> None:
    """原SDK来源摘要不可变；签名后摘要另绑定实际字节和codesign证据。"""
    libraries = native.get("libraries")
    if not isinstance(libraries, list) or len(libraries) != len(profile.LIBRARIES):
        raise RuntimeError("[MAC_MONO_ARM_SIDECARS_INVALID] ARM辅助库清单不完整。")
    originals = {name: digest for name, (_, digest) in profile.LIBRARIES.items()}
    pre_sign = record.get("pre_sign_sidecars_sha256")
    if pre_sign is not None and pre_sign != originals:
        raise RuntimeError("[MAC_MONO_ARM_PRESIGN_INVALID] 签名前摘要须绑定锁定原bottle。")
    if not isinstance(record.get("native_sidecars_sha256"), dict) or set(record["native_sidecars_sha256"]) != set(originals):
        raise RuntimeError("[MAC_MONO_ARM_SIDECARS_INVALID] 缺少完整随包辅助库摘要。")
    for name, (source, digest) in profile.LIBRARIES.items():
        matches = [item for item in libraries if isinstance(item, dict) and item.get("registered_name") == name]
        path = runtime / name
        if (len(matches) != 1 or path.is_symlink() or not path.is_file()
                or matches[0].get("source_name") != source or matches[0].get("source_sha256") != digest
                or matches[0].get("architectures") != ["arm64"]):
            raise RuntimeError("[MAC_MONO_ARM_SIDECARS_INVALID] ARM来源摘要或原始名不匹配。")
        actual = sha256(path)
        item = matches[0]
        if item.get("packaged_sha256") != actual or record["native_sidecars_sha256"][name] != actual:
            raise RuntimeError("[MAC_MONO_ARM_SIDECARS_INVALID] ARM随包字节与两份元数据摘要不一致。")
        signing = item.get("codesign")
        if actual == digest and signing is None:
            continue
        if (pre_sign != originals or not isinstance(signing, dict) or signing.get("method") != "codesign"
                or signing.get("pre_sign_sha256") != digest or signing.get("packaged_sha256") != actual
                or not isinstance(signing.get("cdhash"), str) or not re.fullmatch(r"[0-9a-fA-F]{40}", signing["cdhash"])
                or not isinstance(signing.get("identifier"), str) or not signing["identifier"]):
            raise RuntimeError("[MAC_MONO_ARM_SIGNING_BINDING_INVALID] 变化字节缺少明确codesign与原bottle绑定。")
        try:
            verification = subprocess.run(["/usr/bin/codesign", "--verify", "--strict", "--verbose=2", str(path)],
                                          capture_output=True, text=True, timeout=10, check=False)
            display = subprocess.run(["/usr/bin/codesign", "-d", "--verbose=4", str(path)],
                                     capture_output=True, text=True, timeout=10, check=False)
        except (OSError, subprocess.TimeoutExpired) as error:
            raise RuntimeError("[MAC_MONO_ARM_SIGNATURE_VERIFY_FAILED] 无法核验实际辅助库签名。") from error
        evidence = display.stdout + "\n" + display.stderr
        cdhash = [value.lower() for value in re.findall(r"^CDHash=([0-9a-fA-F]{40})$", evidence, re.M)]
        identifier = re.findall(r"^Identifier=(.+)$", evidence, re.M)
        if (verification.returncode != 0 or display.returncode != 0
                or cdhash != [signing["cdhash"].lower()] or identifier != [signing["identifier"]]):
            raise RuntimeError("[MAC_MONO_ARM_SIGNATURE_VERIFY_FAILED] codesign实际CDHash/Identifier或签名不匹配。")


def validate_mac_record(runtime: Path, record: dict[str, Any], architecture: str) -> None:
    """Mac 对象通道独立来源契约；这些字段不表示六功能已实测通过。"""
    expected = {
        "schema": 3, "platform": "macos", "architecture": architecture,
        "adapter": MAC_OBJECT_ADAPTER, "source_project": "PartyOps.DocumentFormatter.AddIn",
        "source_snapshot_sha256": MAC_SOURCE_SHA256, "source_snapshot_files": 898,
        "rules_sha256": MAC_RULES_SHA256, "features": FEATURES,
        "host_sha256": sha256(runtime / "partyops-document-formatter-host"),
        "timezone": "Asia/Shanghai", "self_contained": True, "minimum_macos": "15.0" if architecture == "arm64" else "11.0",
        "acceptance_profile": "mac-object-limited-candidate", "limitations": MAC_LIMITATIONS,
    }
    if any(record.get(key) != value for key, value in expected.items()):
        raise RuntimeError("[MAC_FORMATTER_SOURCE_RECORD_MISMATCH] Mac 对象后端来源字段不匹配。")
    if architecture == "arm64":
        import importlib.util
        specification = importlib.util.spec_from_file_location("mac_mono_profile", Path(__file__).with_name("macos-mono-input-profile.py"))
        profile = importlib.util.module_from_spec(specification)
        specification.loader.exec_module(profile)
        native = read_json(runtime / "mono-native-source.json", "MAC_MONO_SOURCE")
        if (record.get("runtime_input_profile") != profile.PROFILE
                or native.get("runtime_input_profile") != profile.PROFILE
                or native.get("bottle_sha256") != profile.BOTTLE_SHA
                or native.get("sdk_version") != "6.14.1" or native.get("minimum_macos") != "15.0"
                or native.get("static_runtime", {}).get("source_sha256") != profile.STATIC_SHA
                or native.get("static_runtime", {}).get("architectures") != ["arm64"]
                or record.get("mono_native_source_sha256") != sha256(runtime / "mono-native-source.json")):
            raise RuntimeError("[MAC_MONO_ARM_PROFILE_MISMATCH] ARM必须绑定锁定官方输入profile。")
        if (record.get("native_bundle_mode") != "custom-static" or native.get("bundling_mode") != "custom-static"
                or native.get("bundle_dllmap_target") != "@executable_path/libmono-native-compat.dylib"
                or native.get("bundle_posix_target") != "@executable_path/libMonoPosixHelper.dylib"
                or native.get("bundle_config_sha256") != sha256(runtime / "mono-config.bundle.xml")
                or native.get("original_config_sha256") != native.get("sdk_input_sha256", {}).get("etc/mono/config")
                or native.get("original_config_sha256") != profile.CONFIG_SHA
                or native.get("sdk_input_sha256", {}).get("etc/mono/4.5/machine.config") != profile.MACHINE_CONFIG_SHA
                or native.get("original_dllmaps") != [{"dll": name, "target": target, "os": os_filter} for name, (target, os_filter) in profile.DLLMAPS.items()]
                or native.get("child_environment") != {"MONO_CONFIG": "/dev/null", "removed": ["MONO_ENV_OPTIONS", "MONO_BUNDLED_OPTIONS"]}):
            raise RuntimeError("[MAC_MONO_ARM_CONFIG_INVALID] ARM配置或静态封装模式不匹配。")
        validate_arm_sidecars(runtime, record, native, profile)
    if record.get("self_contained") is not True:
        raise RuntimeError("[MAC_FORMATTER_SOURCE_RECORD_MISMATCH] self_contained 必须是布尔真。")
    notice = runtime / "LICENSE-MONO-RUNTIME.txt"
    if not notice.is_file() or notice.stat().st_size <= 0:
        raise RuntimeError("[MAC_FORMATTER_MONO_LICENSE_MISSING] 缺少内嵌Mono许可。")
    if any(key in record for key in ("capabilities", "word_vtable_map_sha256", "wps_sdk_header_sha256", "wps_sdk_matched_methods", "wps_sdk_mismatched_methods")):
        raise RuntimeError("[MAC_FORMATTER_LEGACY_CLAIM_REJECTED] 对象后端不能冒用 SDK/25 能力记录。")
    statuses = record.get("feature_validation")
    if not isinstance(statuses, dict) or set(statuses) != set(FEATURES) or any(value != "pending-target-package-validation" for value in statuses.values()):
        raise RuntimeError("[MAC_FORMATTER_FEATURE_STATUS_INVALID] 来源构建不能生成功能通过结论。")
    resources = record.get("plugin_resources_sha256")
    directory = runtime / "wps-formatter-plugin"
    if not isinstance(resources, dict) or set(resources) != MAC_PLUGIN_FILES or not directory.is_dir() or directory.is_symlink():
        raise RuntimeError("[MAC_FORMATTER_PLUGIN_INVALID] 固定加载项资源清单不完整。")
    if {item.name for item in directory.iterdir()} != MAC_PLUGIN_FILES:
        raise RuntimeError("[MAC_FORMATTER_PLUGIN_INVALID] 加载项目录包含未登记文件。")
    for name, digest in resources.items():
        path = directory / name
        if path.is_symlink() or not path.is_file() or path.stat().st_size == 0 or sha256(path) != digest:
            raise RuntimeError("[MAC_FORMATTER_PLUGIN_HASH_MISMATCH] 固定资源摘要不匹配。")
    for key in ("managed_host_sha256", "resource_catalog_source_sha256"):
        if not isinstance(record.get(key), str) or not re.fullmatch(r"[0-9a-f]{64}", record[key]):
            raise RuntimeError("[MAC_FORMATTER_BUILD_BINDING_INVALID] 缺少托管构建绑定。")


def validate(runtime: Path, platform_name: str, architecture: str) -> dict[str, Any]:
    runtime = runtime.resolve()
    host = runtime / "partyops-document-formatter-host"
    record_path = runtime / "source-host.json"
    vtable_map = runtime / "word-vtable-map.json"
    wps_license = runtime / "LICENSE-WPS-SDK.txt"
    mono_license = runtime / "LICENSE-MONO-RUNTIME.txt"
    if (
        not runtime.is_dir()
        or host.is_symlink()
        or not host.is_file()
        or host.stat().st_size <= 0
        or not record_path.is_file()
        or not mono_license.is_file()
    ):
        raise RuntimeError("[FORMATTER_RUNTIME_INCOMPLETE] 本机 WPS 原源码宿主不完整。")

    record = read_json(record_path, "FORMATTER_SOURCE_RECORD")
    mac_object = platform_name == "macos" and record.get("adapter") == MAC_OBJECT_ADAPTER
    expected = {
        "schema": 2,
        "platform": platform_name,
        "architecture": architecture,
        "adapter": "wps-native-source-adapter",
        "source_project": "PartyOps.DocumentFormatter.AddIn",
        "source_snapshot_sha256": SOURCE_SNAPSHOT_SHA256,
        "source_snapshot_files": SOURCE_SNAPSHOT_FILES,
        "timezone": "Asia/Shanghai",
        "features": FEATURES,
        "capabilities": 25,
        "host_sha256": sha256(host),
        "word_vtable_map_sha256": WORD_VTABLE_MAP_SHA256,
        "wps_sdk_header_sha256": WPS_SDK_HEADER_SHA256,
        "wps_sdk_matched_methods": 528,
        "wps_sdk_mismatched_methods": 0,
    }
    mismatches = [
        key for key, expected_value in expected.items() if record.get(key) != expected_value
    ]
    if mac_object:
        validate_mac_record(runtime, record, architecture)
    elif not vtable_map.is_file() or not wps_license.is_file():
        raise RuntimeError("[FORMATTER_RUNTIME_INCOMPLETE] WPS 原生适配资源不完整。")
    elif mismatches:
        raise RuntimeError(
            "[FORMATTER_SOURCE_RECORD_MISMATCH] 来源清单字段不匹配："
            + ", ".join(mismatches)
        )
    if not mac_object and sha256(vtable_map) != WORD_VTABLE_MAP_SHA256:
        raise RuntimeError("[FORMATTER_VTABLE_MAP_MISMATCH] WPS 槽位表已变化。")
    try:
        built_at = datetime.fromisoformat(str(record.get("built_at", "")))
    except ValueError as exc:
        raise RuntimeError("[FORMATTER_SOURCE_RECORD_TIME_INVALID] built_at 无效。") from exc
    if built_at.utcoffset() != timedelta(hours=8):
        raise RuntimeError("[FORMATTER_SOURCE_RECORD_TIME_INVALID] built_at 必须使用北京时间 +08:00。")
    runtime_label = record.get("native_bundle_runtime")
    if not isinstance(runtime_label, str) or "Mono JIT compiler version" not in runtime_label:
        raise RuntimeError("[FORMATTER_SOURCE_RECORD_RUNTIME_INVALID] 缺少 Mono 原生封装版本。")

    with tempfile.TemporaryDirectory(prefix="partyops-formatter-runtime-") as work:
        output = Path(work) / "self-test.json"
        # 开启程序集加载追踪，拒绝“依赖构建机 Mono 才通过”的假阳性。
        environment = {key: value for key, value in os.environ.items() if not key.startswith("MONO_")}
        environment.update(MONO_LOG_LEVEL="debug", MONO_LOG_MASK="asm")
        if mac_object and record.get("native_bundle_mode") == "custom-static":
            # custom不内嵌simple的环境绑定；仅本Mac宿主子进程隔离系统/用户config。
            environment["MONO_CONFIG"] = "/dev/null"
            environment.pop("MONO_ENV_OPTIONS", None)
            environment.pop("MONO_BUNDLED_OPTIONS", None)
        try:
            result = subprocess.run(
                [str(host), "--self-test", str(output)],
                check=False,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=30,
                env=environment,
            )
        except (OSError, subprocess.TimeoutExpired) as exc:
            raise RuntimeError("[FORMATTER_RUNTIME_START_FAILED] 宿主无法启动。") from exc
        if result.returncode != 0 or not output.is_file():
            detail = " ".join((result.stderr or result.stdout or "无输出").split())[:800]
            raise RuntimeError(
                f"[FORMATTER_RUNTIME_SELFTEST_FAILED] 退出码 {result.returncode}：{detail}"
            )
        selftest = read_json(output, "FORMATTER_RUNTIME_SELFTEST")
    if (
        selftest.get("schema") != 1
        or selftest.get("passed") is not True
        or selftest.get("engine") != "source-standalone-batch-processor"
        or selftest.get("source_project") != "PartyOps.DocumentFormatter.AddIn"
        or selftest.get("features") != FEATURES
    ):
        raise RuntimeError("[FORMATTER_RUNTIME_SELFTEST_INVALID] 宿主能力结果不完整。")
    trace = result.stdout + result.stderr
    if (
        not has_bundled_mscorlib(trace)
        or "Assembly Loader loaded assembly from location:" in trace
    ):
        raise RuntimeError(
            "[FORMATTER_RUNTIME_ASSEMBLY_NOT_BUNDLED] 基础程序集未由宿主内嵌加载，或仍借用了外部程序集。"
        )
    return {
        "passed": True,
        "platform": platform_name,
        "architecture": architecture,
        "adapter": record["adapter"],
        "host_sha256": expected["host_sha256"],
        "features": len(FEATURES),
        **({"feature_validation": record["feature_validation"], "limitations": record["limitations"], "target_package_verified": False} if mac_object else {"capabilities": 25}),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--runtime", type=Path, required=True)
    parser.add_argument("--platform", choices=("linux", "macos"), required=True)
    parser.add_argument("--architecture", required=True)
    args = parser.parse_args()
    try:
        result = validate(args.runtime, args.platform, args.architecture)
    except (RuntimeError, TypeError) as exc:
        print(str(exc))
        return 2
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
