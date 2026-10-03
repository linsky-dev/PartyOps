"""ARM Windows 组件准备边界；仅 mock 进程/磁盘与 Bash 解析，不运行 WSL 或 Guest。"""
from __future__ import annotations

import importlib.util
import json
import shutil
import subprocess
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from evidence import sha256, write_json

SCRIPT = Path(__file__).parents[1] / "scripts/prepare-windows-arm64-stack.py"
SPEC = importlib.util.spec_from_file_location("windows_arm_stack_preparation", SCRIPT)
stack = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(stack)


@pytest.fixture
def config(tmp_path, monkeypatch):
    value = json.loads(stack.CONFIG.read_text(encoding="utf-8"))
    value["windows_root"] = str(tmp_path / "组件 目录")
    value["wsl_root"] = "/mnt/e/test-stack"
    temporary_config = tmp_path / "config.json"
    write_json(temporary_config, value)
    monkeypatch.setattr(stack, "CONFIG", temporary_config)
    monkeypatch.setattr(stack.shutil, "disk_usage", lambda drive: SimpleNamespace(free=100 * 1024**3))
    monkeypatch.setattr(stack.psutil, "virtual_memory", lambda: SimpleNamespace(available=20 * 1024**3))
    monkeypatch.setattr(stack.psutil, "process_iter", lambda attrs: [])
    return value


@pytest.mark.parametrize("drive,free", [("D:/", 19), ("E:/", 31)])
def test_download_reserves_fail_before_any_wsl(config, monkeypatch, drive, free):
    monkeypatch.setattr(stack.shutil, "disk_usage", lambda name: SimpleNamespace(free=(free if name == drive else 100) * 1024**3))
    no_wsl = Mock(side_effect=AssertionError("下载不得启动 WSL"))
    monkeypatch.setattr(stack, "wsl", no_wsl)
    with pytest.raises(RuntimeError, match="STORAGE_RESERVE"):
        stack.fetch_component(config, "qemu")
    no_wsl.assert_not_called()


@pytest.mark.parametrize("available", [7, 11])
def test_build_memory_gate_precedes_wsl_inspection(config, monkeypatch, available):
    monkeypatch.setattr(stack.psutil, "virtual_memory", lambda: SimpleNamespace(available=available * 1024**3))
    inspection = Mock(side_effect=AssertionError("低内存不得唤醒 WSL"))
    monkeypatch.setattr(stack, "inspect", inspection)
    with pytest.raises(RuntimeError, match="BUILD_MEMORY_RESERVE"):
        stack.build_component(config, "qemu")
    inspection.assert_not_called()


@pytest.mark.parametrize("process_name", ["qemu-system-x86_64.exe", "QEMU-SYSTEM-AARCH64.EXE", "vmware-vmx.exe"])
def test_running_guest_blocks_build(config, monkeypatch, process_name):
    monkeypatch.setattr(stack.psutil, "process_iter", lambda attrs: [SimpleNamespace(info={"name": process_name})])
    with pytest.raises(RuntimeError, match="REQUIRES_STOPPED_GUESTS"):
        stack.build_component(config, "edk2")


def fake_git(config, *, changed_origin=False, changed_tree=False, changed_head=False):
    calls = []
    def run(directory, arguments, **kwargs):
        calls.append(arguments)
        outputs = {"remote": "https://invalid.example/replaced.git" if changed_origin else config["sources"][directory.name]["url"],
                   "status": " M edited.c" if changed_tree else "",
                   "rev-parse": "b" * 40 if changed_head else config["sources"][directory.name]["commit"]}
        return SimpleNamespace(stdout=outputs.get(arguments[0], ""), returncode=0)
    return run, calls


def test_download_and_repeat_use_windows_git_and_fixed_commit(config, monkeypatch):
    mock_git, calls = fake_git(config)
    monkeypatch.setattr(stack, "git", mock_git)
    monkeypatch.setattr(stack, "wsl", Mock(side_effect=AssertionError("不得启动 WSL")))
    first = stack.fetch_component(config, "qemu")
    second = stack.fetch_component(config, "qemu")
    assert first["verified_commit"] == second["verified_commit"] == config["sources"]["qemu"]["commit"]
    assert ["checkout", "--detach", "-q", first["verified_commit"]] in calls
    assert first["build_status"] == "not_run"
    assert first["submodules_status"] == "not_fetched"


@pytest.mark.parametrize("option,reason", [("changed_origin", "SOURCE_ORIGIN_CHANGED"), ("changed_tree", "SOURCE_WORKTREE_CHANGED"), ("changed_head", "SOURCE_COMMIT_MISMATCH")])
def test_download_rejects_changed_source(config, monkeypatch, option, reason):
    (Path(config["windows_root"]) / "sources/qemu/.git").mkdir(parents=True)
    mock_git, _ = fake_git(config, **{option: True})
    monkeypatch.setattr(stack, "git", mock_git)
    with pytest.raises(RuntimeError, match=reason):
        stack.fetch_component(config, "qemu")
    assert not (Path(config["windows_root"]) / "state/sources/qemu.json").exists()


