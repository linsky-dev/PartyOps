"""原版 WPS 路径故障回归：私有路径压缩、中文输入、并发隔离及明确长度诊断。"""
from __future__ import annotations

import hashlib
import json
import os
import shutil
import threading
import uuid
from pathlib import Path
from types import SimpleNamespace

import pytest

from app import official_format_host as host
from app import official_format_service as service
from app.official_format import LocalDocument, OfficialFormatError


def test_compact_upload_keeps_identical_names_and_concurrent_jobs_independent(tmp_path, monkeypatch):
    temp = tmp_path / "中文 空格临时目录"
    temp.mkdir()
    monkeypatch.setattr(service.tempfile, "tempdir", str(temp))
    instance = service.OfficialFormatLocalService(secret="a" * 64, config_dir=tmp_path / "logs", port=0)
    # 两个真正的后台执行线程同时进入处理，不能用串行 mock 掩盖目录冲突。
    barrier = threading.Barrier(2)
    seen = []

    def copy_source(_feature, source, workspace, _options, **_kwargs):
        workspace.mkdir(parents=True, exist_ok=True)
        output = workspace / "o" / (source.stem + "_已排版.docx")
        output.parent.mkdir()
        if workspace.name == "0":
            barrier.wait(timeout=10)
        shutil.copyfile(source, output)
        seen.append((source, output))
        return SimpleNamespace(outputs=[SimpleNamespace(path=output, filename=output.name, content_type="application/octet-stream")],
                               message="夹具真实复制", report=None)

    monkeypatch.setattr(service, "execute_feature", copy_source)
    session = instance.create_session("https://example.test")
    try:
        assert session.workspace.name.startswith("pf-") and session.workspace.parent == temp
        for payload in (b"first synthetic document", b"second synthetic document"):
            path = service._store_compact_upload(session.workspace, ".docx", payload)
            session.documents[uuid.uuid4().hex] = LocalDocument(path, "中文 空格同名", False)
        originals = {path: path.read_bytes() for path in (item.source for item in session.documents.values())}
        jobs = [instance.create_job(session, feature_id="format", document_ids=list(session.documents), options={}) for _ in range(2)]
        instance.executor.shutdown(wait=True)
        assert all(job.state == "completed" for job in jobs)
        assert len(seen) == len({output for _, output in seen}) == 4
        for source, output in seen:
            assert output.read_bytes() == originals[source] and source.read_bytes() == originals[source]
            assert output.parent.parent.name in {"0", "1"}
        assert all(item.filename == "中文 空格同名_已排版.docx" for job in jobs for item in job.outputs.values())
        assert service._document_output_name(next(iter(session.documents.values())), "内容标题_已命名.docx") == "内容标题_已命名.docx"
        instance.remove_session(session.id)
        assert not session.workspace.exists() and not instance.sessions
    finally:
        instance.close()


def test_short_upload_write_failure_cleans_only_new_file(tmp_path, monkeypatch):
    retained = tmp_path / "retained.docx"
    retained.write_bytes(b"keep")
    original_fdopen = os.fdopen

    class WriteFailure:
        def __init__(self, descriptor):
            self.stream = original_fdopen(descriptor, "wb")
        def __enter__(self):
            return self
        def __exit__(self, *_args):
            self.stream.close()
        def write(self, _payload):
            raise OSError("synthetic disk full")

    monkeypatch.setattr(service.os, "fdopen", lambda descriptor, _mode: WriteFailure(descriptor))
    with pytest.raises(OSError, match="disk full"):
        service._store_compact_upload(tmp_path, ".docx", b"synthetic")
    assert list(tmp_path.iterdir()) == [retained] and retained.read_bytes() == b"keep"


