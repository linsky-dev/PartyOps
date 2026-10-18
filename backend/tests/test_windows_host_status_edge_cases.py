"""主机启动诊断、仅本机探测和公开控制文件失败契约。"""

from __future__ import annotations

import socket
import subprocess
from types import SimpleNamespace

import pytest

from app import windows_host_status as status
from app import windows_public_files as public_files


def test_unknown_log_tags_do_not_hide_the_last_known_startup_failure():
    """第三方日志标签不能掩盖此前稳定的服务错误码。"""
    detail = "[DATABASE_LOCKED] 数据库占用\n[THIRD_PARTY_NOTICE] retry later"
    assert status.classify_runtime_failure(detail) == status.DATABASE_LOCKED
    assert status.classify_runtime_failure("[THIRD_PARTY_NOTICE] Bad image") == status.RUNTIME_BINARY_INCOMPATIBLE


def test_non_windows_bind_preflight_keeps_the_selected_address(monkeypatch):
    """跨平台模块不能在非 Windows 探测中调用专属套接字选项。"""
    calls = []

    class Probe:
        def __enter__(self):
            return self

        def __exit__(self, *_args):
            calls.append("closed")

        def setsockopt(self, *_args):
            pytest.fail("非 Windows 不应设置 SO_EXCLUSIVEADDRUSE")

        def bind(self, address):
            calls.append(address)

    monkeypatch.setattr(status, "os", SimpleNamespace(name="posix"))
    monkeypatch.setattr(status.socket, "socket", lambda *_args: Probe())
    status.preflight_bind("192.0.2.8", 18765)
    assert calls == [("192.0.2.8", 18765), "closed"]


@pytest.mark.parametrize("bind_host", ["0.0.0.0", "::", "localhost"])
def test_wildcard_health_probe_uses_loopback_without_dns(monkeypatch, bind_host):
    monkeypatch.setattr(status.socket, "getaddrinfo", lambda *_args: pytest.fail("通配监听无需 DNS"))
    assert status.local_health_probe_host(bind_host) == "127.0.0.1"


@pytest.mark.parametrize("accepted", ["192.0.2.8", "::1"])
def test_hostname_probe_skips_remote_answers_and_accepts_only_local_addresses(monkeypatch, accepted):
    """多地址 DNS 的首个远端答案不能让监督服务向远端发送健康探测。"""
    monkeypatch.setattr(status.socket, "gethostname", lambda: "local-machine")

    def resolve(host, _port):
        addresses = ["192.0.2.8"] if host == "local-machine" else ["198.51.100.9", accepted]
        return [(socket.AF_INET6 if ":" in address else socket.AF_INET, socket.SOCK_STREAM,
                 0, "", (address, 0)) for address in addresses]

    monkeypatch.setattr(status.socket, "getaddrinfo", resolve)
    expected = "[::1]" if accepted == "::1" else accepted
    assert status.local_health_probe_host("configured-host") == expected


@pytest.mark.parametrize("answers", [[], ["198.51.100.9", "2001:db8::9"]])
def test_hostname_without_local_answer_is_rejected(monkeypatch, answers):
    monkeypatch.setattr(status.socket, "gethostname", lambda: "local-machine")

    def resolve(host, _port):
        addresses = ["192.0.2.8"] if host == "local-machine" else answers
        return [(socket.AF_INET6 if ":" in address else socket.AF_INET, socket.SOCK_STREAM,
                 0, "", (address, 0)) for address in addresses]

    monkeypatch.setattr(status.socket, "getaddrinfo", resolve)
    with pytest.raises(ValueError, match="未解析到本机监听地址"):
        status.local_health_probe_host("configured-host")


@pytest.mark.parametrize("outside_root", [False, True])
def test_public_control_file_rejects_unapproved_targets_before_writing(tmp_path, monkeypatch, outside_root):
    """公开写入入口不得接受业务密钥路径或固定根目录外的同名文件。"""
    monkeypatch.setenv("PROGRAMDATA", str(tmp_path / "ProgramData"))
    root = public_files.public_host_ca_path().parent
    target = tmp_path / "outside" / "public-host-ca.pem" if outside_root else root / "private-key.pem"
    with pytest.raises(ValueError, match="固定的非敏感目标"):
        public_files.write_public_control_file(target, b"must not be written")
    assert not target.parent.exists()


@pytest.mark.parametrize("failure_call", [1, 2])
def test_public_control_file_acl_failure_preserves_previous_file_and_removes_temporary(tmp_path, monkeypatch, failure_call):
    """DACL 或所有者设置失败时不能发布新文件，已有桌面控制信息继续可用。"""
    monkeypatch.setenv("PROGRAMDATA", str(tmp_path / "ProgramData"))
    target = public_files.public_host_ca_path()
    target.parent.mkdir(parents=True)
    target.write_bytes(b"previous certificate")
    calls = []

    def fail_acl(command, **kwargs):
        calls.append(command)
        assert command[0] == "icacls.exe"
        assert command[1] != str(target)
        assert kwargs["check"] is False and kwargs["timeout"] == 30
        return subprocess.CompletedProcess(command, int(len(calls) == failure_call))

    # 仅替换系统命令边界；不运行真实 icacls、不触及系统 ProgramData。
    monkeypatch.setattr(public_files, "os", SimpleNamespace(name="nt", getenv=public_files.os.getenv))
    monkeypatch.setattr(public_files.subprocess, "CREATE_NO_WINDOW", 0, raising=False)
    monkeypatch.setattr(public_files.subprocess, "run", fail_acl)
    with pytest.raises(PermissionError, match="普通用户只读权限"):
        public_files.write_public_control_file(target, b"new certificate")
    assert len(calls) == failure_call
    assert target.read_bytes() == b"previous certificate"
    assert list(target.parent.iterdir()) == [target]
    assert "/inheritance:r" in calls[0] and "*S-1-5-32-545:R" in calls[0]
    if failure_call == 2:
        assert "/setowner" in calls[1] and "*S-1-5-32-544" in calls[1]


def test_non_windows_public_file_publishes_complete_content_without_acl_command(tmp_path, monkeypatch):
    """非 Windows 使用文件权限分支，发布后不残留中间文件。"""
    monkeypatch.setenv("PROGRAMDATA", str(tmp_path / "ProgramData"))
    monkeypatch.setattr(public_files, "os", SimpleNamespace(name="posix", getenv=public_files.os.getenv))
    monkeypatch.setattr(public_files.subprocess, "run", lambda *_args, **_kwargs: pytest.fail("非 Windows 不运行 icacls"))
    target = public_files.public_host_ca_path()
    public_files.write_public_control_file(target, b"complete public certificate")
    assert target.read_bytes() == b"complete public certificate"
    assert list(target.parent.iterdir()) == [target]
