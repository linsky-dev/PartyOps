#!/usr/bin/env bash
# 只获取明确选择的 Mac 固件。Linux 阶段完成后仍需 Intel macOS Helper 修改 ramdisk。
set -euo pipefail
export PATH=/mnt/e/codex/PartyOps/.partyops-vm-lab/tools/ipsw-3.1.713:/usr/local/bin:/usr/bin:/bin
root=/mnt/e/codex/PartyOps/.partyops-vm-lab/tools/darwin-vm
cd "$root"
test "$(git rev-parse HEAD)" = 44f374574cb3be1e62f8d85517636c8bc7ff98a9
export DEVNAME=Macmini9,1
export URL=https://updates.cdn-apple.com/2026SummerFCS/fullrestores/140-65618/10445B26-DE2C-43EC-9149-0A831602E74B/UniversalMac_26.6_25G72_Restore.ipsw
chmod +x dt_fixup.py build_tc.py
bash get_files.sh
