#!/usr/bin/env bash
set -euo pipefail
umask 077
# 仅批准的 ARM15 未签名候选；不执行发布、GUI 或真实 WPS 操作。
[[ "${PARTYOPS_CANDIDATE_CONFIRM:-}" == ARM_RC6_CANDIDATE_ONLY ]] || exit 2
[[ "$(uname -s)/$(uname -m)" == Darwin/arm64 ]] || exit 2
CHECKOUT="${PARTYOPS_CANDIDATE_CHECKOUT:?}"
BUNDLE="${RUNNER_TEMP:?}/partyops-arm-candidate-${GITHUB_RUN_ID:?}-${GITHUB_RUN_ATTEMPT:?}"
QA="$CHECKOUT/qa/vm-lab/release-preparation/apple-silicon-build-20261009"
HELPER="$QA/frozen-source.py"
python3.11 "$HELPER" stage --checkout "$CHECKOUT" --input-root "$QA" \
  --approval candidate-input-approval.json --approval-sha256 "${PARTYOPS_CANDIDATE_APPROVAL_SHA256:?}" \
  --candidate-commit "${GITHUB_SHA:?}" --bundle "$BUNDLE"
SOURCE="$BUNDLE/application-source"
export HOME="$BUNDLE/runtime-inputs/home" TMPDIR="$BUNDLE/runtime-inputs/tmp"
mkdir -p "$HOME" "$TMPDIR" "$BUNDLE/reports"
export COREPACK_HOME="$HOME/corepack" UV_CACHE_DIR="$HOME/uv-cache"
export MACOSX_DEPLOYMENT_TARGET=15.0 PARTYOPS_BUILD_JOBS=3
export HOMEBREW_NO_AUTO_UPDATE=1 HOMEBREW_NO_ANALYTICS=1 HOMEBREW_NO_INSTALL_CLEANUP=1
export HOMEBREW_CACHE="$RUNNER_TEMP/partyops-arm-sdk-isolated/cache"
python3.11 -m pip install --disable-pip-version-check 'uv==0.8.20'
corepack enable
manager="$(python3.11 -c 'import json,sys; print(json.load(open(sys.argv[1]))["packageManager"])' "$SOURCE/frontend/package.json")"
[[ "$manager" == pnpm@11.9.0 ]] || { printf '%s\n' '[ARM_CANDIDATE_FRONTEND_MANAGER_MISMATCH] 未批准的前端包管理器。' >&2; exit 2; }
corepack prepare "$manager" --activate
command -v cmake >/dev/null || brew install cmake
command -v ninja >/dev/null || brew install ninja
# 只在可丢弃 ARM runner 安装官方开发工具，固定原 bottle 与 profile仍逐项验证。
brew install --force-bottle mono
prefix="$(brew --prefix mono)"
export PATH="$prefix/bin:$PATH" PKG_CONFIG_PATH="$prefix/lib/pkgconfig"
[[ "$(pkg-config --modversion mono-2)" == 6.14.1 ]] || exit 2
raw="$RUNNER_TEMP/partyops-arm-sdk-isolated/bottle/mono/6.14.1"
export PARTYOPS_MONO_CONFIG_ROOT="$raw/etc/mono"
export PARTYOPS_MONO_FRAMEWORK_ROOT="$raw/lib/mono/4.5"
export PARTYOPS_MONO_LIBRARY_ROOT="$raw/lib"
export PARTYOPS_MONO_INCLUDE_ROOT="$raw/include/mono-2.0"
export PARTYOPS_MONO_BOTTLE_ARCHIVE="$(brew --cache --bottle-tag=arm64_sequoia mono)"
(
  cd "$SOURCE/frontend"
  corepack pnpm install --frozen-lockfile
  corepack pnpm run build
)
python3.11 "$HELPER" freeze --bundle "$BUNDLE"
# 外部参数来自本次受批准冻结进程的独立收据，不从待验 manifest 自动补数量。
read -r manifest_sha file_count <<<"$(python3.11 -c 'import json,sys; d=json.load(open(sys.argv[1])); print(d["manifest_sha256"], d["file_count"])' "$BUNDLE/freeze-receipt.json")"
python3.11 "$HELPER" verify --bundle "$BUNDLE" --manifest-sha256 "$manifest_sha" --file-count "$file_count"
RUNTIMES="$BUNDLE/runtime-inputs/native"
bash "$SOURCE/packaging/macos/build-native-runtimes.sh" --architecture arm64 --output-root "$RUNTIMES"
bash "$SOURCE/scripts/prepare-libreoffice-macos.sh" --architecture arm64 --output-root "$RUNTIMES"
FORMATTER="$BUNDLE/runtime-inputs/formatter-arm64"
bash "$SOURCE/scripts/build-document-formatter-host-unix.sh" \
  --managed-runtime "$BUNDLE/runtime-inputs/managed" --output "$FORMATTER" --platform macos --architecture arm64
