"""原生安装包配置阶段使用的离线运行时自检。"""

from __future__ import annotations

import hashlib
import importlib
import json
import os
import platform
import re
import ssl
import subprocess
import sys
import tempfile
from datetime import datetime, timedelta
from pathlib import Path

ASSET_PATTERN = re.compile(r"(?:src|href)=[\"']/?([^\"'#?]+)")
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
FORMATTER_SOURCE_FIXTURE_SHA256 = "6b360f0372ae657a9e207ea4e0ec05437cea8fecc34113429a6d5670a8fff287"
FORMATTER_GOLDEN_FIXTURE_SHA256 = "bef6831245bc5a064bcf4135a51252f4dfd0a926f79228c7a2487a9c02639133"
FORMATTER_SOURCE_SNAPSHOT_SHA256 = "7ae0eb67a0cb6a2d4a332cde74adf8977d93ae73541f864f01df214d39fefdf2"
FORMATTER_SOURCE_SNAPSHOT_FILES = 898
FORMATTER_WPS_SDK_HEADER_SHA256 = "4d0529c076f8f36ce49301982e0c2bb46cdcc4087c3e9945a9b57d649fe26791"
MAC_OBJECT_ADAPTER = "wps-macos-object-source-adapter"
MAC_OBJECT_SOURCE_SHA256 = "ac8466edf2513ea8e3fe9e61d3b86fb8d7a72ceb6cce366f2d19b59d9c9be171"
MAC_OBJECT_RULES_SHA256 = "2cae1d25146334e66f57a98663dcd6574335f720be6b6aacc53e0dbad165d57b"
MAC_PLUGIN_FILES = frozenset({"bootstrap-carrier.docx", "main.js", "ribbon.xml", "task-lease.js", "product-index.html", "product-main.js", "product-ribbon.xml"})
MAC_LIMITATIONS = ["manual-output-review-required", "wps-window-may-appear", "native-document-cycle-unproven", "rollback-unverified", "strict-golden-parity-not-accepted"]
MAC_FEATURES = ["format", "replace", "redheader", "rename", "convert", "pdf-to-word"]
MAC_CASES = ["format", "replace", "redheader", "rename", "convert-docx", "convert-pdf", "convert-txt", "convert-png-pages", "convert-jpg-long", "pdf-to-word"]


def _runtime_contents(runtime: Path) -> Path:
    if sys.platform == "darwin":
        # PyInstaller 的 macOS BUNDLE 会按 Apple 目录约定把数据放在
        # Contents/Resources，而 Mach-O 入口位于 Contents/MacOS。不能
        # 假设 Resources 一定经符号链接映射回可执行目录；普通 APFS、
        # ZIP 往返和 Installer 都可能暴露这种错误假设。
        resources = runtime.parent / "Resources"
        if resources.is_dir():
            return resources
    internal = runtime / "_internal"
    return internal if internal.is_dir() else runtime


def _native_executable(runtime: Path, relative: str) -> Path:
    """按当前系统返回冻结原生程序名，避免 Windows 自检误找无后缀文件。"""

    candidate = runtime / relative
    return candidate.with_suffix(".exe") if os.name == "nt" else candidate


def _ocr_runtime(runtime: Path) -> tuple[Path, Path, Path]:
    """返回 OCR 可执行文件、词库与动态库目录，遵守各平台包布局。"""

    if sys.platform == "darwin":
        resources = runtime.parent / "Resources" / "ocr"
        return runtime / "tesseract", resources / "tessdata", resources / "lib"
    root = runtime / "ocr"
    return _native_executable(runtime, "ocr/bin/tesseract"), root / "tessdata", root / "lib"


