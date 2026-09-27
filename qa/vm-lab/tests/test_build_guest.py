"""构建克隆必须独立于验收矩阵，运行中的基础盘不可导出。"""
import importlib.util
from pathlib import Path
from types import SimpleNamespace

import pytest
from lab import load_configuration


def controller():
    spec = importlib.util.spec_from_file_location("build_guest", Path(__file__).resolve().parents[1] / "scripts/linux-build-guest.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_build_registration_does_not_change_required_environments():
    module = controller()
    lab, target = module.environment("uos-deb-arm64")
    assert target == "build-uos-deb-arm64"
    assert lab.matrix["targets"][target]["ssh_port"] != lab.matrix["targets"]["uos-deb-arm64"]["ssh_port"]
    matrix, _ = load_configuration()
    assert target not in matrix["targets"] and len(lab.matrix["targets"]) == len(matrix["targets"]) + 1
    assert all(target not in package["required_targets"] for package in matrix["packages"].values())


def test_running_original_disk_cannot_be_cloned():
    module = controller()
    lab = SimpleNamespace(state=lambda target: {}, live=lambda state: True)
    with pytest.raises(RuntimeError, match="BUILD_CLONE_REQUIRES_STOPPED_SOURCE"):
        module.create(lab, "source", "builder")
