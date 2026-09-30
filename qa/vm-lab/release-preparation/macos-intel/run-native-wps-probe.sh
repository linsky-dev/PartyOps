#!/bin/bash
# 在一次性原生 macOS runner 上安装锁定版 WPS 并运行既有最小 JSAPI 探针。
# 受限处理 WPS 欢迎/许可界面；不修改系统安全设置，不上传任何产物。
set -Eeuo pipefail
umask 077

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../../../.." && pwd -P)"
RUNNER_TEMP=${RUNNER_TEMP:-}
if [[ -z "$RUNNER_TEMP" || ! -d "$RUNNER_TEMP" ]]; then
  printf '%s\n' 'RUNNER_TEMP must name an existing directory.' >&2
  exit 2
fi
RUNNER_TEMP="$(cd -- "$RUNNER_TEMP" && pwd -P)"
WORK_DIR="$(/usr/bin/mktemp -d "$RUNNER_TEMP/partyops-wps-native-probe.XXXXXX")"
MOUNT_DIR="$WORK_DIR/mount"
mkdir "$MOUNT_DIR"

PHASE=preflight
FAILURE_CODE=
ARCH=
EXPECTED_SHA=
ACTUAL_SHA=
DMG_MOUNTED=0
RELAY_HTTP=
RELAY_VERSION=
PYTHON=
STATUS_FILE="$WORK_DIR/status.json"
ARTIFACT_MANIFEST="$WORK_DIR/artifact-manifest.txt"

write_artifact_manifest() {
  : >"$ARTIFACT_MANIFEST"
  while IFS= read -r artifact; do
    case "$artifact" in
      *.json|*.txt|*.log|*.png|*.docx|*.headers|*.body)
        printf '%s\n' "${artifact#"$WORK_DIR"/}" >>"$ARTIFACT_MANIFEST"
        ;;
    esac
  done < <(/usr/bin/find "$WORK_DIR" -type f -print | /usr/bin/sort)
}

fail() {
  FAILURE_CODE=$1
  printf 'probe_wrapper_failure=%s phase=%s\n' "$FAILURE_CODE" "$PHASE" >&2
  exit 1
}

write_status() {
  local exit_code=$1
  "$PYTHON" - "$STATUS_FILE" "$PHASE" "$exit_code" "$ARCH" "$EXPECTED_SHA" "$ACTUAL_SHA" "$FAILURE_CODE" "$RELAY_HTTP" "$RELAY_VERSION" "$WORK_DIR" <<'PY'
import json, pathlib, sys, time
path, phase, code, arch, expected, actual, failure, http, version, work = sys.argv[1:]
payload = {
    "schema": 1,
    "status": "passed" if code == "0" else "failed",
    "exit_code": int(code),
    "phase": phase,
    "architecture": arch or None,
    "installer_sha256_expected": expected or None,
    "installer_sha256_actual": actual or None,
    "failure_code": failure or None,
    "relay_http_status": http or None,
    "relay_version": version or None,
    "work_directory": work,
    "finished_epoch": int(time.time()),
}
pathlib.Path(path).write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
PY
}

