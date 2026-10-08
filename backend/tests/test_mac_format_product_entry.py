"""Mac正式任务协议及未知清理留存；仅本地模拟，不代替WPS验收。"""
import json
import threading
from types import SimpleNamespace
from pathlib import Path

import pytest

from app import official_format_host as host, official_format_service as service
from app.official_format import OfficialFormatError


@pytest.mark.parametrize("state", [None, {}, {"task_id": "other", "cleanup_confirmed": True, "lease_released": True, "registration_owned": True, "capability_revoked": True}, {"task_id": "a" * 32, "cleanup_confirmed": False, "lease_released": True, "registration_owned": True, "capability_revoked": True}])
def test_unknown_or_wrong_task_cannot_confirm_cleanup(tmp_path, state):
    marker = tmp_path / host.MAC_CLEANUP_MARKER
    marker.write_text(json.dumps({"schema": 1, "task_id": "a" * 32}), encoding="utf-8")
    assert not host._confirm_mac_cleanup(tmp_path, "a" * 32, {"mac_task": state})
    assert marker.is_file()


@pytest.mark.parametrize("field", ["registration_owned", "capability_revoked"])
@pytest.mark.parametrize("value", [None, False])
def test_persistent_registration_requires_real_owned_entry_and_revocation(tmp_path, field, value):
    task_id = "a" * 32
    marker = tmp_path / host.MAC_CLEANUP_MARKER
    marker.write_text(json.dumps({"schema": 1, "task_id": task_id}), encoding="utf-8")
    state = {"task_id": task_id, "cleanup_confirmed": True, "lease_released": True,
             "registration_owned": True, "capability_revoked": True}
    state[field] = value
    assert not host._confirm_mac_cleanup(tmp_path, task_id, {"mac_task": state})
    assert marker.is_file()


@pytest.mark.parametrize("confirmed", [False, True])
@pytest.mark.parametrize("runtime", ["darwin", "win32", "linux"])
def test_session_cleanup_preserves_only_unconfirmed_mac(tmp_path, monkeypatch, confirmed, runtime):
    monkeypatch.setattr(service.sys, "platform", runtime)
    workspace = tmp_path / "session"
    control = workspace / "j" / "task" / ".source-host"
    control.mkdir(parents=True)
    source = workspace / "input.docx"
    source.write_bytes(b"owned QA input")
    marker = control / host.MAC_CLEANUP_MARKER
    task_id = "a" * 32
    marker.write_text(json.dumps({"schema": 1, "task_id": task_id}), encoding="utf-8")
    if confirmed:
        assert host._confirm_mac_cleanup(control, task_id, {"mac_task": {"task_id": task_id, "cleanup_confirmed": True, "lease_released": True, "registration_owned": True, "capability_revoked": True}})
        assert (control / (".mac-cleanup-confirmed-" + task_id + ".json")).is_file()
    session = service.LocalFormatSession("qa", "mock", "mock", workspace)
    instance = object.__new__(service.OfficialFormatLocalService)
    instance.lock = threading.Lock()
    instance.sessions = {"qa": session}
    instance.remove_session("qa")
    assert source.exists() is (runtime == "darwin" and not confirmed)


def test_actual_python_payload_and_response_binding(tmp_path, monkeypatch):
    monkeypatch.setattr(host.sys, "platform", "darwin")
    executable = tmp_path / "runtime" / "partyops-document-formatter-host"
    executable.parent.mkdir()
    executable.touch()
    source = tmp_path / "input.docx"
    source.write_bytes(b"QA only")
    monkeypatch.setattr(host, "resolve_source_host", lambda: executable)
    seen = {}

    class Completed:
        returncode = 0
        def poll(self):
            return 0

    def launch(command, **kwargs):
        request = json.loads(Path(command[2]).read_text(encoding="utf-8"))
        seen.update(request=request, env=kwargs["env"])
        assert request["mac_task"]["schema"] == 1
        assert "mode" not in request["mac_task"]
        output = Path(request["output_directory"]) / "result.docx"
        output.write_bytes(b"mock native output")
        response = {"mac_task": {"task_id": request["mac_task"]["task_id"], "cleanup_confirmed": True, "lease_released": True, "registration_owned": True, "capability_revoked": True}, "jobs": [{"success": True, "output_paths": [str(output)], "message": "需要人工核对"}]}
        Path(command[4]).write_text(json.dumps(response), encoding="utf-8")
        return Completed()

    monkeypatch.setattr(host.subprocess, "Popen", launch)
    outputs = host.run_source_host("format", source, tmp_path / "workspace", {}, progress=lambda *args: None, cancelled=lambda: False)
    assert len(outputs) == 1
    assert "PARTYOPS_WPS_VTABLE_MAP" not in seen["env"]
    assert seen["env"]["PARTYOPS_MAC_QUOTE_FONT"] == "Times New Roman"
    assert seen["request"]["host_preference"] == "wps"
    assert not (tmp_path / "workspace" / ".source-host" / host.MAC_CLEANUP_MARKER).exists()


