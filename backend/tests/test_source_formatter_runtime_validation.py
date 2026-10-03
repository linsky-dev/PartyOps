"""Linux/macOS 原源码排版宿主的静态与动态发布门禁。"""

from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
from types import ModuleType, SimpleNamespace

import pytest

ROOT = Path(__file__).resolve().parents[2]
TEST_VTABLE_MAP = b"locked-wps-vtable-map\n"
TEST_VTABLE_MAP_SHA256 = hashlib.sha256(TEST_VTABLE_MAP).hexdigest()
BUNDLED_CORE_TRACE = "Mono: Assembly Loader loaded assembly from bundle: 'mscorlib.dll'."


def _module() -> ModuleType:
    spec = importlib.util.spec_from_file_location(
        "partyops_source_formatter_runtime_validation",
        ROOT / "scripts" / "validate-source-formatter-runtime.py",
    )
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _runtime(tmp_path: Path) -> Path:
    runtime = tmp_path / "formatter-host"
    runtime.mkdir()
    host = runtime / "partyops-document-formatter-host"
    host.write_bytes(b"native-wps-adapter")
    (runtime / "word-vtable-map.json").write_bytes(TEST_VTABLE_MAP)
    (runtime / "LICENSE-WPS-SDK.txt").write_text("WPS BSD-3-Clause", encoding="utf-8")
    (runtime / "LICENSE-MONO-RUNTIME.txt").write_text("Mono MIT", encoding="utf-8")
    (runtime / "source-host.json").write_text(
        json.dumps(
            {
                "schema": 2,
                "platform": "linux",
                "architecture": "amd64",
                "adapter": "wps-native-source-adapter",
                "source_project": "PartyOps.DocumentFormatter.AddIn",
                "source_snapshot_sha256": "15c21b886f6a958fb61a3b106266b446a2b959b0085510015eeb790efaa770d3",
                "source_snapshot_files": 898,
                "timezone": "Asia/Shanghai",
                "built_at": "2026-09-01T18:00:00+08:00",
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
                "word_vtable_map_sha256": TEST_VTABLE_MAP_SHA256,
                "wps_sdk_header_sha256": "4d0529c076f8f36ce49301982e0c2bb46cdcc4087c3e9945a9b57d649fe26791",
                "wps_sdk_matched_methods": 528,
                "wps_sdk_mismatched_methods": 0,
                "native_bundle_runtime": "Mono JIT compiler version 6.8.0.123",
            }
        ),
        encoding="utf-8",
    )
    return runtime


@pytest.mark.parametrize(
    "trace",
    [
        BUNDLED_CORE_TRACE,
        "Mono: Assembly Loader loaded assembly from bundle: '/home/liuan/ist_mono/lib/mono/4.5/mscorlib.dll'.",
    ],
)
def test_native_source_formatter_runtime_requires_exact_manifest_and_selftest(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, trace: str
) -> None:
    module = _module()
    module.WORD_VTABLE_MAP_SHA256 = TEST_VTABLE_MAP_SHA256
    runtime = _runtime(tmp_path)

    def fake_run(command: list[str], **kwargs: object) -> SimpleNamespace:
        assert kwargs["env"]["MONO_LOG_LEVEL"] == "debug"
        assert kwargs["env"]["MONO_LOG_MASK"] == "asm"
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
        return SimpleNamespace(returncode=0, stdout=trace, stderr="")

    monkeypatch.setattr(module.subprocess, "run", fake_run)
    result = module.validate(runtime, "linux", "amd64")
    assert result["passed"] is True
    assert result["features"] == 6
    assert result["capabilities"] == 25

    (runtime / "partyops-document-formatter-host").write_bytes(b"tampered")
    with pytest.raises(RuntimeError, match="host_sha256"):
        module.validate(runtime, "linux", "amd64")


