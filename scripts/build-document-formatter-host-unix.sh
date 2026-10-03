#!/usr/bin/env bash
set -euo pipefail
umask 077

# 将 Windows 上由锁定源码构建的 AnyCPU IL，在目标 Linux/macOS 本机封装为
# 单一原生宿主。mkbundle 只提供运行容器；所有六类功能仍执行原项目的
# StandaloneBatchProcessor，脚本和桥接层都不实现第二套排版规则。

MANAGED_RUNTIME=''
OUTPUT=''
PLATFORM=''
ARCHITECTURE=''
while (($#)); do
  case "$1" in
    --managed-runtime) MANAGED_RUNTIME="${2:-}"; shift 2 ;;
    --output) OUTPUT="${2:-}"; shift 2 ;;
    --platform) PLATFORM="${2:-}"; shift 2 ;;
    --architecture) ARCHITECTURE="${2:-}"; shift 2 ;;
    *) printf '未知参数：%s\n' "$1" >&2; exit 2 ;;
  esac
done

if [[ -z "$MANAGED_RUNTIME" || -z "$OUTPUT" ]]; then
  printf '%s\n' '用法：build-document-formatter-host-unix.sh --managed-runtime <AnyCPU目录> --output <空目录> [--platform linux|macos] [--architecture amd64|arm64|x86_64]' >&2
  exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SOURCE="$ROOT/vendor/document-formatter-source"
PYTHON_BIN="${PYTHON_BIN:-$(command -v python3.11 || command -v python3 || true)}"
[[ -n "$PYTHON_BIN" ]] || { printf '%s\n' '[FORMATTER_NATIVE_PYTHON_MISSING] 缺少 Python 3。' >&2; exit 2; }
for command in mkbundle mono file strings; do
  command -v "$command" >/dev/null 2>&1 || {
    printf '[FORMATTER_NATIVE_BUILD_TOOL_MISSING] 缺少构建工具：%s\n' "$command" >&2
    exit 2
  }
done
MONO_CONFIG_ROOT="${PARTYOPS_MONO_CONFIG_ROOT:-}"
if [[ -z "$MONO_CONFIG_ROOT" ]] && command -v pkg-config >/dev/null 2>&1; then
  MONO_CONFIG_ROOT="$(pkg-config --variable=sysconfdir mono 2>/dev/null || true)/mono"
fi
if [[ ! -f "$MONO_CONFIG_ROOT/config" || ! -f "$MONO_CONFIG_ROOT/4.5/machine.config" ]]; then
  printf '[FORMATTER_NATIVE_MONO_CONFIG_MISSING] 缺少 Mono config/machine.config：%s\n' "$MONO_CONFIG_ROOT" >&2
  exit 2
fi
MONO_FRAMEWORK_ROOT="${PARTYOPS_MONO_FRAMEWORK_ROOT:-}"
if [[ -z "$MONO_FRAMEWORK_ROOT" ]] && command -v pkg-config >/dev/null 2>&1; then
  MONO_FRAMEWORK_ROOT="$(pkg-config --variable=prefix mono)/lib/mono/4.5"
fi
for name in mscorlib.dll System.dll System.Core.dll System.Web.Extensions.dll I18N.CJK.dll; do
  [[ -s "$MONO_FRAMEWORK_ROOT/$name" ]] || {
    printf '[FORMATTER_NATIVE_MONO_FRAMEWORK_MISSING] 缺少 Mono 基础程序集：%s/%s\n' "$MONO_FRAMEWORK_ROOT" "$name" >&2
    exit 2
  }
done
[[ -d "$MONO_FRAMEWORK_ROOT/Facades" ]] || {
  printf '%s\n' '[FORMATTER_NATIVE_MONO_FRAMEWORK_MISSING] 缺少 Mono Facades。' >&2
  exit 2
}

