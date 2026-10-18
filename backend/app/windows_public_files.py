"""Windows 普通桌面所需的固定公开控制文件；不开放业务数据或私钥。"""
from __future__ import annotations

import os
import re
import secrets
import subprocess
from pathlib import Path


def public_host_ca_path() -> Path:
    return Path(os.getenv("PROGRAMDATA", "C:/ProgramData")) / "PartyOps" / "public-host-ca.pem"


def write_public_control_file(path: Path, data: bytes) -> None:
    root = public_host_ca_path().parent
    if path.parent.resolve() != root.resolve() or not (
        path.name in {"host-switch-pending.json", "public-host-ca.pem"}
        or re.fullmatch(r"host-config-outcome-[A-Za-z0-9_-]{32,128}\.json", path.name)
    ):
        raise ValueError("公开控制文件只能写入固定的非敏感目标")
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f"{path.name}.{secrets.token_hex(8)}.tmp")
    try:
        with temporary.open("xb") as stream:
            stream.write(data)
        if os.name == "nt":
            for arguments in (
                ["/inheritance:r", "/grant:r", "*S-1-5-18:F", "*S-1-5-32-544:F", "*S-1-5-32-545:R"],
                ["/setowner", "*S-1-5-32-544"],
            ):
                result = subprocess.run(["icacls.exe", str(temporary), *arguments, "/Q"], capture_output=True, text=True, check=False, timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
                if result.returncode:
                    raise PermissionError("公开主机配置无法设置管理员写入、普通用户只读权限")
        else:
            temporary.chmod(0o644)
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def host_ca_for_desktop(data_dir: Path, *, service_managed: bool = True) -> Path:
    if os.name == "nt" and os.getenv("PARTYOPS_ENVIRONMENT") != "test" and service_managed:
        return public_host_ca_path()
    return data_dir / "secrets" / "pki" / "ca.pem"
