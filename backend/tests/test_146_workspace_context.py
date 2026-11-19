"""主机共享范围使用明确的本机或已验证设备上下文。"""

from types import SimpleNamespace

import pytest
from starlette.requests import Request

from app.enums import UserRole
from app.workspace_access import (
    UNIDENTIFIED_REMOTE_DEVICE,
    workspace_request_device_id,
    workspace_root_permissions,
)


def _request(host: str, *, forwarded: bool = False) -> Request:
    headers = [(b"x-forwarded-for", b"192.0.2.5")] if forwarded else []
    return Request({"type": "http", "client": (host, 1234), "headers": headers})


class _Devices:
    def __init__(self, device=None):
        self.device = device

    def get(self, _model, key):
        return self.device if self.device and key == self.device.id else None


@pytest.mark.parametrize(
    ("host", "forwarded"),
    [("192.0.2.5", False), ("127.0.0.1", True), ("", False)],
)
def test_host_share_requires_local_transport_or_a_verified_device(host, forwarded):
    root = SimpleNamespace(enabled=True, source=SimpleNamespace(value="host"))
    member = SimpleNamespace(role=UserRole.STAFF)
    scope = workspace_request_device_id(_request(host, forwarded=forwarded), None)
    assert scope == UNIDENTIFIED_REMOTE_DEVICE
    permissions = workspace_root_permissions(_Devices(), root, member, scope)
    assert permissions["browse"] is False
    assert permissions["download"] is False
    assert permissions["send"] is False


@pytest.mark.parametrize("host", ["127.0.0.1", "::1", "::ffff:127.0.0.1"])
def test_direct_loopback_preserves_member_access_to_host_share(host):
    root = SimpleNamespace(enabled=True, source=SimpleNamespace(value="host"))
    member = SimpleNamespace(role=UserRole.STAFF)
    scope = workspace_request_device_id(_request(host), None)
    assert scope is None
    assert workspace_root_permissions(_Devices(), root, member, scope)["browse"] is True


def test_verified_remote_device_uses_its_host_access_setting():
    root = SimpleNamespace(enabled=True, source=SimpleNamespace(value="host"))
    member = SimpleNamespace(role=UserRole.STAFF)
    device = SimpleNamespace(id="verified-device", active=True, status="online", allow_host_access=False)
    scope = workspace_request_device_id(_request("192.0.2.5"), device)
    assert scope == device.id
    assert workspace_root_permissions(_Devices(device), root, member, scope)["download"] is False
    device.allow_host_access = True
    assert workspace_root_permissions(_Devices(device), root, member, scope)["download"] is True
