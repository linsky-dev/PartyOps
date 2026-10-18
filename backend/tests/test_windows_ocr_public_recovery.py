"""公开成品 OCR 恢复必须保留原 ZIP 门禁与逐文件完整性。"""

import hashlib
import json
import os
import shutil
import subprocess
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
RECOVERED_NAME = "tesseract-5.5.3-windows-amd64-recovered"
MANIFEST_SHA256 = "fa4ef50093a52c72932550a48c01cdea7fe44056d44bcc6460fa40cdffdbdff3"


def test_public_ocr_manifest_is_fixed_and_includes_original_evidence():
    manifest_path = ROOT / "packaging/windows/ocr-public-recovery-manifest.json"
    assert hashlib.sha256(manifest_path.read_bytes()).hexdigest() == MANIFEST_SHA256
    manifest = json.loads(manifest_path.read_bytes())
    assert len(manifest["files"]) == 62
    assert manifest["installer_sha256"] == "a7c044b0b830ce416fdf02c84daa2e7aa4929c0833d9b530bc47486d861a81be"
    for name in ("SOURCE.json", "licenses/LICENSE-tesseract.txt", "licenses/LICENSE-tessdata-fast.txt"):
        assert name in manifest["files"]
    assert manifest["files"]["tessdata/chi_sim.traineddata"] == "a5fcb6f0db1e1d6d8522f39db4e848f05984669172e584e8d76b6b3141e1f730"
    assert manifest["files"]["tessdata/eng.traineddata"] == "7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2"
    assert manifest["files"]["tessdata/osd.traineddata"] == "9cf5d576fcc47564f11265841e5ca839001e7e6f38ff7f7aacf46d15a96b00ff"


@pytest.fixture(scope="module")
def recovered_repo(tmp_path_factory):
    if os.name != "nt":
        pytest.skip("需要 Windows PowerShell 与原生 OCR")
    source = ROOT / "vendor/windows/ocr" / RECOVERED_NAME
    if not source.is_dir():
        pytest.skip("先按恢复文档提取固定公开安装包")
    repo = tmp_path_factory.mktemp("ocr_public_recovery")
    scripts = repo / "packaging/windows"
    scripts.mkdir(parents=True)
    for name in ("prepare-ocr-runtime.ps1", "recover-ocr-from-public-installer.ps1", "ocr-public-recovery-manifest.json"):
        shutil.copyfile(ROOT / "packaging/windows" / name, scripts / name)
    shutil.copytree(source, repo / "vendor/windows/ocr" / RECOVERED_NAME)
    return repo


def prepare(repo, destination="artifacts/validation/ocr"):
    # 明确设置现代目标；每次错误都在写入目标目录之前报告。
    script = "$ErrorActionPreference = 'Stop'; . ./packaging/windows/prepare-ocr-runtime.ps1; $env:PARTYOPS_LEGACY_ARCH = ''; Expand-VerifiedPartyOpsOcrRuntime -RepoRoot (Get-Location).Path -Destination (Join-Path (Get-Location).Path $env:PARTYOPS_OCR_TEST_DESTINATION)"
    return subprocess.run(
        ["powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script],
        # Python 启动 PS5 时不能继承 PS7 的模块路径，否则无法加载内置哈希命令。
        env={**{key: value for key, value in os.environ.items() if key.lower() != "psmodulepath"},
             "PARTYOPS_OCR_TEST_DESTINATION": destination},
        cwd=repo, capture_output=True, text=True, errors="replace", timeout=60,
    )


def test_recovered_ocr_passes_original_version_and_language_checks(recovered_repo):
    result = prepare(recovered_repo)
    assert result.returncode == 0, result.stdout + result.stderr
    source = recovered_repo / "vendor/windows/ocr" / RECOVERED_NAME
    output = recovered_repo / "artifacts/validation/ocr"
    assert (output / "SOURCE.json").read_bytes() == (source / "SOURCE.json").read_bytes()
    manifest = json.loads((recovered_repo / "packaging/windows/ocr-public-recovery-manifest.json").read_bytes())
    assert {p.relative_to(output).as_posix() for p in output.rglob("*") if p.is_file()} == set(manifest["files"])


def test_ocr_target_junction_is_rejected_before_external_files_change(recovered_repo, tmp_path):
    """真实目录联接不能令目标清理或复制触及工作区外的文件。"""
    outside = tmp_path / "outside"
    external_ocr = outside / "ocr"
    external_ocr.mkdir(parents=True)
    sentinel = external_ocr / "保留业务文件.txt"
    sentinel.write_bytes(b"preserve-external-file")
    junction = recovered_repo / "artifacts/junction-check"
    environment = {key: value for key, value in os.environ.items() if key.lower() != "psmodulepath"}
    environment.update(PARTYOPS_OCR_TEST_JUNCTION=str(junction), PARTYOPS_OCR_TEST_EXTERNAL=str(outside))
    created = subprocess.run(
        ["powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
         "$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path $env:PARTYOPS_OCR_TEST_JUNCTION -Target $env:PARTYOPS_OCR_TEST_EXTERNAL | Out-Null"],
        env=environment, capture_output=True, text=True, errors="replace", timeout=30,
    )
    assert created.returncode == 0, created.stdout + created.stderr
    before = {p.relative_to(outside).as_posix(): p.read_bytes() for p in outside.rglob("*") if p.is_file()}
    try:
        result = prepare(recovered_repo, "artifacts/junction-check/ocr")
        assert result.returncode != 0, result.stdout + result.stderr
        assert "[OCR_TARGET_REPARSE_POINT]" in result.stdout + result.stderr
        after = {p.relative_to(outside).as_posix(): p.read_bytes() for p in outside.rglob("*") if p.is_file()}
        assert after == before
        assert sentinel.read_bytes() == b"preserve-external-file"
    finally:
        # rmdir 仅移除该目录联接本身，不递归处理它指向的目录。
        junction.rmdir()


@pytest.mark.parametrize("mode", ["alter", "missing", "extra", "manifest", "bad_zip"])
def test_recovered_ocr_rejects_changed_inputs_before_touching_destination(recovered_repo, mode):
    repo = recovered_repo
    runtime = repo / "vendor/windows/ocr" / RECOVERED_NAME
    if mode == "manifest":
        target = repo / "packaging/windows/ocr-public-recovery-manifest.json"
    elif mode == "extra":
        target = runtime / "bin/lstmtraining.exe"
    elif mode == "bad_zip":
        target = runtime.parent / "tesseract-5.5.3-windows-amd64.zip"
    else:
        target = runtime / "tessdata/chi_sim.traineddata"
    previous = target.read_bytes() if target.exists() else None
    destination = repo / "artifacts/validation/ocr"
    destination.mkdir(parents=True, exist_ok=True)
    sentinel = destination / "保留标记.txt"
    sentinel.write_text("失败不得清理目标", encoding="utf-8")
    try:
        if mode == "missing":
            target.unlink()
        else:
            target.write_bytes(b"changed-input")
        result = prepare(repo)
        assert result.returncode != 0, result.stdout + result.stderr
        assert sentinel.read_text(encoding="utf-8") == "失败不得清理目标"
    finally:
        if previous is None:
            target.unlink(missing_ok=True)
        else:
            target.write_bytes(previous)
        sentinel.unlink(missing_ok=True)
