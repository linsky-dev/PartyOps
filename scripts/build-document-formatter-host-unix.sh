#!/usr/bin/env bash
set -euo pipefail
umask 077

# 将 Windows 上由锁定源码构建的 AnyCPU IL，在目标 Linux/macOS 本机封装为
# 原生宿主（Mac标准静态链接运行时及同目录两份官方辅助库）。mkbundle仅提供运行容器；六类功能仍执行原项目的
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

source_profile=()
[[ "$PLATFORM" != 'macos' ]] || source_profile=(--profile mac-quotes-tnr)
"$PYTHON_BIN" "$ROOT/scripts/verify-document-formatter-source-snapshot.py" --source "$SOURCE" "${source_profile[@]}" >/dev/null
STAGE="$(mktemp -d "$OUTPUT_PARENT/.formatter-native.${PLATFORM}.${ARCHITECTURE}.XXXXXX")"
cleanup() {
  # Mac 候选失败保留全部暂存证据，不沿用旧 Linux 自动删除流程。
  if [[ "$PLATFORM" == 'macos' ]]; then
    printf '[FORMATTER_NATIVE_WORKSPACE_RETAINED] %s\n' "$STAGE" >&2
    return
  fi
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
native_libraries=()
mono_runtime_environment=()
MONO_BUNDLE_CONFIG="$MONO_CONFIG_ROOT/config"
if [[ "$PLATFORM" == 'macos' ]]; then
  # SDK6.12官方dllmap使用compat。仅派生本次bundle配置，系统config不改。
  for native_command in otool lipo cc pkg-config; do command -v "$native_command" >/dev/null || exit 2; done
  [[ "$(pkg-config --modversion mono-2)" == '6.12.0' ]] || { printf '%s\n' '[MAC_MONO_STATIC_SDK_MISMATCH] custom必须使用已核同版本SDK。' >&2; exit 2; }
  MONO_STATIC_LIBDIR="$(pkg-config --variable=libdir mono-2)"
  [[ "$(cd "$MONO_STATIC_LIBDIR" && pwd -P)" == "$(cd "$MONO_LIBRARY_ROOT" && pwd -P)" ]] || { printf '%s\n' '[MAC_MONO_STATIC_SDK_MISMATCH] pkg-config与辅助库必须来自同SDK。' >&2; exit 2; }
  MONO_STATIC_INCLUDE="$(pkg-config --variable=includedir mono-2)"
  [[ -s "$MONO_STATIC_INCLUDE/mono/jit/jit.h" && -s "$MONO_STATIC_INCLUDE/mono/metadata/assembly.h" ]] || { printf '%s\n' '[MAC_MONO_STATIC_HEADERS_MISSING] 缺少官方开发头文件。' >&2; exit 2; }
  "$PYTHON_BIN" - "$MONO_CONFIG_ROOT/config" "$MONO_LIBRARY_ROOT" "$STAGE" "$ARCHITECTURE" <<'MAC_MONO_PY'
import hashlib, json, os, re, shutil, subprocess, sys
from pathlib import Path
import xml.etree.ElementTree as ET

def prepare_native_inputs(config, library_root, stage, architecture, probe):
    original = config.read_bytes()
    root = library_root.resolve(strict=True)
    static = (root / "libmono-2.0.a").resolve(strict=True)
    if static.parent != root or hashlib.sha256(static.read_bytes()).hexdigest() != "87ac2d657bb9278bf9330c871dfd4fb8472b109334913a9b78a04f02663fa372":
        raise RuntimeError("[MAC_MONO_STATIC_RUNTIME_MISMATCH] 静态runtime不是已核官方SDK原件。")
    static_arches = probe(["lipo", "-archs", str(static)]).strip().split()
    if architecture not in static_arches:
        raise RuntimeError("[MAC_MONO_STATIC_RUNTIME_ARCH_MISMATCH] 静态runtime缺目标架构。")
    xml = ET.fromstring(original)
    required = ("System.Native", "System.Net.Security.Native", "System.Security.Cryptography.Native.Apple")
    maps = []
    for name in required:
        candidates = [item for item in xml.findall("dllmap") if item.get("dll") == name]
        if len(candidates) != 1 or candidates[0].get("target") != "$mono_libdir/libmono-native-compat.dylib":
            raise RuntimeError("[MAC_MONO_OFFICIAL_MAP_UNSUPPORTED] 官方SDK dllmap不匹配compat。")
        maps.append(dict(candidates[0].attrib))
    # 同版本官方config的Posix映射也必须明确，未知目标不猜测替换。
    posix = [item for item in xml.findall("dllmap") if item.get("dll") == "MonoPosixHelper"]
    if len(posix) != 1 or posix[0].get("target") != "$mono_libdir/libMonoPosixHelper.dylib":
        raise RuntimeError("[MAC_MONO_OFFICIAL_MAP_UNSUPPORTED] 官方SDK Posix映射未知。")
    maps.append(dict(posix[0].attrib))
    names = ("libmono-native-compat.dylib", "libMonoPosixHelper.dylib")
    libraries = []
    for name in names:
        path = root / name
        chain = []
        visited = set()
        while path.is_symlink():
            if path in visited or len(chain) >= 8:
                raise RuntimeError("[MAC_MONO_LIBRARY_LINK_INVALID] SDK库链接循环。")
            visited.add(path)
            target = os.readlink(path)
            chain.append({"name": path.name, "target": target})
            path = path.parent / target
        actual = path.resolve(strict=True)
        if actual.parent != root or not actual.is_file() or actual.stat().st_size == 0:
            raise RuntimeError("[MAC_MONO_LIBRARY_INVALID] SDK库不在本SDK目录闭包内。")
        arches = probe(["lipo", "-archs", str(actual)]).strip().split()
        if architecture not in arches:
            raise RuntimeError("[MAC_MONO_LIBRARY_ARCH_MISMATCH] SDK库缺目标架构。")
        dependencies = probe(["otool", "-arch", architecture, "-L", str(actual)])
        loads = [line.strip().split(" (", 1)[0] for line in dependencies.splitlines()[1:] if line.strip()]
        # dylib首项是自身install id，不是外部依赖；其余必须是系统库。
        if not loads or any(not value.startswith(("/usr/lib/", "/System/Library/")) for value in loads[1:]):
            raise RuntimeError("[MAC_MONO_LIBRARY_DEPENDENCY_UNSUPPORTED] SDK库有未支持的外部依赖。")
        commands = probe(["otool", "-arch", architecture, "-l", str(actual)])
        minimum = re.findall(r"\b(?:minos|version)\s+(\d+\.\d+(?:\.\d+)?)", "\n".join(re.findall(r"cmd LC_(?:BUILD_VERSION|VERSION_MIN_MACOSX).*?(?=Load command|\Z)", commands, re.S)))
        if len(minimum) != 1 or tuple(map(int, minimum[0].split("."))) > (11, 0, 0):
            raise RuntimeError("[MAC_MONO_LIBRARY_MIN_OS_UNSUPPORTED] SDK库最低系统未知或超过11.0。")
        libraries.append({"registered_name": name, "source_name": actual.name,
                          "source_sha256": hashlib.sha256(actual.read_bytes()).hexdigest(), "packaged_sha256": hashlib.sha256(actual.read_bytes()).hexdigest(), "bytes": actual.stat().st_size,
                          "symlink_chain": chain, "architectures": arches, "minimum_macos": minimum[0],
                          "file_description": probe(["file", "-b", str(actual)]).strip(),
                          "install_name": loads[0], "dependencies": loads[1:]})
        # official短名原已存在；副本保真实库字节，不依赖SDK或私有任务cwd。
        copied = stage / name
        with copied.open("xb") as stream, actual.open("rb") as source: shutil.copyfileobj(source, stream)
        copied.chmod(0o755)
        if hashlib.sha256(copied.read_bytes()).hexdigest() != libraries[-1]["source_sha256"]:
            raise RuntimeError("[MAC_MONO_SIDECAR_COPY_MISMATCH] 辅助库复制内容变化。")
    derived = original.replace(b"$mono_libdir/libmono-native-compat.dylib", b"@executable_path/libmono-native-compat.dylib")
    derived = derived.replace(b"$mono_libdir/libMonoPosixHelper.dylib", b"@executable_path/libMonoPosixHelper.dylib")
    ET.fromstring(derived)
    bundle_config = stage / "mono-config.bundle.xml"
    with bundle_config.open("xb") as stream: stream.write(derived)
    record = {"schema": 1, "platform": "macos", "official_sdk_selection": "config-dllmap-compat",
              "original_config_sha256": hashlib.sha256(original).hexdigest(), "bundle_config_sha256": hashlib.sha256(derived).hexdigest(),
              "original_dllmaps": maps, "bundle_dllmap_target": "@executable_path/libmono-native-compat.dylib",
              "bundle_posix_target": "@executable_path/libMonoPosixHelper.dylib", "runtime_options": [], "embedded_environment": {},
              "bundling_mode": "custom-static", "child_environment": {"MONO_CONFIG": "/dev/null", "removed": ["MONO_ENV_OPTIONS", "MONO_BUNDLED_OPTIONS"]},
              "static_runtime": {"source_name": static.name, "bytes": static.stat().st_size, "source_sha256": hashlib.sha256(static.read_bytes()).hexdigest(), "architectures": static_arches},
              "libraries": libraries, "license": "LICENSE-MONO-RUNTIME.txt"}
    with (stage / "mono-native-source.json").open("x", encoding="utf-8") as stream: json.dump(record, stream, ensure_ascii=False, indent=2)

if __name__ == "__main__":
    prepare_native_inputs(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]), sys.argv[4],
                          lambda args: subprocess.run(args, check=True, capture_output=True, text=True).stdout)
