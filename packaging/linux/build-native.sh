#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FORMAT="${1:-}"
ARCH="${PARTYOPS_BUILD_ARCH:-}"
RUNTIME_PROFILE="${PARTYOPS_RUNTIME_PROFILE:-full}"
DEB_VERSION="1.4.5~rc.6"
RPM_VERSION="1.4.5"
RPM_RELEASE="0.rc.6.1"
ARTIFACTS="$ROOT/artifacts"

if [[ -z "${PYTHON_BIN:-}" && -f "$ROOT/.partyops-build.env" ]]; then
  # shellcheck disable=SC1091
  source "$ROOT/.partyops-build.env"
fi
if [[ -z "${PYTHON_BIN:-}" ]]; then
  if [[ "$RUNTIME_PROFILE" == core ]]; then
    PYTHON_BIN="$(command -v python3.12 || true)"
  else
    PYTHON_BIN="$(command -v python3.11 || command -v python3 || true)"
  fi
fi
if [[ -z "$PYTHON_BIN" || ! -x "$PYTHON_BIN" ]]; then
  echo "未找到可用 Python 3.11，请先运行 ensure-build-environment.sh。" >&2
  exit 2
fi

"$PYTHON_BIN" "$ROOT/scripts/verify-version-consistency.py" \
  --root "$ROOT" --expected "1.4.5-rc.6"
"$PYTHON_BIN" "$ROOT/scripts/verify-full-function-gate.py" verify --root "$ROOT" --scope package

[[ "$FORMAT" == "deb" || "$FORMAT" == "rpm" ]] || {
  echo "用法：build-native.sh deb|rpm（通过 PARTYOPS_BUILD_ARCH 指定 amd64/arm64）" >&2
  exit 2
}
if [[ -z "$ARCH" ]]; then
  case "$(uname -m)" in
    x86_64) ARCH=amd64 ;;
    aarch64|arm64) ARCH=arm64 ;;
    loongarch64|loong64) ARCH=loong64 ;;
    *) echo "不支持的架构：$(uname -m)" >&2; exit 2 ;;
  esac
fi
[[ "$ARCH" == "amd64" || "$ARCH" == "arm64" || "$ARCH" == "loong64" ]] || {
  echo "仅支持 amd64/arm64/loong64：$ARCH" >&2
  exit 2
}
if [[ "$ARCH" == loong64 ]]; then
  [[ "$FORMAT" == deb && "$RUNTIME_PROFILE" == core ]] || {
    echo "Loong64 仅提供显式 core DEB 档。" >&2; exit 2;
  }
elif [[ "$RUNTIME_PROFILE" != full ]]; then
  echo "core 档仅允许 Loong64。" >&2; exit 2
fi
EXPECTED_MACHINE=x86_64
[[ "$ARCH" == arm64 ]] && EXPECTED_MACHINE=aarch64
[[ "$ARCH" == loong64 ]] && EXPECTED_MACHINE=loongarch64
OFFICE_RUNTIME="${PARTYOPS_OFFICE_RUNTIME:-$ROOT/vendor/linux/libreoffice-headless-$ARCH}"
OFFICE_BINARY="$OFFICE_RUNTIME/program/soffice.bin"
if [[ "$ARCH" == loong64 ]]; then
  # 来源及预期 SHA 由目标机核查后固定输入；未知来源不得通过旧 TDF 合同。
  [[ -x "$OFFICE_RUNTIME/program/soffice" && -f "$OFFICE_BINARY" &&
    -f "$OFFICE_RUNTIME/SOURCE.json" && -d "$OFFICE_RUNTIME/licenses" ]] || {
    echo "[OFFICE_RUNTIME_MISSING] Loong64 公文转换运行时闭包不完整。" >&2; exit 2;
  }
  [[ "${PARTYOPS_LOONG64_OFFICE_SHA256:-}" =~ ^[0-9a-f]{64}$ ]] || {
    echo "[OFFICE_EXPECTED_SHA_MISSING] 必须输入经目标机验证的 Loong64 soffice.bin SHA256。" >&2; exit 2;
  }
  [[ "$(sha256sum "$OFFICE_BINARY" | awk '{print $1}')" == "$PARTYOPS_LOONG64_OFFICE_SHA256" ]] || {
    echo "[OFFICE_BINARY_HASH_MISMATCH] Loong64 Office 与固定输入不一致。" >&2; exit 2;
  }
  "$PYTHON_BIN" - "$OFFICE_RUNTIME" "$PARTYOPS_LOONG64_OFFICE_SHA256" <<'PY'
import json
import pathlib
import sys
runtime = pathlib.Path(sys.argv[1])
source = json.loads((runtime / "SOURCE.json").read_text(encoding="utf-8"))
if (source.get("architecture") != "loong64" or
    source.get("soffice_bin_sha256") != sys.argv[2] or
    not source.get("origin") or not source.get("version") or
    not any((runtime / "licenses").iterdir())):
    raise SystemExit("[OFFICE_SOURCE_MISMATCH] Loong64 来源、版本、许可或实际 SHA 未固定")
