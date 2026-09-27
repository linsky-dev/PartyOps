#!/bin/bash
# 在现有 Intel macOS Guest 中准备 Apple ARM64 ramdisk；输出保存在专用交换盘。
set -euo pipefail
test "$(uname -s)" = Darwin
test "$(uname -m)" = x86_64
cd "$(dirname "$0")"
shasum -a 256 -c INPUT.SHA256
destination="$PWD/output"
if test -e "$destination"; then
  echo '已有输出，先回收并核验，禁止覆盖。' >&2
  exit 2
fi
mkdir "$destination"
cp ramdisk.dmg "$destination/ramdisk.dmg"
mountpoint="$(mktemp -d)"
mounted=false
cleanup() {
  if $mounted; then hdiutil detach "$mountpoint"; fi
  rmdir "$mountpoint"
}
trap cleanup EXIT
hdiutil attach -owners off -mountpoint "$mountpoint" "$destination/ramdisk.dmg"
mounted=true
test -d "$mountpoint/System/Library/LaunchDaemons"
test ! -e "$mountpoint/System/Library/LaunchDaemons.old"
mv "$mountpoint/System/Library/LaunchDaemons" "$mountpoint/System/Library/LaunchDaemons.old"
mkdir "$mountpoint/System/Library/LaunchDaemons"
cp com.jprx.bash.plist "$mountpoint/System/Library/LaunchDaemons/"
# 与固定上游实现一致：读取镜像内 Mach-O 签名，不签名或修改 Guest 系统。
find "$mountpoint" -type f -exec codesign -d -vvv {} \; > "$destination/codesign.log" 2>&1
sed -n 's/^CDHash=//p' "$destination/codesign.log" | sort -u > "$destination/all_hashes"
test -s "$destination/all_hashes"
hdiutil detach "$mountpoint"
mounted=false
digest="$(shasum -a 256 "$destination/ramdisk.dmg" | cut -d ' ' -f 1)"
hashes_digest="$(shasum -a 256 "$destination/all_hashes" | cut -d ' ' -f 1)"
guest_uuid="$(ioreg -rd1 -c IOPlatformExpertDevice | sed -n 's/.*"IOPlatformUUID" = "\([^"]*\)".*/\1/p')"
printf '{"os":"Darwin","arch":"x86_64","version":"%s","build":"%s","hardware_uuid":"%s","patched_ramdisk_sha256":"%s","cdhash_list_sha256":"%s","generated_at":"%s"}\n' \
  "$(sw_vers -productVersion)" "$(sw_vers -buildVersion)" "$guest_uuid" "$digest" "$hashes_digest" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$destination/helper-result.json"
sync
echo 'Intel Helper 已生成修改后的 ramdisk 与 CDHash 列表；关机回收后由固定上游 build_tc.py 生成 trustcache。'
