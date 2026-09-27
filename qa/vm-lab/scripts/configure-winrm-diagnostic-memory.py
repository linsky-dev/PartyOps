"""经现有加密 WinRM 通道读写专用 Win7 Guest 的单个 Shell 内存配额。"""
from __future__ import annotations

import argparse
import sys
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
import winrm_memory
from evidence import now, write_json
from lab import load_configuration
from providers import QemuLab
from windows_remote import session

EXPECTED_UUID = "406ef8ee-e342-405f-933f-7ddce0a55f1b"
ROOT = Path("D:/PartyOps-VM-Lab/reports/win7-x64/dotnet-extraction-20260908")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--set-mib", type=int, choices=(150, 512))
    parser.add_argument("--expect-mib", type=int, choices=(150, 512), default=150)
    parser.add_argument("--require-mib", type=int, choices=(512,),
                        help="验收入口只读检查；低于要求即保留阻断，不自动更改 Guest 配额")
    args = parser.parse_args()
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    state = lab.state("win7-x64")
    if state["uuid"] != EXPECTED_UUID or not lab.live(state):
        raise RuntimeError("WINRS_REQUIRES_REGISTERED_RUNNING_WIN7")
    system, observation = winrm_memory.inspect_win7_context(lab, "win7-x64", state)
    client = session(lab, "win7-x64", 60)
    suffix = str(args.set_mib) if args.set_mib else ("preflight-" + str(args.require_mib) if args.require_mib else "read")
    stem = "winrm-quota-" + suffix + "-" + uuid.uuid4().hex[:12]

    def intent(value):
        write_json(ROOT / (stem + "-intent.json"), {"generated_at": now(), "uuid": EXPECTED_UUID, **value})

    result = winrm_memory.configure(client.protocol, args.set_mib, args.expect_mib, before_write=intent)
    result.update(generated_at=now(), target="win7-x64", uuid=EXPECTED_UUID,
                  scope="dedicated-lab-transport-only", runtime_environment_passed=False,
                  guest_identity=system, account_and_processes=observation)
    if args.require_mib:
        result["minimum_required_mib"] = args.require_mib
        result["transport_memory_preflight_passed"] = result["current_mib"] == args.require_mib
    name = stem + ".json"
    write_json(ROOT / name, result)
    print(str(ROOT / name))
    if args.require_mib and not result["transport_memory_preflight_passed"]:
        raise RuntimeError("WINRM_LAB_SHELL_MEMORY_QUOTA_TOO_SMALL")


if __name__ == "__main__":
    main()
