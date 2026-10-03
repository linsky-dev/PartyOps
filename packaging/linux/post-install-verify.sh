#!/usr/bin/env sh
set -u

RUNTIME=/opt/partyops
STATUS_DIR=/var/lib/partyops
STATUS_FILE="$STATUS_DIR/install-verification.json"
TEMP_FILE="$STATUS_DIR/.install-verification.$$"
LOG=/var/log/partyops-package-selftest.log
mkdir -p "$STATUS_DIR"
# 状态文件与界面统一北京时间，显式时区，不能继承安装机器的本地配置。
STARTED_AT="$(TZ=Asia/Shanghai date +%Y-%m-%dT%H:%M:%S%:z)"

PARTYOPS_ASYNC_VERIFY=1 "$RUNTIME/post-install-selftest.sh" "" full
RESULT=$?
if [ "$RESULT" -eq 0 ]; then
  CODE=OK
  STATE=passed
else
  CODE="$(sed -n 's/^\[\([A-Z0-9_]*\)\].*/\1/p' "$LOG" 2>/dev/null | tail -n 1)"
  [ -n "$CODE" ] || CODE=PACKAGE_VERIFY_FAILED
  STATE=failed
fi
FINISHED_AT="$(TZ=Asia/Shanghai date +%Y-%m-%dT%H:%M:%S%:z)"
umask 077
printf '{"version":"1.4.5-rc.6","state":"%s","result_code":"%s","started_at":"%s","finished_at":"%s","log":"/var/log/partyops-package-selftest.log"}\n' \
  "$STATE" "$CODE" "$STARTED_AT" "$FINISHED_AT" >"$TEMP_FILE"
chmod 0600 "$TEMP_FILE"
mv -f -- "$TEMP_FILE" "$STATUS_FILE"
exit "$RESULT"
