#!/usr/bin/env bash
# 仅编译用户确认的本地研究路线；不下载或运行上游默认 iPhone 固件。
set -euo pipefail
source_dir=/mnt/e/codex/PartyOps/.partyops-vm-lab/tools/darwin-vm
expected_darwin=44f374574cb3be1e62f8d85517636c8bc7ff98a9
expected_qemu=6c3ca665bab9a08399f4681e4ada43bfcfb7d493
test "$(git -C "$source_dir" rev-parse HEAD)" = "$expected_darwin"
test "$(git -C "$source_dir/qemu-sptm" rev-parse HEAD)" = "$expected_qemu"
test "$(uname -m)" = x86_64
cd "$source_dir/qemu-sptm"
mkdir -p build
cd build
../configure --target-list=aarch64-softmmu --disable-docs --disable-werror
make -j4
./qemu-system-aarch64 --version
./qemu-system-aarch64 -machine help | grep darwin
sha256sum ./qemu-system-aarch64