def _office_executable(runtime: Path) -> Path:
    """定位安装包自带的无窗口公文转换器，不回退到系统 Office。"""

    # Windows 官方 LibreOffice 同时提供 GUI 子系统入口 soffice.exe 与
    # 控制台入口 soffice.com。冻结程序会捕获子进程输出；此时 26.x 的
    # .exe 入口可能长期等待，.com 才是可观测且会确定退出的 headless CLI。
    if sys.platform == "darwin":
        # 保留完整 LibreOffice.app 的签名边界，并直接执行其真实入口。不要
        # 通过 Resources/office-runtime/program 的兼容链接启动，否则 dyld
        # 与 AppKit 可能按链接位置推导错误的 Bundle 上下文。
        nested = (
            runtime.parent
            / "Resources"
            / "office-runtime"
            / "LibreOffice.app"
            / "Contents"
            / "MacOS"
            / "soffice"
        )
        if nested.is_file():
            return nested
    names = ("soffice.com", "soffice.exe") if os.name == "nt" else ("soffice",)
    for root in (runtime, _runtime_contents(runtime)):
        for name in names:
            candidate = root / "office-runtime" / "program" / name
            if candidate.is_file():
                return candidate
    return runtime / "office-runtime" / "program" / names[0]


def _source_formatter_root(runtime: Path) -> Path:
    return (
        runtime.parent / "Resources" / "formatter-host"
        if sys.platform == "darwin"
        else runtime / "formatter-host"
    )


def _source_formatter_runtime(runtime: Path) -> tuple[Path, Path | None, Path]:
    """返回当前平台的原源码宿主、可选规则程序集和来源清单。

    Windows 直接加载原 .NET Framework 规则程序集；Linux/macOS 使用各自
    的本机 WPS 适配宿主。任何平台都不得因为“不是 Windows”而跳过检查。
    """

    root = _source_formatter_root(runtime)
    if os.name == "nt":
        return (
            root / "PartyOps.DocumentFormatter.Host.exe",
            root / "PartyOps.DocumentFormatter.AddIn.dll",
            root / "source-host.json",
        )
    return root / "partyops-document-formatter-host", None, root / "source-host.json"


def _source_formatter_evidence(runtime: Path) -> Path:
    return _source_formatter_root(runtime) / "runtime-evidence.json"


def _source_formatter_native_adapter_files(runtime: Path) -> tuple[Path, Path, Path]:
    root = _source_formatter_root(runtime)
    return (
        root / "word-vtable-map.json",
        root / "LICENSE-WPS-SDK.txt",
        root / "LICENSE-MONO-RUNTIME.txt",
    )


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _source_formatter_platform() -> str:
    if os.name == "nt":
        return "windows"
    return "macos" if sys.platform == "darwin" else "linux"


def _source_formatter_architecture() -> str:
    if os.name == "nt":
        return "x86" if sys.maxsize <= 2**32 else "x64"
    machine = platform.machine().lower()
    if sys.platform == "darwin":
        return "arm64" if machine in {"arm64", "aarch64"} else "x86_64"
    if machine in {"loongarch64", "loong64"}:
        return "loong64"
    return "arm64" if machine in {"arm64", "aarch64"} else "amd64"