case "$(uname -s)" in
  Linux) DETECTED_PLATFORM='linux' ;;
  Darwin) DETECTED_PLATFORM='macos' ;;
  *) printf '[FORMATTER_NATIVE_PLATFORM_UNSUPPORTED] 当前系统为 %s。\n' "$(uname -s)" >&2; exit 2 ;;
esac
[[ -n "$PLATFORM" ]] || PLATFORM="$DETECTED_PLATFORM"
[[ "$PLATFORM" == "$DETECTED_PLATFORM" ]] || {
  printf '[FORMATTER_NATIVE_PLATFORM_MISMATCH] 当前为 %s，目标为 %s。\n' "$DETECTED_PLATFORM" "$PLATFORM" >&2
  exit 2
}

case "$(uname -m)" in
  x86_64) DETECTED_MACHINE='x86_64' ;;
  aarch64|arm64) DETECTED_MACHINE='arm64' ;;
  *) printf '[FORMATTER_NATIVE_ARCH_UNSUPPORTED] 当前架构为 %s。\n' "$(uname -m)" >&2; exit 2 ;;
esac
if [[ -z "$ARCHITECTURE" ]]; then
  [[ "$PLATFORM" == 'linux' && "$DETECTED_MACHINE" == 'x86_64' ]] && ARCHITECTURE='amd64' || ARCHITECTURE="$DETECTED_MACHINE"
fi
EXPECTED_MACHINE="$ARCHITECTURE"
[[ "$EXPECTED_MACHINE" == 'amd64' ]] && EXPECTED_MACHINE='x86_64'
[[ "$EXPECTED_MACHINE" == "$DETECTED_MACHINE" ]] || {
  printf '[FORMATTER_NATIVE_ARCH_MISMATCH] 当前为 %s，目标为 %s。\n' "$DETECTED_MACHINE" "$ARCHITECTURE" >&2
  exit 2
}
if [[ "$PLATFORM" == 'macos' && "$ARCHITECTURE" != 'arm64' && "$ARCHITECTURE" != 'x86_64' ]] ||
  [[ "$PLATFORM" == 'linux' && "$ARCHITECTURE" != 'arm64' && "$ARCHITECTURE" != 'amd64' ]]; then
  printf '[FORMATTER_NATIVE_ARCH_INVALID] %s 不支持架构 %s。\n' "$PLATFORM" "$ARCHITECTURE" >&2
  exit 2
fi

MANAGED_RUNTIME="$(cd "$MANAGED_RUNTIME" && pwd -P)"
OUTPUT_PARENT="$(dirname "$OUTPUT")"
mkdir -p "$OUTPUT_PARENT"
OUTPUT_PARENT="$(cd "$OUTPUT_PARENT" && pwd -P)"
OUTPUT="$OUTPUT_PARENT/$(basename "$OUTPUT")"
if [[ -e "$OUTPUT" ]] && [[ ! -d "$OUTPUT" ]]; then
  printf '[FORMATTER_NATIVE_OUTPUT_INVALID] 输出必须是目录：%s\n' "$OUTPUT" >&2
  exit 2
