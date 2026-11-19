"""验证历史及当前密钥路径的文件类型和访问权限。"""

from __future__ import annotations

import os
import stat
from pathlib import Path


def verify_private_path(path: Path, *, directory: bool = False) -> None:
    info = path.lstat()
    expected_type = stat.S_ISDIR if directory else stat.S_ISREG
    if not expected_type(info.st_mode) or (
        getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
    ):
        raise PermissionError("密钥必须位于普通本机路径")
    if os.name != "nt" and (info.st_uid != os.geteuid() or stat.S_IMODE(info.st_mode) & 0o077):
        raise PermissionError("密钥路径的权限必须仅授予主机服务账号")