MAC_MONO_PY
  MONO_BUNDLE_CONFIG="$STAGE/mono-config.bundle.xml"
else
for name in libmono-native.so libMonoPosixHelper.so; do
  [[ -s "$MONO_LIBRARY_ROOT/$name" ]] || {
    printf '[FORMATTER_NATIVE_MONO_LIBRARY_MISSING] 缺少 Mono 原生辅助库：%s\n' "$name" >&2
    exit 2
  }
  native_libraries+=(--library "$name,$MONO_LIBRARY_ROOT/$name")
done
fi
# Linux Mono 的 TLS 辅助库也按固定构建环境随宿主封装，不要求用户安装。
if [[ "$PLATFORM" == 'linux' ]]; then
  [[ -s "$MONO_LIBRARY_ROOT/libmono-btls-shared.so" ]] || exit 2
  native_libraries+=(--library "libmono-btls-shared.so,$MONO_LIBRARY_ROOT/libmono-btls-shared.so")
fi
if [[ "$PLATFORM" == 'macos' ]]; then
  # 官方custom/template_main生成标准cc/ld Mach-O，不追加或修改Mach-O载荷布局。
  # 显式程序集清单保持原顺序；custom不支持--library/--env，不启用递归依赖或AOT。
  # SDK构建工具仍用本SDK真实config；产物运行才以/dev/null防外部覆盖。
  # 本SDK静态LLVM/PPDB对象需系统C++/zlib；沿官方CC通道传链接库，不改变C模板语言。
  # 官方压缩模板先初始化Mono API函数表；未压缩模板的config调用已实证NULL崩溃。
  (cd "$STAGE" && /usr/bin/env -u MONO_ENV_OPTIONS -u MONO_BUNDLED_OPTIONS MONO_CONFIG="$MONO_CONFIG_ROOT/config" \
    CC="$(command -v cc) -arch $ARCHITECTURE -lc++ -lz" \
    mkbundle --custom --static -z --nodeps --i18n none --keeptemp \
    --config "$MONO_BUNDLE_CONFIG" --machine-config "$MONO_CONFIG_ROOT/4.5/machine.config" \
    -L "$MANAGED_RUNTIME" -o "$HOST" "${bundle_inputs[@]}")