fi
if [[ -d "$OUTPUT" ]] && [[ -n "$(find "$OUTPUT" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
  printf '[FORMATTER_NATIVE_OUTPUT_NOT_EMPTY] 为避免覆盖制品，输出目录必须为空：%s\n' "$OUTPUT" >&2
  exit 2
fi

assemblies=(
  PartyOps.DocumentFormatter.Host.exe
  PartyOps.DocumentFormatter.AddIn.dll
  Microsoft.Bcl.HashCode.dll
  System.Buffers.dll
  System.Memory.dll
  System.Numerics.Vectors.dll
  System.Runtime.CompilerServices.Unsafe.dll
  UglyToad.PdfPig.dll
  UglyToad.PdfPig.Core.dll
  UglyToad.PdfPig.DocumentLayoutAnalysis.dll
  UglyToad.PdfPig.Fonts.dll
  UglyToad.PdfPig.Package.dll
  UglyToad.PdfPig.Tokenization.dll
  UglyToad.PdfPig.Tokens.dll
)
for name in "${assemblies[@]}"; do
  [[ -s "$MANAGED_RUNTIME/$name" ]] || {
    printf '[FORMATTER_NATIVE_MANAGED_INPUT_MISSING] AnyCPU 载荷缺少：%s\n' "$name" >&2
    exit 2
  }
done

# ILONLY + 32/64 是跨架构输入契约；32bits/32bits-preferred 只能用于 Windows
# 专用宿主，不能拿去生成 ARM64 包。
if command -v pedump >/dev/null 2>&1; then
  cli_flags="$(pedump "$MANAGED_RUNTIME/PartyOps.DocumentFormatter.Host.exe" | awk -F': ' '/Flags:.*ilonly/{value=$2} END{print value}')"
  [[ "$cli_flags" == *'ilonly'* && "$cli_flags" == *'32/64'* ]] || {
    printf '[FORMATTER_NATIVE_MANAGED_ARCH_INVALID] 宿主不是 AnyCPU IL：%s\n' "$cli_flags" >&2
    exit 2
  }
fi

"$PYTHON_BIN" "$ROOT/scripts/verify-document-formatter-source-snapshot.py" --source "$SOURCE" >/dev/null
STAGE="$(mktemp -d "$OUTPUT_PARENT/.formatter-native.${PLATFORM}.${ARCHITECTURE}.XXXXXX")"
cleanup() {
  if [[ -d "$STAGE" ]]; then
    case "$STAGE" in
      "$OUTPUT_PARENT/.formatter-native.${PLATFORM}.${ARCHITECTURE}."*)
        chmod -R u+w "$STAGE" 2>/dev/null || true
        /bin/rm -R -- "$STAGE" 2>/dev/null || true
        ;;
      *) printf '[FORMATTER_NATIVE_CLEANUP_REFUSED] 非预期暂存目录：%s\n' "$STAGE" >&2 ;;
    esac
  fi
}
trap cleanup EXIT