PY
  file "$OFFICE_BINARY" | grep -Eq 'LoongArch|Loongarch' || {
    echo "[OFFICE_RUNTIME_ARCH_MISMATCH] Loong64 Office ELF 不匹配。" >&2; exit 2;
  }
  if ldd "$OFFICE_BINARY" 2>&1 | grep -q 'not found'; then
    echo "[OFFICE_RUNTIME_DEPENDENCY_MISSING] Loong64 Office 存在缺失依赖。" >&2; exit 2
  fi
  OFFICE_VERSION="$(timeout 120 "$OFFICE_RUNTIME/program/soffice" --headless --version)"
  "$PYTHON_BIN" - "$OFFICE_VERSION" "$OFFICE_RUNTIME/SOURCE.json" <<'PY'
import json
import re
import sys
version = json.load(open(sys.argv[2], encoding="utf-8"))["version"]
if not re.search(r"(?<![0-9])" + re.escape(version) + r"(?![0-9])", sys.argv[1]):
    raise SystemExit("[OFFICE_VERSION_MISMATCH] Loong64 Office 实际版本与来源清单不一致")
PY
else
EXPECTED_OFFICE_PATTERN='x86-64|x86_64'
OFFICE_LOADER_NAME=ld-linux-x86-64.so.2
EXPECTED_OFFICE_ARCHIVE_SHA=a893a4f37a8b3fe110da92bb0135f488f8d695cd40cb7ce59c65bb525849bb67
EXPECTED_OFFICE_PACKAGES_SHA=dd5ddb478f8863533b48baf2273411ab7c110f4609a74590223f7d9716dcb6cb
if [[ "$ARCH" == arm64 ]]; then
  EXPECTED_OFFICE_PATTERN='aarch64|ARM64'
  OFFICE_LOADER_NAME=ld-linux-aarch64.so.1
  EXPECTED_OFFICE_ARCHIVE_SHA=a47d693dce67d5f5e15ee6f7ed2faaba5a2234fd21c3cd0227cf0567e63f95a4
  EXPECTED_OFFICE_PACKAGES_SHA=82b2b3b8c65cc1fcd369b86b0ca0b3b3ed675304898354b8f15143ba57365e90
fi
if [[ ! -x "$OFFICE_RUNTIME/program/soffice" || ! -f "$OFFICE_BINARY" ||
  ! -f "$OFFICE_RUNTIME/SOURCE.json" || ! -d "$OFFICE_RUNTIME/licenses" ||
  ! -f "$OFFICE_RUNTIME/private-runtime/$OFFICE_LOADER_NAME" ||
  ! -f "$OFFICE_RUNTIME/PRIVATE_RUNTIME_LIBS.txt" ]]; then
  echo "[OFFICE_RUNTIME_MISSING] 缺少 $ARCH 经许可审计的 LibreOffice headless 运行时、来源清单或许可证。" >&2
  exit 2
fi
file "$OFFICE_BINARY" | grep -Eq "$EXPECTED_OFFICE_PATTERN" || {
  echo "[OFFICE_RUNTIME_ARCH_MISMATCH] LibreOffice 运行时与 $ARCH 不一致。" >&2
  exit 2
}
file "$OFFICE_RUNTIME/private-runtime/$OFFICE_LOADER_NAME" |
  grep -Eq "$EXPECTED_OFFICE_PATTERN" || {
  echo "[OFFICE_PRIVATE_LOADER_ARCH_MISMATCH] LibreOffice 私有加载器与 $ARCH 不一致。" >&2
  exit 2
}
# SOURCE.json、私有依赖清单及 dlopen 模块均属于安装包供应链边界。
# 逐文件复核哈希，避免构建机系统库或未固定的新版本悄悄混入制品。
"$PYTHON_BIN" - "$OFFICE_RUNTIME" "$ARCH" "$EXPECTED_OFFICE_ARCHIVE_SHA" \
  "$OFFICE_LOADER_NAME" "$EXPECTED_OFFICE_PACKAGES_SHA" <<'PY'
import hashlib
import json
import pathlib
import sys

runtime = pathlib.Path(sys.argv[1])
architecture = sys.argv[2]
archive_sha256 = sys.argv[3]
loader_name = sys.argv[4]
packages_sha256 = sys.argv[5]
source = json.loads((runtime / "SOURCE.json").read_text(encoding="utf-8"))
expected_source = {
    "version": "25.8.7.2",
    "architecture": architecture,
    "origin": "The Document Foundation official archive",
    "archive_sha256": archive_sha256,
    "private_runtime_glibc": "2.34",
    "minimum_host_glibc": "2.17",
    "private_runtime_packages_sha256": packages_sha256,
}
for key, expected in expected_source.items():
    if source.get(key) != expected:
        raise SystemExit(
            f"[OFFICE_SOURCE_MISMATCH] {key}={source.get(key)!r}，预期 {expected!r}"
        )

manifest = runtime / "PRIVATE_RUNTIME_LIBS.txt"
packages_manifest = runtime / "PRIVATE_RUNTIME_PACKAGES.txt"
if hashlib.sha256(packages_manifest.read_bytes()).hexdigest() != packages_sha256:
    raise SystemExit("[OFFICE_PRIVATE_PACKAGES_HASH_MISMATCH]")
