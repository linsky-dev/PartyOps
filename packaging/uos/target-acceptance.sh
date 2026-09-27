#!/usr/bin/env bash
set -euo pipefail

URL="${1:-https://127.0.0.1:18765}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RESULT="${PARTYOPS_ACCEPTANCE_RESULT:-$ROOT/artifacts/uos-target-acceptance.txt}"
mkdir -p "$(dirname "$RESULT")"

{
  echo "PartyOps UOS 目标机验收"
  date -Iseconds
  uname -a
  case "$(uname -m)" in
    x86_64) ARCH="amd64" ;;
    aarch64|arm64) ARCH="arm64" ;;
    loongarch64|loong64) ARCH="loong64" ;;
    *) echo "不支持的处理器架构：$(uname -m)" >&2; exit 2 ;;
  esac
  echo "architecture=$ARCH"
  RUNTIME_PROFILE=full
  [[ "$ARCH" == loong64 ]] && RUNTIME_PROFILE=core
  VERSION="${PARTYOPS_VERSION:-1.4.5-rc.6}"
  PACKAGE_VERSION="${PARTYOPS_PACKAGE_VERSION:-1.4.5~rc.6}"
  INSTALLED_VERSION="$(dpkg-query -W -f='${Version}' partyops)"
  INSTALLED_ARCH="$(dpkg-query -W -f='${Architecture}' partyops)"
  echo "installed_version=$INSTALLED_VERSION"
  echo "installed_architecture=$INSTALLED_ARCH"
  test "$INSTALLED_VERSION" = "$PACKAGE_VERSION"
  test "$INSTALLED_ARCH" = "$ARCH"
  test -x /opt/partyops/desktop-launcher.sh
  test -f /usr/share/applications/partyops.desktop
  test -x /opt/partyops/open-local-file.sh
  test -f /usr/share/applications/partyops-file.desktop
  test -f /usr/share/applications/partyops-client.desktop
  if [[ "$RUNTIME_PROFILE" == full ]]; then
    test -x /opt/partyops/llama-server
    LD_LIBRARY_PATH=/opt/partyops /opt/partyops/llama-server --version >/dev/null
  else
    test ! -e /opt/partyops/llama-server
    test -f /opt/partyops/release-manifest.json
    /opt/partyops/partyops --package-self-test
  fi
  grep -q '^Exec=/bin/bash /opt/partyops/desktop-launcher.sh$' \
    /usr/share/applications/partyops.desktop
  DEB="${PARTYOPS_ACCEPTANCE_PACKAGE:-$ROOT/artifacts/PartyOps_${VERSION}_linux_${ARCH}.deb}"
  if [[ -n "${PARTYOPS_ACCEPTANCE_PACKAGE:-}" && ! -f "$DEB" ]]; then
    echo '指定的最终安装包不存在，不能跳过包校验。' >&2
    exit 2
  fi
  if [[ -f "$DEB" ]]; then
    test "$(dpkg-deb -f "$DEB" Architecture)" = "$ARCH"
    test "$(dpkg-deb -f "$DEB" Version)" = "$PACKAGE_VERSION"
    if [[ -n "${PARTYOPS_ACCEPTANCE_SHA256:-}" ]]; then
      printf '%s  %s\n' "$PARTYOPS_ACCEPTANCE_SHA256" "$DEB" | sha256sum -c -
    else
      (cd "$(dirname "$DEB")" && sha256sum -c "SHA256SUMS.$ARCH")
    fi
  fi
  ldd --version 2>&1 | sed -n '1p'
  if [[ "$RUNTIME_PROFILE" == core ]]; then
    echo "Loong64 core 构建基线：Python 3.12.13、glibc 2.38；冻结运行时以安装包自检为准。"
  elif command -v python3.11 >/dev/null 2>&1; then
    python3.11 --version
  elif [[ -x "$ROOT/.partyops-python/bin/python3.11" ]]; then
    "$ROOT/.partyops-python/bin/python3.11" --version
  else
    echo "Python 3.11 仅用于构建，目标运行时已由 PyInstaller 内置。"
  fi
  if systemctl is-active --quiet partyops; then
    echo "launch_mode=systemd"
    systemctl is-enabled partyops
    systemctl show partyops \
      --property=ActiveState,SubState,NRestarts,Restart,RestartUSec \
      --no-pager
  else
    echo "launch_mode=user-session"
  fi
  test -x /opt/partyops/ocr/bin/tesseract
  test -f /opt/partyops/ocr/tessdata/chi_sim.traineddata
  LD_LIBRARY_PATH=/opt/partyops/ocr/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH} \
    TESSDATA_PREFIX=/opt/partyops/ocr/tessdata \
    /opt/partyops/ocr/bin/tesseract --list-langs 2>/dev/null | grep -Fx "chi_sim"
  df -h /var/lib/partyops

  CURL_ARGS=(-fsS)
  if [[ "$URL" == https://* && -f /var/lib/partyops/secrets/pki/ca.pem ]]; then
    CURL_ARGS+=(--cacert /var/lib/partyops/secrets/pki/ca.pem)
  fi
  for attempt in 1 2 3; do
    HEALTH="$(curl "${CURL_ARGS[@]}" "$URL/api/v1/health")"
    echo "health_attempt_${attempt}=$HEALTH"
    grep -q '"status":"ok"' <<<"$HEALTH"
    grep -q '"safe_version":true' <<<"$HEALTH"
    grep -q '"fts5":true' <<<"$HEALTH"
    [[ "$attempt" -eq 3 ]] || sleep 2
  done

  INDEX="$(curl "${CURL_ARGS[@]}" "$URL/")"
  grep -q '党建智办' <<<"$INDEX"
  BOOTSTRAP="$(curl "${CURL_ARGS[@]}" "$URL/api/v1/bootstrap/status")"
  echo "$BOOTSTRAP"
  grep -q '"configured":true' <<<"$BOOTSTRAP"
  echo
  echo "RESULT=passed"
} | tee "$RESULT"

echo "验收记录：$RESULT"