collect_relay_diagnostics() {
  local label url http curl_rc pid executable_path endpoint process_name expected_paths

  if [[ -x /usr/sbin/lsof ]]; then
    /usr/sbin/lsof -nP -iTCP:58890 -iTCP:58891 -sTCP:LISTEN \
      >"$WORK_DIR/relay-diagnostic-all-listeners.log" \
      2>"$WORK_DIR/relay-diagnostic-all-listeners.stderr.log"
    printf 'lsof_exit=%s\n' "$?" >>"$WORK_DIR/relay-diagnostic-all-listeners.log"

    : >"$WORK_DIR/relay-diagnostic-official-wps-listeners.log"
    : >"$WORK_DIR/relay-diagnostic-official-wps-listeners.stderr.log"
    for process_name in wpsoffice wpscloudsvr; do
      if [[ "$process_name" == wpsoffice ]]; then
        expected_paths="$DEST_APP/Contents/MacOS/$EXECUTABLE"
      else
        expected_paths="$("$PYTHON" - "$DEST_APP" <<'PY' 2>>"$WORK_DIR/relay-diagnostic-official-wps-listeners.stderr.log"
import os
import pathlib
import plistlib
import sys

root = pathlib.Path(sys.argv[1]).resolve(strict=True)
for directory, children, _ in os.walk(root, followlinks=False):
    children[:] = [name for name in children if not (pathlib.Path(directory) / name).is_symlink()]
    app = pathlib.Path(directory)
    if app == root or app.suffix.lower() != ".app":
        continue
    info = app / "Contents" / "Info.plist"
    if not info.is_file() or info.is_symlink():
        continue
    with info.open("rb") as stream:
        executable = plistlib.load(stream).get("CFBundleExecutable", "")
    if not isinstance(executable, str) or executable.lower() != "wpscloudsvr":
        continue
    binary = (app / "Contents" / "MacOS" / executable).resolve(strict=False)
    if binary.is_relative_to(root) and binary.is_file() and not any(char in str(binary) for char in "\r\n\t"):
        print(binary)
PY
)"
      fi
      while IFS= read -r pid; do
        [[ -n "$pid" ]] || continue
        while IFS= read -r executable_path; do
          [[ -n "$executable_path" ]] || continue
          if /usr/sbin/lsof -nP -a -p "$pid" -d txt -Fn \
            2>>"$WORK_DIR/relay-diagnostic-official-wps-listeners.stderr.log" |
            /usr/bin/grep -Fx -- "n$executable_path" >/dev/null; then
            printf 'official_process=%s pid=%s executable=%s\n' \
              "$process_name" "$pid" "$executable_path" \
              >>"$WORK_DIR/relay-diagnostic-official-wps-listeners.log"
            /usr/sbin/lsof -nP -a -p "$pid" -iTCP -sTCP:LISTEN \
              >>"$WORK_DIR/relay-diagnostic-official-wps-listeners.log" \
              2>>"$WORK_DIR/relay-diagnostic-official-wps-listeners.stderr.log"
            break
          fi
        done <<<"$expected_paths"
      done < <(/usr/bin/pgrep -x "$process_name" 2>/dev/null || true)
    done
  else
    printf 'lsof_available=false\n' >"$WORK_DIR/relay-diagnostic-all-listeners.log"
    printf 'lsof_available=false\n' >"$WORK_DIR/relay-diagnostic-official-wps-listeners.log"
  fi

  for endpoint in \
    'ipv4-http http://127.0.0.1:58890/version' \
    'ipv4-https https://127.0.0.1:58891/version' \
    'ipv6-http http://[::1]:58890/version' \
    'ipv6-https https://[::1]:58891/version'; do
    label=${endpoint%% *}
    url=${endpoint#* }
    http=none
    http="$(/usr/bin/curl --noproxy '*' --silent --show-error --max-time 3 \
      -D "$WORK_DIR/relay-diagnostic-${label}.headers" \
      -o "$WORK_DIR/relay-diagnostic-${label}.body" \
      -w '%{http_code}' -X POST -H 'Content-Type: application/json' --data '{}' \
      "$url" 2>"$WORK_DIR/relay-diagnostic-${label}.curl.stderr.log")"
    curl_rc=$?
    # 将 curl 退出码与 HTTP 状态写入独立诊断记录，不改变原验收状态。
    printf 'endpoint=%s\ncurl_exit=%s\nhttp_status=%s\ntls_verification=default\nretry=none\n' \
      "$label" "$curl_rc" "${http:-none}" >"$WORK_DIR/relay-diagnostic-${label}.log"
  done
}

on_exit() {
  local rc=$?
  trap - EXIT
  set +e
  if [[ $DMG_MOUNTED == 1 ]]; then
    /usr/bin/hdiutil detach "$MOUNT_DIR" -quiet >>"$WORK_DIR/dmg-detach.log" 2>&1
    DMG_MOUNTED=0
  fi
  if [[ $rc -ne 0 ]]; then
    printf 'status=failed\nphase=%s\nexit_code=%s\nfailure_code=%s\n' \
      "$PHASE" "$rc" "${FAILURE_CODE:-UNEXPECTED_COMMAND_FAILURE}" >"$WORK_DIR/failure.txt"
    : >"$WORK_DIR/wps-processes.txt"
    for process_name in wpsoffice wpscloudsvr wps; do
      /usr/bin/pgrep -x -l "$process_name" >>"$WORK_DIR/wps-processes.txt" 2>&1
    done
    if [[ -x /usr/sbin/screencapture ]]; then
      /usr/sbin/screencapture -x "$WORK_DIR/failure-screen.png" >>"$WORK_DIR/screencapture.log" 2>&1
    fi
    if [[ -f "$WORK_DIR/relay-start.txt" && ( "$PHASE" == relay-version || "$PHASE" == probe ) ]]; then
      collect_relay_diagnostics
    fi
    if [[ -n "${PYTHON:-}" && -x "$PYTHON" ]]; then
      write_status "$rc" >>"$WORK_DIR/status-write.log" 2>&1
    fi
  fi
  write_artifact_manifest
  printf 'native_wps_probe_workdir=%s\n' "$WORK_DIR"
  if [[ $rc -ne 0 ]]; then
    printf 'native_wps_probe_status=failed phase=%s code=%s\n' "$PHASE" "${FAILURE_CODE:-UNEXPECTED_COMMAND_FAILURE}"
  fi
  exit "$rc"
}
trap on_exit EXIT

