"""主机/个人模式的排版实例端点；记录仅是下次绑定提示，不是已就绪证明。"""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from .official_format_service import OfficialFormatLocalService

ENDPOINT_NAME = "official-format-endpoint.json"


def configured_formatter_port(previous: dict[str, str], main_port: int) -> int:
    """重配或升级保留显式端口；旧个人配置按实例主端口补齐独立首选值。"""

    excluded = {main_port, main_port + 1, 18767}
    raw = previous.get("PARTYOPS_OFFICIAL_FORMAT_PORT", "")
    if raw:
        try:
            port = int(raw)
        except ValueError as exc:
            raise ValueError("公文排版端口必须为 1024—65535 的整数") from exc
        if not 1024 <= port <= 65535:
            raise ValueError("公文排版端口必须在 1024—65535 之间")
        if port not in excluded:
            return port
    candidate = main_port + 2 if main_port <= 65533 else 18768
    while candidate in excluded:
        candidate += 1
    return candidate


def endpoint_path(data_dir: Path) -> Path:
    return data_dir / "logs" / ENDPOINT_NAME


def _data_binding(data_dir: Path) -> str:
    return hashlib.sha256(str(data_dir.resolve()).encode("utf-8")).hexdigest()


def _previous_port(path: Path, data_dir: Path, configured: int) -> int | None:
    try:
        if path.is_symlink() or path.stat().st_size > 4096:
            return None
        value = json.loads(path.read_text(encoding="utf-8"))
        if (value.get("format_version") == 1 and value.get("data_binding") == _data_binding(data_dir)
                and value.get("configured_port") == configured
                and type(value.get("port")) is int and 1024 <= value["port"] <= 65535):
            return value["port"]
    except (OSError, ValueError, TypeError, AttributeError):
        return None
    return None


def start_instance_formatter(data_dir: Path, configured_port: int, secret: str) -> OfficialFormatLocalService:
    """只绑定本进程独占套接字；占用时由系统分配端口，绝不连接或终止占用者。"""

    from .official_format import OfficialFormatError
    from .official_format_service import OfficialFormatLocalService

    path = endpoint_path(data_dir)
    previous = _previous_port(path, data_dir, configured_port)
    candidates = list(dict.fromkeys([port for port in (previous, configured_port, 0) if port is not None]))
    service = None
    for port in candidates:
        candidate = OfficialFormatLocalService(secret=secret, config_dir=path.parent, port=port)
        try:
            service = candidate.start()
            break
        except OfficialFormatError as exc:
            candidate.close()
            if exc.code != "LOCAL_FORMAT_PORT_IN_USE" or port == 0:
                raise
    assert service is not None
    value = {"format_version": 1, "data_binding": _data_binding(data_dir),
             "configured_port": configured_port, "port": service.port,
             "pid": os.getpid(), "instance_id": service.instance_id}
    try:
        # 配置不可写则本次启动失败；不能发出无法追溯到本实例的端点。
        if path.is_symlink():
            raise OSError("排版端点记录不能为符号链接")
        temporary = path.with_name(path.name + "." + service.instance_id + ".tmp")
        with temporary.open("x", encoding="utf-8") as stream:
            json.dump(value, stream, ensure_ascii=False)
            stream.write("\n")
        os.replace(temporary, path)
    except OSError as exc:
        service.close()
        raise OfficialFormatError(
            "LOCAL_FORMAT_CONFIG_WRITE_FAILED", "公文排版配置未保存",
            "无法保存当前实例的排版端口，请检查业务数据目录权限后重新打开 PartyOps。",
        ) from exc
    return service
