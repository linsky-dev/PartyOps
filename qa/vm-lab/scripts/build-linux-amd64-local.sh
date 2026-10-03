#!/usr/bin/env bash
# 复用既有 glibc 2.17 本机构建链；POSIX 暂存树通过 metadata 挂载写入 E 盘。
set -euo pipefail
ROOT=/mnt/e/codex/PartyOps/.publish-github
BUILD=/mnt/partyops-build/codex/PartyOps/.partyops-vm-lab/builds/native-amd64
test "$(getconf GNU_LIBC_VERSION)" = 'glibc 2.17'
test "$(uname -m)" = x86_64
mkdir -p /mnt/partyops-build
# WSL 发行版闲置退出后会丢失临时挂载，须在同一次构建进程内建立。
mountpoint -q /mnt/partyops-build || mount -t drvfs E: /mnt/partyops-build -o metadata
mkdir -p "$BUILD"
chmod 700 "$BUILD"
test "$(stat -c '%a' "$BUILD")" = 700
export PYTHON_BIN=/opt/partyops-python/cpython-3.11.15-linux-x86_64-gnu/bin/python3.11
export PARTYOPS_BUILD_ARCH=amd64
export PARTYOPS_BUILD_BASE="$BUILD/portable"
export PARTYOPS_NATIVE_BUILD_BASE="$BUILD/native"
export TMPDIR="$BUILD/tmp"
export PIP_CACHE_DIR="$BUILD/pip-cache"
export PARTYOPS_LINUX_FORMATTER_RUNTIME="$ROOT/.release-gates/formatter-native-complete-linux-amd64"
export PARTYOPS_OFFICE_RUNTIME=/mnt/e/codex/PartyOps/vendor/linux/libreoffice-headless-amd64
export PARTYOPS_FORMATTER_EVIDENCE_DIR=/mnt/d/PartyOps-VM-Lab/reports/formatter-evidence-openeuler-amd64-20260905
mkdir -p "$TMPDIR" "$PARTYOPS_BUILD_BASE" "$PARTYOPS_NATIVE_BUILD_BASE"
cd "$ROOT"
bash packaging/uos/build-portable.sh
bash packaging/linux/build-native.sh deb
bash packaging/linux/build-native.sh rpm