def _validate_mac_object_record(host: Path, record: dict[str, object]) -> dict[str, object]:
    """离线来源/资源自检，不把原声明能力当作目标包功能验收。"""
    architecture = _source_formatter_architecture()
    # 与本机 Mac 包契约一致；ARM 候选为15，Intel仍为11，未知架构不猜测。
    minimum_by_architecture = {"arm64": "15.0", "x86_64": "11.0"}
    if architecture not in minimum_by_architecture:
        raise RuntimeError("Mac 对象后端目标架构无效")
    expected = {"schema": 3, "platform": "macos", "architecture": architecture,
                "adapter": MAC_OBJECT_ADAPTER, "source_project": "PartyOps.DocumentFormatter.AddIn",
                "source_snapshot_sha256": MAC_OBJECT_SOURCE_SHA256, "source_snapshot_files": 898,
                "rules_sha256": MAC_OBJECT_RULES_SHA256, "features": MAC_FEATURES,
                "host_sha256": _sha256(host), "timezone": "Asia/Shanghai", "self_contained": True,
                "minimum_macos": minimum_by_architecture[architecture], "acceptance_profile": "mac-object-limited-candidate", "limitations": MAC_LIMITATIONS}
    if record.get("self_contained") is not True or any(record.get(key) != value for key, value in expected.items()) or any(key in record for key in ("capabilities", "word_vtable_map_sha256", "wps_sdk_header_sha256", "wps_sdk_matched_methods", "wps_sdk_mismatched_methods")):
        raise RuntimeError("Mac 对象后端来源清单无效，不能冒用 SDK/25 能力记录")
    pending = record.get("feature_validation")
    if not isinstance(pending, dict) or set(pending) != set(MAC_FEATURES) or any(state != "pending-target-package-validation" for state in pending.values()):
        raise RuntimeError("Mac 构建来源不能伪造六功能验收结论")
    resources = record.get("plugin_resources_sha256")
    directory = host.parent / "wps-formatter-plugin"
    if not isinstance(resources, dict) or set(resources) != MAC_PLUGIN_FILES or directory.is_symlink() or not directory.is_dir() or {p.name for p in directory.iterdir()} != MAC_PLUGIN_FILES:
        raise RuntimeError("Mac 固定加载项七资源不完整")
    for name, digest in resources.items():
        item = directory / name
        if item.is_symlink() or not item.is_file() or item.stat().st_size <= 0 or _sha256(item) != digest:
            raise RuntimeError("Mac 固定加载项资源摘要不匹配")
    license_path = host.parent / "LICENSE-MONO-RUNTIME.txt"
    if not license_path.is_file() or license_path.stat().st_size <= 0 or "Mono JIT compiler version" not in str(record.get("native_bundle_runtime", "")):
        raise RuntimeError("Mac 内嵌 Mono 来源与许可不完整")
    for key in ("managed_host_sha256", "resource_catalog_source_sha256"):
        if not isinstance(record.get(key), str) or not SHA256_PATTERN.fullmatch(record[key]):
            raise RuntimeError("Mac 托管载荷构建绑定无效")
    try:
        if datetime.fromisoformat(str(record.get("built_at", ""))).utcoffset() != timedelta(hours=8):
            raise ValueError("timezone")
    except ValueError as exc:
        raise RuntimeError("Mac 对象后端构建时间无效") from exc
    return record