else
mkbundle --simple --nodeps --i18n none \
  --config "$MONO_BUNDLE_CONFIG" "${mono_runtime_environment[@]}" \
  --machine-config "$MONO_CONFIG_ROOT/4.5/machine.config" \
  -L "$MANAGED_RUNTIME" -o "$HOST" "${native_libraries[@]}" "${bundle_inputs[@]}"
fi
chmod 0755 "$HOST"

MAP_SHA=''
if [[ "$PLATFORM" != 'macos' ]]; then
"$PYTHON_BIN" "$ROOT/scripts/generate-word-vtable-map.py" \
  --source "$SOURCE" --output "$STAGE/word-vtable-map.json" >/dev/null
MAP_SHA="$({ command -v sha256sum >/dev/null 2>&1 && sha256sum "$STAGE/word-vtable-map.json" || shasum -a 256 "$STAGE/word-vtable-map.json"; } | awk '{print $1}')"
if [[ "$MAP_SHA" != '871fa605d294620b273f19eff20c587e742af6d46903a16216c69913675b5f41' ]]; then
  printf '[FORMATTER_NATIVE_VTABLE_MAP_DIVERGED] WPS 槽位表与 528/528 SDK 核验记录不一致：%s\n' "$MAP_SHA" >&2
  exit 2
