"""Linux 冻结包身份：只接受与当前可执行文件绑定的龙芯 core 清单。"""

from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path

CORE_CAPABILITIES = ("host", "collaboration", "database", "files", "archives", "backup", "ocr")
MAX_MANIFEST_BYTES = 8 * 1024 * 1024


def _unique_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("LINUX_RELEASE_MANIFEST_DUPLICATE_KEY")
        result[key] = value
    return result


def _loong_elf(executable: Path) -> bool:
    # ELF64 小端、EM_LOONGARCH=258、LP64D=3；只核对当前冻结入口。
    with executable.open("rb") as stream:
        header = stream.read(64)
    return (
        len(header) == 64
        and header[:6] == b"\x7fELF\x02\x01"
        and int.from_bytes(header[18:20], "little") == 258
        and int.from_bytes(header[48:52], "little") & 7 == 3
    )


def installed_linux_identity(executable: Path, architecture: str) -> dict[str, object]:
    """失败时拒绝声明包能力；本地哈希绑定不代表发布签名或功能验收。"""

    result: dict[str, object] = {
        "runtime_profile": "unsupported",
        "capabilities": [],
        "package_identity_status": "unsupported",
        "package_identity_reason": "LINUX_PACKAGE_IDENTITY_UNPROVEN",
    }
    try:
        if architecture != "loong64":
            raise ValueError("LINUX_CORE_ARCHITECTURE_UNSUPPORTED")
        manifest_path = executable.parent / "release-manifest.json"
        if executable.is_symlink() or manifest_path.is_symlink():
            raise ValueError("LINUX_RELEASE_MANIFEST_LINK_REJECTED")
        if not _loong_elf(executable):
            raise ValueError("LINUX_EXECUTABLE_ELF_INVALID")
        with manifest_path.open("rb") as stream:
            raw = stream.read(MAX_MANIFEST_BYTES + 1)
        if len(raw) > MAX_MANIFEST_BYTES:
            raise ValueError("LINUX_RELEASE_MANIFEST_TOO_LARGE")
        manifest = json.loads(raw.decode("utf-8-sig"), object_pairs_hook=_unique_object)
        if (
            not isinstance(manifest, dict)
            or type(manifest.get("schema_version")) is not int
            or manifest.get("schema_version") != 1
            or manifest.get("product") != "PartyOps"
            or manifest.get("platform") != "linux-deb"
            or manifest.get("architecture") != architecture
            or manifest.get("runtime_profile") != "core"
            or not isinstance(manifest.get("files"), list)
        ):
            raise ValueError("LINUX_RELEASE_MANIFEST_INVALID")
        matches = [
            entry for entry in manifest["files"]
            if isinstance(entry, dict) and entry.get("path") == executable.name
        ]
        if len(matches) != 1 or not re.fullmatch(r"[0-9a-f]{64}", str(matches[0].get("sha256", ""))):
            raise ValueError("LINUX_CURRENT_EXECUTABLE_NOT_IN_MANIFEST")
        digest = hashlib.sha256()
        size = 0
        with executable.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                size += len(chunk)
                digest.update(chunk)
        if size != matches[0].get("size") or digest.hexdigest() != matches[0]["sha256"]:
            raise ValueError("LINUX_CURRENT_EXECUTABLE_MANIFEST_MISMATCH")
        result.update(
            runtime_profile="core",
            capabilities=list(CORE_CAPABILITIES),
            package_identity_status="verified",
            package_identity_reason="",
        )
    except (OSError, UnicodeError, ValueError, TypeError, AttributeError) as exc:
        reason = str(exc)
        result["package_identity_reason"] = (
            reason if re.fullmatch(r"[A-Z0-9_]+", reason) else "LINUX_PACKAGE_IDENTITY_UNPROVEN"
        )
    return result
