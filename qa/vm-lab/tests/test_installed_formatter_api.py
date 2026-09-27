"""安装版 HTTP 排版探针的边界测试；合成数据不进入真实验收目录。"""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace

import pytest


def load_probe():
    path = Path(__file__).resolve().parents[1] / "guest/installed-formatter-api.py"
    spec = importlib.util.spec_from_file_location("installed_formatter_api", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_streaming_digest(tmp_path):
    probe = load_probe()
    source = tmp_path / "测试 文件.docx"
    source.write_bytes(b"fixture" * (1024 * 1024))
    assert probe.digest(source) == probe.hashlib.sha256(source.read_bytes()).hexdigest()


@pytest.mark.parametrize("fault", ["FONT_CHECK_UNAVAILABLE", "SOURCE_HOST_FAILED", "wrong-hash"])
def test_font_install_does_not_mask_other_failures(tmp_path, monkeypatch, fault):
    probe = load_probe()
    monkeypatch.setattr(Path, "home", lambda: tmp_path)
    source = tmp_path / "qa.ttf"
    source.write_bytes(b"synthetic-font")
    job = {"state": "failed", "outputs": [], "items": [
        {"error_code": "REQUIRED_FONT_MISSING" if fault == "wrong-hash" else fault} for _ in range(2)]}
    with pytest.raises(AssertionError):
        probe.install_qa_font(source, "0" * 64, job)
    assert not (tmp_path / ".local").exists()


def test_font_install_keeps_existing_fonts_and_records_external_input(tmp_path, monkeypatch):
    probe = load_probe()
    monkeypatch.setattr(Path, "home", lambda: tmp_path)
    source = tmp_path / "qa.ttf"
    source.write_bytes(b"synthetic-font")
    calls = []
    monkeypatch.setattr(probe.subprocess, "run", lambda command, **kwargs: calls.append(command))
    job = {"state": "failed", "outputs": [], "items": [{"error_code": "REQUIRED_FONT_MISSING"}] * 2}
    checksum = probe.digest(source)
    result = probe.install_qa_font(source, checksum, job)
    assert Path(result["path"]).read_bytes() == source.read_bytes()
    assert result["sha256"] == checksum and calls[0][:2] == ["fc-cache", "-f"]
    with pytest.raises(AssertionError):
        probe.install_qa_font(source, checksum, job)
    assert len(calls) == 1


@pytest.mark.parametrize(
    ("marker", "uid"),
    [
        ({"uuid": "other", "purpose": "disposable-qa"}, 1000),
        ({"uuid": "owned", "purpose": "business-data"}, 1000),
        ({"uuid": "owned", "purpose": "disposable-qa", "extra": True}, 1000),
        ({"uuid": "owned", "purpose": "disposable-qa"}, 0),
    ],
)
def test_rejects_unowned_guest_or_root_before_any_launch(tmp_path, monkeypatch, marker, uid):
    probe = load_probe()
    monkeypatch.setattr(probe.os, "getuid", lambda: uid, raising=False)
    original_read = Path.read_text

    def read_marker(path, *args, **kwargs):
        if path.as_posix() == "/etc/partyops-vm-lab.json":
            return json.dumps(marker)
        return original_read(path, *args, **kwargs)

    monkeypatch.setattr(Path, "read_text", read_marker)
    monkeypatch.setattr("sys.argv", [
        "installed-formatter-api.py", "--uuid", "owned", "--host-sha256", "0" * 64,
        "--source", str(tmp_path / "source.docx"), "--source-sha256", "1" * 64,
        "--output", str(tmp_path / "output"),
    ])
    started = []
    monkeypatch.setattr(probe.subprocess, "Popen", lambda *a, **k: started.append(True))
    with pytest.raises(AssertionError):
        probe.main()
    assert started == [] and not (tmp_path / "output").exists()


@pytest.mark.parametrize("scenario", ["pass", "job-fail", "early-exit", "slow-stop"])
def test_orchestration_never_promotes_failed_partial_checks(tmp_path, monkeypatch, scenario):
    """此处只测探针编排；替身和合成报告全部限制在 pytest 临时目录。"""
    probe = load_probe()
    runtime = tmp_path / "installed"
    (runtime / "formatter-host").mkdir(parents=True)
    executable = runtime / "partyops"
    executable.write_bytes(b"synthetic-program")
    host = runtime / "formatter-host/partyops-document-formatter-host"
    host.write_bytes(b"synthetic-host")
    (runtime / "VERSION").write_text("1.4.5-rc.6")
    source = tmp_path / "source.docx"
    source.write_bytes(b"synthetic-input")
    marker = tmp_path / "marker.json"
    marker.write_text(json.dumps({"uuid": "owned", "purpose": "disposable-qa"}))
    boot_id = tmp_path / "boot-id"
    boot_id.write_text("synthetic-boot-id")

    class SandboxPath:
        def __new__(cls, value):
            mapping = {"/etc/partyops-vm-lab.json": marker, "/opt/partyops/partyops": executable,
                       "/proc/sys/kernel/random/boot_id": boot_id, "/proc/4321/exe": executable}
            return mapping.get(str(value), Path(value))

        @staticmethod
        def home():
            return tmp_path

    monkeypatch.setattr(probe, "Path", SandboxPath)
    monkeypatch.setattr(probe.os, "getuid", lambda: 1000, raising=False)
    monkeypatch.setattr(probe.time, "sleep", lambda _: None)
    monkeypatch.setenv("PARTYOPS_DOCUMENT_FORMATTER_HOST", "forbidden-developer-host")
    monkeypatch.setenv("MONO_PATH", "forbidden-developer-runtime")
    monkeypatch.setenv("LD_LIBRARY_PATH", "forbidden-wps-test-path")
    monkeypatch.setenv("LD_LIBRARY_PATH_ORIG", "forbidden-wps-test-path")
    monkeypatch.setattr("sys.argv", [
        "probe", "--uuid", "owned", "--host-sha256", probe.digest(host),
        "--source", str(source), "--source-sha256", probe.digest(source),
        "--output", str(tmp_path / "output"),
    ])

    class Process:
        pid = 4321
        stopped = False
        killed = False

        def poll(self):
            return 1 if scenario == "early-exit" or self.stopped else None

        def terminate(self):
            self.stopped = True

        def wait(self, timeout):
            if scenario == "slow-stop" and not self.killed:
                raise probe.subprocess.TimeoutExpired("synthetic", timeout)
            return 0

        def kill(self):
            self.killed = True

    process = Process()

    def launch(command, **kwargs):
        assert command == [str(executable)]
        assert kwargs["env"]["PARTYOPS_ENVIRONMENT"] == "production"
        assert "PARTYOPS_DOCUMENT_FORMATTER_HOST" not in kwargs["env"]
        assert "MONO_PATH" not in kwargs["env"]
        assert "LD_LIBRARY_PATH" not in kwargs["env"]
        assert "LD_LIBRARY_PATH_ORIG" not in kwargs["env"]
        return process

    monkeypatch.setattr(probe.subprocess, "Popen", launch)
    cookies = []
    monkeypatch.setattr(probe.http.cookiejar, "CookieJar", lambda: cookies)
    state = {"configured": False, "health_count": 0, "uploads": 0}
    calls = []

    class Response:
        def __init__(self, value):
            self.headers = {"Content-Type": "application/json" if isinstance(value, dict) else "application/octet-stream"}
            self.value = value

        def __enter__(self):
            return self

        def __exit__(self, *args):
            return None

        def read(self):
            return json.dumps(self.value).encode() if isinstance(self.value, dict) else self.value

    class Opener:
        def open(self, query, timeout):
            path = probe.urllib.request.urlparse(query.full_url).path
            calls.append((query.method, path))
            if path == "/api/v1/health":
                state["health_count"] += 1
                if state["health_count"] == 1:
                    raise probe.urllib.error.URLError("synthetic startup delay")
                return Response({"status": "ok", "app_version": "1.4.5-rc.6"})
            if path.endswith("/bootstrap/status"):
                return Response({"configured": state["configured"]})
            if path.endswith("/bootstrap/host"):
                state["configured"] = True
                return Response({"id": "qa-user"})
            if path.endswith("/auth/login"):
                cookies.append(SimpleNamespace(name="partyops_csrf", value="synthetic-csrf"))
                return Response({"id": "qa-user"})
            if path.endswith("/local-ticket"):
                assert query.get_header("X-partyops-csrf") == "synthetic-csrf"
                return Response({"local_base_url": "http://127.0.0.1:18868", "ticket": "synthetic-ticket"})
            if path == "/v1/sessions":
                assert query.get_header("Authorization") == "Bearer synthetic-ticket"
                return Response({"session_token": "synthetic-local-token", "session_id": "session"})
            if path == "/v1/capabilities":
                return Response({"source_host_ready": True, "capability_count": 25, "features": list(range(6))})
            assert query.get_header("X-partyops-local-token") == "synthetic-local-token"
            if path.endswith("/documents"):
                state["uploads"] += 1
                assert b'name="document"' in query.data
                return Response({"document_id": str(state["uploads"])})
            if path.endswith("/jobs"):
                assert len(json.loads(query.data)["document_ids"]) == 2
                return Response({"id": "job", "state": "queued"})
            if path.endswith("/jobs/job"):
                return Response({"id": "job", "state": "failed" if scenario == "job-fail" else "completed",
                                 "outputs": [{"id": "one"}, {"id": "two"}]})
            if "/outputs/" in path:
                return Response(b"synthetic-result")
            assert query.method == "DELETE"
            return Response({"removed": True})

    monkeypatch.setattr(probe.urllib.request, "build_opener", lambda *args: Opener())
    if scenario == "pass":
        probe.main()
    else:
        with pytest.raises(AssertionError):
            probe.main()
    report_text = (tmp_path / "output/installed-api-evidence.json").read_text(encoding="utf-8")
    report = json.loads(report_text)
    assert report["status"] == ("passed" if scenario == "pass" else "failed")
    assert report["runtime_environment_passed"] is False
    assert "synthetic-ticket" not in report_text and "synthetic-local-token" not in report_text
    assert source.read_bytes() == b"synthetic-input"