fi
cp "$ROOT/packaging/wps-formatter-adapter/LICENSE-WPS-SDK.txt" "$STAGE/LICENSE-WPS-SDK.txt"
else
  # 固定七件来自本轮正式托管载荷；不给 Mac 对象后端附加旧 SDK 槽位记录。
  "$PYTHON_BIN" - "$ROOT" "$MANAGED_RUNTIME" "$STAGE" <<'PY'
import hashlib, re, shutil, sys
from pathlib import Path
root, managed, stage = map(Path, sys.argv[1:])
catalog = root / "packaging/windows/formatter-host/MacSessionCoordinator.cs"
source = catalog.read_text(encoding="utf-8")
section = source.split("internal static class MacPluginResources", 1)[1]
hashes = dict(re.findall(r'\{"([a-zA-Z0-9.-]+)","([0-9a-f]{64})"\}', section))
expected = {"bootstrap-carrier.docx", "main.js", "ribbon.xml", "task-lease.js", "product-index.html", "product-main.js", "product-ribbon.xml"}
directory = managed / "wps-formatter-plugin"
if set(hashes) != expected or not directory.is_dir() or directory.is_symlink() or {p.name for p in directory.iterdir()} != expected:
    raise SystemExit("[MAC_FORMATTER_PLUGIN_CATALOG_INVALID] 正式载荷固定七件不完整。")
target = stage / "wps-formatter-plugin"
target.mkdir()
for name, digest in hashes.items():
    item = directory / name
    if item.is_symlink() or not item.is_file() or hashlib.sha256(item.read_bytes()).hexdigest() != digest:
        raise SystemExit("[MAC_FORMATTER_PLUGIN_HASH_MISMATCH] 正式载荷与源码目录摘要不一致。")
    shutil.copyfile(item, target / name)
PY
fi
cp "$ROOT/packaging/formatter-host/LICENSE-MONO-RUNTIME.txt" "$STAGE/LICENSE-MONO-RUNTIME.txt"

