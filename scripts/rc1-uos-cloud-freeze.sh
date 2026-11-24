#!/usr/bin/env bash
# Actual RC1 application executable freezer, UOS V20 glibc 2.28, not a DEB.
# Runs on a native CPU inside an offline container. Does not access WPS.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd -P)"
cd "$ROOT"
[[ "$PARTYOPS_UOS_APP_ONLY" == 1 ]] || { echo 'UOS_APP_ONLY_FLAG_REQUIRED' >&2; exit 2; }
ARCH="$PARTYOPS_BUILD_ARCH"
case "$(uname -m):$ARCH" in x86_64:amd64|aarch64:arm64) ;; *) echo 'UOS_NATIVE_CPU_REQUIRED' >&2; exit 2;; esac
[[ "$(getconf GNU_LIBC_VERSION)" == 'glibc 2.28' ]] || { echo 'UOS_GLIBC_228_REQUIRED' >&2; exit 2; }
[[ "$(git rev-parse HEAD)" == "$SOURCE_SHA" ]] || { echo 'UOS_SOURCE_SHA_MISMATCH' >&2; exit 2; }
INPUT="$ROOT/artifacts/uos-app-inputs/$ARCH"
WORK="$ROOT/artifacts/uos-app-work/$ARCH"
OUT="$ROOT/artifacts/uos-app-freeze/$ARCH"
WHEELHOUSE="$INPUT/wheels"
PYBASE="$INPUT/python/python"
PY="$PYBASE/bin/python3.11"
[[ -x "$PY" && -f "$ROOT/frontend/dist/client/index.html" ]] || { echo 'UOS_INPUTS_OR_FRONTEND_MISSING' >&2; exit 2; }
for file in "$INPUT/sqlite-amalgamation-3510300.zip" "$INPUT/pysqlite3-0.5.4.tar.gz"; do
  [[ -s "$file" ]] || { echo "UOS_CLOUD_INPUT_MISSING:$file" >&2; exit 2; }
done
mkdir -p "$WORK" "$OUT"
"$PY" -m venv "$WORK/venv"
PIP="$WORK/venv/bin/python"
"$PIP" -m pip install --no-index --find-links "$WHEELHOUSE" -r "$ROOT/packaging/uos/requirements-build.txt"
"$PIP" "$ROOT/scripts/validate-uos-wheelhouse.py" \
  --architecture "$ARCH" --runtime-profile uos-v20 --wheelhouse "$WHEELHOUSE" \
  --requirements "$ROOT/backend/requirements.txt" \
  "$ROOT/backend/requirements-local-ai.txt" "$ROOT/packaging/uos/requirements-build.txt"
mkdir -p "$WORK/sqlite" "$WORK/pysqlite3"
"$PY" - "$INPUT/sqlite-amalgamation-3510300.zip" "$WORK/sqlite" <<'PY'
from pathlib import Path
from zipfile import ZipFile
import sys
with ZipFile(sys.argv[1]) as z:
    allowed = ('sqlite3.c', 'sqlite3.h')
    for item in allowed:
        (Path(sys.argv[2])/item).write_bytes(z.read('sqlite-amalgamation-3510300/'+item))
PY
"$PY" - "$INPUT/pysqlite3-0.5.4.tar.gz" "$WORK/pysqlite3" <<'PY'
import sys,tarfile
from pathlib import Path,PurePosixPath
with tarfile.open(sys.argv[1],'r:gz') as archive:
    members=archive.getmembers()
    for m in members:
        parts=PurePosixPath(m.name).parts
        if not parts or parts[0]!='pysqlite3-0.5.4' or '..' in parts or not (m.isfile() or m.isdir()):
            raise SystemExit('UOS_PYSQLITE_UNSAFE_ARCHIVE')
    for m in members:
        if not m.isfile(): continue
        parts=PurePosixPath(m.name).parts[1:]
        if not parts: continue
        out=Path(sys.argv[2],*parts)
        out.parent.mkdir(parents=True,exist_ok=True)
        with archive.extractfile(m) as source:
            out.write_bytes(source.read())
PY
cp "$WORK/sqlite/sqlite3.c" "$WORK/sqlite/sqlite3.h" "$WORK/pysqlite3/"
export CC="$(command -v gcc)" CXX="$(command -v g++)"
export AR="$(command -v ar)" RANLIB="$(command -v ranlib)"
export CFLAGS="-O3 -g0 -fPIC" CXXFLAGS="-O3 -g0 -fPIC"
export LDSHARED="$CC -pthread -shared -Wl,-z,noexecstack -Wl,--exclude-libs,ALL"
(
  cd "$WORK/pysqlite3"
  "$PIP" setup.py build_static bdist_wheel
)
"$PIP" -m pip install --no-index --find-links "$WHEELHOUSE" \
 -r "$ROOT/backend/requirements.txt" \
 -r "$ROOT/backend/requirements-local-ai.txt"
"$PIP" -m pip install --no-index "$WORK"/pysqlite3/dist/pysqlite3-*.whl
"$PIP" -m pip check
PARTYOPS_DATA_DIR="$WORK/smoke-data" PARTYOPS_ENVIRONMENT=production \
PARTYOPS_STRICT_SQLITE=true PARTYOPS_SEED_DEMO=false PYTHONPATH="$ROOT/backend" \
"$PIP" - <<'PY'
from app.database import db_runtime
from uvicorn.config import Config
c=db_runtime.validate_capabilities()
assert c['fts5'] is True
u=Config('app.main:app',loop='asyncio',http='h11',ws='none',workers=1)
u.load()
print('UOS_FRESH_SOURCE_DEPENDENCIES=PASS')
PY
PYTHON_BASE_LIB="$PYBASE/lib"
[[ -f "$PYTHON_BASE_LIB/libpython3.11.so.1.0" ]] || { echo 'UOS_SHARED_PYTHON_MISSING' >&2; exit 2; }
PARTYOPS_RUNTIME_PROFILE=uos-v20 LD_LIBRARY_PATH="$PYTHON_BASE_LIB" \
 "$PIP" -m PyInstaller --noconfirm --clean \
 --distpath "$OUT/dist" --workpath "$WORK/pyinstaller-work" \
 "$ROOT/packaging/uos/partyops.spec"
RT="$OUT/dist/PartyOps"
for exe in partyops partyops-client partyops-wizard partyops-updater; do
  [[ -x "$RT/$exe" ]] || { echo "UOS_ENTRY_MISSING:$exe" >&2; exit 2; }
done
for path in _internal/alembic.ini _internal/alembic/env.py _internal/frontend/index.html; do
  [[ -f "$RT/$path" ]] || { echo "UOS_RESOURCE_MISSING:$path" >&2; exit 2; }
done
[[ -d "$RT/_internal/alembic/versions" ]] || { echo 'UOS_MIGRATIONS_MISSING' >&2; exit 2; }
LD_LIBRARY_PATH="$PYTHON_BASE_LIB" "$RT/partyops-updater" --help >/dev/null
LD_LIBRARY_PATH="$PYTHON_BASE_LIB" "$RT/partyops-wizard" --help >/dev/null
echo 'UOS_CURRENT_SOURCE_APP_FREEZE=PASS_ONLY_NOT_NATIVE_DEB'