HOST="$STAGE/partyops-document-formatter-host"
bundle_inputs=()
for name in "${assemblies[@]}"; do bundle_inputs+=("$MANAGED_RUNTIME/$name"); done
# 原 AddIn 含未执行的 Windows VSTO 引用，不能递归扫描这些平台专用组件。
# 显式封装完整 Mono 运行库及 Facades；同名业务依赖优先保留原工具版本。
for framework in "$MONO_FRAMEWORK_ROOT"/*.dll "$MONO_FRAMEWORK_ROOT"/Facades/*.dll; do
  [[ -f "$framework" ]] || continue
  [[ ! -f "$MANAGED_RUNTIME/$(basename "$framework")" ]] || continue
  bundle_inputs+=("$framework")
done
if [[ "$PLATFORM" == 'macos' ]]; then
  export MACOSX_DEPLOYMENT_TARGET='11.0'
fi
MONO_LIBRARY_ROOT="${PARTYOPS_MONO_LIBRARY_ROOT:-$(pkg-config --variable=libdir mono)}"
library_extension='so'
[[ "$PLATFORM" != 'macos' ]] || library_extension='dylib'
native_libraries=()
for name in "libmono-native.$library_extension" "libMonoPosixHelper.$library_extension"; do
  [[ -s "$MONO_LIBRARY_ROOT/$name" ]] || {
    printf '[FORMATTER_NATIVE_MONO_LIBRARY_MISSING] 缺少 Mono 原生辅助库：%s\n' "$name" >&2
    exit 2
  }
  native_libraries+=(--library "$name,$MONO_LIBRARY_ROOT/$name")
done
# Linux Mono 的 TLS 辅助库也按固定构建环境随宿主封装，不要求用户安装。
if [[ "$PLATFORM" == 'linux' ]]; then
  [[ -s "$MONO_LIBRARY_ROOT/libmono-btls-shared.so" ]] || exit 2
  native_libraries+=(--library "libmono-btls-shared.so,$MONO_LIBRARY_ROOT/libmono-btls-shared.so")
fi
mkbundle --simple --nodeps --i18n none \
  --config "$MONO_CONFIG_ROOT/config" \
  --machine-config "$MONO_CONFIG_ROOT/4.5/machine.config" \
  -L "$MANAGED_RUNTIME" -o "$HOST" "${native_libraries[@]}" "${bundle_inputs[@]}"
chmod 0755 "$HOST"

"$PYTHON_BIN" "$ROOT/scripts/generate-word-vtable-map.py" \
  --source "$SOURCE" --output "$STAGE/word-vtable-map.json" >/dev/null
MAP_SHA="$({ command -v sha256sum >/dev/null 2>&1 && sha256sum "$STAGE/word-vtable-map.json" || shasum -a 256 "$STAGE/word-vtable-map.json"; } | awk '{print $1}')"
if [[ "$MAP_SHA" != '871fa605d294620b273f19eff20c587e742af6d46903a16216c69913675b5f41' ]]; then
  printf '[FORMATTER_NATIVE_VTABLE_MAP_DIVERGED] WPS 槽位表与 528/528 SDK 核验记录不一致：%s\n' "$MAP_SHA" >&2
  exit 2
fi
cp "$ROOT/packaging/wps-formatter-adapter/LICENSE-WPS-SDK.txt" "$STAGE/LICENSE-WPS-SDK.txt"
cp "$ROOT/packaging/formatter-host/LICENSE-MONO-RUNTIME.txt" "$STAGE/LICENSE-MONO-RUNTIME.txt"

SELFTEST="$STAGE/.self-test.json"
"$HOST" --self-test "$SELFTEST"
"$PYTHON_BIN" - "$SELFTEST" <<'PY'
import json
import sys
from pathlib import Path

payload = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
if not (
    payload.get("passed") is True
    and payload.get("engine") == "source-standalone-batch-processor"
    and payload.get("features")
    == ["format", "replace", "redheader", "rename", "convert", "pdf-to-word"]
):
    raise SystemExit("[FORMATTER_NATIVE_SELFTEST_INVALID] 原源码宿主自检不完整。")
PY
/bin/rm -- "$SELFTEST"

description="$(file -b "$HOST")"
if [[ "$PLATFORM" == 'linux' ]]; then
  command -v ldd >/dev/null 2>&1 || { printf '%s\n' '[FORMATTER_NATIVE_LDD_MISSING] 缺少 ldd。' >&2; exit 2; }
  expected_description='x86-64'
  [[ "$ARCHITECTURE" == 'arm64' ]] && expected_description='ARM aarch64'
  [[ "$description" == *ELF* && "$description" == *"$expected_description"* ]] || {
    printf '[FORMATTER_NATIVE_BINARY_INVALID] 不是目标 ELF：%s\n' "$description" >&2
    exit 2
  }
  dependencies="$(ldd "$HOST" 2>&1)"
  [[ "$dependencies" != *'not found'* && "$dependencies" != *'libmono'* ]] || {
    printf '%s\n' '[FORMATTER_NATIVE_DEPENDENCY_INVALID] 宿主仍依赖外部 Mono 或缺失动态库。' >&2
    exit 2
  }
else
  for command in lipo otool; do
    command -v "$command" >/dev/null 2>&1 || {
      printf '[FORMATTER_NATIVE_BUILD_TOOL_MISSING] 缺少构建工具：%s\n' "$command" >&2
      exit 2
    }
  done
  [[ "$description" == *'Mach-O 64-bit'* && "$description" == *"$ARCHITECTURE"* ]] || {
    printf '[FORMATTER_NATIVE_BINARY_INVALID] 不是目标 Mach-O：%s\n' "$description" >&2
    exit 2
  }
  [[ "$(lipo -archs "$HOST")" == "$ARCHITECTURE" ]] || {
    printf '%s\n' '[FORMATTER_NATIVE_BINARY_NOT_THIN] 排版宿主必须是当前架构单架构 Mach-O。' >&2
    exit 2
  }
  bad_dependency="$(otool -L "$HOST" | tail -n +2 | awk '{print $1}' | grep -Ev '^(/usr/lib/|/System/Library/)' | head -1 || true)"
  [[ -z "$bad_dependency" ]] || {
    printf '[FORMATTER_NATIVE_DEPENDENCY_INVALID] 排版宿主引用构建机路径：%s\n' "$bad_dependency" >&2
    exit 2
  }
  minos="$(otool -l "$HOST" | awk '/LC_BUILD_VERSION/{seen=1; next} seen && $1=="minos"{print $2; exit}')"
  "$PYTHON_BIN" - "$minos" <<'PY'
import sys

try:
    value = tuple(int(item) for item in sys.argv[1].split(".")[:2])
except ValueError:
    raise SystemExit("[FORMATTER_NATIVE_DEPLOYMENT_TARGET_MISSING] Mach-O 缺少 minos。")
if value > (11, 0):
    raise SystemExit(f"[FORMATTER_NATIVE_DEPLOYMENT_TARGET_TOO_NEW] minos={sys.argv[1]}")
PY
fi

if strings "$HOST" | grep -Eq '(/mnt/[a-z]/|/home/|/Users/|/opt/homebrew/|/usr/local/)'; then
  printf '%s\n' '[FORMATTER_NATIVE_BUILD_PATH_LEAK] 原生宿主包含构建机绝对路径。' >&2
  exit 2
fi

HOST_SHA="$({ command -v sha256sum >/dev/null 2>&1 && sha256sum "$HOST" || shasum -a 256 "$HOST"; } | awk '{print $1}')"
MONO_VERSION="$(mono --version | head -1 | sed 's/[[:space:]]\+/ /g')"
"$PYTHON_BIN" - "$STAGE/source-host.json" "$PLATFORM" "$ARCHITECTURE" "$HOST_SHA" "$MAP_SHA" "$MONO_VERSION" <<'PY'
import json
import sys
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo

path, platform, architecture, host_sha, map_sha, mono_version = sys.argv[1:]
payload = {
    "schema": 2,
    "platform": platform,
    "architecture": architecture,
    "adapter": "wps-native-source-adapter",
    "source_project": "PartyOps.DocumentFormatter.AddIn",
    "source_snapshot_sha256": "15c21b886f6a958fb61a3b106266b446a2b959b0085510015eeb790efaa770d3",
    "source_snapshot_files": 898,
    "features": ["format", "replace", "redheader", "rename", "convert", "pdf-to-word"],
    "capabilities": 25,
    "built_at": datetime.now(ZoneInfo("Asia/Shanghai")).isoformat(),
    "timezone": "Asia/Shanghai",
    "host_sha256": host_sha,
    "word_vtable_map_sha256": map_sha,
    "wps_sdk_header_sha256": "4d0529c076f8f36ce49301982e0c2bb46cdcc4087c3e9945a9b57d649fe26791",
    "wps_sdk_matched_methods": 528,
    "wps_sdk_mismatched_methods": 0,
    "native_bundle_runtime": mono_version,
}
Path(path).write_text(
    json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
)
PY

"$PYTHON_BIN" "$ROOT/scripts/validate-source-formatter-runtime.py" \
  --runtime "$STAGE" --platform "$PLATFORM" --architecture "$ARCHITECTURE"

if [[ -d "$OUTPUT" ]]; then rmdir "$OUTPUT"; fi
mv "$STAGE" "$OUTPUT"
trap - EXIT
printf '[FORMATTER_NATIVE_BUILD_OK] %s/%s %s\n' "$PLATFORM" "$ARCHITECTURE" "$OUTPUT"