seen: set[str] = set()
for number, raw_line in enumerate(manifest.read_text(encoding="utf-8").splitlines(), 1):
    fields = raw_line.split("\t")
    if len(fields) != 3:
        raise SystemExit(f"[OFFICE_PRIVATE_MANIFEST_INVALID] 第 {number} 行格式错误")
    name, _source_path, expected_sha256 = fields
    if name in seen:
        raise SystemExit(f"[OFFICE_PRIVATE_MANIFEST_INVALID] 重复文件：{name}")
    seen.add(name)
    target = runtime / "private-runtime" / name
    if not target.is_file():
        raise SystemExit(f"[OFFICE_PRIVATE_RUNTIME_MISSING] {name}")
    actual = hashlib.sha256(target.read_bytes()).hexdigest()
    if actual != expected_sha256:
        raise SystemExit(f"[OFFICE_PRIVATE_RUNTIME_HASH_MISMATCH] {name}")

required = {
    loader_name,
    "libfreebl3.chk",
    "libfreebl3.so",
    "libfreeblpriv3.chk",
    "libfreeblpriv3.so",
    "libnssckbi.so",
    "libnsssysinit.so",
    "libsoftokn3.chk",
    "libsoftokn3.so",
}
missing = sorted(required - seen)
if missing:
    raise SystemExit(f"[OFFICE_DLOPEN_RUNTIME_MISSING] {', '.join(missing)}")

for name in (
    "libavmediaqt6.so",
    "libavmediagtk.so",
    "libavmediagst.so",
    "liblibreofficekitgtk.so",
    "libofficebean.so",
):
    if (runtime / "program" / name).exists():
        raise SystemExit(f"[OFFICE_EXTERNAL_UI_FORBIDDEN] {name}")

wrapper = (runtime / "program" / "soffice").read_text(encoding="utf-8")
for contract in (
    "../private-runtime",
    "--library-path",
    'if [[ "$status" -eq 81 ]]',
):
    if contract not in wrapper:
        raise SystemExit(f"[OFFICE_WRAPPER_CONTRACT_MISSING] {contract}")
