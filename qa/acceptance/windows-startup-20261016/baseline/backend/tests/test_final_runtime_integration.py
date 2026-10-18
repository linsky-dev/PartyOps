"""最终合并交叉路径：权限检查、配置迁移与实际排版端点保持一致。"""

from __future__ import annotations

import json
import os

import pytest

from app import official_format_instance, setup_wizard, startup_selftest


def test_migrated_formatter_config_still_checks_saved_personal_directory(monkeypatch, tmp_path):
    config_root = tmp_path / "配置"
    config_root.mkdir()
    data = tmp_path / "原始 业务数据"
    config = config_root / "personal.env"
    config.write_text(f"PARTYOPS_PORT=18825\nPARTYOPS_DATA_DIR={setup_wizard.shlex.quote(str(data))}\nPARTYOPS_BOOTSTRAP_TOKEN=fixture\n", encoding="utf-8")
    (config_root / "mode.json").write_text(json.dumps({"mode": "personal", "config_path": str(config)}), encoding="utf-8")
    monkeypatch.setattr(setup_wizard, "config_root", lambda: config_root)
    monkeypatch.delenv("PARTYOPS_OFFICIAL_FORMAT_PORT", raising=False)
    monkeypatch.setenv("PARTYOPS_DATA_DIR", str(tmp_path / "foreign-control-dir"))
    calls = []
    monkeypatch.setattr(setup_wizard, "_preflight_personal_runtime_access", lambda path, directory: calls.append((path, directory)))
    rewritten = setup_wizard._rewrite_personal_port(config, 18825)
    assert rewritten["PARTYOPS_OFFICIAL_FORMAT_PORT"] == "18827"
    assert setup_wizard.preflight_configured_personal_runtime_access()["checked"] is True
    assert calls == [(config, data)]
    explicit = setup_wizard.load_host_environment(config, inherit_environment=False)
    assert explicit["PARTYOPS_DATA_DIR"] == str(data)
    assert explicit["PARTYOPS_BOOTSTRAP_TOKEN"] == "fixture"
    assert "PATH" not in explicit


def test_permission_rejection_precedes_formatter_migration_or_new_process(monkeypatch, tmp_path):
    config = tmp_path / "personal.env"
    config.write_text(f"PARTYOPS_PORT=18825\nPARTYOPS_DATA_DIR={setup_wizard.shlex.quote(str(tmp_path))}\n", encoding="utf-8")
    original = config.read_bytes()
    def denied(*_args):
        raise setup_wizard.HostStartupError("RUNTIME_PERMISSION_DENIED", "fixture")
    monkeypatch.setattr(setup_wizard, "_preflight_personal_runtime_access", denied)
    monkeypatch.setattr(setup_wizard, "_spawn", lambda *_: pytest.fail("权限失败不得启动进程"))
    monkeypatch.setattr(setup_wizard, "_rewrite_personal_port", lambda *_: pytest.fail("权限失败不得写配置"))
    with pytest.raises(setup_wizard.HostStartupError) as error:
        setup_wizard.launch_personal(config)
    assert error.value.code == "RUNTIME_PERMISSION_DENIED" and config.read_bytes() == original


def test_merged_port_migration_runs_owned_endpoint_startup_probe(monkeypatch, tmp_path):
    monkeypatch.delenv("PARTYOPS_OFFICIAL_FORMAT_PORT", raising=False)
    config = tmp_path / "personal.env"
    main_port = startup_selftest._reserve_loopback_port()
    if main_port > 65533:
        main_port = 18825
    config.write_text(f"PARTYOPS_PORT={main_port}\n", encoding="utf-8")
    environment = setup_wizard._rewrite_personal_port(config, main_port)
    configured = int(environment["PARTYOPS_OFFICIAL_FORMAT_PORT"])
    formatter = official_format_instance.start_instance_formatter(tmp_path, configured, "a" * 64)
    try:
        startup_selftest._probe_owned_formatter(tmp_path, os.getpid())
        response = startup_selftest._read_json(f"http://127.0.0.1:{formatter.port}/health")
        assert response["instance_id"] == formatter.instance_id
    finally:
        formatter.close()
