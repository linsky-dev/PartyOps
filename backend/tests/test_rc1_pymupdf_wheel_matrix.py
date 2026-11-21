"""Published PyMuPDF 2.1 native wheels cannot be extrapolated to other OSes."""

from __future__ import annotations

import importlib.util
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "rc1_pymupdf_wheel_matrix",
    ROOT / "scripts/rc1_pymupdf_21_wheel_matrix.py",
)
assert spec and spec.loader
matrix = importlib.util.module_from_spec(spec)
spec.loader.exec_module(matrix)

OFFICIAL = (
    "pymupdf-2.1-cp311-abi3-win_amd64.whl",
    "pymupdf-2.1-cp311-abi3-manylinux_2_28_x86_64.whl",
    "pymupdf-2.1-cp311-abi3-manylinux_2_28_aarch64.whl",
    "pymupdf-2.1-cp311-abi3-macosx_11_0_arm64.whl",
)


def _lock(tmp_path: Path, names: tuple[str, ...], version: str = "2.1") -> Path:
    file = tmp_path / "candidate.lock"
    records = ",\n".join(
        f'  {{url = "https://files.pythonhosted.org/packages/{name}"}}'
        for name in names
    )
    file.write_text(
        f'[[package]]\nname = "pymupdf"\nversion = "{version}"\n'
        f"wheels = [\n{records}\n]\n",
        encoding="utf-8",
    )
    return file


def test_real_four_wheel_inventory_does_not_certify_other_targets(tmp_path: Path) -> None:
    result = matrix.inspect(_lock(tmp_path, OFFICIAL))
    targets = result["targets"]
    assert targets["windows-10-x64"]["status"] == "WHEEL_PRESENT_UNVERIFIED_NATIVE"
    assert targets["windows-11-x64"]["status"] == "WHEEL_PRESENT_UNVERIFIED_NATIVE"
    assert targets["macos-apple-silicon"]["status"] == "WHEEL_PRESENT_UNVERIFIED_NATIVE"
    assert targets["macos-intel"]["status"] == "BLOCKED"
    assert targets["windows-11-arm64"]["status"] == "BLOCKED"
    assert targets["deepin-deb-loong64"]["status"] == "BLOCKED"
    assert targets["windows-7-x86"]["status"] == "BLOCKED"
    assert not result["all_platform_binary_compatibility_proven"]
    assert not result["release_ready"]
    assert not result["production_lock_changed"]


def test_a_sdist_cannot_replace_missing_native_windows_wheel(tmp_path: Path) -> None:
    linux_only = OFFICIAL[1:]
    with pytest.raises(ValueError, match="Windows x64.*absent"):
        matrix.inspect(_lock(tmp_path, linux_only))


def test_unrelated_pymupdf_lite_arm64_is_not_full_pymupdf(tmp_path: Path) -> None:
    deceptive = OFFICIAL + ("pymupdf_lite-2.1-cp311-abi3-win_arm64.whl",)
    result = matrix.inspect(_lock(tmp_path, deceptive))
    assert result["targets"]["windows-11-arm64"]["status"] == "BLOCKED"


def test_wrong_pymupdf_dependency_version_cannot_pass(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="PyMuPDF==2.1"):
        matrix.inspect(_lock(tmp_path, OFFICIAL, version="1.26.0"))


def test_pure_python_wheel_does_not_count_as_native_binary() -> None:
    assert not matrix.wheel_matches("pymupdf-2.1-py3-none-any.whl", "win_amd64")