def test_source_pin_is_not_resolved_from_moving_branch(config):
    config["sources"]["qemu"]["commit"] = "main"
    with pytest.raises(RuntimeError, match="SOURCE_PIN_INVALID"):
        stack.fetch_component(config, "qemu")


def test_nonempty_unregistered_source_directory_is_preserved(config):
    directory = Path(config["windows_root"]) / "sources/qemu"
    directory.mkdir(parents=True)
    marker = directory / "原有文件.txt"
    marker.write_text("保留", encoding="utf-8")
    with pytest.raises(RuntimeError, match="DIRECTORY_NOT_EMPTY"):
        stack.fetch_component(config, "qemu")
    assert marker.read_text(encoding="utf-8") == "保留"


def test_microsoft_certificate_hash_cannot_be_substituted(config, monkeypatch):
    directory = Path(config["windows_root"]) / "sources/secureboot_objects"
    for cert in config["certificates"]:
        path = directory / cert["path"]
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"wrong-public-certificate")
    (directory / ".git").mkdir()
    mock_git, _ = fake_git(config)
    monkeypatch.setattr(stack, "git", mock_git)
    with pytest.raises(RuntimeError, match="CERTIFICATE_HASH_MISMATCH"):
        stack.fetch_component(config, "secureboot_objects")


def prepare_build(config, monkeypatch):
    monkeypatch.setattr(stack, "inspect", lambda cfg: {"missing_dependencies": []})
    commit = config["sources"]["qemu"]["commit"]
    write_json(Path(config["windows_root"]) / "state/sources/qemu.json", {"verified_commit": commit})
    def readonly_wsl(cfg, arguments, **kwargs):
        if "rev-parse" in arguments:
            return commit
        if "diff" in arguments:
            assert arguments[-1] == "HEAD"
            return ""
        return "[]"
    monkeypatch.setattr(stack, "wsl", readonly_wsl)


def test_missing_dependencies_block_before_compilation(config, monkeypatch):
    monkeypatch.setattr(stack, "inspect", lambda cfg: {"missing_dependencies": ["gcc-aarch64-linux-gnu"]})
    compiler = Mock(side_effect=AssertionError("不能开始编译"))
    monkeypatch.setattr(stack.subprocess, "run", compiler)
    with pytest.raises(RuntimeError, match="DEPENDENCIES_MISSING"):
        stack.build_component(config, "edk2")
    compiler.assert_not_called()


def test_build_timeout_is_persisted_and_is_never_guest_pass(config, monkeypatch):
    prepare_build(config, monkeypatch)
    monkeypatch.setattr(stack.subprocess, "run", Mock(side_effect=subprocess.TimeoutExpired("build", 7200)))
    with pytest.raises(subprocess.TimeoutExpired):
        stack.build_component(config, "qemu")
    result = json.loads((Path(config["windows_root"]) / "state/builds/qemu/result.json").read_text())
    assert result["exit_code"] is None
    assert result["error"] == "TimeoutExpired"
    assert result["runtime_environment_passed"] is False
    assert result["outputs"] == []


def test_successful_process_without_expected_binaries_is_failure(config, monkeypatch):
    prepare_build(config, monkeypatch)
    monkeypatch.setattr(stack.subprocess, "run", Mock(return_value=SimpleNamespace(returncode=0)))
    with pytest.raises(RuntimeError, match="COMPONENT_BUILD_FAILED"):
        stack.build_component(config, "qemu")
    result = json.loads((Path(config["windows_root"]) / "state/builds/qemu/result.json").read_text())
    assert result["runtime_environment_passed"] is False
    assert result["outputs"] == []


def test_resume_refuses_changed_context(config, monkeypatch):
    prepare_build(config, monkeypatch)
    write_json(Path(config["windows_root"]) / "state/builds/qemu/context.json", {"context_sha256": "old"})
    with pytest.raises(RuntimeError, match="BUILD_CONTEXT_CHANGED"):
        stack.build_component(config, "qemu")


def test_git_invocation_does_not_modify_global_config_or_use_credentials(tmp_path, monkeypatch):
    run = Mock(return_value=SimpleNamespace(returncode=0, stdout="", stderr=""))
    monkeypatch.setattr(stack.subprocess, "run", run)
    stack.git(tmp_path, ["fetch", "--depth", "1", "origin", "a" * 40])
    arguments = run.call_args.args[0]
    assert "credential.helper=" in arguments
    assert "core.autocrlf=false" in arguments
    assert "--global" not in arguments
    assert run.call_args.kwargs["env"]["GIT_TERMINAL_PROMPT"] == "0"


def test_build_script_parses_in_windows_git_bash_without_starting_wsl():
    git = shutil.which("git")
    bash = Path(git).parents[1] / "bin/bash.exe" if git else Path("missing-git-bash")
    if not bash.is_file():
        pytest.skip("当前控制器未提供 Git Bash 解析器")
    result = subprocess.run([str(bash), "-n", stack.BUILD_SCRIPT.as_posix()], capture_output=True, text=True, timeout=30, check=False)
    assert result.returncode == 0, result.stderr
    assert sha256(stack.BUILD_SCRIPT)