PY
while IFS= read -r -d '' link; do
  resolved="$(readlink -f -- "$link" 2>/dev/null || true)"
  case "$resolved" in
    "$OFFICE_RUNTIME"/*) ;;
    *) echo "[OFFICE_RUNTIME_SYMLINK_INVALID] LibreOffice 运行时包含越界或损坏链接：$link" >&2; exit 2 ;;
  esac
done < <(find "$OFFICE_RUNTIME" -type l -print0)
fi
if [[ "$ARCH" == loong64 ]]; then
  while IFS= read -r -d '' link; do
    resolved="$(readlink -f -- "$link" 2>/dev/null || true)"
    case "$resolved" in
      "$OFFICE_RUNTIME"/*) ;;
      *) echo "[OFFICE_RUNTIME_SYMLINK_INVALID] Loong64 Office 链接越界或损坏：$link" >&2; exit 2 ;;
    esac
  done < <(find "$OFFICE_RUNTIME" -type l -print0)
fi
[[ "$(uname -s)" == Linux ]] || {
  echo "原生包只能在 Linux manylinux2014 构建环境生成。" >&2
  exit 2
}
HOST_MACHINE="$(uname -m)"
if [[ "$ARCH" == loong64 && "$HOST_MACHINE" != loongarch64 && "$HOST_MACHINE" != loong64 ]]; then
  echo "Loong64 core 必须在目标架构原生封包与自检，不接受跨架构例外。" >&2; exit 2
fi
if [[ "$HOST_MACHINE" != "$EXPECTED_MACHINE" &&
  ! ( "$ARCH" == loong64 && "$HOST_MACHINE" == loong64 ) &&
  "${PARTYOPS_ALLOW_CROSS_PACKAGE:-0}" != "1" ]]; then
  echo "目标为 $EXPECTED_MACHINE、当前为 $HOST_MACHINE；仅封装已在目标架构自检通过的载荷时，才可显式设置 PARTYOPS_ALLOW_CROSS_PACKAGE=1。" >&2
  exit 2
fi
GLIBC="$(getconf GNU_LIBC_VERSION | awk '{print $2}')"
EXPECTED_GLIBC=2.17
[[ "$ARCH" == loong64 ]] && EXPECTED_GLIBC=2.38
[[ "$GLIBC" == "$EXPECTED_GLIBC" ]] || {
  echo "正式包要求 glibc $EXPECTED_GLIBC 构建基线，当前为 $GLIBC；拒绝生成伪兼容制品。" >&2
  exit 2
}
if [[ "$ARCH" == loong64 ]] &&
  [[ "$("$PYTHON_BIN" -c 'import platform;print(platform.python_version())')" != 3.12.13 ]]; then
  echo "Loong64 core 必须使用实际 Python 3.12.13。" >&2; exit 2
fi

WHEELHOUSE="$ROOT/vendor/wheels/$ARCH"
shopt -s nullglob
PACKAGING_WHEELS=("$WHEELHOUSE"/packaging-*.whl)
shopt -u nullglob
[[ "${#PACKAGING_WHEELS[@]}" -eq 1 ]] || {
  echo "离线 wheelhouse 必须且只能包含一个 packaging wheel。" >&2
  exit 2
}
# 基础构建解释器刻意不预装第三方包。直接从已经纳入 vendor 哈希门禁的
# 纯 Python wheel 加载 packaging，避免联网安装和构建机全局环境漂移。
RUNTIME_REQUIREMENTS="$ROOT/backend/requirements-local-ai.txt"
[[ "$ARCH" == loong64 ]] && RUNTIME_REQUIREMENTS="$ROOT/packaging/uos/requirements-core.txt"
PYTHONPATH="${PACKAGING_WHEELS[0]}${PYTHONPATH:+:$PYTHONPATH}" \
  "$PYTHON_BIN" "$ROOT/scripts/validate-uos-wheelhouse.py" \
  --architecture "$ARCH" \
  --runtime-profile "$RUNTIME_PROFILE" \
  --wheelhouse "$WHEELHOUSE" \
  --requirements "$ROOT/backend/requirements.txt" \
    "$RUNTIME_REQUIREMENTS" \
    "$ROOT/packaging/uos/requirements-build.txt"

PORTABLE="$ARTIFACTS/PartyOps-linux-$ARCH.tar.zst"
[[ -f "$PORTABLE" ]] || {
  echo "缺少严格模式便携载荷：$PORTABLE" >&2
  exit 2
}
BUILD_PARENT="${PARTYOPS_NATIVE_BUILD_BASE:-$ROOT/.build-linux}"
mkdir -p "$BUILD_PARENT" "$ARTIFACTS"
BUILD_PARENT="$(cd "$BUILD_PARENT" && pwd -P)"
# DEB/RPM 元数据要求真实 POSIX 权限。WSL DrvFS 未启用 metadata 时会把
# DEBIAN/control 等文件和目录全部呈现为 0777，dpkg-deb 会直接拒绝；
# 先实测权限语义，不满足时把暂存树放到 Linux 本地文件系统。
MODE_PROBE="$(mktemp -d "$BUILD_PARENT/.mode-probe.XXXXXX")"
touch "$MODE_PROBE/file"
chmod 0700 "$MODE_PROBE"
chmod 0600 "$MODE_PROBE/file"
if [[ "$(stat -c '%a' "$MODE_PROBE")" != "700" ||
  "$(stat -c '%a' "$MODE_PROBE/file")" != "600" ]]; then
  PROBE_PARENT="$BUILD_PARENT"
  BUILD_PARENT="${TMPDIR:-/tmp}/partyops-native-build"
  mkdir -p "$BUILD_PARENT"
  chmod 0700 "$BUILD_PARENT"
  echo "原生包暂存目录 $PROBE_PARENT 不保存 POSIX 权限；改用 $BUILD_PARENT。"
fi
rm -f -- "$MODE_PROBE/file"
rmdir -- "$MODE_PROBE"
BUILD_PARENT="$(cd "$BUILD_PARENT" && pwd -P)"
BUILD="$(mktemp -d "$BUILD_PARENT/native.XXXXXX")"
cleanup() {
  status=$?
  trap - EXIT
  case "$BUILD" in
    "$BUILD_PARENT/native."*) rm -rf -- "$BUILD" ;;
    *) echo "拒绝清理异常构建目录：$BUILD" >&2 ;;
  esac
  exit "$status"
}
trap cleanup EXIT
PKG="$BUILD/root"
PORTABLE_COPY="$BUILD/portable.tar.zst"
command -v zstd >/dev/null 2>&1 || {
  echo "缺少 zstd，无法安全验证 Linux 便携载荷。" >&2
  exit 2
}
SOURCE_SHA="$(sha256sum "$PORTABLE" | awk '{print $1}')"
cp -- "$PORTABLE" "$PORTABLE_COPY"
COPY_SHA="$(sha256sum "$PORTABLE_COPY" | awk '{print $1}')"
[[ "$SOURCE_SHA" == "$COPY_SHA" ]] || {
  echo "Linux 便携载荷复制期间发生变化，拒绝继续构建。" >&2
  exit 2
}
zstd -dc -- "$PORTABLE_COPY" |
  "$PYTHON_BIN" "$ROOT/scripts/validate-portable-tar.py" --expected-root PartyOps
mkdir -p "$PKG/opt/partyops" "$PKG/etc/partyops" \
  "$PKG/usr/share/applications" "$PKG/usr/share/icons/hicolor/scalable/apps" \
  "$PKG/lib/systemd/system" "$PKG/usr/share/polkit-1/actions"
zstd -dc -- "$PORTABLE_COPY" |
  tar --extract --file - --directory "$BUILD" \
    --no-same-owner --no-same-permissions
cp -a "$BUILD/PartyOps/." "$PKG/opt/partyops/"
"$PYTHON_BIN" "$ROOT/scripts/validate-source-formatter-runtime.py" \
  --runtime "$PKG/opt/partyops/formatter-host" \
  --platform linux --architecture "$ARCH"
# 原生包对旧便携载荷再执行一次默认拒绝权限收敛。只有固定应用入口可以
# 执行；共享库、WASM、图片和许可证必须保持 0644，避免麒麟安全中心把
# libgcc_s.so.1 等运行库误判为自启动程序。
find "$PKG/opt/partyops" -type f -exec chmod 0644 {} +
chmod 0755 \
  "$PKG/opt/partyops/partyops" \
  "$PKG/opt/partyops/partyops-client" \
  "$PKG/opt/partyops/partyops-wizard" \
  "$PKG/opt/partyops/partyops-updater" \
  "$PKG/opt/partyops/start.sh" \
  "$PKG/opt/partyops/stop.sh" \
  "$PKG/opt/partyops/desktop-launcher.sh" \
  "$PKG/opt/partyops/open-local-file.sh" \
  "$PKG/opt/partyops/install-desktop-shortcut.sh" \
  "$PKG/opt/partyops/install-internal-ca.sh" \
  "$PKG/opt/partyops/ocr/bin/tesseract" \
  "$PKG/opt/partyops/formatter-host/partyops-document-formatter-host"
if [[ "$ARCH" == loong64 ]]; then
  chmod 0755 \
    "$PKG/opt/partyops/formatter-host/partyops-document-formatter-host.bin" \
    "$PKG/opt/partyops/formatter-host/private/liblol/ld.so.1"
fi
if [[ -f "$PKG/opt/partyops/llama-server" ]]; then
  chmod 0755 "$PKG/opt/partyops/llama-server"
fi
# LibreOffice 自带多个受许可约束的本机入口和动态库；在 PartyOps 基础
# 载荷完成权限收敛后再复制，并在下方按内容恢复必要执行位与相对布局。
cp -a "$OFFICE_RUNTIME" "$PKG/opt/partyops/office-runtime"
# WSL DrvFS 未启用 metadata 时会把 LibreOffice 的普通资源、共享库和真实
# 程序一律呈现为 0777。RPM 会对每个带执行位的文件运行脚本识别，普通
# autotext/配置文件因此可能令 rpmbuild 直接失败；国产系统安全中心也会把
# 这些资源误判为程序。只在封包副本中先收敛全部权限，再按文件内容恢复
# program 根目录下的 ELF 程序和 shebang 启动脚本，不修改哈希审计的 vendor。
OFFICE_PACKAGE_RUNTIME="$PKG/opt/partyops/office-runtime"
find "$OFFICE_PACKAGE_RUNTIME" -type d -exec chmod 0755 {} +
find "$OFFICE_PACKAGE_RUNTIME" -type f -exec chmod 0644 {} +
while IFS= read -r -d '' office_candidate; do
  office_header="$(LC_ALL=C head -c 2 "$office_candidate" 2>/dev/null || true)"
  office_description="$(LC_ALL=C file -b "$office_candidate")"
  # LibreOffice 随包携带 libpython*.so.*-gdb.py。它虽然带 shebang，但只是
  # 调试器加载的辅助脚本，不是运行入口；若按 shebang 恢复执行位，后续
  # 共享库权限门禁会正确拒绝这个形似 .so 的文件。所有 *.so* 名称一律
  # 保持 0644，只恢复普通命名的启动脚本和真正的 ELF 可执行文件。
  if [[ "$office_candidate" != *.so* && "$office_header" == '#!' ]] ||
    [[ "$office_description" == *ELF* && "$office_description" == *executable* ]]; then
    chmod 0755 "$office_candidate"
  fi
done < <(find "$OFFICE_PACKAGE_RUNTIME/program" -maxdepth 1 -type f -print0)
# 私有 ELF 加载器是唯一允许带执行位的 *.so* 文件；它是 PartyOps 直接
# exec 的启动入口，其余共享库继续保持 0644。
if [[ "$ARCH" != loong64 ]]; then
  chmod 0755 "$OFFICE_PACKAGE_RUNTIME/private-runtime/$OFFICE_LOADER_NAME"
fi
EXPECTED_PAYLOAD_PATTERN='x86-64'
[[ "$ARCH" == arm64 ]] && EXPECTED_PAYLOAD_PATTERN='ARM aarch64'
[[ "$ARCH" == loong64 ]] && EXPECTED_PAYLOAD_PATTERN='LoongArch|Loongarch'
file "$PKG/opt/partyops/partyops" | grep -Eq "$EXPECTED_PAYLOAD_PATTERN" || {
  echo "便携载荷主程序架构与目标 $ARCH 不一致，拒绝封装。" >&2
  exit 2
}
[[ -s "$PKG/opt/partyops/update-public-key.txt" ]] || {
  echo "便携载荷缺少更新信任公钥，拒绝生成无法应用内升级的正式包。" >&2
  exit 2
}
for desktop_entry in partyops.desktop partyops-file.desktop partyops-client.desktop; do
  # Windows/DrvFS 检出可能带 CRLF。desktop-file-validate 在部分 UOS 版本会
  # 把节名末尾的 CR 当成格式错误，因此封包边界必须强制规范为 UTF-8/LF。
  sed 's/\r$//' "$ROOT/packaging/uos/$desktop_entry" \
    >"$PKG/usr/share/applications/$desktop_entry"
  if LC_ALL=C grep -q "$(printf '\r')" "$PKG/usr/share/applications/$desktop_entry"; then
    echo "桌面入口换行规范化失败：$desktop_entry" >&2
    exit 2
  fi
done
cp "$ROOT/packaging/uos/partyops.svg" "$PKG/usr/share/icons/hicolor/scalable/apps/partyops.svg"
cp "$ROOT/packaging/uos/partyops.service" "$ROOT/packaging/uos/partyops-updater.service" \
  "$PKG/lib/systemd/system/"
cp "$ROOT/packaging/linux/partyops-install-verify.service" \
  "$PKG/lib/systemd/system/"
cp "$ROOT/packaging/uos/cn.partyops.update.policy" "$PKG/usr/share/polkit-1/actions/"
# 源码可能位于不保存 POSIX 权限的 WSL DrvFS；复制后显式收敛静态配置，
# 避免 systemd 单元、桌面入口和 polkit 策略被误标为可执行文件。
chmod 0644 \
  "$PKG/usr/share/applications/partyops.desktop" \
  "$PKG/usr/share/applications/partyops-file.desktop" \
  "$PKG/usr/share/applications/partyops-client.desktop" \
  "$PKG/usr/share/icons/hicolor/scalable/apps/partyops.svg" \
  "$PKG/lib/systemd/system/partyops.service" \
  "$PKG/lib/systemd/system/partyops-updater.service" \
  "$PKG/lib/systemd/system/partyops-install-verify.service" \
  "$PKG/usr/share/polkit-1/actions/cn.partyops.update.policy"
cp "$ROOT/packaging/linux/post-install-selftest.sh" \
  "$ROOT/packaging/linux/post-install-services.sh" \
  "$ROOT/packaging/linux/post-install-verify.sh" \
  "$ROOT/packaging/linux/post-install-transaction.sh" \
  "$PKG/opt/partyops/"
chmod 0755 \
  "$PKG/opt/partyops/post-install-selftest.sh" \
  "$PKG/opt/partyops/post-install-services.sh" \
  "$PKG/opt/partyops/post-install-verify.sh" \
  "$PKG/opt/partyops/post-install-transaction.sh"
while IFS= read -r -d '' executable; do
  case "$executable" in
    "$PKG/opt/partyops/partyops"|"$PKG/opt/partyops/partyops-client"|\
    "$PKG/opt/partyops/partyops-wizard"|"$PKG/opt/partyops/partyops-updater"|\
    "$PKG/opt/partyops/start.sh"|"$PKG/opt/partyops/stop.sh"|\
    "$PKG/opt/partyops/desktop-launcher.sh"|\
    "$PKG/opt/partyops/open-local-file.sh"|\
    "$PKG/opt/partyops/install-desktop-shortcut.sh"|\
    "$PKG/opt/partyops/install-internal-ca.sh"|\
    "$PKG/opt/partyops/ocr/bin/tesseract"|\
    "$PKG/opt/partyops/formatter-host/partyops-document-formatter-host"|\
    "$PKG/opt/partyops/llama-server"|\
    "$PKG/opt/partyops/post-install-selftest.sh"|\
    "$PKG/opt/partyops/post-install-services.sh"|\
    "$PKG/opt/partyops/post-install-verify.sh"|\
    "$PKG/opt/partyops/post-install-transaction.sh"|\
    "$PKG/opt/partyops/office-runtime/"*) ;;
    "$PKG/opt/partyops/formatter-host/partyops-document-formatter-host.bin"|\
    "$PKG/opt/partyops/formatter-host/private/liblol/ld.so.1")
      [[ "$ARCH" == loong64 ]] || { echo "非 Loong64 载荷含私有 formatter 入口。" >&2; exit 2; } ;;
    *)
      echo "原生包包含未授权的可执行文件：$executable" >&2
      exit 2
      ;;
  esac
done < <(find "$PKG/opt/partyops" -type f -perm /111 -print0)
if [[ "$ARCH" == loong64 ]]; then
  FORBIDDEN_EXECUTABLE_SO="$(find "$PKG/opt/partyops" -type f -name '*.so*' -perm /111 \
    ! -path "$PKG/opt/partyops/formatter-host/private/liblol/ld.so.1" -print -quit)"
else
  FORBIDDEN_EXECUTABLE_SO="$(find "$PKG/opt/partyops" -type f -name '*.so*' -perm /111 \
    ! -path "$OFFICE_PACKAGE_RUNTIME/private-runtime/$OFFICE_LOADER_NAME" -print -quit)"
fi
if [[ -n "$FORBIDDEN_EXECUTABLE_SO" ]]; then
  echo "原生包共享库被错误标记为可执行文件，拒绝封装。" >&2
  exit 2
fi
"$PYTHON_BIN" "$ROOT/scripts/validate-source-formatter-runtime.py" \
  --runtime "$PKG/opt/partyops/formatter-host" \
  --platform linux --architecture "$ARCH"
if [[ "$ARCH" == loong64 ]]; then
  "$PYTHON_BIN" "$ROOT/scripts/verify-linux-elf-glibc.py" \
    --root "$PKG/opt/partyops" --max-glibc 2.38
  PARTYOPS_DATA_DIR="$BUILD/core-selftest-data" \
  PARTYOPS_ENVIRONMENT=production PARTYOPS_STRICT_SQLITE=true \
  PARTYOPS_SEED_DEMO=false \
  "$PKG/opt/partyops/partyops" --package-self-test
fi
(cd "$PKG/opt/partyops" && find . -type f ! -name release-files.sha256 -print0 | \
  sort -z | xargs -0 sha256sum >release-files.sha256)

if [[ "$FORMAT" == deb ]]; then
  PACKAGE_DESCRIPTION='原生离线主机、协同、中文 OCR 和公文处理；复杂本地智能未随包提供。'
  PACKAGE_LIBC_VERSION=2.38
  if [[ "$ARCH" != loong64 ]]; then
    PACKAGE_DESCRIPTION='原生离线主机、协同、中文 OCR、语义重排和本地 LLM。'
    PACKAGE_LIBC_VERSION=2.17
  fi
  mkdir -p "$PKG/DEBIAN"
  cat >"$PKG/DEBIAN/control" <<EOF
Package: partyops
Version: $DEB_VERSION
Section: office
Priority: optional
Architecture: $ARCH
Maintainer: PartyOps Local
Depends: libc6 (>= $PACKAGE_LIBC_VERSION), bash, systemd, util-linux, coreutils, iproute2, curl, xdg-utils, policykit-1
Description: 党建智办 PartyOps 局域网协同系统
 $PACKAGE_DESCRIPTION
EOF
  cp "$ROOT/packaging/linux/pre-install-stop.sh" "$PKG/DEBIAN/preinst"
  {
    cat "$ROOT/packaging/linux/post-install-configure.sh"
    printf '\n/opt/partyops/post-install-transaction.sh %s deb\n' "$ARCH"
  } >"$PKG/DEBIAN/postinst"
  {
    cat <<'EOF'
#!/bin/sh
set -e
case "${1:-}" in remove|deconfigure) ;; *) exit 0 ;; esac
export PARTYOPS_PACKAGE_REMOVE=1
EOF
    cat "$ROOT/packaging/linux/pre-install-stop.sh"
  } >"$PKG/DEBIAN/prerm"
  cat >"$PKG/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
systemctl daemon-reload >/dev/null 2>&1 || true
echo "PartyOps 业务数据保留在 /var/lib/partyops，卸载不会自动删除。" >&2
EOF
  chmod 0755 "$PKG/DEBIAN/preinst" "$PKG/DEBIAN/postinst" "$PKG/DEBIAN/prerm" "$PKG/DEBIAN/postrm"
  OUTPUT="$ARTIFACTS/PartyOps_1.4.5-rc.6_linux_${ARCH}.deb"
  if dpkg-deb --help 2>&1 | grep -q -- '--root-owner-group'; then
    dpkg-deb --root-owner-group --build "$PKG" "$OUTPUT"
  else
    # manylinux2014 的 dpkg-deb 早于 --root-owner-group。构建进程本身以
    # 隔离 root 运行时，先逐项证明载荷所有权，再使用兼容参数封装。
    if find "$PKG" \( ! -uid 0 -o ! -gid 0 \) -print -quit | grep -q .; then
      echo "旧版 dpkg-deb 环境中的载荷并非全部 root:root，拒绝封装。" >&2
      exit 2
    fi
    dpkg-deb --build "$PKG" "$OUTPUT"
  fi
else
  RPM_ARCH=x86_64
  [[ "$ARCH" == arm64 ]] && RPM_ARCH=aarch64
  PAYLOAD="$BUILD/partyops-payload.tar.gz"
  tar -czf "$PAYLOAD" -C "$PKG" .
  mkdir -p "$BUILD/rpmbuild/BUILD" "$BUILD/rpmbuild/RPMS" "$BUILD/rpmbuild/SOURCES" \
    "$BUILD/rpmbuild/SPECS" "$BUILD/rpmbuild/SRPMS"
  cp "$PAYLOAD" "$BUILD/rpmbuild/SOURCES/"
  RPM_PRE_SCRIPT="$(sed 's/%/%%/g' "$ROOT/packaging/linux/pre-install-stop.sh")"
  RPM_POST_SCRIPT="$(sed 's/%/%%/g' "$ROOT/packaging/linux/post-install-configure.sh")"
  cat >"$BUILD/rpmbuild/SPECS/partyops.spec" <<EOF
%global __os_install_post %{nil}
Name: partyops
Version: $RPM_VERSION
Release: %{partyops_release}
Summary: 党建智办 PartyOps 局域网协同系统
License: GPL-3.0-or-later AND AGPL-3.0-only
BuildArch: $RPM_ARCH
# 依赖只由下方经 glibc 2.17 实机门禁审计的清单定义。包内 Python、OCR、
# llama.cpp 与 LibreOffice 闭包不应被旧版 rpmbuild 误识别为宿主 Qt/KDE、
# UNO 或更高 glibc 依赖，否则国产系统会在运行前被错误拒装。
AutoReqProv: no
Requires: glibc >= 2.17, bash, systemd, util-linux, coreutils, iproute, curl, xdg-utils, polkit
Source0: partyops-payload.tar.gz

%description
原生离线主机、协同、中文 OCR、语义重排和本地 LLM。

%prep
%build
%install
mkdir -p %{buildroot}
tar -xzf %{SOURCE0} -C %{buildroot}

%pre
$RPM_PRE_SCRIPT

%post
$RPM_POST_SCRIPT
/opt/partyops/post-install-transaction.sh $ARCH rpm

%preun
if [ "\$1" -eq 0 ]; then
  export PARTYOPS_PACKAGE_REMOVE=1
$RPM_PRE_SCRIPT
fi

%postun
systemctl daemon-reload >/dev/null 2>&1 || true
if [ "\$1" -eq 0 ]; then
  echo "PartyOps 业务数据保留在 /var/lib/partyops，卸载不会自动删除。" >&2
fi

%files
/opt/partyops
/etc/partyops
/usr/share/applications/partyops.desktop
/usr/share/applications/partyops-file.desktop
/usr/share/applications/partyops-client.desktop
/usr/share/icons/hicolor/scalable/apps/partyops.svg
/usr/share/polkit-1/actions/cn.partyops.update.policy
/lib/systemd/system/partyops.service
/lib/systemd/system/partyops-updater.service
/lib/systemd/system/partyops-install-verify.service
%if 0%{?with_rollback_cache}
%dir %attr(0700,root,root) /var/cache/partyops
%dir %attr(0700,root,root) /var/cache/partyops/update-transactions
/var/cache/partyops/current.rpm
/var/cache/partyops/current.rpm.sha256
%endif
EOF
  # RPM 的脚本阶段拿不到原始安装包路径。先构建一个内容相同、Release
  # 略低且不递归包含自身的回滚包，再把它嵌入正式包。首次升级失败时可
  # 降级到该包；后续成功升级会用刚验证过的正式制品原子更新此缓存。
  # 回滚种子与稳定版保持同一 Version，只降低 RPM Release；这样首次安装
  # 失败可以恢复二进制，同时不会被运行时版本门禁误判成 rc 旧版本。
  SEED_RELEASE="0.rc.2.0"
  rpmbuild \
    --target "$RPM_ARCH" \
    --define "_topdir $BUILD/rpmbuild" \
    --define "partyops_release $SEED_RELEASE" \
    --define "with_rollback_cache 0" \
    -bb "$BUILD/rpmbuild/SPECS/partyops.spec"
  SEED_RPM="$BUILD/rpmbuild/RPMS/$RPM_ARCH/partyops-$RPM_VERSION-$SEED_RELEASE.$RPM_ARCH.rpm"
  [[ -f "$SEED_RPM" ]] || {
    echo "RPM 回滚种子包未生成，拒绝构建不可回滚制品。" >&2
    exit 2
  }
  mkdir -p "$PKG/var/cache/partyops/update-transactions"
  cp "$SEED_RPM" "$PKG/var/cache/partyops/current.rpm"
  sha256sum "$SEED_RPM" | awk '{print $1}' >"$PKG/var/cache/partyops/current.rpm.sha256"
  chmod 0700 "$PKG/var/cache/partyops" "$PKG/var/cache/partyops/update-transactions"
  chmod 0644 "$PKG/var/cache/partyops/current.rpm"
  chmod 0644 "$PKG/var/cache/partyops/current.rpm.sha256"
  tar -czf "$PAYLOAD" -C "$PKG" .
  cp "$PAYLOAD" "$BUILD/rpmbuild/SOURCES/partyops-payload.tar.gz"
  rpmbuild \
    --target "$RPM_ARCH" \
    --define "_topdir $BUILD/rpmbuild" \
    --define "partyops_release $RPM_RELEASE" \
    --define "with_rollback_cache 1" \
    -bb "$BUILD/rpmbuild/SPECS/partyops.spec"
  OUTPUT="$ARTIFACTS/PartyOps-1.4.5-0.rc.6.1.${RPM_ARCH}.rpm"
  cp "$BUILD/rpmbuild/RPMS/$RPM_ARCH/partyops-$RPM_VERSION-$RPM_RELEASE.$RPM_ARCH.rpm" "$OUTPUT"
fi
if [[ "$FORMAT" == deb ]]; then
  PACKAGE_IDENTITY="$(dpkg-deb --field "$OUTPUT" Package Version Architecture | tr '\n' '|')"
  [[ "$PACKAGE_IDENTITY" == "Package: partyops|Version: $DEB_VERSION|Architecture: $ARCH|" ]] || {
    echo "DEB 元数据与冻结版本/架构不一致：$PACKAGE_IDENTITY" >&2
    exit 2
  }
else
  PACKAGE_IDENTITY="$(rpm -qp --queryformat '%{NAME}|%{VERSION}|%{RELEASE}|%{ARCH}' "$OUTPUT")"
  [[ "$PACKAGE_IDENTITY" == "partyops|$RPM_VERSION|$RPM_RELEASE|$RPM_ARCH" ]] || {
    echo "RPM 元数据与冻结版本/架构不一致：$PACKAGE_IDENTITY" >&2
    exit 2
  }
fi
(
  cd "$(dirname "$OUTPUT")"
  output_name="$(basename "$OUTPUT")"
  sha256sum "$output_name" >"$output_name.sha256"
)
echo "原生安装包已生成：$OUTPUT"
