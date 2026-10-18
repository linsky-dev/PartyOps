"""龙芯 core 只能由当前冻结 ELF 和同目录发布清单证明。"""

from __future__ import annotations

import hashlib
import json
import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

from app import platform_info


def _installed_executable(tmp_path: Path, *, architecture: str = "loong64", profile: str = "core") -> Path:
    executable = tmp_path / "partyops"
    header = bytearray(64)
    header[:6] = b"\x7fELF\x02\x01"
    header[18:20] = (258).to_bytes(2, "little")
    header[48:52] = (3).to_bytes(4, "little")
    executable.write_bytes(header)
    manifest = {
        "schema_version": 1,
        "product": "PartyOps",
        "platform": "linux-deb",
        "architecture": architecture,
        "runtime_profile": profile,
        "files": [{"path": "partyops", "size": len(header), "sha256": hashlib.sha256(header).hexdigest()}],
    }
    (tmp_path / "release-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    return executable


def test_valid_frozen_loong_core_capabilities_and_channel(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    executable = _installed_executable(tmp_path)
    monkeypatch.setattr(platform_info, "sys", SimpleNamespace(platform="linux", frozen=True, executable=str(executable)))
    monkeypatch.setattr(platform_info.platform, "machine", lambda: "loongarch64")
    release = tmp_path / "os-release"
    release.write_text("ID=deepin\nVERSION_ID=25\n", encoding="utf-8")
    info = platform_info.detect_platform_info(os_release_path=release)
    assert info["runtime_profile"] == "core"
    assert info["package_identity_status"] == "verified"
    assert info["capabilities"] == list(platform_info.CORE_CAPABILITIES)
    assert platform_info.update_platform_key(info) == "linux-deb"


@pytest.mark.parametrize("damage", ["missing", "wrong-arch", "wrong-profile", "wrong-hash", "wrong-elf", "duplicate-key"])
def test_invalid_frozen_loong_never_claims_core(tmp_path: Path, monkeypatch: pytest.MonkeyPatch, damage: str) -> None:
    executable = _installed_executable(tmp_path)
    manifest_path = tmp_path / "release-manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if damage == "missing":
        manifest_path.unlink()
    elif damage == "wrong-elf":
        executable.write_bytes(b"invalid")
    elif damage == "duplicate-key":
        manifest_path.write_text(manifest_path.read_text(encoding="utf-8").replace('"product":', '"product":"PartyOps","product":'), encoding="utf-8")
    else:
        if damage == "wrong-arch":
            manifest["architecture"] = "amd64"
        elif damage == "wrong-profile":
            manifest["runtime_profile"] = "full"
        else:
            manifest["files"][0]["sha256"] = "0" * 64
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    monkeypatch.setattr(platform_info, "sys", SimpleNamespace(platform="linux", frozen=True, executable=str(executable)))
    monkeypatch.setattr(platform_info.platform, "machine", lambda: "loong64")
    info = platform_info.detect_platform_info(os_release_path=tmp_path / "os-release")
    assert info["runtime_profile"] == "unsupported"
    assert info["capabilities"] == []
    assert platform_info.update_platform_key(info) == ""


def test_development_linux_keeps_existing_full_profile(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(platform_info, "sys", SimpleNamespace(platform="linux", frozen=False))
    monkeypatch.setattr(platform_info.platform, "machine", lambda: "loong64")
    info = platform_info.detect_platform_info(os_release_path=tmp_path / "os-release")
    assert info["runtime_profile"] == "full"
    assert "local_llm" in info["capabilities"]


def test_backend_import_does_not_require_complex_ai_modules() -> None:
    # 独立解释器排除已缓存模块，模拟普通包未安装复杂 AI 依赖的导入路径。
    script = """
import sys
class BlockAI:
    def find_spec(self, fullname, path=None, target=None):
        if fullname.split('.')[0] in {'onnxruntime', 'tokenizers', 'hf_xet', 'hfxet'}:
            raise ModuleNotFoundError(fullname)
sys.meta_path.insert(0, BlockAI())
import app.main
print('backend-import-ok')
"""
    result = subprocess.run(
        [sys.executable, "-c", script], cwd=Path(__file__).parents[1],
        capture_output=True, text=True, timeout=30, check=False,
    )
    assert result.returncode == 0, result.stderr
    assert "backend-import-ok" in result.stdout