PHASE=toolchain
for required in /usr/bin/uname /usr/bin/stat /bin/launchctl /usr/bin/curl /usr/bin/shasum /usr/bin/hdiutil /usr/bin/codesign /usr/bin/lipo /usr/bin/ditto /usr/bin/open /usr/bin/sudo /usr/bin/osascript; do
  [[ -x "$required" ]] || fail "REQUIRED_TOOL_MISSING:${required}"
done
if command -v python3.11 >/dev/null 2>&1; then
  PYTHON="$(command -v python3.11)"
elif command -v python3 >/dev/null 2>&1; then
  PYTHON="$(command -v python3)"
elif [[ -x /usr/bin/python3 ]]; then
  PYTHON=/usr/bin/python3
else
  fail PYTHON3_UNAVAILABLE
fi
"$PYTHON" -c 'import sys, zoneinfo; print(sys.version); assert sys.version_info >= (3, 9)' >"$WORK_DIR/python-version.txt" 2>&1 || fail PYTHON_VERSION_UNSUPPORTED

PHASE=architecture
ARCH="$(/usr/bin/uname -m)"
case "$ARCH" in
  x86_64)
    DMG_URL='https://package.mac.wpscdn.cn/mac_wps_pkg/12.1.29166/WPS_Office_12.1.29166(29166)_x64.dmg'
    EXPECTED_SHA='79d121a5269ce2b2b0422fbf0d61c1e21b382c1b46b41be24ce3e4b8021b8d46'
    ;;
  arm64)
    DMG_URL='https://package.mac.wpscdn.cn/mac_wps_pkg/12.1.29166/WPS_Office_12.1.29166(29166)_arm64.dmg'
    EXPECTED_SHA='f493b07f48e4911d5836d51afd8b4254e2b12aa16d2d0816b1474c3fa0785c31'
    ;;
  *) fail "UNSUPPORTED_NATIVE_ARCH:${ARCH}" ;;
esac

PHASE=gui-session
GUI_USER="$(/usr/bin/stat -f '%Su' /dev/console)" || fail GUI_CONSOLE_USER_UNAVAILABLE
CURRENT_USER="$(/usr/bin/id -un)" || fail GUI_USER_UNAVAILABLE
[[ "$GUI_USER" == "$CURRENT_USER" && "$GUI_USER" != root && "$GUI_USER" != loginwindow ]] || fail GUI_CONSOLE_USER_MISMATCH
GUI_UID="$(/usr/bin/id -u)"
/bin/launchctl print "gui/$GUI_UID" >/dev/null 2>"$WORK_DIR/gui-launchctl.stderr.log" || fail GUI_LAUNCHCTL_DOMAIN_UNAVAILABLE

PHASE=preinstalled-wps-check
for candidate in \
  /Applications/wpsoffice.app \
  '/Applications/WPS Office.app' \
  "$HOME/Applications/wpsoffice.app" \
  "$HOME/Applications/WPS Office.app"; do
  [[ ! -e "$candidate" ]] || fail "PREEXISTING_WPS_REFUSE_OVERWRITE:${candidate}"
done
: >"$WORK_DIR/preexisting-wps-processes.txt"
for process_name in wpsoffice wpscloudsvr wps; do
  if /usr/bin/pgrep -x -l "$process_name" >>"$WORK_DIR/preexisting-wps-processes.txt" 2>&1; then
    fail PREEXISTING_WPS_PROCESS_REFUSE_OVERWRITE
  fi
done
if /usr/bin/mdfind 'kMDItemCFBundleIdentifier == "com.kingsoft.wpsoffice.mac"' >"$WORK_DIR/preinstalled-wps-search.txt" 2>&1; then
  [[ ! -s "$WORK_DIR/preinstalled-wps-search.txt" ]] || fail PREEXISTING_WPS_REFUSE_OVERWRITE
fi

PHASE=download
CACHE_DIR="$RUNNER_TEMP/partyops-wps-inputs"
/bin/mkdir -p "$CACHE_DIR"
CACHE_DMG="$CACHE_DIR/WPS_Office_12.1.29166_${ARCH}.dmg"
DMG="$CACHE_DMG"
DOWNLOAD_OK=0
if [[ -L "$CACHE_DMG" ]]; then
  fail OFFICIAL_DMG_CACHE_LINK_REJECTED
