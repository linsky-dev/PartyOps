"""Mac工作域选择与正式API任务链；合成Host不代替安装包WPS验收。"""
import hashlib
import json
from pathlib import Path
from types import SimpleNamespace

import pytest

from app import official_format_features as features
from app import official_format_host as host
from app import official_format_service as service
from app.official_format import LocalDocument, OfficialFormatError


def _mac_home(tmp_path, monkeypatch, user):
    home = tmp_path / user
    downloads = home / "Downloads"
    downloads.mkdir(parents=True)
    monkeypatch.setattr(service.sys, "platform", "darwin")
    monkeypatch.setattr(Path, "home", classmethod(lambda cls: home))
    return downloads


@pytest.mark.parametrize("user", ["用户甲", "第二用户 空格"])
@pytest.mark.parametrize("feature", ["format", "replace", "redheader", "rename", "convert", "pdf-to-word"])
def test_api_source_work_output_and_cleanup_share_current_user_downloads(tmp_path, monkeypatch, user, feature):
    downloads = _mac_home(tmp_path, monkeypatch, user)
    retained = downloads / "真实文档.docx"
    retained.write_bytes(b"real user document must remain")
    monkeypatch.delenv("PARTYOPS_FORMATTER_TEST_LOCAL_ENGINE", raising=False)
    monkeypatch.setattr(features, "_font_issues", lambda: [])
    monkeypatch.setattr(features, "diagnose_docx", lambda _path: None)
    monkeypatch.setattr(service, "capabilities_payload", lambda: {"features": [{"id": feature}]})
    executable = tmp_path / "原随包Host" / "partyops-document-formatter-host"
    monkeypatch.setattr(host, "resolve_source_host", lambda: executable)
    seen = []

    def launch(command, **kwargs):
        request = json.loads(Path(command[2]).read_text(encoding="utf-8"))
        source = Path(request["source_paths"][0])
        output_root = Path(request["output_directory"])
        assert source.parent.parent == downloads.resolve()
        assert downloads.resolve() in output_root.parents
        assert downloads.resolve() in Path(kwargs["cwd"]).parents
        assert downloads.resolve() in Path(request["mac_task"]["state_directory"]).parents
        assert request["mac_task"]["resources"] == str((executable.parent / "wps-formatter-plugin").resolve())
        assert request["feature_id"] == feature and request["host_preference"] == "wps"
        output = output_root / (source.stem + "_结果.docx")
        output.write_bytes(source.read_bytes())
        state = {"task_id": request["mac_task"]["task_id"], "cleanup_confirmed": True,
                 "lease_released": True, "registration_owned": True, "capability_revoked": True}
        Path(command[4]).write_text(json.dumps({"mac_task": state, "jobs": [
            {"success": True, "output_paths": [str(output)], "message": "合成原Host回执"}]}), encoding="utf-8")
        seen.append((source, output))
        return SimpleNamespace(poll=lambda: 0, returncode=0)

    monkeypatch.setattr(host.subprocess, "Popen", launch)
    instance = service.OfficialFormatLocalService(secret="a" * 64, config_dir=tmp_path / "日志", port=0)
    session = instance.create_session("https://example.test")
    try:
        assert session.workspace.parent == downloads.resolve()
        sibling = instance.create_session("https://example.test")
        assert sibling.workspace != session.workspace
        instance.remove_session(sibling.id)
        payload = b"owned uploaded bytes unchanged"
        suffix = ".pdf" if feature == "pdf-to-word" else ".docx"
        source = service._store_compact_upload(session.workspace, suffix, payload)
        session.documents["doc"] = LocalDocument(source, "原始中文名", False)
        job = instance.create_job(session, feature_id=feature, document_ids=["doc"], options={})
        instance.executor.shutdown(wait=True)
        assert job.state == "completed" and len(seen) == len(job.outputs) == 1
        assert hashlib.sha256(source.read_bytes()).digest() == hashlib.sha256(payload).digest()
        output = next(iter(job.outputs.values()))
        assert output.filename == "原始中文名_结果.docx"
        assert session.workspace in output.path.parents and output.path.read_bytes() == payload
        assert not any(session.workspace.rglob(host.MAC_CLEANUP_MARKER))
        instance.remove_session(session.id)
        assert not session.workspace.exists()
        assert retained.read_bytes() == b"real user document must remain"
    finally:
        instance.close()