def _validate_mac_object_evidence(payload: dict[str, object], host: Path) -> dict[str, object]:
    record = json.loads((host.parent / "source-host.json").read_text(encoding="utf-8"))
    _validate_mac_object_record(host, record)
    expected = {"schema": 2, "status": "limited-candidate", "acceptance_profile": "mac-object-limited-candidate",
                "platform": "macos", "architecture": _source_formatter_architecture(), "adapter": MAC_OBJECT_ADAPTER,
                "provider": "wps", "host_sha256": _sha256(host), "features": MAC_FEATURES, "feature_cases": 10,
                "source_snapshot_sha256": MAC_OBJECT_SOURCE_SHA256, "rules_sha256": MAC_OBJECT_RULES_SHA256,
                "plugin_resources_sha256": record["plugin_resources_sha256"], "limitations": MAC_LIMITATIONS,
                "package_validation_passed": False, "publication_ready": False,
                "silent": False, "rollback_capability_verified": False, "document_cycle_identity_proven": False,
                "timezone": "Asia/Shanghai"}
    if any(payload.get(key) != value for key, value in expected.items()) or any(key in payload for key in ("capabilities", "golden_sha256", "rendered_pages")):
        raise RuntimeError("Mac 候选证据无效，不能冒用金样或最终包通过结论")
    if any(payload.get(key) is not False for key in ("package_validation_passed", "publication_ready", "silent", "rollback_capability_verified", "document_cycle_identity_proven")):
        raise RuntimeError("Mac 候选限制标记必须为布尔假")
    cases = payload.get("cases")
    if not isinstance(cases, list) or len(cases) != 10 or any(not isinstance(c, dict) for c in cases) or [c.get("case") for c in cases] != MAC_CASES:
        raise RuntimeError("Mac 必须保留六功能十场景逐项状态")
    for case in cases:
        state = case.get("status")
        if state not in {"passed", "passed-with-limitations", "failed", "not-run"} or state == "passed-with-limitations" and case["case"] != "format":
            raise RuntimeError("Mac 其它功能不可因排版限制豁免")
        if state in {"passed", "passed-with-limitations"}:
            outputs = case.get("outputs")
            if case.get("execution_kind") not in {"managed-source-host", "native-selfcontained-host"} or any(case.get(key) is not True for key in ("source_unchanged", "cleanup_confirmed", "lease_released", "registration_owned", "capability_revoked")) or not isinstance(case.get("receipt_sha256"), str) or not SHA256_PATTERN.fullmatch(case["receipt_sha256"]) or not isinstance(outputs, list) or not outputs or any(not isinstance(item, dict) or type(item.get("bytes")) is not int or item["bytes"] <= 0 or not isinstance(item.get("sha256"), str) or not SHA256_PATTERN.fullmatch(item["sha256"]) for item in outputs):
                raise RuntimeError("Mac 通过场景缺少真实回执/输出或清理确认")
            if state == "passed-with-limitations" and case.get("manual_review_required") is not True:
                raise RuntimeError("Mac 有限排版必须提示人工核对")
    tested_hash = payload.get("tested_host_sha256")
    if tested_hash is not None and (not isinstance(tested_hash, str) or not SHA256_PATTERN.fullmatch(tested_hash) or tested_hash not in {record["host_sha256"], record.get("pre_sign_host_sha256"), record["managed_host_sha256"]}):
        raise RuntimeError("Mac 实测Host与最终签名Host来源未绑定")
    if any(c.get("status") in {"passed", "passed-with-limitations"} for c in cases) and tested_hash is None:
        raise RuntimeError("Mac 通过场景缺少实测Host")
    final_verified = tested_hash == record["host_sha256"] and all(c.get("status") in {"passed", "passed-with-limitations"} and c.get("execution_kind") == "native-selfcontained-host" for c in cases)
    if payload.get("final_host_verified") is not final_verified:
        raise RuntimeError("Mac 最终Host验收结论与逐项证据不一致")
    states = {}
    for feature in MAC_FEATURES:
        selected = [c["status"] for c in cases if c["case"].startswith("convert-")] if feature == "convert" else [c["status"] for c in cases if c["case"] == feature]
        states[feature] = "failed" if "failed" in selected else "not-run" if "not-run" in selected else "passed-with-limitations" if "passed-with-limitations" in selected else "passed"
    if payload.get("feature_validation") != states:
        raise RuntimeError("Mac 六功能汇总与场景状态不一致")
    try:
        if datetime.fromisoformat(str(payload.get("verified_at", ""))).utcoffset() != timedelta(hours=8):
            raise ValueError("timezone")
    except ValueError as exc:
        raise RuntimeError("Mac 实测时间无效") from exc
    return payload