fi
if [[ -f "$CACHE_DMG" ]]; then
  ACTUAL_SHA="$(/usr/bin/shasum -a 256 "$CACHE_DMG" | /usr/bin/awk '{print $1}')"
  printf 'source=cache bytes=%s sha256=%s\n' "$(/usr/bin/stat -f '%z' "$CACHE_DMG")" "$ACTUAL_SHA" >"$WORK_DIR/download-source.txt"
  [[ "$ACTUAL_SHA" == "$EXPECTED_SHA" ]] || fail OFFICIAL_DMG_CACHE_SHA256_MISMATCH
  DOWNLOAD_OK=1
elif [[ "${PARTYOPS_WPS_CACHE_HIT:-}" == true ]]; then
  fail OFFICIAL_DMG_CACHE_FILE_MISSING
fi
# 两次各最多十分钟，为三十分钟 job 留出安装、探针及诊断上传时间。
for DOWNLOAD_ATTEMPT in 1 2; do
  [[ "$DOWNLOAD_OK" == 0 ]] || break
  DOWNLOAD_PART="$WORK_DIR/download-attempt-${DOWNLOAD_ATTEMPT}.part"
  set +e
  DOWNLOAD_HTTP="$(/usr/bin/curl --fail --location --silent --show-error \
    --connect-timeout 30 --max-time 600 -w '%{http_code}' \
    -D "$WORK_DIR/download-attempt-${DOWNLOAD_ATTEMPT}.headers" \
    --output "$DOWNLOAD_PART" "$DMG_URL" \
    2>"$WORK_DIR/download-attempt-${DOWNLOAD_ATTEMPT}.stderr.log")"
  DOWNLOAD_RC=$?
  set -e
  DOWNLOAD_BYTES=0
  if [[ -f "$DOWNLOAD_PART" ]]; then
    DOWNLOAD_BYTES="$(/usr/bin/stat -f '%z' "$DOWNLOAD_PART")"
  fi
  printf 'attempt=%s curl_exit=%s http=%s bytes=%s\n' \
    "$DOWNLOAD_ATTEMPT" "$DOWNLOAD_RC" "${DOWNLOAD_HTTP:-none}" "$DOWNLOAD_BYTES" >>"$WORK_DIR/download-attempts.log"
  if [[ $DOWNLOAD_RC -eq 0 && "$DOWNLOAD_HTTP" == 200 ]]; then
    ACTUAL_SHA="$(/usr/bin/shasum -a 256 "$DOWNLOAD_PART" | /usr/bin/awk '{print $1}')"
    [[ "$ACTUAL_SHA" == "$EXPECTED_SHA" ]] || fail OFFICIAL_DMG_SHA256_MISMATCH
    /bin/mv "$DOWNLOAD_PART" "$CACHE_DMG"
    printf 'source=download attempt=%s bytes=%s sha256=%s\n' "$DOWNLOAD_ATTEMPT" "$DOWNLOAD_BYTES" "$ACTUAL_SHA" >"$WORK_DIR/download-source.txt"
    DOWNLOAD_OK=1
    break
  fi
  if [[ $DOWNLOAD_ATTEMPT -eq 1 && "$DOWNLOAD_HTTP" == 429 ]]; then
    /bin/sleep 20
    continue
  fi
  if [[ $DOWNLOAD_ATTEMPT -eq 1 && ( "$DOWNLOAD_HTTP" =~ ^5[0-9][0-9]$ || $DOWNLOAD_RC -eq 28 ) ]]; then
    /bin/sleep 2
    continue
  fi
  fail OFFICIAL_DMG_DOWNLOAD_FAILED
done
[[ "$DOWNLOAD_OK" == 1 && -f "$DMG" ]] || fail OFFICIAL_DMG_DOWNLOAD_FAILED

PHASE=dmg-verify-and-mount
/usr/bin/hdiutil attach -readonly -nobrowse -mountpoint "$MOUNT_DIR" "$DMG" >"$WORK_DIR/dmg-attach.log" 2>&1 || fail DMG_READONLY_MOUNT_FAILED
DMG_MOUNTED=1
APP_CANDIDATES=()
for candidate in "$MOUNT_DIR"/*.app; do
  [[ -d "$candidate" ]] && APP_CANDIDATES+=("$candidate")
done
if [[ ${#APP_CANDIDATES[@]} -eq 0 ]]; then
  for candidate in "$MOUNT_DIR"/*/*.app; do
    [[ -d "$candidate" ]] && APP_CANDIDATES+=("$candidate")
  done
