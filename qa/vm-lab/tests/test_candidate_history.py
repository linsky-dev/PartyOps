"""候选制品保留去重测试：不能改变已有证据引用或覆盖损坏备份。"""
import importlib.util
from pathlib import Path
from types import SimpleNamespace

import pytest
from evidence import sha256

spec = importlib.util.spec_from_file_location(
    "record_build", Path(__file__).resolve().parents[1] / "scripts/record-build.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def test_repeated_backup_reuses_content_and_legacy_path(tmp_path):
    candidate = tmp_path / "fixture.deb"
    candidate.write_bytes(b"synthetic package")
    package = {"path": str(candidate), "sha256": sha256(candidate)}
    legacy = tmp_path / "candidate-history/build-old/fixture.deb"
    legacy.parent.mkdir(parents=True)
    legacy.write_bytes(candidate.read_bytes())
    lab = SimpleNamespace(check_space=lambda *args: pytest.fail("不应重复分配空间"))
    assert module.preserve_candidate(lab, tmp_path, package) == legacy
    assert module.preserve_candidate(lab, tmp_path, package) == legacy
    assert len(list((tmp_path / "candidate-history").rglob("*.deb"))) == 1


def test_new_backup_is_verified_and_corruption_cannot_be_overwritten(tmp_path):
    candidate = tmp_path / "fixture.deb"
    candidate.write_bytes(b"synthetic package")
    package = {"path": str(candidate), "sha256": sha256(candidate)}
    lab = SimpleNamespace(check_space=lambda *args: None)
    backup = module.preserve_candidate(lab, tmp_path, package)
    assert sha256(backup) == package["sha256"]
    backup.write_bytes(b"corrupted history")
    with pytest.raises(RuntimeError, match="CANDIDATE_HISTORY_HASH_MISMATCH"):
        module.preserve_candidate(lab, tmp_path, package)
    assert backup.read_bytes() == b"corrupted history"
