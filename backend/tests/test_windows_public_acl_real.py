"""真实 Windows 受限令牌验证：公开控制文件只读、私钥不可读及单网卡 TLS 探测。"""
from __future__ import annotations

import ctypes
import json
import os
import socket
import ssl
import subprocess
import tempfile
import threading
import urllib.request
from contextlib import contextmanager
from ctypes import wintypes
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import pytest

from app import __version__
from app import setup_wizard as wizard
from app.config import Settings
from app.pki import ensure_tls_material
from app.windows_host_status import probe_loopback_health
from app.windows_public_files import public_host_ca_path, write_public_control_file


@contextmanager
def ordinary_windows_token():
    """实际切换线程令牌：Administrators 为 deny-only，移除最大权限集。"""
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    advapi = ctypes.WinDLL("advapi32", use_last_error=True)
    kernel.GetCurrentProcess.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel.LocalFree.argtypes = [wintypes.HLOCAL]
    advapi.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(wintypes.HANDLE)]
    advapi.ConvertStringSidToSidW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(wintypes.LPVOID)]
    class SIDAndAttributes(ctypes.Structure):
        _fields_ = [("sid", wintypes.LPVOID), ("attributes", wintypes.DWORD)]
    advapi.CreateRestrictedToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.DWORD, ctypes.POINTER(SIDAndAttributes), wintypes.DWORD, wintypes.LPVOID, wintypes.DWORD, wintypes.LPVOID, ctypes.POINTER(wintypes.HANDLE)]
    advapi.ImpersonateLoggedOnUser.argtypes = [wintypes.HANDLE]
    original, restricted, sid = wintypes.HANDLE(), wintypes.HANDLE(), wintypes.LPVOID()
    try:
        if not advapi.OpenProcessToken(kernel.GetCurrentProcess(), 0xF01FF, ctypes.byref(original)):
            raise ctypes.WinError(ctypes.get_last_error())
        if not advapi.ConvertStringSidToSidW("S-1-5-32-544", ctypes.byref(sid)):
            raise ctypes.WinError(ctypes.get_last_error())
        disabled = SIDAndAttributes(sid, 0)
        if not advapi.CreateRestrictedToken(original, 1, 1, ctypes.byref(disabled), 0, None, 0, None, ctypes.byref(restricted)):
            raise ctypes.WinError(ctypes.get_last_error())
        if not advapi.ImpersonateLoggedOnUser(restricted):
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            assert not ctypes.windll.shell32.IsUserAnAdmin()
            yield
        finally:
            if not advapi.RevertToSelf():
                raise ctypes.WinError(ctypes.get_last_error())
    finally:
        if restricted:
            kernel.CloseHandle(restricted)
        if original:
            kernel.CloseHandle(original)
        if sid:
            kernel.LocalFree(sid)


def protect_directory(path: Path, *, public: bool):
    grants = ["*S-1-5-18:(OI)(CI)F", "*S-1-5-32-544:(OI)(CI)F"]
    if public:
        grants += ["*S-1-5-32-545:(RX)"]  # 故意不继承，复现安装后的控制目录边界。
    for args in (["/inheritance:r", "/grant:r", *grants], ["/setowner", "*S-1-5-32-544"]):
        result = subprocess.run(["icacls.exe", str(path), *args, "/Q"], capture_output=True, text=True)
        assert result.returncode == 0, result.stderr