SELFTEST="$STAGE/.self-test.json"
if [[ "$PLATFORM" == 'macos' ]]; then
  /usr/bin/env -u MONO_ENV_OPTIONS -u MONO_BUNDLED_OPTIONS MONO_CONFIG=/dev/null "$HOST" --self-test "$SELFTEST"
else
  "$HOST" --self-test "$SELFTEST"
fi
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
[[ "$PLATFORM" == 'macos' ]] || /bin/rm -- "$SELFTEST"

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
  minos="$(otool -l "$HOST" | awk '$1=="cmd"&&$2=="LC_BUILD_VERSION"{seen="build";next} $1=="cmd"&&$2=="LC_VERSION_MIN_MACOSX"{seen="legacy";next} seen=="build"&&$1=="minos"{print $2;exit} seen=="legacy"&&$1=="version"{print $2;exit}')"
  "$PYTHON_BIN" - "$minos" <<'PY'
import sys

try:
    value = tuple(int(item) for item in sys.argv[1].split(".")[:2])
    if len(value) != 2 or any(item < 0 for item in value):
        raise ValueError("missing version")
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
"$PYTHON_BIN" - "$STAGE/source-host.json" "$PLATFORM" "$ARCHITECTURE" "$HOST_SHA" "$MAP_SHA" "$MONO_VERSION" "$ROOT" "$MANAGED_RUNTIME" <<'PY'
import hashlib
import json
import sys
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo

path, platform, architecture, host_sha, map_sha, mono_version, root, managed = sys.argv[1:]
payload = {
    "schema": 2,
    "platform": platform,
    "architecture": architecture,
    "adapter": "wps-native-source-adapter",
    "source_project": "PartyOps.DocumentFormatter.AddIn",
    "source_snapshot_sha256": "7ae0eb67a0cb6a2d4a332cde74adf8977d93ae73541f864f01df214d39fefdf2",
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
if platform == "macos":
    def digest(item):
        return hashlib.sha256(Path(item).read_bytes()).hexdigest()
    # 规则、托管宿主与资源分别绑定；构建自检不是目标包六功能实测。
    rules_sha = digest(Path(managed) / "PartyOps.DocumentFormatter.AddIn.dll")
    if rules_sha != "2cae1d25146334e66f57a98663dcd6574335f720be6b6aacc53e0dbad165d57b":
        raise SystemExit("[MAC_FORMATTER_RULES_MISMATCH] 载荷不是用户授权的引号规则构建。")
    payload.update(schema=3, adapter="wps-macos-object-source-adapter",
        source_snapshot_sha256="ac8466edf2513ea8e3fe9e61d3b86fb8d7a72ceb6cce366f2d19b59d9c9be171",
        rules_sha256=rules_sha, managed_host_sha256=digest(Path(managed) / "PartyOps.DocumentFormatter.Host.exe"),
        resource_catalog_source_sha256=digest(Path(root) / "packaging/windows/formatter-host/MacSessionCoordinator.cs"),
        self_contained=True, minimum_macos="11.0", acceptance_profile="mac-object-limited-candidate",
        native_bundle_mode="custom-static", native_sidecars=["libmono-native-compat.dylib", "libMonoPosixHelper.dylib"],
        native_sidecars_sha256={name: digest(Path(path).parent / name) for name in ("libmono-native-compat.dylib", "libMonoPosixHelper.dylib")},
        mono_native_source_sha256=digest(Path(path).parent / "mono-native-source.json"),
        feature_validation={feature: "pending-target-package-validation" for feature in payload["features"]},
        limitations=["manual-output-review-required", "wps-window-may-appear", "native-document-cycle-unproven", "rollback-unverified", "strict-golden-parity-not-accepted"],
        plugin_resources_sha256={item.name: digest(item) for item in (Path(path).parent / "wps-formatter-plugin").iterdir()})
    for key in ("capabilities", "word_vtable_map_sha256", "wps_sdk_header_sha256", "wps_sdk_matched_methods", "wps_sdk_mismatched_methods"):
        del payload[key]
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