@pytest.mark.parametrize("platform", ["win32", "linux", "darwin"])
def test_host_uses_compact_output_and_preserves_deep_chinese_source(tmp_path, monkeypatch, platform):
    source_root = tmp_path / ("中文 深层 " * 9).rstrip() / ("资料 空格 " * 6).rstrip()
    source_root.mkdir(parents=True)
    source = source_root / "输入 公文.docx"
    source.write_bytes(b"synthetic document bytes")
    original = hashlib.sha256(source.read_bytes()).hexdigest()
    workspace = tmp_path / "w"
    monkeypatch.setattr(host.sys, "platform", platform)
    monkeypatch.setattr(host, "resolve_source_host", lambda: Path("unused-host.exe"))
    # 本例验证可读取的用户深路径原文件；Windows 太长的源路径有单独反例。
    checked = []
    monkeypatch.setattr(host, "_check_windows_source_paths", lambda *args: checked.append(args))

    def copied(command, **_kwargs):
        request = Path(command[command.index("--request") + 1])
        response = Path(command[command.index("--response") + 1])
        payload = json.loads(request.read_text(encoding="utf-8"))
        assert Path(payload["source_paths"][0]) == source.resolve()
        assert Path(payload["output_directory"]) == workspace.resolve() / "o"
        output = Path(payload["output_directory"]) / "输出 公文.docx"
        shutil.copyfile(source, output)
        response.write_text(json.dumps({"jobs": [{"success": True, "output_paths": [str(output)], "host_display_name": "fixture only"}]}), encoding="utf-8")
        return SimpleNamespace(poll=lambda: 0, returncode=0)

    monkeypatch.setattr(host.subprocess, "Popen", copied)
    result = host.run_source_host("format", source, workspace, {"compatibility_mode": "wps"}, progress=lambda *_: None, cancelled=lambda: False)
    assert checked and result[0].path.read_bytes() == source.read_bytes()
    assert hashlib.sha256(source.read_bytes()).hexdigest() == original


@pytest.mark.parametrize("platform", ["win32", "linux"])
def test_overlong_utf16_host_path_stops_before_process_or_control_files(tmp_path, monkeypatch, platform):
    source = tmp_path / "u12345678.docx"
    source.write_bytes(b"keep")
    workspace = tmp_path / ("资料😀 " * 35)
    monkeypatch.setattr(host.sys, "platform", platform)
    monkeypatch.setattr(host, "resolve_source_host", lambda: Path("unused-host.exe"))
    monkeypatch.setattr(host.subprocess, "Popen", lambda *_args, **_kwargs: pytest.fail("过长路径不可启动宿主"))
    assert host._windows_path_units(workspace) > len(str(workspace))
    if platform == "win32":
        with pytest.raises(OfficialFormatError) as failure:
            host.run_source_host("format", source, workspace, {}, progress=lambda *_: None, cancelled=lambda: False)
        assert failure.value.code == "SOURCE_FORMATTER_PATH_TOO_LONG"
        assert "TEMP/TMP" in failure.value.detail and "259" in failure.value.detail
        assert not workspace.exists() and source.read_bytes() == b"keep"
    else:
        host._check_windows_source_paths(source, workspace / ".source-host", workspace / "o", "_已排版")


def test_windows_budget_boundary_uses_full_atomic_temp_leaf(tmp_path, monkeypatch):
    monkeypatch.setattr(host.sys, "platform", "win32")
    source = tmp_path / "u12345678.docx"
    # 不写入这个纯边界路径；真实落盘复制由其他测试和 .NET 夹具执行。
    base = Path("D:/") / ("a" * 170)
    measured = host._windows_path_units(base / ("." + source.stem + "_已排版." + "0" * 32 + ".tmp.docx"))
    at_limit = Path("D:/") / ("a" * (170 + 259 - measured))
    host._check_windows_source_paths(source, tmp_path, at_limit, "_已排版")
    with pytest.raises(OfficialFormatError, match="260"):
        host._check_windows_source_paths(source, tmp_path, Path(str(at_limit) + "b"), "_已排版")