def test_native_source_formatter_runtime_rejects_wrong_platform_and_fake_success(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    module = _module()
    module.WORD_VTABLE_MAP_SHA256 = TEST_VTABLE_MAP_SHA256
    runtime = _runtime(tmp_path)
    with pytest.raises(RuntimeError, match="platform"):
        module.validate(runtime, "macos", "amd64")

    def fake_run(command: list[str], **_kwargs: object) -> SimpleNamespace:
        Path(command[2]).write_text('{"passed":true}', encoding="utf-8")
        return SimpleNamespace(returncode=0, stdout="", stderr="")

    monkeypatch.setattr(module.subprocess, "run", fake_run)
    with pytest.raises(RuntimeError, match="能力结果不完整"):
        module.validate(runtime, "linux", "amd64")


@pytest.mark.parametrize(
    "trace",
    [
        "",
        "Mono: Assembly Loader loaded assembly from bundle: 'mscorlib.dll.bak'.",
        "Mono: Assembly Loader loaded assembly from bundle: '/usr/lib/mono/fake-mscorlib.dll'.",
        "Mono: Assembly Loader probing location: '/usr/lib/mono/4.5/mscorlib.dll'.",
        "Mono: Assembly Loader loaded assembly from bundle: '/usr/lib/mono/4.5/\nmscorlib.dll'.",
        BUNDLED_CORE_TRACE + "\nMono: Assembly Loader loaded assembly from location: '/usr/lib/mono/4.5/System.dll'.",
    ],
)
def test_native_runtime_rejects_external_or_unobserved_assemblies(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, trace: str
) -> None:
    """即使业务自检返回成功，也不能借用构建机程序集或缺失加载证据。"""
    module = _module()
    module.WORD_VTABLE_MAP_SHA256 = TEST_VTABLE_MAP_SHA256
    runtime = _runtime(tmp_path)

    def fake_run(command: list[str], **_kwargs: object) -> SimpleNamespace:
        Path(command[2]).write_text(
            json.dumps({
                "schema": 1,
                "passed": True,
                "engine": "source-standalone-batch-processor",
                "source_project": "PartyOps.DocumentFormatter.AddIn",
                "features": module.FEATURES,
            }),
            encoding="utf-8",
        )
        return SimpleNamespace(returncode=0, stdout=trace, stderr="")

    monkeypatch.setattr(module.subprocess, "run", fake_run)
    with pytest.raises(RuntimeError, match="FORMATTER_RUNTIME_ASSEMBLY_NOT_BUNDLED"):
        module.validate(runtime, "linux", "amd64")


def test_native_source_formatter_builder_and_packages_keep_complete_runtime() -> None:
    """发布脚本不能只复制宿主文件而漏掉槽位表或原生运行时许可。"""

    builder = (ROOT / "scripts" / "build-document-formatter-host-unix.sh").read_text(
        encoding="utf-8"
    )
    portable = (ROOT / "packaging" / "uos" / "build-portable.sh").read_text(
        encoding="utf-8"
    )
    macos = (ROOT / "packaging" / "macos" / "build-pkg.sh").read_text(
        encoding="utf-8"
    )
    validation = (
        ROOT / "packaging" / "macos" / "validate-bundle.sh"
    ).read_text(encoding="utf-8")

    # 不递归加载未执行的 VSTO 引用，但必须显式带齐基础程序集和中文编码。
    assert "mkbundle --simple --nodeps --i18n none" in builder
    assert '"$MONO_FRAMEWORK_ROOT"/*.dll' in builder
    assert '"$MONO_FRAMEWORK_ROOT"/Facades/*.dll' in builder
    assert 'bundle_inputs+=("$framework")' in builder
    assert "mscorlib.dll" in builder and "I18N.CJK.dll" in builder
    assert '"libmono-native.$library_extension"' in builder
    assert '"libMonoPosixHelper.$library_extension"' in builder
    assert '"${native_libraries[@]}"' in builder
    assert "32/64" in builder and "32bits-preferred" in builder
    assert "MACOSX_DEPLOYMENT_TARGET='11.0'" in builder
    assert "FORMATTER_NATIVE_DEPLOYMENT_TARGET_TOO_NEW" in builder
    assert "wps_sdk_matched_methods\": 528" in builder
    assert "wps_sdk_mismatched_methods\": 0" in builder
    assert "LICENSE-MONO-RUNTIME.txt" in builder
    assert 'cp -a "$FORMATTER_RUNTIME/." "$RUNTIME/formatter-host/"' in portable
    assert '/usr/bin/ditto "$FORMATTER_RUNTIME"' in macos
    for script in (portable, macos, validation):
        assert "word-vtable-map.json" in script
        assert "LICENSE-MONO-RUNTIME.txt" in script
