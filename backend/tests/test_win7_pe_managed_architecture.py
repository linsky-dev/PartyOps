"""Windows 7 PE 架构门禁对 AnyCPU 与官方双架构前置包的回归测试。"""

from __future__ import annotations

import hashlib
import importlib.util
from pathlib import Path
from types import ModuleType, SimpleNamespace

import pytest

ROOT = Path(__file__).resolve().parents[2]


def _module() -> ModuleType:
    spec = importlib.util.spec_from_file_location(
        "partyops_validate_win7_pe",
        ROOT / "scripts" / "validate-win7-pe.py",
    )
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def test_anycpu_formatter_dependencies_are_valid_for_amd64() -> None:
    """原排版源码的纯 IL AnyCPU DLL 在 x64 进程中合法加载。"""

    module = _module()
    library = ROOT / "vendor/document-formatter-source/lib/UglyToad.PdfPig.dll"

    assert module.validate_pe(library, "amd64", {}) == []


def test_untrusted_x86_executable_is_not_treated_as_amd64() -> None:
    """仅有 I386 机器值而没有 AnyCPU 语义时仍必须被 x64 门禁拒绝。"""

    module = _module()
    installer = (
        ROOT
        / "vendor/windows/dotnet-framework-4.8/ndp48-x86-x64-allos-enu.exe"
    )

    errors = module.validate_pe(installer, "amd64", {})
    assert any("架构为 0x014c，期望 amd64" in error for error in errors)


def test_exact_verified_dotnet_installer_is_allowed_as_universal() -> None:
    """固定路径与哈希匹配时，微软 x86/x64 离线引导程序可随包分发。"""

    module = _module()
    installer = (
        ROOT
        / "vendor/windows/dotnet-framework-4.8/ndp48-x86-x64-allos-enu.exe"
    ).resolve()

    assert module.validate_pe(
        installer,
        "amd64",
        {},
        {installer: _sha256(installer)},
    ) == []


@pytest.mark.parametrize("dll,name", [
    ("api-ms-win-core-synch-l1-2-0.dll", "WaitOnAddress"),
    ("kernel32.dll", "WakeByAddressAll"),
    ("kernel32.dll", "WakeByAddressSingle"),
    ("bcryptprimitives.dll", "ProcessPrng"),
])
def test_reject_rust_imports_unavailable_on_win7(monkeypatch, tmp_path, dll, name):
    """CPython3.8轮子标签不能证明其Rust标准库仍支持Win7。"""
    module = _module()
    image = SimpleNamespace(
        FILE_HEADER=SimpleNamespace(Machine=0x8664),
        OPTIONAL_HEADER=SimpleNamespace(MajorSubsystemVersion=6, MinorSubsystemVersion=1),
        DIRECTORY_ENTRY_IMPORT=[SimpleNamespace(dll=dll.encode(), imports=[SimpleNamespace(name=name.encode())])],
        close=lambda: None,
    )
    monkeypatch.setattr(module.pefile, "PE", lambda *args, **kwargs: image)
    errors = module.validate_pe(tmp_path / "calamine.pyd", "amd64", {})
    assert errors and any("Win7 不支持" in error for error in errors)


def test_ucrt_sleep_import_remains_compatible(monkeypatch, tmp_path):
    """官方app-local UCRT的APISet Sleep转发不能与Win8同步接口混淆。"""
    module = _module()
    image = SimpleNamespace(
        FILE_HEADER=SimpleNamespace(Machine=0x8664),
        OPTIONAL_HEADER=SimpleNamespace(MajorSubsystemVersion=6, MinorSubsystemVersion=1),
        DIRECTORY_ENTRY_IMPORT=[SimpleNamespace(dll=b"api-ms-win-core-synch-l1-2-0.dll", imports=[SimpleNamespace(name=b"Sleep")])],
        close=lambda: None,
    )
    monkeypatch.setattr(module.pefile, "PE", lambda *args, **kwargs: image)
    assert module.validate_pe(tmp_path / "ucrtbase.dll", "amd64", {}) == []