fi
[[ ${#APP_CANDIDATES[@]} -eq 1 ]] || fail "DMG_APP_BUNDLE_COUNT:${#APP_CANDIDATES[@]}"
SOURCE_APP=${APP_CANDIDATES[0]}
/usr/bin/codesign --verify --deep --strict --verbose=2 "$SOURCE_APP" >"$WORK_DIR/codesign-verify.stdout.log" 2>"$WORK_DIR/codesign-verify.stderr.log" || fail CODESIGN_VERIFY_FAILED
/usr/bin/codesign -dv --verbose=4 "$SOURCE_APP" >"$WORK_DIR/codesign-details.stdout.log" 2>"$WORK_DIR/codesign-details.txt" || fail CODESIGN_DETAILS_FAILED
TEAM_ID="$(/usr/bin/sed -n 's/^TeamIdentifier=//p' "$WORK_DIR/codesign-details.txt" | /usr/bin/head -n 1)"
[[ "$TEAM_ID" == YK4WKE5WAM ]] || fail "CODESIGN_TEAM_ID_MISMATCH:${TEAM_ID:-missing}"
EXECUTABLE="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$SOURCE_APP/Contents/Info.plist" 2>"$WORK_DIR/bundle-executable.stderr.log")" || fail WPS_BUNDLE_EXECUTABLE_MISSING
EXECUTABLE_PATH="$SOURCE_APP/Contents/MacOS/$EXECUTABLE"
[[ -f "$EXECUTABLE_PATH" ]] || fail WPS_EXECUTABLE_FILE_MISSING
/usr/bin/lipo -archs "$EXECUTABLE_PATH" >"$WORK_DIR/wps-executable-architectures.txt" 2>&1 || fail WPS_EXECUTABLE_ARCH_UNKNOWN
case " $(cat "$WORK_DIR/wps-executable-architectures.txt") " in
  *" $ARCH "*) ;;
  *) fail "WPS_EXECUTABLE_ARCH_MISMATCH:${ARCH}" ;;
esac

PHASE=install-app
DEST_APP=/Applications/wpsoffice.app
[[ ! -e "$DEST_APP" ]] || fail PREEXISTING_WPS_REFUSE_OVERWRITE
/usr/bin/sudo -n /usr/bin/ditto "$SOURCE_APP" "$DEST_APP" >"$WORK_DIR/app-copy.stdout.log" 2>"$WORK_DIR/app-copy.stderr.log" || fail WPS_APP_COPY_FAILED
/usr/bin/codesign --verify --deep --strict "$DEST_APP" >"$WORK_DIR/installed-app-codesign.stdout.log" 2>"$WORK_DIR/installed-app-codesign.stderr.log" || fail INSTALLED_WPS_SIGNATURE_INVALID
/usr/bin/hdiutil detach "$MOUNT_DIR" -quiet >"$WORK_DIR/dmg-detach.log" 2>&1 || fail DMG_DETACH_FAILED
DMG_MOUNTED=0

PHASE=launch-wps
/usr/bin/open -a "$DEST_APP" >"$WORK_DIR/open-wps.stdout.log" 2>"$WORK_DIR/open-wps.stderr.log" || fail WPS_OPEN_FAILED
/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$DEST_APP/Contents/Info.plist" >"$WORK_DIR/wps-version.txt" 2>"$WORK_DIR/wps-version.stderr.log" || fail WPS_VERSION_READ_FAILED
[[ "$(cat "$WORK_DIR/wps-version.txt")" == 12.1.29166 ]] || fail WPS_VERSION_MISMATCH

PHASE=welcome-inspect
WELCOME_SCRIPT="$SCRIPT_DIR/handle-wps-welcome.applescript"
[[ -f "$WELCOME_SCRIPT" ]] || fail WPS_WELCOME_SCRIPT_MISSING
if [[ -x /usr/sbin/screencapture ]]; then
  /usr/sbin/screencapture -x "$WORK_DIR/welcome-before.png" >"$WORK_DIR/welcome-before-screenshot.log" 2>&1 || true
fi
WELCOME_READY=0
for _ in {1..20}; do
  if ! /usr/bin/pgrep -x wpsoffice >/dev/null 2>&1; then
    /bin/sleep 1
    continue
  fi
  /usr/bin/osascript "$WELCOME_SCRIPT" inspect >"$WORK_DIR/welcome-before.txt" 2>"$WORK_DIR/welcome-inspect.stderr.log" || fail WPS_WELCOME_ACCESSIBILITY_OR_TREE_FAILED
  if /usr/bin/grep -q '^matching_welcome_windows=1$' "$WORK_DIR/welcome-before.txt"; then
    WELCOME_READY=1
    break
  fi
  /usr/bin/grep -q '^matching_welcome_windows=0$' "$WORK_DIR/welcome-before.txt" || fail WPS_WELCOME_CONTROLS_NOT_UNIQUE
  /bin/sleep 1