def test_unknown_native_failure_retains_downloads_source_and_workspace(tmp_path, monkeypatch):
    downloads = _mac_home(tmp_path, monkeypatch, "失败用户 空格")
    monkeypatch.setattr(service, "capabilities_payload", lambda: {"features": [{"id": "format"}]})
    monkeypatch.setattr(host, "resolve_source_host", lambda: tmp_path / "host")

    def fail_feature(_feature, source, workspace, _options, **callbacks):
        return host.run_source_host("format", source, workspace, {}, **callbacks)

    def launch(command, **_kwargs):
        request = json.loads(Path(command[2]).read_text(encoding="utf-8"))
        (Path(request["output_directory"]) / "unknown-work.docx").write_bytes(b"unconfirmed work")
        Path(command[4]).write_text(json.dumps({"fatal": True, "message": "native result unknown"}), encoding="utf-8")
        return SimpleNamespace(poll=lambda: 2, returncode=2)

    monkeypatch.setattr(service, "execute_feature", fail_feature)
    monkeypatch.setattr(host.subprocess, "Popen", launch)
    instance = service.OfficialFormatLocalService(secret="a" * 64, config_dir=tmp_path, port=0)
    session = instance.create_session("https://example.test")
    try:
        source = service._store_compact_upload(session.workspace, ".docx", b"keep owned source")
        session.documents["doc"] = LocalDocument(source, "原名", False)
        job = instance.create_job(session, feature_id="format", document_ids=["doc"], options={})
        instance.executor.shutdown(wait=True)
        assert job.state == "failed" and job.items[0].error_code == "SOURCE_FORMATTER_FATAL"
        instance.remove_session(session.id)
        assert session.workspace.parent == downloads.resolve() and source.read_bytes() == b"keep owned source"
        assert any(session.workspace.rglob(host.MAC_CLEANUP_MARKER))
        assert any(session.workspace.rglob("unknown-work.docx"))
    finally:
        instance.close()


@pytest.mark.parametrize("platform", ["win32", "linux"])
def test_other_platforms_keep_system_temp_allocator(tmp_path, monkeypatch, platform):
    monkeypatch.setattr(service.sys, "platform", platform)
    monkeypatch.setattr(Path, "home", classmethod(lambda cls: pytest.fail("不可探测非Mac用户Downloads")))
    called = []
    monkeypatch.setattr(service.tempfile, "mkdtemp", lambda **kwargs: called.append(kwargs) or str(tmp_path))
    assert service._allocate_session_workspace() == tmp_path
    assert called == [{"prefix": "pf-"}]


@pytest.mark.parametrize("failure", ["missing", "not-directory", "permission", "full"])
def test_unavailable_downloads_is_actionable_without_temp_fallback(tmp_path, monkeypatch, failure):
    home = tmp_path / "当前用户"
    home.mkdir()
    downloads = home / "Downloads"
    if failure == "not-directory":
        downloads.write_bytes(b"keep")
    elif failure != "missing":
        downloads.mkdir()
    monkeypatch.setattr(service.sys, "platform", "darwin")
    monkeypatch.setattr(Path, "home", classmethod(lambda cls: home))
    calls = []
    def allocation(**kwargs):
        calls.append(kwargs)
        assert kwargs["dir"] == downloads.resolve()
        raise PermissionError("denied") if failure == "permission" else OSError("disk full")
    monkeypatch.setattr(service.tempfile, "mkdtemp", allocation)
    with pytest.raises(OfficialFormatError) as raised:
        service._allocate_session_workspace()
    assert raised.value.code == "MAC_FORMAT_WORKSPACE_UNAVAILABLE"
    assert "下载" in raised.value.detail and "原文件未改变" in raised.value.detail
    assert len(calls) == (1 if failure in {"permission", "full"} else 0)
    assert list(home.iterdir()) == ([] if failure == "missing" else [downloads])


def test_downloads_alias_uses_canonical_directory(tmp_path, monkeypatch):
    home = tmp_path / "别名用户"
    home.mkdir()
    canonical = tmp_path / "实际下载 空格"
    canonical.mkdir()
    try:
        (home / "Downloads").symlink_to(canonical, target_is_directory=True)
    except OSError:
        pytest.skip("当前宿主未提供目录符号链接权限")
    monkeypatch.setattr(service.sys, "platform", "darwin")
    monkeypatch.setattr(Path, "home", classmethod(lambda cls: home))
    workspace = service._allocate_session_workspace()
    assert workspace.parent == canonical.resolve()
    workspace.rmdir()
