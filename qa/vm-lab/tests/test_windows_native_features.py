"""安装版多成品验证：拒绝后续坏页、文件替换和无法解码的 PDF。"""
import importlib.util
from pathlib import Path

import fitz
import pytest


def controller():
    path = Path(__file__).resolve().parents[1] / "scripts/exercise-windows-native-features.py"
    spec = importlib.util.spec_from_file_location("native_features_test", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@pytest.mark.parametrize("scenario", ["valid", "second-image-corrupt", "changed-hash", "invalid-pdf"])
def test_decodes_every_output_and_rechecks_hash(tmp_path, scenario):
    module = controller()
    with fitz.open() as document:
        document.new_page().insert_text((40, 40), "PartyOps acceptance")
        document.save(tmp_path / "output.pdf")
        for name in ("page-1.png", "page-2.png"):
            document[0].get_pixmap().save(tmp_path / name)
    if scenario == "second-image-corrupt":
        (tmp_path / "page-2.png").write_bytes(b"not an image")
    if scenario == "invalid-pdf":
        (tmp_path / "output.pdf").write_bytes(b"%PDF fake content")
    rows = [{"filename": path.name, "sha256": module.native.sha256(path)} for path in tmp_path.iterdir()]
    if scenario == "changed-hash":
        (tmp_path / "page-2.png").write_bytes(b"replaced after capture")
    result = {"cases": [{"outputs": rows}]}
    if scenario == "valid":
        checked = module.validate_outputs(result, tmp_path)
        assert len(checked) == 3
        assert sum(row.get("pages", 0) for row in checked) == 1
        assert all(row.get("width", 1) > 0 for row in checked)
    else:
        with pytest.raises((RuntimeError, fitz.FileDataError, fitz.mupdf.FzErrorBase)):
            module.validate_outputs(result, tmp_path)
