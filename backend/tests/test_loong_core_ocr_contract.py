"""Loong64 普通包 OCR 候选输入的拒绝分支。"""

from __future__ import annotations

import hashlib
import importlib.util
import json
import subprocess
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "loong_ocr_contract", ROOT / "scripts/validate-loong64-deepin-ocr.py"
)
assert SPEC is not None and SPEC.loader is not None
ocr = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ocr)
PRODUCTION_FIXTURE_SHA256 = "fa6214516c340d208f134f0ef935bcef7c97da0da23ff224aba3d4a8db24b074"


def _synthetic_fixture(tmp_path: Path, monkeypatch: pytest.MonkeyPatch | None = None) -> Path:
    """提供单测专用字节输入；不代表 Loong64 设备上的中文 OCR 实测。"""
    fixture = tmp_path / "synthetic-ocr-input.dat"
    fixture.write_bytes(b"synthetic OCR contract input\n")
    if monkeypatch is not None:
        monkeypatch.setattr(ocr, "FIXTURE_SHA256", hashlib.sha256(fixture.read_bytes()).hexdigest())
    return fixture


def _elf(machine: int = 258, abi: int = 3) -> bytes:
    header = bytearray(64)
    header[:6] = b"\x7fELF\x02\x01"
    header[18:20] = machine.to_bytes(2, "little")
    header[48:52] = abi.to_bytes(4, "little")
    return bytes(header)


def _candidate(tmp_path: Path) -> Path:
    root = tmp_path / "ocr"
    files = {
        "bin/tesseract": (_elf(), "tesseract-ocr"),
        "lib/libtesseract.so": (_elf(), "libtesseract5"),
        "tessdata/chi_sim.traineddata": (b"Chinese language data", "tesseract-ocr-chi-sim"),
        "tessdata/eng.traineddata": (b"English language data", "tesseract-ocr-eng"),
        "licenses/copyright": (b"Deepin package copyright", "tesseract-ocr"),
    }
    packages = {
        name: {
            "version": version,
            "origin": ocr.SOURCE_ORIGIN,
            "architecture": "all" if name.endswith(("chi-sim", "eng")) else "loong64",
            "deb_sha256": "a" * 64,
        }
        for name, version in {
            **ocr.PACKAGE_VERSIONS,
            "libtesseract5": "5.5.0-1deepin1",
        }.items()
    }
    entries = {}
    for name, (content, package) in files.items():
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
        entries[name] = {
            "package": package,
            "package_version": packages[package]["version"],
            "source_path": f"/usr/{name}",
            "bytes": len(content),
            "sha256": hashlib.sha256(content).hexdigest(),
        }
    (root / "SOURCE.json").write_text(
        json.dumps({
            "schema": 1, "origin": ocr.SOURCE_ORIGIN, "architecture": "loong64",
            "version": "5.5.0", "packages": packages, "files": entries,
        }), encoding="utf-8"
    )
    return root


def _runtime(monkeypatch: pytest.MonkeyPatch, root: Path, *, missing: bool = False,
             languages: str = "chi_sim\neng\n") -> None:
    def fake_run(command: list[str], **_: object) -> subprocess.CompletedProcess[str]:
        if command[0] == "ldd":
            dependency = "libtesseract.so => not found" if missing else (
                f"libtesseract.so => {root / 'lib/libtesseract.so'} (0x1)"
            )
            return subprocess.CompletedProcess(command, 0, dependency, "")
        if "--version" in command:
            return subprocess.CompletedProcess(command, 0, "tesseract 5.5.0\n", "")
        if "--list-langs" in command:
            return subprocess.CompletedProcess(command, 0, languages, "")
        return subprocess.CompletedProcess(command, 0, "同志们 格式文字\n", "")

    monkeypatch.setattr(ocr.subprocess, "run", fake_run)


