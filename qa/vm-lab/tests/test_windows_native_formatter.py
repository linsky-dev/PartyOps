"""安装版 WPS 控制器反例；不连接产品，不运行 WPS，不读取真实票据。"""
from __future__ import annotations

import copy
import importlib.util
import json
import shutil
import zipfile
from pathlib import Path
from types import SimpleNamespace

import pytest
from evidence import sha256, write_json


@pytest.fixture
def formatter():
    path = Path(__file__).resolve().parents[1] / "scripts/exercise-windows-native-formatter.py"
    spec = importlib.util.spec_from_file_location("native_formatter_test", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@pytest.mark.parametrize("compact", [False, True, "image-pdf"])
@pytest.mark.parametrize("changed", ["none", "word", "input", "preference", "job", "duplicate"])
def test_wps_proof_requires_this_job_real_wps_and_exact_input(formatter, tmp_path, changed, compact):
    job, document = "a" * 32, "b" * 32
    source = tmp_path / ("pf-fixture" if compact else "partyops-official-format-fixture") / (document + ".docx")
    source.parent.mkdir()
    source.write_bytes(b"synthetic source")
    control = (source.parent / "j" / job / "0" if compact else source.parent / "jobs" / job / document) / ".source-host"
    if compact == "image-pdf":
        control = control.parent / "source-pdf" / ".source-host"
    control.mkdir(parents=True)
    request = {"feature_id": "format", "host_preference": "wps", "source_paths": [str(source)]}
    response = {"jobs": [{"success": True, "host_display_name": "WPS Office"}]}
    checksum = sha256(source)
    if changed == "word":
        response["jobs"][0]["host_display_name"] = "Microsoft Word"
    elif changed == "input":
        source.write_bytes(b"different")
    elif changed == "preference":
        request["host_preference"] = "wps-preferred"
    write_json(control / "request.json", request)
    write_json(control / "response.json", response)
    if changed == "duplicate":
        other = tmp_path / "partyops-official-format-other/jobs" / job / document / ".source-host"
        write_json(other / "response.json", response)
    if changed == "job":
        job = "c" * 32
    if changed == "none":
        result = formatter.wps_proof(tmp_path, job, document, "format", checksum, 0 if compact else None)
        assert result["host_display_name"] == "WPS Office" and result["response_sha256"] == sha256(control / "response.json")
    else:
        with pytest.raises(RuntimeError, match="NATIVE_"):
            formatter.wps_proof(tmp_path, job, document, "format", checksum, 0 if compact else None)


@pytest.mark.parametrize("fault", ["owner", "pid", "creation", "package"])
def test_foreign_formatter_is_rejected_before_session_ticket_sent(formatter, monkeypatch, fault):
    process = {"pid": 42, "owner_sid": "ordinary", "session_id": 2, "created_at": "2026-09-08T02:00:00Z",
               "executable_path": r"E:\PartyOps1\PartyOps\PartyOps.exe", "sha256": "a" * 64,
               "address": "127.0.0.1", "port": 18827}
    actual = copy.deepcopy(process)
    if fault == "owner":
        actual["owner_sid"] = "administrator"
    elif fault == "pid":
        actual["pid"] = 777
    elif fault == "creation":
        actual["created_at"] = "2026-09-08T01:00:00Z"
    else:
        actual["sha256"] = "old-package"
    run = SimpleNamespace(origin="http://127.0.0.1:18825", wizard_origin=None,
                          wait_health=lambda: None, login=lambda: None,
                          request=lambda *_: {"local_base_url": "http://127.0.0.1:18827", "ticket": "synthetic-never-send"},
                          guard_url=lambda _: process, account={"sid": "ordinary", "session_id": 2},
                          executable=process["executable_path"], install={"installed_executable_sha256": "a" * 64},
                          inspect_listener=lambda _: {"username": "PartyOpsNativeQA", "sid": "ordinary", "enabled": True,
                                                      "administrator": False, "listeners": [actual]})
    monkeypatch.setattr(formatter.urllib.request, "build_opener", lambda *_: SimpleNamespace(open=lambda *_args, **_kw: pytest.fail("票据不应发往另一实例")))
    with pytest.raises(RuntimeError, match="NATIVE_"):
        formatter.InstalledFormatter(run)


def test_local_session_ticket_and_token_remain_in_memory_only(formatter, monkeypatch):
    calls = []
    monkeypatch.setattr(formatter.InstalledFormatter, "guard", lambda self: {"pid": 42})

    class Response:
        def __init__(self):
            self.headers = {"Content-Type": "application/json"}
        def __enter__(self):
            return self
        def __exit__(self, *_):
            return False
        def read(self):
            return json.dumps({"session_id": "a" * 32, "session_token": "synthetic-token"}).encode()

    def opened(request, **_kwargs):
        calls.append(request)
        return Response()

    monkeypatch.setattr(formatter.urllib.request, "build_opener", lambda *_: SimpleNamespace(open=opened))
    state = {"runtime_process": {"pid": 42}}
    run = SimpleNamespace(origin="http://127.0.0.1:18825", wizard_origin=None, wait_health=lambda: None, login=lambda: None,
                          state=state, request=lambda *_: {"local_base_url": "http://127.0.0.1:18827", "ticket": "synthetic-ticket"})
    client = formatter.InstalledFormatter(run)
    assert calls[0].get_header("Authorization") == "Bearer synthetic-ticket"
    client.request("/v1/capabilities")
    assert calls[1].get_header("X-partyops-local-token") == "synthetic-token"
    assert state == {"runtime_process": {"pid": 42}}
    client.close()
    assert not client.token and client.session_path is None


def test_local_redirect_is_always_rejected_without_following_ticket(formatter):
    with pytest.raises(RuntimeError, match="NATIVE_FORMATTER_REDIRECT_REJECTED"):
        formatter.NoRedirect().redirect_request(None, None, 302, "Found", {}, "http://127.0.0.1:18768/")


@pytest.mark.parametrize("fault", ["none", "semantic", "visual", "archive"])
def test_complete_golden_gate_rejects_changed_document_or_page(formatter, tmp_path, monkeypatch, fault):
    import fitz
    from lxml import etree

    original = formatter.native.REPO
    repo, install = tmp_path / "repo", tmp_path / "install"
    fixtures = repo / "backend/tests/fixtures/document-formatter-source"
    fixtures.mkdir(parents=True)
    for name in ("input-manual-break.docx", "expected-source-formatted.docx"):
        shutil.copy2(original / "backend/tests/fixtures/document-formatter-source" / name, fixtures / name)
    (repo / "scripts").mkdir()
    shutil.copy2(original / "scripts/verify-document-formatter-parity.py", repo / "scripts/verify-document-formatter-parity.py")
    host = install / "formatter-host/PartyOps.DocumentFormatter.Host.exe"
    host.parent.mkdir(parents=True)
    host.write_bytes(b"fixture-host-never-executed")
    write_json(install / "release-manifest.json", {"files": [{"path": "formatter-host/PartyOps.DocumentFormatter.Host.exe", "sha256": sha256(host)}]})
    run = SimpleNamespace(run_directory=tmp_path / "run", reports=tmp_path / "reports",
                          context={"source_fingerprint": "fixture-source", "package_sha256": "fixture-package"})
    run.reports.mkdir()
    write_json(run.run_directory / "install-binding.json", {"source_fingerprint": "fixture-source", "package": {"sha256": "fixture-package"},
               "windows_payload": {"manifest": {"sha256": sha256(install / "release-manifest.json")}}})
    monkeypatch.setattr(formatter.native, "REPO", repo)
    monkeypatch.setattr(formatter.native, "INSTALL", str(install))
    closed = []

    class FixtureClient:
        def __init__(self, _run):
            self.count = 0
            self.job_records = [{"archive_error": "PermissionError"}] if fault == "archive" else []
        def guard(self):
            return {"pid": 42}
        def request(self, _path):
            return {"source_host_ready": True}
        def upload(self, _path, _filename):
            self.count += 1
            return f"{self.count:032x}"
        def job(self, feature, documents, _options, output, extension):
            results = {}
            for index, (key, item) in enumerate(documents.items()):
                path = output / (item["label"] + extension)
                if feature == "format":
                    golden = fixtures / "expected-source-formatted.docx"
                    with zipfile.ZipFile(golden) as source, zipfile.ZipFile(path, "w") as destination:
                        for name in source.namelist():
                            content = source.read(name)
                            if name == "word/document.xml" and fault == "semantic":
                                xml = etree.fromstring(content)
                                xml.find(".//{http://schemas.openxmlformats.org/wordprocessingml/2006/main}t").text = "changed text"
                                content = etree.tostring(xml)
                            destination.writestr(name, content)
                else:
                    with fitz.open() as document:
                        page = document.new_page()
                        page.insert_text((72, 72), "changed" if fault == "visual" and index else "same")
                        document.save(path)
                results[key] = path
            return results, [{"feature": feature, "host_display_name": "fixture WPS"} for _ in documents]
        def close(self):
            closed.append(True)

    monkeypatch.setattr(formatter, "InstalledFormatter", FixtureClient)
    if fault == "none":
        evidence = formatter.exercise(run)
        result = json.loads(evidence.read_text(encoding="utf-8"))
        assert result["status"] == "passed" and len(result["outputs"]) == 2
        assert len(result["wps_engine_evidence"]) == 5
    else:
        expected = "NATIVE_WPS_CONTROL_ARCHIVE_FAILED" if fault == "archive" else "NATIVE_GOLDEN_.*_MISMATCH"
        with pytest.raises(RuntimeError, match=expected):
            formatter.exercise(run)
        result = json.loads(next(run.reports.glob("formatter-golden-*/evidence.json")).read_text(encoding="utf-8"))
        assert result["status"] == "failed"
    assert result["runtime_environment_passed"] is False and closed == [True]


@pytest.mark.parametrize("fault", ["immediate_failure", "poll_failure", "poll_transport"])
def test_failed_job_keeps_ids_item_errors_and_host_response_before_cleanup(formatter, tmp_path, monkeypatch, fault):
    client = formatter.InstalledFormatter.__new__(formatter.InstalledFormatter)
    client.run = SimpleNamespace(work=tmp_path / "work")
    client.session_path = "/v1/sessions/" + "a" * 32
    client.private_values = ["synthetic-ticket", "synthetic-session-token"]
    client.job_records = []
    job_id, document_id = "b" * 32, "c" * 32
    output = tmp_path / "evidence"
    output.mkdir()
    control = client.run.work / "temp/partyops-official-format-fixture/jobs" / job_id / document_id / ".source-host"
    write_json(control / "request.json", {"feature_id": "format", "host_preference": "wps", "token": "synthetic-session-token"})
    message = "KWPS.Application 未注册；synthetic-ticket"
    write_json(control / "response.json", {"jobs": [{"success": False, "message": message}], "cookie": "synthetic-session-token"})
    (control / "progress.jsonl").write_text(json.dumps({"message": "synthetic-session-token"}), encoding="utf-8")
    failed = {"id": job_id, "state": "failed", "items": [{"document_id": document_id, "state": "failed",
              "error_code": "SOURCE_FORMATTER_FAILED", "message": message, "report": {"ticket": "synthetic-ticket"}}],
              "outputs": [], "authorization": "synthetic-ticket"}
    queued = {"id": job_id, "state": "queued", "items": [], "outputs": []}
    requests = []

    def request(path, *_args):
        requests.append(path)
        if len(requests) == 1:
            return failed if fault == "immediate_failure" else queued
        if fault == "poll_transport":
            raise RuntimeError("NATIVE_FORMATTER_HTTP_STATUS_500")
        return failed

    client.request = request
    monkeypatch.setattr(formatter.time, "sleep", lambda *_: None)
    with pytest.raises(RuntimeError, match="NATIVE_FORMATTER_"):
        client.job("format", {document_id: {"sha256": "fixture", "label": "source"}}, {}, output, ".docx")
    record = json.loads((output / ("job-" + job_id + ".json")).read_text(encoding="utf-8"))
    assert record["job_id"] == job_id and record["session_id"] == "a" * 32
    assert record["snapshots"][0]["response"]["id"] == job_id
    if fault != "poll_transport":
        assert record["snapshots"][-1]["response"]["items"][0]["error_code"] == "SOURCE_FORMATTER_FAILED"
    assert len(record["host_controls"]) == 3 and control.exists()
    for path in output.rglob("*.json"):
        text = path.read_text(encoding="utf-8")
        assert "synthetic-ticket" not in text and "synthetic-session-token" not in text
    archived = next(output.rglob("response.json.redacted.json"))
    assert "未注册" in archived.read_text(encoding="utf-8")
    assert "cookie" not in json.loads(archived.read_text(encoding="utf-8"))


def test_archive_does_not_import_another_job(formatter, tmp_path):
    foreign = tmp_path / "temp/partyops-official-format-other/jobs" / ("d" * 32) / ("c" * 32) / ".source-host/response.json"
    write_json(foreign, {"jobs": [{"success": True, "host_display_name": "old WPS"}]})
    result = formatter.archive_host_controls(tmp_path / "temp", "b" * 32, {"c" * 32: {}}, tmp_path / "out", lambda value: value)
    assert result == [{"document_id": "c" * 32, "status": "host_control_missing"}]
    assert not (tmp_path / "out").exists()


def test_failed_cli_returns_new_evidence_location(formatter, monkeypatch, tmp_path, capsys):
    import sys

    run = SimpleNamespace(last_formatter_evidence=tmp_path / "new-failed-run/evidence.json")
    monkeypatch.setattr(formatter.native, "NativeRun", lambda _args: run)

    def failed(_run):
        raise RuntimeError("NATIVE_FORMATTER_JOB_NOT_COMPLETED")

    monkeypatch.setattr(formatter, "exercise", failed)
    monkeypatch.setattr(sys, "argv", ["formatter", "--run-directory", str(tmp_path), "--work", str(tmp_path),
                                     "--data-directory", str(tmp_path), "--identity-receipt", str(tmp_path / "identity.json"),
                                     "--port", "18825"])
    assert formatter.main() == 1
    result = json.loads(capsys.readouterr().out)
    assert result["evidence"] == str(run.last_formatter_evidence)
    assert result["status"] == "failed" and result["runtime_environment_passed"] is False
