"""DEB/RPM 安装后自检的成功与硬失败分支。"""

from __future__ import annotations

import hashlib
import json
import os
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

from app import package_selftest
from app.database import db_runtime


def _runtime(tmp_path: Path) -> Path:
    is_macos = sys.platform == "darwin"
    runtime = (
        tmp_path / "PartyOps.app" / "Contents" / "MacOS"
        if is_macos
        else tmp_path / "PartyOps"
    )
    contents = runtime.parent / "Resources" if is_macos else runtime / "_internal"
    frontend = contents / "frontend"
    frontend.mkdir(parents=True)
    (frontend / "index.html").write_text(
        '<script src="assets/app.js"></script>', encoding="utf-8"
    )
    (frontend / "assets").mkdir()
    (frontend / "assets" / "app.js").write_text("// ok", encoding="utf-8")
    ocr_binary = runtime / "tesseract" if is_macos else runtime / "ocr" / "bin" / "tesseract"
    ocr_tessdata = (
        runtime.parent / "Resources" / "ocr" / "tessdata"
        if is_macos
        else runtime / "ocr" / "tessdata"
    )
    ocr_binary.parent.mkdir(parents=True)
    ocr_tessdata.mkdir(parents=True)
    executable_suffix = ".exe" if os.name == "nt" else ""
    if not is_macos:
        ocr_binary = ocr_binary.with_suffix(executable_suffix)
    ocr_binary.write_bytes(b"binary")
    (ocr_tessdata / "chi_sim.traineddata").write_bytes(b"data")
    (runtime / f"llama-server{executable_suffix}").write_bytes(b"binary")
    if is_macos:
        office = (
            runtime.parent
            / "Resources"
            / "office-runtime"
            / "LibreOffice.app"
            / "Contents"
            / "MacOS"
            / "soffice"
        )
    else:
        office = runtime / "office-runtime" / "program" / f"soffice{executable_suffix}"
    office.parent.mkdir(parents=True)
    office.write_bytes(b"binary")
    formatter = (
        runtime.parent / "Resources" / "formatter-host"
        if is_macos
        else runtime / "formatter-host"
    )
    formatter.mkdir()
    host = formatter / (
        "PartyOps.DocumentFormatter.Host.exe"
        if os.name == "nt"
        else "partyops-document-formatter-host"
    )
    host.write_bytes(b"MZ" if os.name == "nt" else b"native")
    if os.name != "nt":
        vtable_map = formatter / "word-vtable-map.json"
        vtable_map.write_bytes(b"test-vtable-map")
        (formatter / "LICENSE-WPS-SDK.txt").write_text("WPS license", encoding="utf-8")
        (formatter / "LICENSE-MONO-RUNTIME.txt").write_text("Mono license", encoding="utf-8")
        bridge = formatter / "wps-bridge"
        (bridge / "plugin").mkdir(parents=True)
        (bridge / "plugin" / "index.html").write_text("<!doctype html>", encoding="utf-8")
        (bridge / "plugin" / "main.js").write_text("// bridge", encoding="utf-8")
        (bridge / "LICENSE-WPS-SDK.txt").write_text("BSD-3-Clause", encoding="utf-8")
    record = {
        "schema": 2,
        "timezone": "Asia/Shanghai",
        "platform": (
            "windows" if os.name == "nt" else "macos" if sys.platform == "darwin" else "linux"
        ),
        "architecture": package_selftest._source_formatter_architecture(),
        "adapter": (
            "dotnet-framework-com-source"
            if os.name == "nt"
            else "wps-native-source-adapter"
        ),
        "source_project": "PartyOps.DocumentFormatter.AddIn",
        "source_snapshot_sha256": package_selftest.FORMATTER_SOURCE_SNAPSHOT_SHA256,
        "source_snapshot_files": package_selftest.FORMATTER_SOURCE_SNAPSHOT_FILES,
        "features": [
            "format",
            "replace",
            "redheader",
            "rename",
            "convert",
            "pdf-to-word",
        ],
        "capabilities": 25,
        "host_sha256": hashlib.sha256(host.read_bytes()).hexdigest(),
    }
    if os.name != "nt":
        record.update(
            {
                "built_at": "2026-09-01T09:00:00+08:00",
                "word_vtable_map_sha256": hashlib.sha256(vtable_map.read_bytes()).hexdigest(),
                "wps_sdk_header_sha256": package_selftest.FORMATTER_WPS_SDK_HEADER_SHA256,
                "wps_sdk_matched_methods": 528,
                "wps_sdk_mismatched_methods": 0,
                "native_bundle_runtime": "Mono JIT compiler version 6.8.0.123",
            }
        )
    if os.name == "nt":
        rules = formatter / "PartyOps.DocumentFormatter.AddIn.dll"
        rules.write_bytes(b"MZ")
        record["rules_sha256"] = hashlib.sha256(rules.read_bytes()).hexdigest()
    (formatter / "source-host.json").write_text(
        json.dumps(record),
        encoding="utf-8",
    )
    evidence = {
        "schema": 1,
        "status": "passed",
        "timezone": "Asia/Shanghai",
        "verified_at": "2026-09-01T09:00:00+08:00",
        "platform": record["platform"],
        "architecture": record["architecture"],
        "provider": "wps",
        "adapter": record["adapter"],
        "host_sha256": record["host_sha256"],
        "source_sha256": package_selftest.FORMATTER_SOURCE_FIXTURE_SHA256,
        "golden_sha256": package_selftest.FORMATTER_GOLDEN_FIXTURE_SHA256,
        "semantic_signature_sha256": "1" * 64,
        "rendered_pages": 3,
        "feature_cases": 10,
        "features": record["features"],
        "capabilities": 25,
        "parity_evidence_sha256": "2" * 64,
        "feature_evidence_sha256": "3" * 64,
    }
    if os.name != "nt":
        evidence["native_adapter_files_sha256"] = {
            name: hashlib.sha256((formatter / name).read_bytes()).hexdigest()
            for name in (
                "word-vtable-map.json",
                "LICENSE-WPS-SDK.txt",
                "LICENSE-MONO-RUNTIME.txt",
            )
        }
    (formatter / "runtime-evidence.json").write_text(
        json.dumps(evidence),
        encoding="utf-8",
    )
    return runtime