done
[[ "$WELCOME_READY" == 1 ]] || fail WPS_WELCOME_CONTROLS_MISSING

PHASE=welcome-consent-mouse
CONSENT_MOUSE="$RUNNER_TEMP/wps-consent-click"
[[ -x "$CONSENT_MOUSE" ]] || fail WPS_CONSENT_MOUSE_HELPER_MISSING
"$CONSENT_MOUSE" >"$WORK_DIR/welcome-consent-mouse.txt" 2>"$WORK_DIR/welcome-consent-mouse.stderr.log" || fail WPS_CONSENT_MOUSE_FAILED
/usr/bin/grep -q '^mouse_down_up_posted=true$' "$WORK_DIR/welcome-consent-mouse.txt" || fail WPS_CONSENT_MOUSE_UNCONFIRMED
CONSENT_READY=0
for _ in {1..10}; do
  /bin/sleep 1
  /usr/bin/osascript "$WELCOME_SCRIPT" state >"$WORK_DIR/welcome-consent-state.txt" 2>"$WORK_DIR/welcome-consent-state.stderr.log" || fail WPS_CONSENT_STATE_UNAVAILABLE
  if /usr/bin/grep -q '^checkbox_value=1$' "$WORK_DIR/welcome-consent-state.txt" &&
    /usr/bin/grep -q '^start_enabled=true$' "$WORK_DIR/welcome-consent-state.txt"; then
    CONSENT_READY=1
    break
  fi
done
if [[ -x /usr/sbin/screencapture ]]; then
  /usr/sbin/screencapture -x "$WORK_DIR/welcome-consent-after.png" >"$WORK_DIR/welcome-consent-screenshot.log" 2>&1 || true
fi
[[ "$CONSENT_READY" == 1 ]] || fail WPS_CONSENT_DID_NOT_ENABLE_START

PHASE=welcome-accept
/usr/bin/osascript "$WELCOME_SCRIPT" accept >"$WORK_DIR/welcome-action.txt" 2>"$WORK_DIR/welcome-accept.stderr.log" || fail WPS_WELCOME_ACTION_FAILED
/usr/bin/grep -q '^start_now_clicked=true$' "$WORK_DIR/welcome-action.txt" || fail WPS_WELCOME_ACTION_UNCONFIRMED
WELCOME_CLOSED=0
for _ in {1..10}; do
  /bin/sleep 1
  /usr/bin/osascript "$WELCOME_SCRIPT" inspect >"$WORK_DIR/welcome-after.txt" 2>"$WORK_DIR/welcome-after.stderr.log" || fail WPS_WELCOME_AFTER_TREE_FAILED
  if /usr/bin/grep -q '^matching_welcome_windows=0$' "$WORK_DIR/welcome-after.txt"; then
    WELCOME_CLOSED=1
    break
  fi
done
if [[ "$WELCOME_CLOSED" != 1 ]]; then
  /bin/cp "$WORK_DIR/welcome-after.txt" "$WORK_DIR/welcome-axpress-after.txt"
  if [[ -x /usr/sbin/screencapture ]]; then
    /usr/sbin/screencapture -x "$WORK_DIR/welcome-axpress-after.png" >"$WORK_DIR/welcome-axpress-screenshot.log" 2>&1 || true
  fi
  PHASE=welcome-bounds-fallback
  /usr/bin/osascript "$WELCOME_SCRIPT" bounds-click >"$WORK_DIR/welcome-bounds-action.txt" 2>"$WORK_DIR/welcome-bounds.stderr.log" || fail WPS_WELCOME_BOUNDS_CLICK_FAILED
  /usr/bin/grep -q '^bounds_click_once=true$' "$WORK_DIR/welcome-bounds-action.txt" || fail WPS_WELCOME_BOUNDS_CLICK_UNCONFIRMED
  for _ in {1..10}; do
    /bin/sleep 1
    /usr/bin/osascript "$WELCOME_SCRIPT" inspect >"$WORK_DIR/welcome-after.txt" 2>"$WORK_DIR/welcome-after.stderr.log" || fail WPS_WELCOME_AFTER_TREE_FAILED
    if /usr/bin/grep -q '^matching_welcome_windows=0$' "$WORK_DIR/welcome-after.txt"; then
      WELCOME_CLOSED=1
      break
    fi
  done
fi
if [[ -x /usr/sbin/screencapture ]]; then
  /usr/sbin/screencapture -x "$WORK_DIR/welcome-after.png" >"$WORK_DIR/welcome-after-screenshot.log" 2>&1 || true