@pytest.mark.skipif(os.name != "nt", reason="真实 Windows 令牌与 ACL 验证")
def test_real_ordinary_token_can_read_public_ca_receipts_and_probe_explicit_lan(monkeypatch):
    if not ctypes.windll.shell32.IsUserAnAdmin():
        pytest.skip("创建受保护隔离样本需要管理员令牌")
    addresses = wizard.discover_lan_addresses()
    assert addresses, "本机没有可验证的 LAN IPv4，不能声称单网卡验收通过"
    lan = addresses[0]
    workspace = Path(__file__).resolve().parents[2]
    temporary_parent = workspace / "backend" / ".test-data"
    temporary_parent.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(dir=temporary_parent, prefix="real-public-acl-") as raw_root:
        root = Path(raw_root).resolve()
        assert workspace in root.parents  # 退出时的递归清理由此限定到本次隔离目录。
        control = root / "ProgramData" / "PartyOps"
        data, user = root / "private-host-data", root / "user"
        for path in (control, data, user):
            path.mkdir(parents=True)
        protect_directory(control, public=True)
        protect_directory(data, public=False)
        # 模拟真实 AppData：仅当前用户 SID 可写，其 Administrators 组被禁用也可用。
        sid = subprocess.run(["powershell.exe", "-NoProfile", "-Command", "[Security.Principal.WindowsIdentity]::GetCurrent().User.Value"], capture_output=True, text=True, check=True).stdout.strip()
        result = subprocess.run(["icacls.exe", str(user), "/grant:r", f"*{sid}:(OI)(CI)F", "/Q"], capture_output=True, text=True)
        assert result.returncode == 0
        monkeypatch.setenv("PROGRAMDATA", str(root / "ProgramData"))
        monkeypatch.setenv("PARTYOPS_ENVIRONMENT", "production")
        monkeypatch.setattr(wizard, "config_root", lambda: user)
        settings = Settings(data_dir=data, mode="host", environment="production", host=lan, bind_host=lan, advertise_host=lan, strict_sqlite=False)
        material = ensure_tls_material(settings)
        public_ca = public_host_ca_path()
        pending = control / "host-switch-pending.json"
        receipt = control / ("host-config-outcome-" + "R" * 32 + ".json")
        for path in (pending, receipt):
            write_public_control_file(path, b'{"non_sensitive":true}')
        expected_ca = public_ca.read_bytes()
        installed = []
        monkeypatch.setattr(wizard, "install_internal_ca", lambda path: installed.append(path.read_bytes()))
        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                payload = ({"status": "ok", "mode": "host", "app_version": __version__, "sqlite": {"safe_version": True, "fts5": True}} if self.path == "/api/v1/health" else {"configured": True})
                body = json.dumps(payload).encode()
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
            def log_message(self, *_args):
                pass
        server = ThreadingHTTPServer((lan, 0), Handler)
        port = server.server_port
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.load_cert_chain(str(material["server_cert_path"]), str(settings.tls_key_file))
        server.socket = context.wrap_socket(server.socket, server_side=True)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        # 测试只访问自身监听，不允许环境代理把请求带到网络代理。
        def local_urlopen(request, *, context=None, timeout=5):
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=context))
            return opener.open(request, timeout=timeout)
        monkeypatch.setattr(urllib.request, "urlopen", local_urlopen)
        try:
            with pytest.raises(OSError):
                socket.create_connection(("127.0.0.1", port), timeout=1)
            with wizard._host_configuration_mutex(), ordinary_windows_token():
                kernel = ctypes.WinDLL("kernel32", use_last_error=True)
                kernel.OpenMutexW.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.LPCWSTR]
                kernel.OpenMutexW.restype = wintypes.HANDLE
                assert not kernel.OpenMutexW(0x100000, False, "Global\\PartyOps.HostConfiguration")
                assert ctypes.get_last_error() == 5
                for path in (public_ca, pending, receipt):
                    assert path.read_bytes()
                    with pytest.raises(PermissionError):
                        path.write_bytes(b"unauthorized modification")
                for private in (data / "secrets" / "pki" / "ca.key", data / "secrets" / "pki" / "server.key", material["ca_path"]):
                    with pytest.raises(PermissionError):
                        private.read_bytes()
                assert wizard.wait_for_host_health(lan, port, tls=True, timeout=5, data_dir=data, bind_host=lan) == f"https://{lan}:{port}"
                assert probe_loopback_health(port, tls=True, ca_file=public_ca, bind_host=lan, expected_version=__version__) == (True, "")
                assert wizard.configured_runtime_status(f"https://{lan}:{port}", ca_file=public_ca, bind_host=lan)
                wizard._wait_and_install_ca(public_ca)
            assert [value.replace(b"\r\n", b"\n") for value in installed] == [expected_ca]
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=3)
        evidence = workspace / "qa" / "acceptance" / "windows-startup-20261016" / "real-public-acl.json"
        evidence.parent.mkdir(parents=True, exist_ok=True)
        evidence.write_text(json.dumps({"ordinary_token_admin": False, "mutex_ordinary_token_denied": True, "public_read": True, "public_write_denied": True, "private_keys_read_denied": True, "protected_ca_read_denied": True, "explicit_lan_tls_health": True, "loopback_unavailable": True, "desktop_ca_copy": True}, indent=2), encoding="utf-8")