def test_runtime_contents_supports_onefile_and_onedir(tmp_path: Path) -> None:
    runtime = tmp_path / "runtime"
    runtime.mkdir()
    assert package_selftest._runtime_contents(runtime) == runtime
    (runtime / "_internal").mkdir()
    assert package_selftest._runtime_contents(runtime) == runtime / "_internal"
    expected_suffix = ".exe" if os.name == "nt" else ""
    assert package_selftest._native_executable(runtime, "tool").name == (
        f"tool{expected_suffix}"
    )


def test_windows_office_selftest_prefers_console_entry(tmp_path: Path) -> None:
    """Windows 捕获输出时必须使用会确定退出的 soffice.com。"""

    runtime = _runtime(tmp_path)
    program = runtime / "office-runtime" / "program"
    if os.name == "nt":
        console = program / "soffice.com"
        console.write_bytes(b"console")
        assert package_selftest._office_executable(runtime) == console
        console.unlink()
        assert package_selftest._office_executable(runtime) == program / "soffice.exe"
    elif sys.platform != "darwin":
        assert package_selftest._office_executable(runtime) == program / "soffice"
    else:
        assert package_selftest._office_executable(runtime).name == "soffice"
        assert "LibreOffice.app" in str(package_selftest._office_executable(runtime))


