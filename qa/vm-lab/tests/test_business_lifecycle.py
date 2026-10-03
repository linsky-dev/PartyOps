"""业务驱动器的关键反例；合成证据仅验证控制器，不计入 Guest 通过。"""
import importlib.util
from pathlib import Path

import pytest


def load_guest():
    path = Path(__file__).resolve().parents[1] / "guest/linux-business-lifecycle.py"
    spec = importlib.util.spec_from_file_location("business_lifecycle", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_process_restart_cannot_satisfy_guest_reboot():
    module = load_guest()
    run = object.__new__(module.GuestRun)
    run.state = {"business_verified": True, "boot_id_before": "same-kernel"}
    run.boot = "same-kernel"
    run.desktop_start = lambda: pytest.fail("同 boot ID 时不得继续启动和追认")
    with pytest.raises(RuntimeError, match="PROCESS_RESTART_IS_NOT_GUEST_REBOOT"):
        run.after_reboot()


@pytest.mark.parametrize("fault", ["summary", "task", "mention", "duplicate"])
def test_collaboration_rejects_missing_cross_account_results(fault):
    module = load_guest()
    summary = {"collaborating": 1, "reviewing": 1, "step_assigned": 1}
    tasks = {"items": [{"id": "expected"}]}
    notifications = [{"entity_id": "expected", "notification_type": "mention"}]
    if fault == "summary":
        summary["reviewing"] = 0
    elif fault == "task":
        tasks["items"] = [{"id": "unrelated"}]
    elif fault == "mention":
        notifications[0]["notification_type"] = "other"
    else:
        notifications *= 2
    with pytest.raises(RuntimeError, match="COLLABORATION_"):
        module.GuestRun.validate_collaboration_visibility(summary, tasks, notifications, "expected")


def test_attachment_corruption_blocks_persistence_even_with_same_account_and_task():
    module = load_guest()
    run = object.__new__(module.GuestRun)
    run.state = {"user_id": "user", "task_id": "task", "task_title": "事项",
                 "attachment_id": "attachment", "attachment_sha256": module.hashlib.sha256(b"original").hexdigest()}
    run.login = lambda: {"id": "user"}
    run.request = lambda path: {"id": "task", "title": "事项"} if "/tasks/" in path else b"changed"
    with pytest.raises(RuntimeError, match="ATTACHMENT_NOT_PRESERVED"):
        run.check_business_data()


def test_reboot_with_replaced_account_is_rejected_before_attachment_read():
    module = load_guest()
    run = object.__new__(module.GuestRun)
    run.state = {"user_id": "original"}
    run.login = lambda: {"id": "replacement"}
    run.request = lambda path: pytest.fail("账号身份已变化时不得继续")
    with pytest.raises(RuntimeError, match="ACCOUNT_NOT_PRESERVED"):
        run.check_business_data()


@pytest.mark.parametrize("outside", [True, False])
def test_uninstall_data_cleanup_rejects_other_directory_before_mutation(tmp_path, outside):
    module = load_guest()
    run = object.__new__(module.GuestRun)
    run.work = tmp_path / "owned-run"
    run.work.mkdir()
    data = (tmp_path if outside else run.work) / ("中文 空格业务数据" if outside else "real-user-data")
    data.mkdir()
    (data / "keep.txt").write_text("保留", encoding="utf-8")
    run.state = {"data_dir": str(data)}
    run.uninstall_keep = lambda: pytest.fail("错误清除范围时不得先卸载")
    with pytest.raises(RuntimeError, match="TEST_DATA_CLEANUP_OUTSIDE_OWNED_WORK"):
        run.uninstall_remove_test_data()
    assert (data / "keep.txt").read_text(encoding="utf-8") == "保留"