fi
[[ "$WELCOME_CLOSED" == 1 ]] || fail WPS_WELCOME_STILL_VISIBLE

PHASE=relay-handler-proof
# 只接受已验签 WPS 包内实际声明协议的应用；优先 cloud server 子应用。
set +e
RELAY_HANDLER_RELATIVE="$("$PYTHON" - "$DEST_APP" "$WORK_DIR/relay-handler.txt" <<'PY'
import os
import pathlib
import plistlib
import sys

root = pathlib.Path(sys.argv[1]).resolve(strict=True)
proof = pathlib.Path(sys.argv[2])
scheme = "ksoWPSCloudSvr"
root_registration = []
child_matches = []
for directory, children, _ in os.walk(root, followlinks=False):
    children[:] = [name for name in children if not (pathlib.Path(directory) / name).is_symlink()]
    app = pathlib.Path(directory)
    if app.suffix.lower() != ".app":
        continue
    info = app / "Contents" / "Info.plist"
    if not info.is_file() or info.is_symlink():
        continue
    with info.open("rb") as stream:
        metadata = plistlib.load(stream)
    executable = metadata.get("CFBundleExecutable", "")
    if app != root and executable.lower() != "wpscloudsvr" and "wpscloudsvr" not in app.name.lower():
        continue
    types = metadata.get("CFBundleURLTypes", [])
    registered = [item for entry in types if isinstance(entry, dict)
                  for item in entry.get("CFBundleURLSchemes", [])
                  if isinstance(item, str) and item.lower() == scheme.lower()]
    if registered:
        resolved = app.resolve(strict=True)
        if not resolved.is_relative_to(root):
            proof.write_text("handler=outside_verified_bundle\n", encoding="utf-8")
            sys.exit(3)
        relative = resolved.relative_to(root).as_posix()
        if any(char in relative for char in "\r\n\t"):
            proof.write_text("handler=invalid_relative_path\n", encoding="utf-8")
            sys.exit(3)
        if app == root:
            root_registration = registered
        else:
            child_matches.append((relative, registered))
if len(child_matches) > 1 or any(len(registered) != 1 for _, registered in child_matches):
    proof.write_text("handler=not_unique\n", encoding="utf-8")
    sys.exit(3)
if child_matches:
    relative, registered = child_matches[0]
    selected = "wpscloudsvr_child"
elif len(root_registration) == 1:
    relative, registered = ".", root_registration
    selected = "root_app"
elif root_registration:
    proof.write_text("handler=not_unique\n", encoding="utf-8")
    sys.exit(3)
else:
    proof.write_text("handler=missing\n", encoding="utf-8")
    sys.exit(2)
proof.write_text(f"scheme={registered[0]}\nselected={selected}\nrelative_app={relative}\n", encoding="utf-8")
print(relative)
PY
)"
HANDLER_RC=$?
set -e
case "$HANDLER_RC" in
  0) ;;
  2) fail WPS_RELAY_SCHEME_HANDLER_MISSING ;;
  *) fail WPS_RELAY_SCHEME_HANDLER_INVALID ;;
esac
RELAY_HANDLER_APP="$DEST_APP/$RELAY_HANDLER_RELATIVE"
/usr/bin/codesign --verify --strict "$RELAY_HANDLER_APP" >"$WORK_DIR/relay-handler-codesign.stdout.log" 2>"$WORK_DIR/relay-handler-codesign.stderr.log" || fail WPS_RELAY_HANDLER_SIGNATURE_INVALID
/usr/bin/codesign -dv --verbose=4 "$RELAY_HANDLER_APP" >"$WORK_DIR/relay-handler-details.stdout.log" 2>"$WORK_DIR/relay-handler-details.stderr.log" || fail WPS_RELAY_HANDLER_SIGNATURE_INVALID
RELAY_HANDLER_TEAM="$(/usr/bin/sed -n 's/^TeamIdentifier=//p' "$WORK_DIR/relay-handler-details.stderr.log" | /usr/bin/head -n 1)"
[[ "$RELAY_HANDLER_TEAM" == YK4WKE5WAM ]] || fail WPS_RELAY_HANDLER_TEAM_MISMATCH

PHASE=relay-start
# 显式指定上面已核实的官方应用，避免同名 URI 被其他应用接管；只发送一次。
/usr/bin/open -g -a "$RELAY_HANDLER_APP" 'ksoWPSCloudSvr://start=RelayHttpServer' >"$WORK_DIR/relay-start.stdout.log" 2>"$WORK_DIR/relay-start.stderr.log" || fail WPS_RELAY_PROTOCOL_OPEN_FAILED
printf 'scheme=ksoWPSCloudSvr\nrelative_app=%s\nopen_result=accepted\n' "$RELAY_HANDLER_RELATIVE" >"$WORK_DIR/relay-start.txt"