def _validate_source_formatter_record(
    host_binary: Path,
    rules_binary: Path | None,
    source_record: Path,
) -> dict[str, object]:
    """校验来源、平台、能力和文件哈希，禁止伪装成已适配的空宿主。"""

    required = [host_binary, source_record]
    if rules_binary is not None:
        required.append(rules_binary)
    if any(not path.is_file() or path.stat().st_size <= 0 for path in required):
        raise RuntimeError("原排版源码宿主不完整")
    try:
        record = json.loads(source_record.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise RuntimeError("原排版源码宿主来源清单无效") from exc
    expected_features = ["format", "replace", "redheader", "rename", "convert", "pdf-to-word"]
    if sys.platform == "darwin" and record.get("adapter") == MAC_OBJECT_ADAPTER:
        return _validate_mac_object_record(host_binary, record)
    if (
        record.get("schema") != 2
        or record.get("timezone") != "Asia/Shanghai"
        or record.get("source_project") != "PartyOps.DocumentFormatter.AddIn"
        or record.get("source_snapshot_sha256") != FORMATTER_SOURCE_SNAPSHOT_SHA256
        or record.get("source_snapshot_files") != FORMATTER_SOURCE_SNAPSHOT_FILES
        or record.get("platform") != _source_formatter_platform()
        or record.get("architecture") != _source_formatter_architecture()
        or record.get("features") != expected_features
        or record.get("capabilities") != 25
        or record.get("host_sha256") != _sha256(host_binary)
    ):
        raise RuntimeError("原排版源码宿主来源清单无效")
    if rules_binary is not None and record.get("rules_sha256") != _sha256(rules_binary):
        raise RuntimeError("原排版源码规则程序集哈希不一致")
    expected_adapter = (
        "dotnet-framework-com-source"
        if os.name == "nt"
        else "wps-native-source-adapter"
    )
    if record.get("adapter") != expected_adapter:
        raise RuntimeError("原排版源码宿主适配类型无效")
    if os.name != "nt":
        vtable_map = host_binary.parent / "word-vtable-map.json"
        notices = (
            host_binary.parent / "LICENSE-WPS-SDK.txt",
            host_binary.parent / "LICENSE-MONO-RUNTIME.txt",
        )
        if (
            not vtable_map.is_file()
            or any(not path.is_file() or path.stat().st_size <= 0 for path in notices)
            or record.get("word_vtable_map_sha256") != _sha256(vtable_map)
            or record.get("wps_sdk_header_sha256") != FORMATTER_WPS_SDK_HEADER_SHA256
            or record.get("wps_sdk_matched_methods") != 528
            or record.get("wps_sdk_mismatched_methods") != 0
            or "Mono JIT compiler version" not in str(record.get("native_bundle_runtime", ""))
        ):
            raise RuntimeError("原排版源码宿主 WPS 原生运行时不完整")
        try:
            built_at = datetime.fromisoformat(str(record.get("built_at", "")))
        except ValueError as exc:
            raise RuntimeError("原排版源码宿主构建时间无效") from exc
        if built_at.utcoffset() != timedelta(hours=8):
            raise RuntimeError("原排版源码宿主构建时间不是北京时间")
    return record


def _validate_source_formatter_evidence(
    evidence_path: Path,
    host_binary: Path,
    adapter: object,
) -> dict[str, object]:
    """校验当前包确实在对应平台用真实 WPS 通过金样与六功能测试。"""

    try:
        payload = json.loads(evidence_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise RuntimeError("原排版源码宿主缺少真实 WPS 验收证据") from exc
    if sys.platform == "darwin" and adapter == MAC_OBJECT_ADAPTER:
        return _validate_mac_object_evidence(payload, host_binary)
    hashes = (
        payload.get("host_sha256"),
        payload.get("source_sha256"),
        payload.get("golden_sha256"),
        payload.get("semantic_signature_sha256"),
        payload.get("parity_evidence_sha256"),
        payload.get("feature_evidence_sha256"),
    )
    native_files = payload.get("native_adapter_files_sha256")
    expected_native_files: dict[str, Path] = {}
    if os.name != "nt":
        # 宿主目录已经由平台布局定位，原生适配资源必须与宿主相邻，不能从
        # PATH 或用户目录回退查找。
        expected_native_files = {
            "word-vtable-map.json": host_binary.parent / "word-vtable-map.json",
            "LICENSE-WPS-SDK.txt": host_binary.parent / "LICENSE-WPS-SDK.txt",
            "LICENSE-MONO-RUNTIME.txt": host_binary.parent / "LICENSE-MONO-RUNTIME.txt",
        }
    if (
        payload.get("schema") != 1
        or payload.get("status") != "passed"
        or payload.get("timezone") != "Asia/Shanghai"
        or not str(payload.get("verified_at", "")).endswith("+08:00")
        or payload.get("platform") != _source_formatter_platform()
        or payload.get("architecture") != _source_formatter_architecture()
        or payload.get("provider") != "wps"
        or payload.get("adapter") != adapter
        or payload.get("host_sha256") != _sha256(host_binary)
        or payload.get("source_sha256") != FORMATTER_SOURCE_FIXTURE_SHA256
        or payload.get("golden_sha256") != FORMATTER_GOLDEN_FIXTURE_SHA256
        or payload.get("rendered_pages") != 3
        or payload.get("feature_cases") != 10
        or payload.get("features")
        != ["format", "replace", "redheader", "rename", "convert", "pdf-to-word"]
        or payload.get("capabilities") != 25
        or any(not isinstance(value, str) or not SHA256_PATTERN.fullmatch(value) for value in hashes)
        or (
            os.name != "nt"
            and (
                not isinstance(native_files, dict)
                or set(native_files) != set(expected_native_files)
                or any(
                    not path.is_file()
                    or native_files.get(name) != _sha256(path)
                    for name, path in expected_native_files.items()
                )
            )
        )
    ):
        raise RuntimeError("原排版源码宿主真实 WPS 验收证据无效")
    return payload


def _run_source_formatter_selftest(runtime: Path, host_binary: Path) -> dict[str, object]:
    """要求宿主实际加载排版规则并报告能力，而不打开用户文档或 WPS 窗口。"""

    formatter_environment = _native_child_environment(runtime)
    if sys.platform == "darwin":
        # 只绑定formatter自检；其他随包原生程序及父环境保持原样。
        formatter_environment["MONO_CONFIG"] = "/dev/null"
        formatter_environment.pop("MONO_ENV_OPTIONS", None)
        formatter_environment.pop("MONO_BUNDLED_OPTIONS", None)

    with tempfile.TemporaryDirectory(prefix="partyops-formatter-selftest-") as work:
        output = Path(work) / "result.json"
        result = subprocess.run(
            [str(host_binary), "--self-test", str(output)],
            check=False,
            capture_output=True,
            env=formatter_environment,
            timeout=30,
        )
        if result.returncode != 0 or not output.is_file():
            raise RuntimeError(_native_failure_detail("原排版源码宿主无法启动", result))
        try:
            payload = json.loads(output.read_text(encoding="utf-8"))
        except (OSError, ValueError) as exc:
            raise RuntimeError("原排版源码宿主自检结果无效") from exc
    if (
        payload.get("schema") != 1
        or payload.get("passed") is not True
        or payload.get("source_project") != "PartyOps.DocumentFormatter.AddIn"
        or payload.get("features")
        != ["format", "replace", "redheader", "rename", "convert", "pdf-to-word"]
    ):
        raise RuntimeError("原排版源码宿主自检结果无效")
    return payload


def _native_child_environment(runtime: Path, library: Path | None = None) -> dict[str, str]:
    """为随包原生助手构造不受冻结引导器污染的环境。"""

    environment = os.environ.copy()
    for key in tuple(environment):
        if key.startswith("_PYI_") or key in {
            "PYTHONHOME",
            "PYTHONPATH",
            "PYTHONEXECUTABLE",
            "LD_PRELOAD",
        }:
            environment.pop(key, None)
    if sys.platform == "darwin":
        # PyInstaller onefile 会设置 _PYI_*，启动上下文还可能携带 DYLD_*。
        # 这些变量不得泄漏给独立 Mach-O；否则 Intel 辅助程序可能加载临时
        # 解包目录中的错误动态库，出现启动变慢、退出或只在 Finder 下失败。
        for key in tuple(environment):
            if key.startswith("DYLD_"):
                environment.pop(key, None)
        environment.pop("LD_LIBRARY_PATH", None)
        environment["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin"
    else:
        library_path = library or runtime
        environment["LD_LIBRARY_PATH"] = os.pathsep.join(
            part
            for part in (
                str(library_path),
                environment.get("LD_LIBRARY_PATH", ""),
            )
            if part
        )
    return environment


def _native_runtime_timeout() -> int:
    """macOS Intel 首次初始化嵌入 Metal 运行时可能超过 30 秒。"""

    return 120 if sys.platform == "darwin" else 30


def _native_failure_detail(label: str, result: subprocess.CompletedProcess[bytes] | subprocess.CompletedProcess[str]) -> str:
    """返回有界且可审计的原生运行时错误，避免发布日志只剩模糊摘要。"""

    def excerpt(value: object) -> str:
        if isinstance(value, bytes):
            text = value.decode("utf-8", errors="replace")
        else:
            text = str(value or "")
        compact = " ".join(text.split())
        return compact[:800] or "无输出"

    return (
        f"{label}（退出码 {result.returncode}；"
        f"stdout={excerpt(getattr(result, 'stdout', ''))}；"
        f"stderr={excerpt(getattr(result, 'stderr', ''))}）"
    )


def run_selftest(runtime: Path) -> dict[str, object]:
    """验证冻结资源、数据库和公文能力；仅已证明的 core 包免查复杂 AI。"""

    runtime_profile = "full"
    if sys.platform.startswith("linux") and _source_formatter_architecture() == "loong64":
        from app.linux_runtime_identity import installed_linux_identity

        # 自检运行目录就是冻结入口目录；不读取可被修改的环境变量选包档。
        identity = installed_linux_identity(runtime / "partyops", "loong64")
        if identity["package_identity_status"] != "verified":
            raise RuntimeError(f"龙芯安装包身份无效：{identity['package_identity_reason']}")
        runtime_profile = "core"

    contents = _runtime_contents(runtime)
    frontend = contents / "frontend"
    index = frontend / "index.html"
    if not index.is_file():
        raise RuntimeError("前端入口缺失")
    html = index.read_text(encoding="utf-8")
    missing_assets = sorted(
        asset for asset in ASSET_PATTERN.findall(html) if not (frontend / asset).is_file()
    )
    if missing_assets:
        raise RuntimeError(f"前端静态资源缺失：{', '.join(missing_assets)}")

    from app.database import db_runtime

    sqlite = db_runtime.validate_capabilities()
    if not sqlite.get("safe_version") or not sqlite.get("fts5"):
        raise RuntimeError("SQLite 安全版本或 FTS5 自检失败")

    ocr, tessdata, ocr_library = _ocr_runtime(runtime)
    language = tessdata / "chi_sim.traineddata"
    if not ocr.is_file() or not language.is_file():
        raise RuntimeError("中文 OCR 运行时不完整")
    ocr_environment = _native_child_environment(runtime, ocr_library)
    ocr_environment["TESSDATA_PREFIX"] = str(language.parent)
    ocr_arguments = [str(ocr), "--list-langs"]
    if os.name == "nt":
        # Windows 原生 getenv 不能可靠读取中文路径，必须从 Unicode 命令行传入。
        ocr_environment.pop("TESSDATA_PREFIX", None)
        ocr_arguments += ["--tessdata-dir", str(language.parent)]
    ocr_result = subprocess.run(
        ocr_arguments,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        env=ocr_environment,
        timeout=30,
    )
    if ocr_result.returncode != 0 or "chi_sim" not in ocr_result.stdout.split():
        raise RuntimeError("中文 OCR 语言包无法加载")

    smart_versions: dict[str, str] = {}
    for package in (("numpy",) if runtime_profile == "core" else ("numpy", "onnxruntime", "tokenizers")):
        module = importlib.import_module(package)
        smart_versions[package] = str(getattr(module, "__version__", "unknown"))
    # macOS Intel 的 setup-python 可能携带只服务旧算法的 OpenSSL legacy
    # provider；发布包会剔除它。这里显式验证 PartyOps 实际使用的现代 TLS 与
    # Ed25519 签名链，防止闭包瘦身造成“能安装但加密功能启动时才失败”。
    ssl.create_default_context()
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey

    crypto_key = Ed25519PrivateKey.generate()
    crypto_payload = b"PartyOps package self-test"
    crypto_key.public_key().verify(crypto_key.sign(crypto_payload), crypto_payload)
    if runtime_profile == "full":
        llama = _native_executable(runtime, "llama-server")
        if not llama.is_file():
            raise RuntimeError("本地 LLM 运行时缺失")
        llama_environment = _native_child_environment(runtime)
        llama_result = subprocess.run(
            [str(llama), "--version"],
            check=False,
            capture_output=True,
            env=llama_environment,
            timeout=_native_runtime_timeout(),
        )
        if llama_result.returncode != 0:
            raise RuntimeError("本地 LLM 运行时无法启动")

    office = _office_executable(runtime)
    if not office.is_file():
        raise RuntimeError("公文转换运行时缺失")
    # LibreOffice 会读取用户配置，即使这里只查询版本。使用一次性本地配置
    # 可隔离构建机上的旧版本、损坏锁文件和并发实例，安装后也不会污染用户
    # 的系统 LibreOffice 配置。as_uri() 同时正确处理 Windows 盘符和空格。
    with tempfile.TemporaryDirectory(prefix="partyops-office-selftest-") as profile:
        office_result = subprocess.run(
            [
                str(office),
                "--headless",
                f"-env:UserInstallation={Path(profile).resolve().as_uri()}",
                "--version",
            ],
            check=False,
            capture_output=True,
            env=_native_child_environment(runtime),
            timeout=120,
        )
    if office_result.returncode != 0:
        raise RuntimeError(_native_failure_detail("公文转换运行时无法启动", office_result))

    from app.official_format_features import FEATURE_DEFINITIONS, PRODUCT_CAPABILITIES

    if len(FEATURE_DEFINITIONS) != 6 or len(PRODUCT_CAPABILITIES) != 25:
        raise RuntimeError("公文排版能力清单不完整")
    host_binary, rules_binary, source_record = _source_formatter_runtime(runtime)
    source_record_payload = _validate_source_formatter_record(
        host_binary, rules_binary, source_record
    )
    if os.name != "nt" and source_record_payload["adapter"] != MAC_OBJECT_ADAPTER and any(
        not path.is_file() or path.stat().st_size <= 0
        for path in _source_formatter_native_adapter_files(runtime)
    ):
        raise RuntimeError("原排版源码宿主缺少 WPS 原生适配资源")
    source_evidence = _validate_source_formatter_evidence(
        _source_formatter_evidence(runtime),
        host_binary,
        source_record_payload["adapter"],
    )
    source_selftest = _run_source_formatter_selftest(runtime, host_binary)

    return {
        "passed": True,
        "runtime_profile": runtime_profile,
        "architecture": os.uname().machine if hasattr(os, "uname") else "windows",
        "sqlite": sqlite,
        "frontend_assets": len(ASSET_PATTERN.findall(html)),
        "ocr": "chi_sim",
        "smart_runtime": smart_versions,
        "crypto": "tls-ed25519-passed",
        "llama": "passed" if runtime_profile == "full" else "not-provided",
        "deferred_checks": [] if runtime_profile == "full" else [
            {"capability": "semantic_rerank", "reason": "core-package-does-not-provide-local-ai"},
            {"capability": "local_llm", "reason": "core-package-does-not-provide-local-ai"},
        ],
        "document_formatter": {
            "features": len(FEATURE_DEFINITIONS),
            **({"declared_catalog_entries": len(PRODUCT_CAPABILITIES), "native_feature_validation": source_evidence["feature_validation"], "final_host_verified": source_evidence["final_host_verified"], "package_function_validation": "pending", "limitations": source_evidence["limitations"]} if source_record_payload["adapter"] == MAC_OBJECT_ADAPTER else {"capabilities": len(PRODUCT_CAPABILITIES)}),
            "office_runtime": "passed",
            "source_host": "passed",
            "source_adapter": source_record_payload["adapter"],
            "source_host_selftest": source_selftest["engine"],
            "source_wps_evidence": source_evidence["verified_at"],
        },
    }


def main(runtime: Path | None = None) -> int:
    try:
        result = run_selftest(runtime or Path(sys.executable).resolve().parent)
    except Exception as exc:  # 自检入口必须返回稳定中文摘要，完整异常由调用方记录。
        print(json.dumps({"passed": False, "error": str(exc)}, ensure_ascii=False))
        return 2
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0