[[ "$(lipo -archs "$FORMATTER/partyops-document-formatter-host")" == arm64 ]] || exit 2
python3.11 "$SOURCE/scripts/validate-source-formatter-runtime.py" --runtime "$FORMATTER" --platform macos --architecture arm64
export PARTYOPS_MACOS_OCR_RUNTIME="$RUNTIMES/ocr-arm64"
export PARTYOPS_MACOS_LLAMA_RUNTIME="$RUNTIMES/llama-arm64"
export PARTYOPS_MACOS_OFFICE_RUNTIME="$RUNTIMES/office-arm64"
export PARTYOPS_MACOS_FORMATTER_RUNTIME="$FORMATTER"
OUTPUT="$SOURCE/artifacts/arm15-candidate-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"
python3.11 "$HELPER" verify --bundle "$BUNDLE" --manifest-sha256 "$manifest_sha" --file-count "$file_count"
build_status=0
bash "$SOURCE/packaging/macos/build-pkg.sh" --architecture arm64 --unsigned-candidate \
  --output-directory "$OUTPUT" --source-inputs-manifest "$BUNDLE/source-inputs.json" \
  --source-inputs-sha256 "$manifest_sha" --source-inputs-file-count "$file_count" \
  >"$BUNDLE/reports/build.log" 2>&1 || build_status=$?
# 即使打包已产生诊断文件，重复前端构建造成漂移也不能上传为通过候选。
source_status=0
python3.11 "$HELPER" verify --bundle "$BUNDLE" --manifest-sha256 "$manifest_sha" --file-count "$file_count" \
  >"$BUNDLE/reports/postbuild-source.log" 2>&1 || source_status=$?
python3.11 - "$BUNDLE/reports/build-status.json" "$build_status" "$source_status" <<'PY'
import json,sys
from pathlib import Path
Path(sys.argv[1]).write_text(json.dumps({"build_exit_code":int(sys.argv[2]),"postbuild_source_exit_code":int(sys.argv[3]),
 "source_drift":sys.argv[3]!="0","gui_wps_validated":False,"publication_authorized":False}, indent=2)+"\n")
PY
if [[ "$build_status" != 0 || "$source_status" != 0 ]]; then
  # 仅本次隔离构建日志的有界脱敏摘要；不上传工作目录或原始完整日志。
  python3.11 - "$BUNDLE/reports" <<'DIAGNOSTICS_PY'
import json,re,sys
from pathlib import Path
root=Path(sys.argv[1])
summaries={}
for name in ('build.log','postbuild-source.log'):
    text='\n'.join((root/name).read_text(encoding='utf-8',errors='replace').splitlines()[-100:])[-24000:]
    text=re.sub(r'https?://\S+','[URL]',text)
    text=re.sub(r'(?im)^.*(?:authorization|password|secret|token).*$','[敏感字段行已隐藏]',text)
    text=text.replace(str(root.parent),'$CANDIDATE')
    summaries[name]=text
(root/'failure-summary.json').write_text(json.dumps(summaries,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(summaries,ensure_ascii=False))
DIAGNOSTICS_PY
  exit 2
fi
PACKAGE="$OUTPUT/PartyOps_1.4.5-rc.6_macos_arm64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg"
test -s "$PACKAGE"
[[ ! -e /Applications/PartyOps.app ]] || { printf '%s\n' '[ARM_CANDIDATE_INSTALL_DIR_OCCUPIED] 拒绝覆盖已有应用。' >&2; exit 2; }
(
  cd "$OUTPUT"
  shasum -a 256 -c "$(basename "$PACKAGE").sha256"
)
sudo /usr/sbin/installer -pkg "$PACKAGE" -target / >"$BUNDLE/reports/install.log" 2>&1
APP=/Applications/PartyOps.app
codesign --verify --deep --strict --verbose=2 "$APP"
bash "$SOURCE/packaging/macos/validate-bundle.sh" "$APP" arm64 >"$BUNDLE/reports/installed-bundle.log" 2>&1
python3.11 "$QA/candidate-cli-smoke.py" --app "$APP" --workspace "$BUNDLE/reports"
python3.11 "$HELPER" verify --bundle "$BUNDLE" --manifest-sha256 "$manifest_sha" --file-count "$file_count"
python3.11 - "$PACKAGE" "$BUNDLE/reports/candidate-validation.json" "$manifest_sha" "$file_count" <<'PY'
import hashlib,json,sys
from pathlib import Path
package,output,manifest_sha,count=sys.argv[1:]
Path(output).write_text(json.dumps({"schema":1,"package_sha256":hashlib.sha256(Path(package).read_bytes()).hexdigest(),
 "source_input_manifest_sha256":manifest_sha,"source_input_file_count":int(count),
 "postbuild_source_verified":True,"installer_exit_code":0,"installed_bundle_verifier_exit_code":0,
 "installed_cli_smoke_passed":True,"architecture":"arm64","minimum_macos":"15.0",
 "gui_wps_validated":False,"publication_authorized":False},indent=2)+"\n")
PY
printf 'candidate_verified=true\ncandidate_output=%s\n' "$OUTPUT" >>"${GITHUB_OUTPUT:?}"