PHASE=relay-version
TRANSIENT_RETRIED=0
RATE_LIMIT_RETRIED=0
RELAY_ATTEMPT=0
while :; do
  RELAY_ATTEMPT=$((RELAY_ATTEMPT + 1))
  set +e
  RELAY_HTTP="$(/usr/bin/curl --noproxy '*' --silent --show-error --max-time 3 \
    -D "$WORK_DIR/relay-version.headers" -o "$WORK_DIR/relay-version.body" \
    -w '%{http_code}' -X POST -H 'Content-Type: application/json' --data '{}' \
    http://127.0.0.1:58890/version 2>"$WORK_DIR/relay-version.curl.stderr.log")"
  CURL_RC=$?
  set -e
  if [[ $CURL_RC -eq 0 && "$RELAY_HTTP" == 200 && -s "$WORK_DIR/relay-version.body" ]]; then
    RELAY_VERSION="$(tr -d '\r\n' <"$WORK_DIR/relay-version.body")"
    break
  fi
  printf 'attempt=%s curl_exit=%s http=%s\n' "$RELAY_ATTEMPT" "$CURL_RC" "${RELAY_HTTP:-none}" >>"$WORK_DIR/relay-attempts.log"
  if [[ $CURL_RC -eq 7 ]]; then
    if [[ $RELAY_ATTEMPT -lt 20 ]]; then
      sleep 1
      continue
    fi
    FAILURE_CODE=WPS_RELAY_CONNECTION_REFUSED
    exit 1
  fi
  if [[ "$RELAY_HTTP" == 429 ]]; then
    if [[ $RATE_LIMIT_RETRIED -eq 0 ]]; then
      RATE_LIMIT_RETRIED=1
      sleep 20
      continue
    fi
    FAILURE_CODE=WPS_RELAY_RATE_LIMITED
    exit 1
  fi
  if [[ "$RELAY_HTTP" =~ ^5[0-9][0-9]$ || $CURL_RC -eq 28 ]]; then
    if [[ $TRANSIENT_RETRIED -eq 0 ]]; then
      TRANSIENT_RETRIED=1
      sleep 2
      continue
    fi
    FAILURE_CODE=WPS_RELAY_TRANSIENT_FAILURE
    exit 1
  fi
  if [[ $CURL_RC -ne 0 ]]; then
    FAILURE_CODE="WPS_RELAY_CURL_FAILED_${CURL_RC}"
  else
    FAILURE_CODE="WPS_RELAY_HTTP_FAILED_${RELAY_HTTP:-UNKNOWN}"
  fi
  exit 1
done
[[ "$RELAY_HTTP" == 200 && -n "$RELAY_VERSION" ]] || fail WPS_RELAY_VERSION_UNAVAILABLE

PHASE=probe
PROBE_OUTPUT="$WORK_DIR/probe-output.docx"
PROBE_EVIDENCE="$WORK_DIR/probe-evidence.json"
set +e
"$PYTHON" "$REPO_ROOT/scripts/probe-wps-native-bridge.py" \
  --platform macos --architecture "$ARCH" --no-start-relay \
  --input "$REPO_ROOT/backend/tests/fixtures/document-formatter-source/input-manual-break.docx" \
  --output "$PROBE_OUTPUT" --paragraph-index 3 --timeout-seconds 60 \
  --plugin-root "$REPO_ROOT/packaging/wps-formatter-adapter/plugin" \
  --evidence "$PROBE_EVIDENCE" >"$WORK_DIR/probe.stdout.log" 2>"$WORK_DIR/probe.stderr.log"
PROBE_RC=$?
set -e
[[ $PROBE_RC -eq 0 ]] || { FAILURE_CODE=WPS_NATIVE_PROBE_FAILED; exit "$PROBE_RC"; }
"$PYTHON" - "$PROBE_EVIDENCE" "$ARCH" <<'PY' || fail PROBE_EVIDENCE_CONTRACT_FAILED
import json, pathlib, sys
data = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
assert data.get("passed") is True
assert data.get("provider") == "wps" and data.get("platform") == "macos"
assert data.get("architecture") == sys.argv[2]
assert data.get("silent") is True and data.get("character_unit_first_line_indent") == 2
assert data.get("input_sha256") != data.get("output_sha256")
PY

PHASE=complete
FAILURE_CODE=
write_status 0
write_artifact_manifest
printf 'native_wps_probe_status=passed\nnative_wps_probe_arch=%s\nnative_wps_probe_workdir=%s\n' "$ARCH" "$WORK_DIR"
