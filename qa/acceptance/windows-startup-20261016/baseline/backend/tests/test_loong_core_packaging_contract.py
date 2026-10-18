"""Loong64 普通包的构建档、清单与 ABI 检查契约。"""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

ROOT = Path(__file__).resolve().parents[2]


def _script_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def test_loong_wheel_profile_uses_actual_python_and_glibc_tags(tmp_path: Path) -> None:
    validator = _script_module("loong_wheel_validator", ROOT / "scripts/validate-uos-wheelhouse.py")
    environment = validator.linux_environment("loong64")
    assert environment["python_full_version"] == "3.12.13"
    assert environment["platform_machine"] == "loongarch64"
    validator.validate_wheel_platform(
        tmp_path / "numpy-2.2.6-cp312-cp312-linux_loongarch64.whl", "loong64", "core"
    )
    with pytest.raises(ValueError, match="高于发布基线 2.38"):
        validator.validate_wheel_platform(
            tmp_path / "numpy-2.2.6-cp312-cp312-manylinux_2_39_loongarch64.whl", "loong64", "core"
        )
    with pytest.raises(ValueError, match="loong64 仅允许"):
        validator.validate(tmp_path, [], "loong64", "full")
    with pytest.raises(ValueError, match="core 仅允许"):
        validator.validate(tmp_path, [], "amd64", "core")


def test_linux_core_manifest_binds_only_frozen_entry(tmp_path: Path) -> None:
    runtime = tmp_path / "runtime"
    runtime.mkdir()
    (runtime / "partyops").write_bytes(b"frozen-entry")
    (runtime / "other.bin").write_bytes(b"other")
    output = runtime / "release-manifest.json"
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/generate-release-manifest.py"),
         "--root", str(runtime), "--output", str(output), "--version", "1.4.5-rc.6",
         "--tag", "v1.4.5-rc.6", "--commit", "a" * 40, "--platform", "linux-deb",
         "--architecture", "loong64", "--runtime-profile", "core", "--only-file", "partyops"],
        check=False, capture_output=True, text=True, timeout=20,
    )
    assert result.returncode == 0, result.stderr
    payload = json.loads(output.read_text(encoding="utf-8"))
    assert payload["runtime_profile"] == "core"
    assert [entry["path"] for entry in payload["files"]] == ["partyops"]
    assert payload["files"][0]["size"] == len(b"frozen-entry")


def test_actual_elf_symbol_gate_rejects_newer_glibc(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    verifier = _script_module("loong_elf_verifier", ROOT / "scripts/verify-linux-elf-glibc.py")
    (tmp_path / "partyops").write_bytes(b"\x7fELF" + b"\0" * 60)

    def version_info(needed_glibc: str) -> str:
        return f"""Version definition section '.gnu.version_d' contains 1 entry:
 Addr: 0x0000000000000000  Offset: 0x00000000  Link: 4 (.dynstr)
  000000: Rev: 1  Flags: BASE  Index: 1  Cnt: 1  Name: GLIBC_2.40
Version needs section '.gnu.version_r' contains 1 entry:
 Addr: 0x0000000000000000  Offset: 0x00000000  Link: 4 (.dynstr)
  000000: Version: 1  File: libc.so.6  Cnt: 1
  0x0010:   Name: {needed_glibc}  Flags: none  Version: 2
"""

    monkeypatch.setattr(verifier.subprocess, "run", lambda *_args, **_kwargs: SimpleNamespace(
        returncode=0, stdout=version_info("GLIBC_2.38"), stderr="",
    ))
    assert verifier.verify(tmp_path, (2, 38)) == (1, 2, 38)
    monkeypatch.setattr(verifier.subprocess, "run", lambda *_args, **_kwargs: SimpleNamespace(
        returncode=0, stdout=version_info("GLIBC_2.39"), stderr="",
    ))
    with pytest.raises(ValueError, match="高于目标 GLIBC_2.38"):
        verifier.verify(tmp_path, (2, 38))


def test_core_build_chain_retains_office_ocr_and_selftest_gate() -> None:
    portable = (ROOT / "packaging/uos/build-portable.sh").read_text(encoding="utf-8")
    native = (ROOT / "packaging/linux/build-native.sh").read_text(encoding="utf-8")
    spec = (ROOT / "packaging/uos/partyops.spec").read_text(encoding="utf-8")
    assert '"$RUNTIME_PROFILE" == core' in portable
    assert "requirements-core.txt" in portable and "tesseract-runtime.tar.gz" in portable
    assert "--only-file partyops" in portable
    assert "PARTYOPS_LOONG64_OFFICE_SHA256" in native
    assert "--package-self-test" in native
    assert native.index('cp -a "$OFFICE_RUNTIME"') < native.index('"$PKG/opt/partyops/partyops" --package-self-test')
    assert 'ai_excludes = ["onnxruntime", "tokenizers", "hf_xet", "hfxet"]' in spec


def _run_python_shared_library_preflight(
    base_prefix: Path, config: dict[str, object]
) -> int:
    portable = (ROOT / "packaging/uos/build-portable.sh").read_text(encoding="utf-8")
    marker = "if ! \"$PYTHON_BIN\" - <<'PY'\n"
    body = portable.split(marker, 1)[1].split("\nPY\nthen", 1)[0]
    body = body.replace("import sys\n", "").replace("import sysconfig\n", "")
    sysconfig = SimpleNamespace(get_config_var=lambda key: config.get(key))
    try:
        exec(body, {"sys": SimpleNamespace(base_prefix=str(base_prefix)), "sysconfig": sysconfig})
    except SystemExit as result:
        return int(result.code)
    return 0


def test_python_shared_library_preflight_checks_multiarch_and_legacy_layout(tmp_path: Path) -> None:
    library = "libpython3.12.so"
    multiarch = tmp_path / "usr/lib/loongarch64-linux-gnu"
    multiarch.mkdir(parents=True)
    (multiarch / library).write_bytes(b"shared")
    config = {"Py_ENABLE_SHARED": 1, "LDLIBRARY": library, "LIBDIR": str(multiarch)}
    assert _run_python_shared_library_preflight(tmp_path / "venv", config) == 0

    missing_config = {**config, "LIBDIR": str(tmp_path / "missing")}
    assert _run_python_shared_library_preflight(tmp_path / "venv", missing_config) == 1
    assert _run_python_shared_library_preflight(
        tmp_path / "venv", {**config, "Py_ENABLE_SHARED": 0}
    ) == 1

    legacy_prefix = tmp_path / "legacy-python"
    legacy_library = legacy_prefix / "lib" / library
    legacy_library.parent.mkdir(parents=True)
    legacy_library.write_bytes(b"shared")
    assert _run_python_shared_library_preflight(
        legacy_prefix, {**config, "LIBDIR": str(tmp_path / "missing")}
    ) == 0
