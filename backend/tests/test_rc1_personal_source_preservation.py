"""Prevent a missing historical data path from becoming an empty new database."""

import shlex

import pytest

from app import setup_wizard


def test_missing_old_personal_disk_preserves_saved_config(tmp_path, monkeypatch):
    config_root = tmp_path / "config"
    config_root.mkdir()
    original = tmp_path / "disconnected-volume" / "原业务"
    target = tmp_path / "新的 数据目录"
    config = config_root / "personal.env"
    original_text = (
        "PARTYOPS_MODE=personal\nPARTYOPS_PORT=18775\n"
        + "PARTYOPS_DATA_DIR=" + shlex.quote(str(original)) + "\n"
        + "PARTYOPS_BOOTSTRAP_TOKEN=synthetic-old-token\n"
    )
    config.write_text(original_text, encoding="utf-8")
    monkeypatch.setattr(setup_wizard, "config_root", lambda: config_root)
    monkeypatch.setattr(setup_wizard, "deactivate_windows_host_for_user_mode",
                        lambda: pytest.fail("must not stop anything before source check"))
    with pytest.raises(ValueError, match="原业务数据所在磁盘暂时不可用"):
        setup_wizard.write_personal_config(target)
    assert not target.exists()
    assert config.read_text(encoding="utf-8") == original_text
    assert not original.exists()


def test_saved_personal_source_check_accepts_new_install_and_available_path(tmp_path):
    config = tmp_path / "personal.env"
    setup_wizard._assert_saved_personal_source_present(config)
    data = tmp_path / "中文 原业务"
    data.mkdir()
    config.write_text("PARTYOPS_MODE=personal\nPARTYOPS_DATA_DIR=" + shlex.quote(str(data)) + "\n", encoding="utf-8")
    setup_wizard._assert_saved_personal_source_present(config)


def test_saved_personal_source_check_rejects_relative_path(tmp_path):
    config = tmp_path / "personal.env"
    config.write_text("PARTYOPS_MODE=personal\nPARTYOPS_DATA_DIR=relative\n", encoding="utf-8")
    with pytest.raises(ValueError, match="配置不完整"):
        setup_wizard._assert_saved_personal_source_present(config)
