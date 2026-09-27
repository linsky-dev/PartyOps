#!/usr/bin/env bash
# 创建可由 VMware 和 mtools 交换的 FAT32 虚拟磁盘，不挂载宿主物理磁盘。
set -euo pipefail
root=/mnt/e/codex/PartyOps/.partyops-vm-lab
destination="$root/tools/macos-arm64-helper-20260906"
test ! -e "$destination/helper.raw"
test "$(df --output=avail -B1 "$root" | tail -n 1)" -gt 34359738368
mkdir -p "$destination"
truncate -s 512M "$destination/helper.raw"
mkfs.fat -F 32 -n PARTYHELPER "$destination/helper.raw"
mcopy -i "$destination/helper.raw" "$root/tools/darwin-vm/firmware/ramdisk.dmg" ::/ramdisk.dmg
mcopy -i "$destination/helper.raw" "$root/tools/darwin-vm/launchdaemons/com.jprx.bash.plist" ::/com.jprx.bash.plist
mcopy -i "$destination/helper.raw" /mnt/e/codex/PartyOps/.publish-github/qa/vm-lab/guest/macos-arm64-helper.sh ::/helper.sh
cd "$root/tools/darwin-vm/firmware"
sha256sum ramdisk.dmg > "$destination/INPUT.SHA256"
cd "$root/tools/darwin-vm/launchdaemons"
sha256sum com.jprx.bash.plist >> "$destination/INPUT.SHA256"
mcopy -i "$destination/helper.raw" "$destination/INPUT.SHA256" ::/INPUT.SHA256
mdir -i "$destination/helper.raw" ::/
