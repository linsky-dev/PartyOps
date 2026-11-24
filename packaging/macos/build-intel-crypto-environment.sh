#!/bin/bash
set -euo pipefail

if (($# != 3)); then
  printf '%s\n' '用法：build-intel-crypto-environment.sh <repository-root> <build-root> <venv-path>' >&2
  exit 2
fi

ROOT="$(cd "$1" && pwd -P)"
BUILD_ROOT="$(cd "$2" && pwd -P)"
VENV="$3"
PYTHON="${PARTYOPS_MACOS_PYTHON:-python3.11}"
UV="${PARTYOPS_UV:-uv}"

if [[ "$(uname -s)" != 'Darwin' || "$(uname -m)" != 'x86_64' ]]; then
  printf '%s\n' '[MACOS_INTEL_CRYPTO_HOST_REQUIRED] 固定 OpenSSL 源码构建只在原生 macOS Intel 上运行。' >&2
  exit 2
fi
if [[ "${MACOSX_DEPLOYMENT_TARGET:-}" != '11.0' ]]; then
  printf '%s\n' '[MACOS_INTEL_CRYPTO_DEPLOYMENT_TARGET_INVALID] Intel 密码运行时必须使用 macOS 11.0 基线。' >&2
  exit 2
fi
for command in "$PYTHON" "$UV" curl gzip tar make shasum awk; do
  if ! command -v "$command" >/dev/null 2>&1; then
    printf '[MACOS_INTEL_CRYPTO_TOOL_MISSING] 缺少构建工具：%s\n' "$command" >&2
    exit 2
  fi
done
if [[ -e "$VENV" ]]; then
  printf '%s\n' '[MACOS_INTEL_CRYPTO_VENV_EXISTS] 拒绝复用已有虚拟环境。' >&2
  exit 2
fi

OPENSSL_VERSION='3.5.7'
OPENSSL_SOURCE_NAME="openssl-$OPENSSL_VERSION.tar.gz"
OPENSSL_SOURCE_URL="https://github.com/openssl/openssl/releases/download/openssl-$OPENSSL_VERSION/$OPENSSL_SOURCE_NAME"
OPENSSL_SOURCE_SHA256='a8c0d28a529ca480f9f36cf5792e2cd21984552a3c8e4aa11a24aa31aeac98e8'
OPENSSL_SOURCE_DIR="$BUILD_ROOT/openssl-source"
OPENSSL_SOURCE="$OPENSSL_SOURCE_DIR/$OPENSSL_SOURCE_NAME"
OPENSSL_PREFIX="$BUILD_ROOT/openssl-static"

# 固定静态闭包，避免 Runner 的 Homebrew/setup-python OpenSSL 与冻结包收集内容不一致。
"$PYTHON" -m venv "$VENV"
/bin/mkdir -p "$OPENSSL_SOURCE_DIR"
curl --fail --location --silent --show-error --retry 1 \
  "$OPENSSL_SOURCE_URL" --output "$OPENSSL_SOURCE.download"
OPENSSL_ACTUAL_SHA256="$(shasum -a 256 "$OPENSSL_SOURCE.download" | awk '{print $1}')"
if [[ "$OPENSSL_ACTUAL_SHA256" != "$OPENSSL_SOURCE_SHA256" ]]; then
  printf '[MACOS_OPENSSL_SOURCE_HASH_MISMATCH] 期望 %s，实际 %s。\n' \
    "$OPENSSL_SOURCE_SHA256" "$OPENSSL_ACTUAL_SHA256" >&2
  exit 2
fi
/bin/mv "$OPENSSL_SOURCE.download" "$OPENSSL_SOURCE"
gzip -dc "$OPENSSL_SOURCE" | "$PYTHON" \
  "$ROOT/scripts/validate-portable-tar.py" \
  --expected-root "openssl-$OPENSSL_VERSION" \
  --max-members 100000 --max-bytes 1073741824 --allow-implicit-root
/bin/mkdir -p "$OPENSSL_SOURCE_DIR/unpacked"
tar -xzf "$OPENSSL_SOURCE" -C "$OPENSSL_SOURCE_DIR/unpacked"
(
  cd "$OPENSSL_SOURCE_DIR/unpacked/openssl-$OPENSSL_VERSION"
  ./Configure darwin64-x86_64-cc no-shared no-tests \
    -mmacosx-version-min="$MACOSX_DEPLOYMENT_TARGET" \
    --prefix="$OPENSSL_PREFIX" --openssldir="$OPENSSL_PREFIX/ssl"
  make -j"${PARTYOPS_BUILD_JOBS:-2}"
  make install_sw
)

OPENSSL_STATIC=1 OPENSSL_DIR="$OPENSSL_PREFIX" \
  UV_PROJECT_ENVIRONMENT="$VENV" "$UV" sync --project "$ROOT/backend" --python "$PYTHON" \
  --frozen --no-dev --no-cache --no-binary-package cryptography
CRYPTOGRAPHY_OPENSSL_VERSION="$(
  "$VENV/bin/python" -c \
    'from cryptography.hazmat.backends.openssl.backend import backend; print(backend.openssl_version_text())'
)"
if [[ "$CRYPTOGRAPHY_OPENSSL_VERSION" != "OpenSSL $OPENSSL_VERSION "* ]]; then
  printf '[MACOS_CRYPTOGRAPHY_OPENSSL_MISMATCH] 期望 OpenSSL %s，实际 %s。\n' \
    "$OPENSSL_VERSION" "$CRYPTOGRAPHY_OPENSSL_VERSION" >&2
  exit 2
fi
printf '[MACOS_INTEL_CRYPTO_BUILD_PASS] %s\n' "$CRYPTOGRAPHY_OPENSSL_VERSION"
