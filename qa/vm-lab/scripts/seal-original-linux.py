"""等待原版安装器正常关机，卸载 ISO，再核对系统盘冷启动并封存基础快照。"""
import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, sha256, write_json
from identity import seal_baseline, wait_probe, wait_stopped
from lab import load_configuration
from providers import QemuLab


def seal(target, timeout):
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    if matrix["targets"][target]["os"] != "linux":
        raise RuntimeError("LINUX_ORIGINAL_INSTALL_REQUIRED")
    state = lab.state(target)
    if sha256(Path(state["base"])) != media[matrix["targets"][target]["media"]]["sha256"]:
        raise RuntimeError("ORIGINAL_INSTALL_MEDIA_CHANGED")
    record = {"target": target, "started_at": now(), "status": "pending",
              "runtime_environment_passed": False, "vm_uuid": state["uuid"], "media_sha256": state["source_sha256"]}
    receipt = lab.root / "reports" / target / "original-install-completed-20260906.json"
    if receipt.exists() and json.loads(receipt.read_text()).get("status") == "passed":
        raise RuntimeError("EXISTING_COMPLETED_INSTALL_RECEIPT_PRESERVED")
    write_json(receipt, record)
    try:
        if not state.get("installation_media_detached"):
            wait_stopped(lab, target, timeout)
            state = lab.state(target)
            state.update(installation_media_detached=True, seed=None)
            lab.save(target, state)
        accel = "tcg" if matrix["targets"][target]["arch"] == "aarch64" else "whpx"
        lab.start(target, accel, False)
        first = wait_probe(lab, target, timeout)
        if first.get("installed_package") is not False:
            raise RuntimeError("CLEAN_ORIGINAL_GUEST_REQUIRED")
        record["system_disk_identity"] = first
        write_json(receipt, record)
        binding = seal_baseline(lab, target, accel, timeout)
        record.update(status="passed", baseline=binding, completed_at=now())
    except Exception as exc:
        record.update(status="blocked", error=type(exc).__name__ + ": " + str(exc))
        raise
    finally:
        write_json(receipt, record)
    return record


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("target")
    parser.add_argument("--timeout", type=int, default=900)
    args = parser.parse_args()
    print(json.dumps(seal(args.target, args.timeout), ensure_ascii=False, indent=2))
