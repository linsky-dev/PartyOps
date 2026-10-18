"""仅恢复已确认未进入 Guest 的超长停止命令；不重发安装器。"""
from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path
from types import SimpleNamespace

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(ROOT / "qa/vm-lab"))


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    transition = load("runtime_transition_recovery", HERE / "runtime-transition.py")
    module = load("runtime_recovery_transport", ROOT / "qa/vm-lab/scripts/exercise-windows-business.py")
    contract = transition.contract
    attempt_file = HERE / "runtime-stop-old.attempt.json"
    old = contract.read(attempt_file)
    failed = contract.read(HERE / "runtime-stop-old.result.json")
    transition.require(failed.get("status") == "failed-inspect-without-retry"
                       and failed.get("attempt_sha256") == contract.digest(attempt_file)
                       and old.get("action") == "stop-old"
                       and old.get("allow_qa_process_termination") is True, "EXACT_FAILED_STOP_REQUIRED")
    recovery = HERE / "runtime-stop-old-transport-recovery.attempt.json"
    transition.require(not recovery.exists(), "RECOVERY_ALREADY_ATTEMPTED")
    inputs, boot = transition.preserved_inputs()
    transition.require(inputs == old["inputs"], "BASELINE_CHANGED")
    matrix, media = module.load_configuration()
    matrix["defaults"].update(primary_root=str(transition.LAB_ROOT), fallback_root=str(transition.LAB_ROOT))
    lab = module.QemuLab(matrix, transition.LAB_ROOT, media)
    contract.bind_guest(lab, module.qmp)
    client = module.windows.connect(lab, matrix["targets"]["win10-x64"])
    try:
        runner = object.__new__(module.WindowsRun)
        runner.client, runner.args = client, SimpleNamespace(uuid=contract.UUID)
        desktop = runner.desktop("Inspect")
        inventory = json.loads(module.windows.execute(client, transition.INVENTORY))
        actual = contract.probe(client, module.windows.execute)
        target = transition.old_target(actual, inventory, desktop)
        transition.require(target == old["target"] and actual["boot_id"] == boot["boot_id"], "ORIGINAL_PROCESS_CHANGED")
        contract.exclusive(recovery, {"time": contract.now(), "original_attempt_sha256": contract.digest(attempt_file),
            "reason": "SSH 命令行长度限制；只读确认原 PID、创建时间、路径、用户和 HTTP 所有者未变", "target": target})
        result = json.loads(transition.execute_stop_script(client, module.windows.execute,
                            transition.controlled_stop_script(target, True)))
        contract.bind_guest(lab, module.qmp)
        transition.require(transition.preserved_inputs()[0] == inputs, "BASELINE_CHANGED_AFTER_STOP")
        contract.exclusive(HERE / "runtime-stop-old-transport-recovery.result.json",
            {"status": "completed", "time": contract.now(), "attempt_sha256": contract.digest(recovery), "result": result})
        print("原 QA 进程已受控停止；原失败回执保留，未执行安装。")
    finally:
        client.close()


if __name__ == "__main__":
    main()
