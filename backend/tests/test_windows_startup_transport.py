"""真实 PowerShell 无提权替身、监听诊断和显式重新配置回归。"""
from __future__ import annotations

import base64
import errno
import json
import os
import socket
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from app import windows_host_status as status
from app.windows_elevation import elevation_command


class WindowsStartupTransportTests(unittest.TestCase):
    @unittest.skipUnless(os.name == "nt", "需要真实 Windows PowerShell")
    def test_powershell_transport_preserves_arguments_and_runas_contract(self):
        # 替身保留 Start-Process 调用契约，通过 .NET 启动无副作用 Python 子进程。
        with tempfile.TemporaryDirectory(dir=Path(__file__).resolve().parents[1]) as root:
            directory = Path(root) / "中文 space's"
            directory.mkdir()
            child = directory / "argv's child.py"
            output = directory / "argv.json"
            child.write_text("import json,sys; from pathlib import Path; Path(sys.argv[1]).write_text(json.dumps(sys.argv[2:], ensure_ascii=False), encoding='utf-8')", encoding="utf-8")
            arguments = [str(child), str(output), "", "a b", "中文", "single'quote", 'double"quote', "C:\\space dir\\", "C:\\plain\\", "$(throw 'injection'); & test"]
            command = elevation_command(sys.executable, arguments)
            script = base64.b64decode(command[-1]).decode("utf-16-le")
            substitute = r'''
function Start-Process {
param([string]$FilePath,[string]$ArgumentList,[string]$Verb,[switch]$Wait,[switch]$PassThru)
if($Verb -ne 'RunAs' -or -not $Wait -or -not $PassThru){throw 'missing elevation contract'}
$i=New-Object System.Diagnostics.ProcessStartInfo
$i.FileName=$FilePath; $i.Arguments=$ArgumentList; $i.UseShellExecute=$false; $i.CreateNoWindow=$true
$p=[System.Diagnostics.Process]::Start($i); $p.WaitForExit(); return $p
}
'''
            command[-1] = base64.b64encode((substitute + script).encode("utf-16-le")).decode("ascii")
            result = subprocess.run(command, capture_output=True, text=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("PARTYOPS_ELEVATION_CHILD_EXIT", result.stdout)
            self.assertEqual(json.loads(output.read_text(encoding="utf-8")), arguments[2:])

    @unittest.skipUnless(os.name == "nt", "需要真实 Windows PowerShell")
    def test_powershell_reports_start_failure_separately_from_child_exit(self):
        script = base64.b64decode(elevation_command("unused.exe", ["arg"])[-1]).decode("utf-16-le")
        for code in (1223, 786, 1260, 5):
            prefix = "function Start-Process { throw (New-Object System.ComponentModel.Win32Exception(%s)) }; " % code
            command = elevation_command("unused.exe", ["arg"])
            command[-1] = base64.b64encode((prefix + script).encode("utf-16-le")).decode("ascii")
            result = subprocess.run(command, capture_output=True, text=True, timeout=30)
            self.assertEqual(result.returncode, code, result.stderr)
            self.assertIn("PARTYOPS_ELEVATION_START_FAILED", result.stderr)
            self.assertNotIn("PARTYOPS_ELEVATION_CHILD_EXIT", result.stdout)
        command[-1] = base64.b64encode(("function Start-Process { return [pscustomobject]@{ExitCode=1223} }; " + script).encode("utf-16-le")).decode("ascii")
        result = subprocess.run(command, capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 1223)
        self.assertIn("PARTYOPS_ELEVATION_CHILD_EXIT", result.stdout)

    def test_bind_errors_have_distinct_diagnostics_and_close_socket(self):
        for number, code in ((10049, status.BIND_ADDRESS_UNAVAILABLE), (10048, status.PORT_IN_USE), (10013, status.BIND_PERMISSION_DENIED)):
            with self.subTest(number=number), patch.object(status.socket, "socket") as factory:
                probe = factory.return_value.__enter__.return_value
                error = OSError(errno.EINVAL, "synthetic bind failure")
                error.winerror = number
                probe.bind.side_effect = error
                with self.assertRaisesRegex(RuntimeError, code):
                    status.preflight_bind("192.0.2.123", 18765)
                probe.bind.assert_called_once_with(("192.0.2.123", 18765))
                factory.return_value.__exit__.assert_called_once()
                self.assertEqual(status.classify_runtime_failure("", winerror=number), code)
                self.assertEqual(status.classify_runtime_failure(f"[WinError {number}] bind"), code)
        self.assertEqual(status.classify_runtime_failure("设备安全端口启动失败，检查内部 CA 权限"), status.RUNTIME_PERMISSION_DENIED)
        self.assertEqual(status.classify_runtime_failure("设备安全端口启动超时"), status.CHILD_EXITED)

    def test_successful_probe_releases_real_socket(self):
        with socket.socket() as probe:
            probe.bind(("127.0.0.1", 0))
            port = probe.getsockname()[1]
        status.preflight_bind("127.0.0.1", port)
        with socket.socket() as probe:
            probe.bind(("127.0.0.1", port))


def test_elevation_failure_classification_preserves_phase():
    import pytest

    from app import setup_wizard as wizard
    cases = [
        (1223, "", "PARTYOPS_ELEVATION_START_FAILED", "ADMIN_CANCELLED"),
        (786, "", "PARTYOPS_ELEVATION_START_FAILED", "ADMIN_POLICY_BLOCKED"),
        (1260, "", "PARTYOPS_ELEVATION_START_FAILED", "ADMIN_POLICY_BLOCKED"),
        (5, "", "PARTYOPS_ELEVATION_START_FAILED", "ADMIN_START_FAILED"),
        (1223, "PARTYOPS_ELEVATION_CHILD_EXIT", "", "ADMIN_CHILD_FAILED"),
        (786, "PARTYOPS_ELEVATION_CHILD_EXIT", "", "ADMIN_CHILD_FAILED"),
        (1, "", "", "ADMIN_START_FAILED"),
    ]
    for returncode, stdout, stderr, code in cases:
        with pytest.raises(wizard.HostStartupError) as raised:
            wizard._check_windows_elevation_result(subprocess.CompletedProcess([], returncode, stdout, stderr), "主机配置")
        assert raised.value.code == code


def test_explicit_host_reconfiguration_updates_only_network_fields(monkeypatch, tmp_path):
    from app import setup_wizard as wizard
    monkeypatch.setenv("PARTYOPS_ENVIRONMENT", "test")
    monkeypatch.setattr(wizard, "config_root", lambda: tmp_path / "config")
    data = tmp_path / "data"
    data.mkdir()
    override = data / "network-settings.json"
    override.write_text(json.dumps({"bind_host": "192.0.2.8", "advertise_host": "old", "port": 12345, "audit": "keep"}), encoding="utf-8")
    wizard.write_host_config("127.0.0.1", 18765, data)
    assert json.loads(override.read_text(encoding="utf-8")) == {"bind_host": "127.0.0.1", "advertise_host": "127.0.0.1", "port": 18765, "audit": "keep"}


def test_network_override_restored_when_host_configuration_fails(monkeypatch, tmp_path):
    import pytest

    from app import setup_wizard as wizard
    monkeypatch.setenv("PARTYOPS_ENVIRONMENT", "test")
    monkeypatch.setattr(wizard, "config_root", lambda: tmp_path / "config")
    data = tmp_path / "data"
    data.mkdir()
    override = data / "network-settings.json"
    previous = '{"bind_host":"192.0.2.8","audit":"keep"}'
    override.write_text(previous, encoding="utf-8")
    monkeypatch.setattr(wizard, "_write_data_root_marker", lambda *_a: (_ for _ in ()).throw(OSError("synthetic write failure")))
    with pytest.raises(OSError, match="synthetic"):
        wizard.write_host_config("127.0.0.1", 18765, data)
    assert override.read_text(encoding="utf-8") == previous


def test_loading_explicit_bind_does_not_silently_widen(monkeypatch, tmp_path):
    from app.config import get_settings, reset_settings_cache
    monkeypatch.setenv("PARTYOPS_DATA_DIR", str(tmp_path))
    monkeypatch.setenv("PARTYOPS_BIND_HOST", "192.0.2.8")
    reset_settings_cache()
    try:
        assert get_settings().network_bind_host == "192.0.2.8"
        (tmp_path / "network-settings.json").write_text('{"bind_host":"127.0.0.1"}', encoding="utf-8")
        reset_settings_cache()
        assert get_settings().network_bind_host == "127.0.0.1"
    finally:
        reset_settings_cache()


def test_main_preflights_both_ports_before_tls_or_threads(monkeypatch):
    from types import SimpleNamespace

    import pytest
    import uvicorn

    from app import main, pki
    calls = []
    monkeypatch.setattr(main, "os", SimpleNamespace(name="nt"))
    monkeypatch.setattr(main, "settings", SimpleNamespace(network_bind_host="192.0.2.8", port=18765, agent_port=18766, tls_enabled=True))
    def bind(host, port):
        calls.append((host, port))
        if port == 18766:
            raise RuntimeError("[BIND_ADDRESS_UNAVAILABLE] synthetic")
    monkeypatch.setattr(status, "preflight_bind", bind)
    monkeypatch.setattr(pki, "ensure_tls_material", lambda *_a: pytest.fail("TLS must follow preflight"))
    monkeypatch.setattr(uvicorn, "run", lambda *_a, **_k: pytest.fail("must fail before serving"))
    with pytest.raises(RuntimeError, match="BIND_ADDRESS_UNAVAILABLE"):
        main.run()
    assert calls == [("192.0.2.8", 18765), ("192.0.2.8", 18766)]


def test_agent_thread_failure_keeps_original_cause(monkeypatch):
    from types import SimpleNamespace

    import pytest
    import uvicorn

    from app import main, pki
    monkeypatch.setattr(main, "settings", SimpleNamespace(network_bind_host="127.0.0.1", port=18765, agent_port=18766, tls_enabled=True, tls_cert_file="cert", tls_key_file="key", tls_client_ca_file="ca", tls_require_client_cert=False))
    monkeypatch.setattr(status, "preflight_bind", lambda *_a: None)
    monkeypatch.setattr(pki, "ensure_tls_material", lambda *_a: None)
    failure = OSError(10049, "requested address is not valid")
    def fail():
        raise failure
    monkeypatch.setattr(uvicorn, "Config", lambda *_a, **_k: None)
    monkeypatch.setattr(uvicorn, "Server", lambda *_a: SimpleNamespace(started=False, run=fail))
    class Thread:
        def __init__(self, target, **kwargs):
            self.target = target
        def start(self):
            self.target()
        def is_alive(self):
            return False
    monkeypatch.setattr(main.threading, "Thread", Thread)
    with pytest.raises(RuntimeError) as raised:
        main.run()
    assert raised.value.__cause__ is failure
    assert status.classify_runtime_failure(str(raised.value)) == status.BIND_ADDRESS_UNAVAILABLE


def test_non_windows_startup_keeps_existing_binding_flow(monkeypatch):
    from types import SimpleNamespace

    import pytest
    import uvicorn

    from app import main
    monkeypatch.setattr(main, "os", SimpleNamespace(name="posix"))
    monkeypatch.setattr(main, "settings", SimpleNamespace(network_bind_host="127.0.0.1", port=18765, tls_enabled=False))
    monkeypatch.setattr(status, "preflight_bind", lambda *_a: pytest.fail("Windows-only probe"))
    calls = []
    monkeypatch.setattr(uvicorn, "run", lambda *_a, **options: calls.append(options))
    main.run()
    assert calls[0]["host"] == "127.0.0.1"
    assert calls[0]["port"] == 18765


def test_reconfiguring_explicit_lan_binding_never_widens(monkeypatch, tmp_path):
    from app import setup_wizard as wizard
    monkeypatch.setenv("PARTYOPS_ENVIRONMENT", "test")
    monkeypatch.setattr(wizard, "config_root", lambda: tmp_path / "config")
    monkeypatch.setattr(wizard, "discover_lan_addresses", lambda: ["192.168.1.8", "192.168.1.9"])
    data = tmp_path / "data"
    data.mkdir()
    override = data / "network-settings.json"
    override.write_text('{"bind_host":"192.168.1.8","port":18765}', encoding="utf-8")
    for selected_host in ("192.168.1.8", "192.168.1.9"):
        config = wizard.write_host_config(selected_host, 18765, data)
        assert json.loads(override.read_text(encoding="utf-8"))["bind_host"] == selected_host
        assert wizard.load_host_environment(config)["PARTYOPS_BIND_HOST"] == selected_host
    override.write_text('{"bind_host":"0.0.0.0","port":18765}', encoding="utf-8")
    config = wizard.write_host_config("192.168.1.9", 18765, data)
    assert json.loads(override.read_text(encoding="utf-8"))["bind_host"] == "0.0.0.0"
    assert wizard.load_host_environment(config)["PARTYOPS_BIND_HOST"] == "0.0.0.0"




if __name__ == "__main__":
    unittest.main(verbosity=2)
