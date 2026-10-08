"""六个既有入口传入正式Mac协议；仅模拟进程，不表示WPS已验收。"""
import json
from pathlib import Path

import pytest

from app import official_format_host as host


@pytest.mark.parametrize("feature", ["format", "replace", "redheader", "rename", "convert", "pdf-to-word"])
def test_existing_six_features_keep_real_task_binding(tmp_path, monkeypatch, feature):
    monkeypatch.setattr(host.sys, "platform", "darwin")
    executable = tmp_path / "runtime" / "partyops-document-formatter-host"
    executable.parent.mkdir()
    executable.touch()
    source = tmp_path / ("source.pdf" if feature == "pdf-to-word" else "source.docx")
    source.write_bytes(b"only mock source")
    monkeypatch.setattr(host, "resolve_source_host", lambda: executable)

    class Process:
        returncode = 0

        def poll(self):
            return 0

    def launch(command, **kwargs):
        payload = json.loads(Path(command[2]).read_text(encoding="utf-8"))
        assert payload["feature_id"] == feature
        assert payload["mac_task"]["schema"] == 1
        assert "mode" not in payload["mac_task"]
        assert payload["host_preference"] == "wps"
        output = Path(payload["output_directory"]) / "result.docx"
        output.write_bytes(b"mock result")
        Path(command[4]).write_text(json.dumps({"mac_task": {"task_id": payload["mac_task"]["task_id"], "cleanup_confirmed": True, "lease_released": True, "registration_owned": True, "capability_revoked": True}, "jobs": [{"success": True, "output_paths": [str(output)], "message": "人工核对"}]}), encoding="utf-8")
        return Process()

    monkeypatch.setattr(host.subprocess, "Popen", launch)
    assert host.run_source_host(feature, source, tmp_path / "workspace", {}, progress=lambda *args: None, cancelled=lambda: False)
    assert source.read_bytes() == b"only mock source"