def test_macos_ocr_uses_standard_bundle_resource_layout(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    runtime = tmp_path / "PartyOps.app" / "Contents" / "MacOS"
    monkeypatch.setattr(package_selftest.sys, "platform", "darwin")
    binary, tessdata, library = package_selftest._ocr_runtime(runtime)
    assert binary == runtime / "tesseract"
    assert tessdata == runtime.parent / "Resources" / "ocr" / "tessdata"
    assert library == runtime.parent / "Resources" / "ocr" / "lib"


def test_macos_office_prefers_complete_nested_app(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    """macOS 公文转换必须从完整嵌套 App 启动，不能依赖兼容链接。"""

    runtime = tmp_path / "PartyOps.app" / "Contents" / "MacOS"
    nested = (
        runtime.parent
        / "Resources"
        / "office-runtime"
        / "LibreOffice.app"
        / "Contents"
        / "MacOS"
        / "soffice"
    )
    nested.parent.mkdir(parents=True)
    nested.write_bytes(b"native")
    monkeypatch.setattr(package_selftest.sys, "platform", "darwin")

    assert package_selftest._office_executable(runtime) == nested


def test_macos_bundle_helpers_fail_over_without_resource_links(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    """ZIP/安装器未建立兼容链接时仍应走明确的回退路径。"""

    runtime = tmp_path / "PartyOps.app" / "Contents" / "MacOS"
    runtime.mkdir(parents=True)
    monkeypatch.setattr(package_selftest.sys, "platform", "darwin")

    assert package_selftest._runtime_contents(runtime) == runtime
    assert package_selftest._office_executable(runtime).name in {
        "soffice.com",
        "soffice.exe",
        "soffice",
    }


@pytest.mark.parametrize(
    ("system_name", "machine", "expected_platform", "expected_architecture"),
    [
        ("linux", "x86_64", "linux", "amd64"),
        ("linux", "aarch64", "linux", "arm64"),
        ("linux", "loongarch64", "linux", "loong64"),
        ("darwin", "x86_64", "macos", "x86_64"),
        ("darwin", "arm64", "macos", "arm64"),
    ],
)
def test_source_formatter_platform_and_architecture_contract(
    monkeypatch: pytest.MonkeyPatch,
    system_name: str,
    machine: str,
    expected_platform: str,
    expected_architecture: str,
) -> None:
    """两种 Linux 与两种 macOS 架构必须映射到发布清单的固定名称。"""

    monkeypatch.setattr(package_selftest, "os", SimpleNamespace(name="posix"))
    monkeypatch.setattr(package_selftest.sys, "platform", system_name)
    monkeypatch.setattr(package_selftest.platform, "machine", lambda: machine)

    assert package_selftest._source_formatter_platform() == expected_platform
    assert package_selftest._source_formatter_architecture() == expected_architecture


def test_source_formatter_runtime_uses_native_bundle_layouts(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    """非 Windows 包只接受对应 Bundle/运行时目录中的原生 WPS 宿主。"""

    monkeypatch.setattr(package_selftest, "os", SimpleNamespace(name="posix"))

    linux_runtime = tmp_path / "linux-runtime"
    monkeypatch.setattr(package_selftest.sys, "platform", "linux")
    linux_host, linux_rules, linux_record = package_selftest._source_formatter_runtime(
        linux_runtime
    )
    assert linux_host == linux_runtime / "formatter-host" / "partyops-document-formatter-host"
    assert linux_rules is None
    assert linux_record == linux_runtime / "formatter-host" / "source-host.json"

    mac_runtime = tmp_path / "PartyOps.app" / "Contents" / "MacOS"
    monkeypatch.setattr(package_selftest.sys, "platform", "darwin")
    mac_host, mac_rules, mac_record = package_selftest._source_formatter_runtime(mac_runtime)
    expected_root = mac_runtime.parent / "Resources" / "formatter-host"
    assert mac_host == expected_root / "partyops-document-formatter-host"
    assert mac_rules is None
    assert mac_record == expected_root / "source-host.json"


@pytest.mark.parametrize(
    ("field", "invalid_value"),
    [
        ("schema", 1),
        ("timezone", "UTC"),
        ("source_project", "placeholder"),
        ("source_snapshot_sha256", "0" * 64),
        ("source_snapshot_files", 0),
        ("platform", "other"),
        ("architecture", "other"),
        ("features", ["format"]),
        ("capabilities", 24),
        ("host_sha256", "0" * 64),
    ],
)
def test_source_formatter_record_rejects_every_contract_mismatch(
    tmp_path: Path, field: str, invalid_value: object
) -> None:
    """来源清单任一约束漂移都必须阻断安装，不能只校验文件存在。"""

    runtime = _runtime(tmp_path)
    host, rules, record_path = package_selftest._source_formatter_runtime(runtime)
    record = json.loads(record_path.read_text(encoding="utf-8"))
    record[field] = invalid_value
    record_path.write_text(json.dumps(record), encoding="utf-8")

    with pytest.raises(RuntimeError, match="原排版源码宿主来源清单无效"):
        package_selftest._validate_source_formatter_record(host, rules, record_path)


def test_source_formatter_record_rejects_missing_invalid_and_tampered_files(
    tmp_path: Path,
) -> None:
    runtime = _runtime(tmp_path)
    host, rules, record_path = package_selftest._source_formatter_runtime(runtime)
    original_host = host.read_bytes()

    host.unlink()
    with pytest.raises(RuntimeError, match="原排版源码宿主不完整"):
        package_selftest._validate_source_formatter_record(host, rules, record_path)

    host.write_bytes(original_host)
    record_path.write_text("{", encoding="utf-8")
    with pytest.raises(RuntimeError, match="原排版源码宿主来源清单无效"):
        package_selftest._validate_source_formatter_record(host, rules, record_path)

    runtime = _runtime(tmp_path / "adapter")
    host, rules, record_path = package_selftest._source_formatter_runtime(runtime)
    record = json.loads(record_path.read_text(encoding="utf-8"))
    record["adapter"] = "fake-adapter"
    record_path.write_text(json.dumps(record), encoding="utf-8")
    with pytest.raises(RuntimeError, match="原排版源码宿主适配类型无效"):
        package_selftest._validate_source_formatter_record(host, rules, record_path)

    runtime = _runtime(tmp_path / "rules")
    host, rules, record_path = package_selftest._source_formatter_runtime(runtime)
    rules_for_test = rules or (tmp_path / "rules" / "formatter-rules.bin")
    rules_for_test.parent.mkdir(parents=True, exist_ok=True)
    rules_for_test.write_bytes(b"rules")
    record = json.loads(record_path.read_text(encoding="utf-8"))
    record["rules_sha256"] = "0" * 64
    record_path.write_text(json.dumps(record), encoding="utf-8")
    with pytest.raises(RuntimeError, match="原排版源码规则程序集哈希不一致"):
        package_selftest._validate_source_formatter_record(
            host, rules_for_test, record_path
        )

    runtime = _runtime(tmp_path / "native-no-rules")
    host, _rules, record_path = package_selftest._source_formatter_runtime(runtime)
    assert package_selftest._validate_source_formatter_record(
        host, None, record_path
    )["schema"] == 2


def test_source_formatter_selftest_rejects_process_and_payload_failures(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    runtime = _runtime(tmp_path)
    host, _rules, _record = package_selftest._source_formatter_runtime(runtime)

    monkeypatch.setattr(
        package_selftest.subprocess,
        "run",
        lambda *_args, **_kwargs: SimpleNamespace(
            returncode=9, stdout=b"", stderr=b"native failure"
        ),
    )
    with pytest.raises(RuntimeError, match="原排版源码宿主无法启动（退出码 9"):
        package_selftest._run_source_formatter_selftest(runtime, host)

    def invalid_json(command: list[str], **_kwargs: object) -> SimpleNamespace:
        Path(command[2]).write_text("{", encoding="utf-8")
        return SimpleNamespace(returncode=0, stdout=b"", stderr=b"")

    monkeypatch.setattr(package_selftest.subprocess, "run", invalid_json)
    with pytest.raises(RuntimeError, match="原排版源码宿主自检结果无效"):
        package_selftest._run_source_formatter_selftest(runtime, host)

    def invalid_contract(command: list[str], **_kwargs: object) -> SimpleNamespace:
        Path(command[2]).write_text(
            json.dumps({"schema": 1, "passed": False}), encoding="utf-8"
        )
        return SimpleNamespace(returncode=0, stdout=b"", stderr=b"")

    monkeypatch.setattr(package_selftest.subprocess, "run", invalid_contract)
    with pytest.raises(RuntimeError, match="原排版源码宿主自检结果无效"):
        package_selftest._run_source_formatter_selftest(runtime, host)


def test_source_formatter_evidence_rejects_missing_and_tampered_contract(
    tmp_path: Path,
) -> None:
    runtime = _runtime(tmp_path)
    host, _rules, record_path = package_selftest._source_formatter_runtime(runtime)
    adapter = json.loads(record_path.read_text(encoding="utf-8"))["adapter"]
    evidence_path = package_selftest._source_formatter_evidence(runtime)
    payload = package_selftest._validate_source_formatter_evidence(
        evidence_path, host, adapter
    )
    assert payload["provider"] == "wps"

    evidence_path.unlink()
    with pytest.raises(RuntimeError, match="缺少真实 WPS 验收证据"):
        package_selftest._validate_source_formatter_evidence(
            evidence_path, host, adapter
        )

    runtime = _runtime(tmp_path / "tampered")
    host, _rules, record_path = package_selftest._source_formatter_runtime(runtime)
    adapter = json.loads(record_path.read_text(encoding="utf-8"))["adapter"]
    evidence_path = package_selftest._source_formatter_evidence(runtime)
    evidence = json.loads(evidence_path.read_text(encoding="utf-8"))
    evidence["feature_cases"] = 11
    evidence_path.write_text(json.dumps(evidence), encoding="utf-8")
    with pytest.raises(RuntimeError, match="真实 WPS 验收证据无效"):
        package_selftest._validate_source_formatter_evidence(
            evidence_path, host, adapter
        )

def test_macos_native_helpers_strip_frozen_loader_state(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    monkeypatch.setattr(package_selftest.sys, "platform", "darwin")
    monkeypatch.setenv("_PYI_APPLICATION_HOME_DIR", "/tmp/partyops-onefile")
    monkeypatch.setenv("PYTHONHOME", "/tmp/python-home")
    monkeypatch.setenv("PYTHONPATH", "/tmp/python-path")
    monkeypatch.setenv("DYLD_LIBRARY_PATH", "/tmp/dyld")
    monkeypatch.setenv("LD_LIBRARY_PATH", "/tmp/ld")

    environment = package_selftest._native_child_environment(tmp_path)

    assert "_PYI_APPLICATION_HOME_DIR" not in environment
    assert "PYTHONHOME" not in environment
    assert "PYTHONPATH" not in environment
    assert "DYLD_LIBRARY_PATH" not in environment
    assert "LD_LIBRARY_PATH" not in environment
    assert environment["PATH"] == "/usr/bin:/bin:/usr/sbin:/sbin"
    assert package_selftest._native_runtime_timeout() == 120


def test_selftest_rejects_missing_frontend_assets_and_sqlite(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    runtime = tmp_path / "empty"
    runtime.mkdir()
    with pytest.raises(RuntimeError, match="前端入口缺失"):
        package_selftest.run_selftest(runtime)

    runtime = _runtime(tmp_path)
    frontend_assets = package_selftest._runtime_contents(runtime) / "frontend" / "assets"
    (frontend_assets / "app.js").unlink()
    with pytest.raises(RuntimeError, match="前端静态资源缺失"):
        package_selftest.run_selftest(runtime)

    (frontend_assets / "app.js").write_text(
        "// ok", encoding="utf-8"
    )
    monkeypatch.setattr(
        db_runtime,
        "validate_capabilities",
        lambda: {"safe_version": False, "fts5": True},
    )
    with pytest.raises(RuntimeError, match="SQLite 安全版本或 FTS5"):
        package_selftest.run_selftest(runtime)


def test_selftest_rejects_incomplete_or_unusable_native_runtimes(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    runtime = _runtime(tmp_path)
    monkeypatch.setattr(
        db_runtime,
        "validate_capabilities",
        lambda: {"safe_version": True, "fts5": True},
    )
    _ocr, tessdata, _library = package_selftest._ocr_runtime(runtime)
    (tessdata / "chi_sim.traineddata").unlink()
    with pytest.raises(RuntimeError, match="中文 OCR 运行时不完整"):
        package_selftest.run_selftest(runtime)

    (tessdata / "chi_sim.traineddata").write_bytes(b"data")
    monkeypatch.setattr(
        package_selftest.subprocess,
        "run",
        lambda *_args, **_kwargs: SimpleNamespace(returncode=1, stdout=""),
    )
    with pytest.raises(RuntimeError, match="中文 OCR 语言包无法加载"):
        package_selftest.run_selftest(runtime)

    monkeypatch.setattr(
        package_selftest.subprocess,
        "run",
        lambda *_args, **_kwargs: SimpleNamespace(returncode=0, stdout="eng\n"),
    )
    with pytest.raises(RuntimeError, match="中文 OCR 语言包无法加载"):
        package_selftest.run_selftest(runtime)


def test_selftest_validates_smart_runtime_and_llama(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    runtime = _runtime(tmp_path)
    monkeypatch.setattr(
        db_runtime,
        "validate_capabilities",
        lambda: {"safe_version": True, "fts5": True},
    )
    monkeypatch.setattr(
        package_selftest.importlib,
        "import_module",
        lambda name: SimpleNamespace(__version__="unknown" if name == "tokenizers" else "1.0"),
    )
    results = iter(
        [
            SimpleNamespace(returncode=0, stdout="eng\nchi_sim\n"),
            SimpleNamespace(returncode=1, stdout=""),
        ]
    )
    monkeypatch.setattr(
        package_selftest.subprocess, "run", lambda *_args, **_kwargs: next(results)
    )
    with pytest.raises(RuntimeError, match="本地 LLM 运行时无法启动"):
        package_selftest.run_selftest(runtime)

    package_selftest._native_executable(runtime, "llama-server").unlink()
    monkeypatch.setattr(
        package_selftest.subprocess,
        "run",
        lambda *_args, **_kwargs: SimpleNamespace(returncode=0, stdout="eng\nchi_sim\n"),
    )
    with pytest.raises(RuntimeError, match="本地 LLM 运行时缺失"):
        package_selftest.run_selftest(runtime)


@pytest.mark.skipif(not sys.platform.startswith("linux"), reason="龙芯 core 为 Linux 安装包")
def test_loong_core_selftest_requires_manifest_and_keeps_ordinary_checks(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(package_selftest.platform, "machine", lambda: "loongarch64")
    runtime = _runtime(tmp_path)
    monkeypatch.setattr(db_runtime, "validate_capabilities", lambda: {"safe_version": True, "fts5": True})
    with pytest.raises(RuntimeError, match="龙芯安装包身份无效"):
        package_selftest.run_selftest(runtime)

    header = bytearray(64)
    header[:6] = b"\x7fELF\x02\x01"
    header[18:20] = (258).to_bytes(2, "little")
    header[48:52] = (3).to_bytes(4, "little")
    executable = runtime / "partyops"
    executable.write_bytes(header)
    (runtime / "release-manifest.json").write_text(json.dumps({
        "schema_version": 1, "product": "PartyOps", "platform": "linux-deb",
        "architecture": "loong64", "runtime_profile": "core",
        "files": [{"path": "partyops", "size": len(header), "sha256": hashlib.sha256(header).hexdigest()}],
    }), encoding="utf-8")
    (runtime / "llama-server").unlink()

    imported: list[str] = []
    def fake_import(name: str) -> SimpleNamespace:
        imported.append(name)
        if name in {"onnxruntime", "tokenizers", "hf_xet"}:
            raise ImportError(name)
        return SimpleNamespace(__version__="2.0")
    monkeypatch.setattr(package_selftest.importlib, "import_module", fake_import)

    calls: list[list[str]] = []
    def fake_run(command: list[str], **_kwargs: object) -> SimpleNamespace:
        calls.append(command)
        if "--self-test" in command:
            Path(command[2]).write_text(json.dumps({
                "schema": 1, "passed": True, "engine": "source-standalone-batch-processor",
                "source_project": "PartyOps.DocumentFormatter.AddIn",
                "features": ["format", "replace", "redheader", "rename", "convert", "pdf-to-word"],
            }), encoding="utf-8")
        return SimpleNamespace(returncode=0, stdout="eng\nchi_sim\n", stderr="")
    monkeypatch.setattr(package_selftest.subprocess, "run", fake_run)
    result = package_selftest.run_selftest(runtime)
    assert result["passed"] is True
    assert result["runtime_profile"] == "core"
    assert result["smart_runtime"] == {"numpy": "2.0"}
    assert result["llama"] == "not-provided"
    assert {item["capability"] for item in result["deferred_checks"]} == {"semantic_rerank", "local_llm"}
    assert imported == ["numpy"]
    assert any("--list-langs" in command for command in calls)
    assert any("--version" in command and "soffice" in command[0] for command in calls)

    (runtime / "ocr" / "tessdata" / "chi_sim.traineddata").unlink()
    with pytest.raises(RuntimeError, match="中文 OCR 运行时不完整"):
        package_selftest.run_selftest(runtime)


def test_selftest_rejects_missing_or_unusable_office_runtime(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    runtime = _runtime(tmp_path)
    monkeypatch.setattr(
        db_runtime,
        "validate_capabilities",
        lambda: {"safe_version": True, "fts5": True},
    )
    monkeypatch.setattr(
        package_selftest.importlib,
        "import_module",
        lambda _name: SimpleNamespace(__version__="1.0"),
    )
    office = package_selftest._office_executable(runtime)
    office.unlink()
    monkeypatch.setattr(
        package_selftest.subprocess,
        "run",
        lambda *_args, **_kwargs: SimpleNamespace(
            returncode=0, stdout="eng\nchi_sim\n"
        ),
    )
    with pytest.raises(RuntimeError, match="公文转换运行时缺失"):
        package_selftest.run_selftest(runtime)

    office.write_bytes(b"binary")
    results = iter(
        [
            SimpleNamespace(returncode=0, stdout="eng\nchi_sim\n"),
            SimpleNamespace(returncode=0, stdout=""),
            SimpleNamespace(returncode=1, stdout=""),
        ]
    )
    monkeypatch.setattr(
        package_selftest.subprocess, "run", lambda *_args, **_kwargs: next(results)
    )
    with pytest.raises(
        RuntimeError,
        match=r"公文转换运行时无法启动（退出码 1；stdout=无输出；stderr=无输出）",
    ):
        package_selftest.run_selftest(runtime)


def test_native_failure_detail_is_bounded_and_decodes_bytes() -> None:
    result = SimpleNamespace(returncode=-9, stdout=b"", stderr=b"x" * 900)
    detail = package_selftest._native_failure_detail("原生运行时失败", result)
    assert detail.startswith("原生运行时失败（退出码 -9；stdout=无输出；stderr=")
    assert detail.endswith("）")
    assert len(detail) < 900


def test_selftest_and_cli_success_and_failure(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    runtime = _runtime(tmp_path)
    monkeypatch.setattr(
        db_runtime,
        "validate_capabilities",
        lambda: {"safe_version": True, "fts5": True},
    )
    monkeypatch.setattr(
        package_selftest.importlib,
        "import_module",
        lambda _name: SimpleNamespace(__version__="1.0"),
    )
    results = iter(
        [
            SimpleNamespace(returncode=0, stdout="eng\nchi_sim\n"),
            SimpleNamespace(returncode=0, stdout=""),
            SimpleNamespace(returncode=0, stdout=""),
        ]
    )
    calls: list[dict[str, object]] = []

    def fake_run(*_args: object, **kwargs: object) -> SimpleNamespace:
        calls.append({"command": _args[0], **kwargs})
        command = _args[0]
        if isinstance(command, list) and "--self-test" in command:
            Path(command[2]).write_text(
                json.dumps(
                    {
                        "schema": 1,
                        "passed": True,
                        "engine": "source-standalone-batch-processor",
                        "source_project": "PartyOps.DocumentFormatter.AddIn",
                        "features": [
                            "format",
                            "replace",
                            "redheader",
                            "rename",
                            "convert",
                            "pdf-to-word",
                        ],
                    }
                ),
                encoding="utf-8",
            )
            return SimpleNamespace(returncode=0, stdout="", stderr="")
        return next(results)

    monkeypatch.setattr(package_selftest.subprocess, "run", fake_run)
    result = package_selftest.run_selftest(runtime)
    assert result["passed"] is True
    assert result["frontend_assets"] == 1
    assert result["smart_runtime"] == {
        "numpy": "1.0",
        "onnxruntime": "1.0",
        "tokenizers": "1.0",
    }
    assert result["crypto"] == "tls-ed25519-passed"
    assert result["document_formatter"] == {
        "features": 6,
        "capabilities": 25,
        "office_runtime": "passed",
        "source_host": "passed",
        "source_adapter": (
            "dotnet-framework-com-source"
            if os.name == "nt"
            else "wps-native-source-adapter"
        ),
        "source_host_selftest": "source-standalone-batch-processor",
        "source_wps_evidence": "2026-09-01T09:00:00+08:00",
    }
    ocr_environment = calls[0]["env"]
    assert isinstance(ocr_environment, dict)
    if os.name == "nt":
        assert "TESSDATA_PREFIX" not in ocr_environment
    else:
        assert ocr_environment["TESSDATA_PREFIX"] == str(
            package_selftest._ocr_runtime(runtime)[1]
        )
    if sys.platform == "darwin":
        assert "LD_LIBRARY_PATH" not in ocr_environment
    else:
        assert ocr_environment["LD_LIBRARY_PATH"].split(os.pathsep)[0] == str(
            runtime / "ocr" / "lib"
        )
    assert calls[1]["timeout"] == (120 if sys.platform == "darwin" else 30)
    assert calls[2]["timeout"] == 120
    office_command = calls[2]["command"]
    assert isinstance(office_command, list)
    assert office_command[1] == "--headless"
    assert str(office_command[2]).startswith("-env:UserInstallation=file:")
    assert office_command[3] == "--version"

    from app import official_format_features

    monkeypatch.setattr(official_format_features, "FEATURE_DEFINITIONS", {})
    monkeypatch.setattr(
        package_selftest.subprocess,
        "run",
        lambda command, **_kwargs: SimpleNamespace(
            returncode=0,
            stdout="eng\nchi_sim\n" if "--list-langs" in command else "",
            stderr="",
        ),
    )
    with pytest.raises(RuntimeError, match="公文排版能力清单不完整"):
        package_selftest.run_selftest(runtime)

    monkeypatch.setattr(package_selftest, "run_selftest", lambda _runtime: result)
    assert package_selftest.main(runtime) == 0
    assert '"passed": true' in capsys.readouterr().out
    monkeypatch.setattr(
        package_selftest,
        "run_selftest",
        lambda _runtime: (_ for _ in ()).throw(RuntimeError("自检失败")),
    )
    assert package_selftest.main(runtime) == 2
    assert "自检失败" in capsys.readouterr().out
