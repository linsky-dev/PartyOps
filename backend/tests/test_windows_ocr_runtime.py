"""原版 Win10 复现的中文安装目录 OCR 回归：环境变量不能覆盖 Unicode 参数。"""
import os
from pathlib import Path
from types import SimpleNamespace

import pytest
from PIL import Image

from app import intake


@pytest.fixture
def windows_ocr(monkeypatch, tmp_path):
    monkeypatch.setattr(intake.sys, "platform", "win32")
    language_dir = tmp_path / '中文 程序' / 'ocr' / 'tessdata'
    language_dir.mkdir(parents=True)
    (language_dir / 'chi_sim.traineddata').write_bytes(b'pinned-language')
    directory = str(language_dir)
    monkeypatch.setenv("TESSDATA_PREFIX", directory)
    return directory


def test_unicode_tessdata_uses_relative_arguments_and_child_only_environment(windows_ocr, monkeypatch):
    inputs = []

    def run(command, **options):
        assert command[1:] == ["input.png", "stdout", "--tessdata-dir", ".", "-l", "chi_sim"]
        assert "TESSDATA_PREFIX" not in options["env"]
        assert os.environ["TESSDATA_PREFIX"] == windows_ocr
        assert options["timeout"] == 20 and options["check"] is False
        path = Path(options['cwd']) / command[1]
        assert (path.parent / 'chi_sim.traineddata').read_bytes() == b'pinned-language'
        with Image.open(path) as image:
            assert image.getpixel((0, 0)) == (255, 255, 255)
        inputs.append(path)
        return SimpleNamespace(returncode=0, stdout="支部党员大会".encode(), stderr=b"")

    monkeypatch.setattr(intake.subprocess, "run", run)
    assert intake._image_ocr(Image.new("RGBA", (4, 4), (0, 0, 0, 0)), timeout=20) == "支部党员大会"
    assert not inputs[0].exists()
    assert not inputs[0].parent.exists()
    assert (Path(windows_ocr) / 'chi_sim.traineddata').read_bytes() == b'pinned-language'
    assert os.environ["TESSDATA_PREFIX"] == windows_ocr


@pytest.mark.parametrize("failure,expected", [
    (FileNotFoundError(), intake.pytesseract.TesseractNotFoundError),
    (intake.subprocess.TimeoutExpired("tesseract", 10), RuntimeError),
])
def test_windows_ocr_failure_preserves_existing_error_contract(windows_ocr, monkeypatch, failure, expected):
    def run(*_args, **_kwargs):
        raise failure

    monkeypatch.setattr(intake.subprocess, "run", run)
    with pytest.raises(expected):
        intake._image_ocr(Image.new("RGB", (4, 4)), timeout=10)


def test_windows_ocr_nonzero_is_never_reported_as_recognized_text(windows_ocr, monkeypatch):
    monkeypatch.setattr(intake.subprocess, "run", lambda *_args, **_kwargs:
                        SimpleNamespace(returncode=1, stdout=b"", stderr=b"Illegal byte sequence"))
    with pytest.raises(intake.pytesseract.TesseractError, match="Illegal byte sequence"):
        intake._image_ocr(Image.new("RGB", (4, 4)), timeout=10)


def test_non_windows_preserves_standard_pytesseract_call(windows_ocr, monkeypatch):
    monkeypatch.setattr(intake.sys, "platform", "linux")
    monkeypatch.setattr(intake.pytesseract, "image_to_string", lambda _image, **options:
                        "标准引擎" if options == {"lang": "chi_sim", "timeout": 10} else "错误参数")
    assert intake._image_ocr(Image.new("RGB", (4, 4)), timeout=10) == "标准引擎"