def test_valid_source_and_runtime_contract(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    root = _candidate(tmp_path)
    _runtime(monkeypatch, root)
    fixture = _synthetic_fixture(tmp_path, monkeypatch)
    assert ocr.validate(root, fixture)["version"] == "5.5.0"


def test_tampered_synthetic_fixture_rejected(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    root = _candidate(tmp_path)
    fixture = _synthetic_fixture(tmp_path, monkeypatch)
    fixture.write_bytes(b"tampered synthetic OCR input\n")
    with pytest.raises(ValueError, match="OCR_CHINESE_FIXTURE_INVALID"):
        ocr.validate(root, fixture, check_runtime=False)


def test_production_hash_rejects_synthetic_fixture(tmp_path: Path) -> None:
    root = _candidate(tmp_path)
    fixture = _synthetic_fixture(tmp_path)
    assert ocr.FIXTURE_SHA256 == PRODUCTION_FIXTURE_SHA256
    assert hashlib.sha256(fixture.read_bytes()).hexdigest() != PRODUCTION_FIXTURE_SHA256
    with pytest.raises(ValueError, match="OCR_CHINESE_FIXTURE_INVALID"):
        ocr.validate(root, fixture, check_runtime=False)


@pytest.mark.parametrize("change,error", [
    ("tamper", "OCR_SOURCE_ENTRY_MISMATCH"),
    ("wrong_arch", "OCR_ELF_ARCH_ABI_INVALID"),
    ("wrong_abi", "OCR_ELF_ARCH_ABI_INVALID"),
    ("missing_lib", "OCR_PRIVATE_CLOSURE_INCOMPLETE"),
    ("missing_data", "OCR_PRIVATE_CLOSURE_INCOMPLETE"),
    ("wrong_origin", "OCR_SOURCE_PACKAGE_INVALID"),
])
def test_invalid_candidate_rejected(tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
                                   change: str, error: str) -> None:
    root = _candidate(tmp_path)
    fixture = _synthetic_fixture(tmp_path, monkeypatch)
    if change == "tamper":
        (root / "tessdata/eng.traineddata").write_bytes(b"tampered")
    elif change == "wrong_arch":
        (root / "bin/tesseract").write_bytes(_elf(machine=62))
    elif change == "wrong_abi":
        (root / "bin/tesseract").write_bytes(_elf(abi=1))
    elif change == "missing_lib":
        (root / "lib/libtesseract.so").unlink()
    elif change == "missing_data":
        (root / "tessdata/chi_sim.traineddata").unlink()
    else:
        source = json.loads((root / "SOURCE.json").read_text(encoding="utf-8"))
        source["packages"]["tesseract-ocr"]["origin"] = "local build"
        (root / "SOURCE.json").write_text(json.dumps(source), encoding="utf-8")
    if change in {"wrong_arch", "wrong_abi"}:
        source = json.loads((root / "SOURCE.json").read_text(encoding="utf-8"))
        source["files"]["bin/tesseract"]["sha256"] = hashlib.sha256(
            (root / "bin/tesseract").read_bytes()
        ).hexdigest()
        (root / "SOURCE.json").write_text(json.dumps(source), encoding="utf-8")
    with pytest.raises(ValueError, match=error):
        ocr.validate(root, fixture, check_runtime=False)


def test_missing_dynamic_dependency_rejected(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    root = _candidate(tmp_path)
    fixture = _synthetic_fixture(tmp_path, monkeypatch)
    _runtime(monkeypatch, root, missing=True)
    with pytest.raises(ValueError, match="OCR_RUNTIME_DEPENDENCY_MISSING"):
        ocr.validate(root, fixture)


def test_missing_loaded_language_rejected(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    root = _candidate(tmp_path)
    fixture = _synthetic_fixture(tmp_path, monkeypatch)
    _runtime(monkeypatch, root, languages="eng\n")
    with pytest.raises(ValueError, match="OCR_LANGUAGES_UNAVAILABLE"):
        ocr.validate(root, fixture)