@pytest.mark.parametrize("reply", ["missing", "cancelled", "fatal"])
def test_unknown_exit_or_cancel_retains_mac_workcopy(tmp_path, monkeypatch, reply):
    monkeypatch.setattr(host.sys, "platform", "darwin")
    executable = tmp_path / "partyops-document-formatter-host"
    source = tmp_path / "input.docx"
    source.write_bytes(b"owned source unchanged")
    monkeypatch.setattr(host, "resolve_source_host", lambda: executable)
    class Process:
        returncode = 3
        polls = 0
        def poll(self):
            self.polls += 1
            return None if self.polls == 1 else 3
    def launch(command, **kwargs):
        payload = json.loads(Path(command[2]).read_text(encoding="utf-8"))
        (Path(payload["output_directory"]) / "open-work.docx").write_bytes(b"still open mock workcopy")
        if reply != "missing":
            response = {"fatal": reply == "fatal", "message": "mock unknown", "jobs": [{"success": False, "cancelled": True}]}
            Path(command[4]).write_text(json.dumps(response), encoding="utf-8")
        return Process()
    monkeypatch.setattr(host.subprocess, "Popen", launch)
    with pytest.raises(OfficialFormatError):
        host.run_source_host("format", source, tmp_path / "workspace", {}, progress=lambda *args: None, cancelled=lambda: True)
    assert (tmp_path / "workspace" / ".source-host" / host.MAC_CLEANUP_MARKER).is_file()
    assert (tmp_path / "workspace" / ".source-host" / "cancel.flag").is_file()
    assert (tmp_path / "workspace" / "o" / "open-work.docx").is_file()
    assert source.read_bytes() == b"owned source unchanged"


@pytest.mark.parametrize("remove", ["session", "document"])
def test_remove_vs_worker_before_marker_barrier(tmp_path, monkeypatch, remove):
    """确定性复现worker已过取消检查、尚未写宿主marker时的删除竞态。"""
    monkeypatch.setattr(service.sys, "platform", "darwin")
    monkeypatch.setattr(service, "capabilities_payload", lambda: {"features": [{"id": "format"}]})
    entered, resume = threading.Event(), threading.Event()
    workspace = tmp_path / "session"
    workspace.mkdir()
    source = workspace / "input.docx"
    source.write_bytes(b"owned source")
    session = service.LocalFormatSession("qa", "mock", "mock", workspace)
    session.documents["doc"] = SimpleNamespace(source=source, output=None, original_stem="QA")
    instance = service.OfficialFormatLocalService(secret="a" * 32, config_dir=tmp_path)
    instance.sessions[session.id] = session
    def feature(feature_id, input_path, task_workspace, options, **callbacks):
        entered.set()
        assert resume.wait(5)
        assert input_path.is_file() and workspace.is_dir()
        control = task_workspace / ".source-host"
        control.mkdir(parents=True)
        (control / host.MAC_CLEANUP_MARKER).write_text('{"schema":1,"task_id":"mock"}', encoding="utf-8")
        raise OfficialFormatError("MAC_FORMAT_CLEANUP_UNCONFIRMED", "mock", "mock unknown native outcome")
    monkeypatch.setattr(service, "execute_feature", feature)
    try:
        job = instance.create_job(session, feature_id="format", document_ids=["doc"], options={})
        assert entered.wait(5)
        assert session.mac_pending_jobs == 1
        assert not any(workspace.rglob(host.MAC_CLEANUP_MARKER))
        if remove == "session":
            instance.remove_session(session.id)
            assert job.cancel_event.is_set()
            with pytest.raises(OfficialFormatError):
                instance.create_job(session, feature_id="format", document_ids=["doc"], options={})
        else:
            instance.remove_document(session, "doc")
            assert "doc" in session.documents
        assert source.read_bytes() == b"owned source"
    finally:
        resume.set()
        instance.executor.shutdown(wait=True)
    assert session.mac_pending_jobs == 0
    assert any(workspace.rglob(host.MAC_CLEANUP_MARKER))
    assert source.is_file()
