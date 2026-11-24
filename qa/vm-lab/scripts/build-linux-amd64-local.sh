#!/usr/bin/env bash
# 复用 glibc 2.17 兼容工具链。构建临时目录由调用者提供。
set -euo pipefail
ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../../.." && pwd)
BUILD=${PARTYOPS_COMPAT_BUILD_DIR:?请指定用于构建的 POSIX 工作目录 PARTYOPS_COMPAT_BUILD_DIR}
test "$(getconf GNU_LIBC_VERSION)" = 'glibc 2.17'
test "$(uname -m)" = x86_64
[[ "$BUILD" = /* && "$BUILD" != / ]]
umask 077
mkdir -p "$BUILD"
test "$(stat -c '%a' "$BUILD")" = 700
export PYTHON_BIN=${PYTHON_BIN:-python3.11}
export PARTYOPS_BUILD_ARCH=amd64
export PARTYOPS_BUILD_BASE="$BUILD/portable"
export PARTYOPS_NATIVE_BUILD_BASE="$BUILD/native"
export TMPDIR="$BUILD/tmp"
export PIP_CACHE_DIR="$BUILD/pip-cache"
export PARTYOPS_LINUX_FORMATTER_RUNTIME="${PARTYOPS_LINUX_FORMATTER_RUNTIME:-$ROOT/.release-gates/formatter-native-complete-linux-amd64}"
export PARTYOPS_OFFICE_RUNTIME="${PARTYOPS_OFFICE_RUNTIME:-$ROOT/vendor/linux/libreoffice-headless-amd64}"
export PARTYOPS_FORMATTER_EVIDENCE_DIR=${PARTYOPS_FORMATTER_EVIDENCE_DIR:?请指定已核验排版证据目录}
mkdir -p "$TMPDIR" "$PARTYOPS_BUILD_BASE" "$PARTYOPS_NATIVE_BUILD_BASE"
cd "$ROOT"
bash packaging/uos/build-portable.sh
bash packaging/linux/build-native.sh deb
bash packaging/linux/build-native.sh rpm
