"""验证实际待合并函数的 Loong64 选包行为；不代表 Loong64 二进制已完成构建。"""
import ast
import hashlib
import importlib.util
import os
import stat
import tempfile
import types
import zipfile
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]


def load_functions(path, names, namespace):
    tree = ast.parse((ROOT / path).read_text(encoding="utf-8"))
    nodes = [node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name in names
             or isinstance(node, ast.Assign) and any(isinstance(target, ast.Name) and target.id in names for target in node.targets)]
    assert len(nodes) == len(names)
    # 只执行当前补丁目录内实际函数，隔离不属于架构选包测试的服务和数据库依赖。
    exec(compile(ast.Module(body=nodes, type_ignores=[]), str(ROOT / path), "exec"), namespace)  # noqa: S102
    return namespace


@pytest.fixture
def platform_info():
    spec = importlib.util.spec_from_file_location("app.loong_platform_contract", ROOT / "app/platform_info.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@pytest.mark.parametrize("machine", ["loong64", "loongarch64"])
def test_linux_alias_and_distribution_remain_precise(platform_info, monkeypatch, tmp_path, machine):
    monkeypatch.setattr(platform_info.platform, "machine", lambda: machine)
    monkeypatch.setattr(platform_info.sys, "platform", "linux")
    release = tmp_path / "os-release"
    release.write_text('ID=deepin\nVERSION_ID="25"\n', encoding="utf-8")
    result = platform_info.detect_platform_info(os_release_path=release)
    assert result["architecture"] == "loong64"
    assert result["distribution"] == "deepin"
    assert platform_info.update_platform_key(result) == "linux-deb"


@pytest.mark.parametrize("os_name,system,allowed", [("posix", "linux", True), ("posix", "darwin", False), ("nt", "win32", False)])
def test_loong_architecture_is_linux_only(platform_info, os_name, system, allowed):
    namespace = load_functions("app/update_executor.py", {"_architecture"}, {
        "normalize_architecture": platform_info.normalize_architecture,
        "platform": types.SimpleNamespace(machine=lambda: "loongarch64"),
        "os": types.SimpleNamespace(name=os_name), "sys": types.SimpleNamespace(platform=system)})
    if allowed:
        assert namespace["_architecture"]() == "loong64"
    else:
        with pytest.raises(RuntimeError, match="系统架构"):
            namespace["_architecture"]()


def routes():
    class Problem(Exception):
        pass
    return load_functions("app/routers/updates.py", {"V3_PLATFORM_ARTIFACTS", "V4_PLATFORM_ARTIFACTS", "_validate_v4_platform_artifacts"},
                          {"ProblemException": Problem})


def manifest(name="PartyOps_1.4.5-rc.6_linux_loong64.deb", platform="linux-deb"):
    return {"format_version": 4, "package_role": "platform-update", "target_platform": platform,
            "target_architecture": "loong64", "platform_artifacts": {platform: {"loong64": name}},
            "artifacts": {name: {"sha256": hashlib.sha256(b"loong64-payload").hexdigest(), "size": 15}}}


def test_v4_accepts_loong_deb_without_expanding_legacy_v3():
    namespace, value = routes(), manifest()
    assert "loong64" not in namespace["V3_PLATFORM_ARTIFACTS"]["linux-deb"]
    namespace["_validate_v4_platform_artifacts"](value, value["artifacts"])


@pytest.mark.parametrize("name,platform", [("PartyOps_1.4.5-rc.6_linux_amd64.deb", "linux-deb"), ("PartyOps-1.4.5.loongarch64.rpm", "linux-rpm")])
def test_wrong_architecture_or_unrequested_rpm_rejected(name, platform):
    namespace, value = routes(), manifest(name, platform)
    with pytest.raises(namespace["ProblemException"]):
        namespace["_validate_v4_platform_artifacts"](value, value["artifacts"])


def select_namespace(signature=True):
    return load_functions("app/update_executor.py", {"_select_artifact"}, {
        "Path": Path, "zipfile": zipfile, "stat": stat, "tempfile": tempfile, "hashlib": hashlib,
        "_verify_manifest_signature": lambda value: signature, "_assert_update_not_downgrade": lambda value: None,
        "_safe_member": lambda value: value, "_fsync": os.fsync, "_atomic_replace": os.replace})


def package(tmp_path):
    value = manifest()
    name = next(iter(value["artifacts"]))
    archive = tmp_path / "update.zip"
    with zipfile.ZipFile(archive, "w") as destination:
        destination.writestr(name, b"loong64-payload")
    value["artifacts"][name]["size"] = len(b"loong64-payload")
    return archive, value, tmp_path / name


def test_verified_loong_artifact_is_extracted_by_actual_executor(tmp_path):
    archive, value, target = package(tmp_path)
    result = select_namespace()["_select_artifact"](archive, value, "loong64", target, "linux-deb")
    assert result == target
    assert result.read_bytes() == b"loong64-payload"


@pytest.mark.parametrize("failure", ["signature", "hash", "missing_architecture"])
def test_rejected_update_preserves_existing_cache(tmp_path, failure):
    archive, value, target = package(tmp_path)
    target.write_bytes(b"previous-verified-cache")
    if failure == "hash":
        next(iter(value["artifacts"].values()))["sha256"] = "0" * 64
    elif failure == "missing_architecture":
        value["platform_artifacts"]["linux-deb"] = {"amd64": "PartyOps_1.4.5-rc.6_linux_amd64.deb"}
    namespace = select_namespace(signature=failure != "signature")
    with pytest.raises(RuntimeError):
        namespace["_select_artifact"](archive, value, "loong64", target, "linux-deb")
    assert target.read_bytes() == b"previous-verified-cache"
    assert not list(tmp_path.glob("*.verified"))
