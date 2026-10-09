#!/usr/bin/env bash
set -euo pipefail
umask 077
# 第一次运行只验证官方压缩静态模板与BCL/Posix；不构建WPS或完整产品。
[[ "$(uname -s)/$(uname -m)" == 'Darwin/arm64' ]] || exit 2
[[ "${PARTYOPS_PROBE_CONFIRM:-}" == ARM_RUNTIME_STATIC_PROBE ]] || exit 2
[[ -n "${RUNNER_TEMP:-}" ]] || exit 2
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../../../.." && pwd)"
PYTHON_BIN="${PYTHON_BIN:-$(command -v python3)}"
STAGE="$(mktemp -d "$RUNNER_TEMP/partyops-arm-runtime-probe.XXXXXX")"
printf '[ARM_RUNTIME_PROBE_WORKSPACE] %s\n' "$STAGE"
for tool in mono mcs mkbundle pkg-config otool lipo cc; do command -v "$tool" >/dev/null || exit 2; done
[[ "$(pkg-config --modversion mono-2)" == '6.14.1' ]] || exit 2
[[ "$(mono --version | head -1)" == *'version 6.14.1 '* ]] || exit 2
CONFIG="${PARTYOPS_MONO_CONFIG_ROOT:?}/config"
FRAMEWORK="${PARTYOPS_MONO_FRAMEWORK_ROOT:?}"
LIBRARY="${PARTYOPS_MONO_LIBRARY_ROOT:?}"
INCLUDE="${PARTYOPS_MONO_INCLUDE_ROOT:?}"
"$PYTHON_BIN" "$ROOT/scripts/macos-mono-input-profile.py" \
  --config "$CONFIG" --library-root "$LIBRARY" --stage "$STAGE" \
  --framework-root "$FRAMEWORK" --include-root "$INCLUDE" \
  --link-include-root "$(pkg-config --variable=includedir mono-2)" \
  --bottle-archive "${PARTYOPS_MONO_BOTTLE_ARCHIVE:?}"
"$PYTHON_BIN" - "$(pkg-config --variable=libdir mono-2)/libmono-2.0.a" <<'PY'
import hashlib, sys
from pathlib import Path
if hashlib.sha256(Path(sys.argv[1]).read_bytes()).hexdigest() != '1691588705499cfe9b925a217a7cd5e44fa3b9785743cb3b235ad9f108642570':
    raise SystemExit('静态链接实际输入摘要不符')
PY
cat > "$STAGE/Hello.cs" <<'CS'
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
class Hello {
    [DllImport("MonoPosixHelper", EntryPoint="Mono_Posix_Syscall_getpid")]
    static extern int GetPid();
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint="_dyld_image_count")]
    static extern uint ImageCount();
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint="_dyld_get_image_name")]
    static extern IntPtr ImageName(uint index);
    static int Main() {
        const string text = "中文原生静态宿主";
        if (Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text)) != text) return 11;
        if (CultureInfo.GetCultureInfo("zh-CN").Name != "zh-CN") return 12;
        if (Enumerable.Range(1, 3).Sum() != 6 || !Regex.IsMatch(text, "^中文")) return 16;
        string path = Path.Combine(Path.GetTempPath(), "partyops-arm-probe-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, text, Encoding.UTF8);
        if (File.ReadAllText(path, Encoding.UTF8) != text) return 13;
        File.Delete(path);
        try { throw new InvalidOperationException(text); }
        catch (InvalidOperationException error) { if (error.Message != text) return 14; }
        if (GetPid() <= 0) return 15;
        // 读取dyld实际加载image，而非只相信dllmap中的别名或SDK安装状态。
        int posixImages = 0;
        for (uint index = 0; index < ImageCount(); index++) {
            string image = Marshal.PtrToStringAnsi(ImageName(index));
            if (Path.GetFileName(image) == "libMonoPosixHelper.dylib") {
                Console.WriteLine("ARM_RUNTIME_POSIX_IMAGE=" + image);
                posixImages++;
            }
        }
        if (posixImages != 1) return 17;
        Console.WriteLine("ARM_RUNTIME_STATIC_PROBE_OK: Hello/BCL/中文/异常/Posix");
        return 0;
    }
}
CS
export MACOSX_DEPLOYMENT_TARGET='15.0'
/usr/bin/env -u MONO_ENV_OPTIONS -u MONO_BUNDLED_OPTIONS MONO_CONFIG="$CONFIG" \
  mcs -platform:anycpu -r:System.Core -out:"$STAGE/Hello.exe" "$STAGE/Hello.cs"
inputs=("$STAGE/Hello.exe")
for assembly in "$FRAMEWORK"/*.dll "$FRAMEWORK"/Facades/*.dll; do
  [[ -f "$assembly" ]] || continue
  inputs+=("$assembly")
done
(cd "$STAGE" && /usr/bin/env -u MONO_ENV_OPTIONS -u MONO_BUNDLED_OPTIONS MONO_CONFIG="$CONFIG" \
  CC="$(command -v cc) -arch arm64 -lc++ -lz" \
  mkbundle --custom --static -z --nodeps --i18n none --keeptemp \
  --config "$STAGE/mono-config.bundle.xml" --machine-config "${PARTYOPS_MONO_CONFIG_ROOT}/4.5/machine.config" \
  -o "$STAGE/hello-static" "${inputs[@]}")
[[ "$(lipo -archs "$STAGE/hello-static")" == 'arm64' ]] || exit 2
otool -L "$STAGE/hello-static" > "$STAGE/host-dependencies.txt"
otool -l "$STAGE/hello-static" > "$STAGE/host-load-commands.txt"
"$PYTHON_BIN" - "$ROOT" "$STAGE" <<'PY'
import importlib.util, json, sys
from pathlib import Path
root, stage = map(Path, sys.argv[1:])
spec = importlib.util.spec_from_file_location('arm_profile', root / 'scripts/macos-mono-input-profile.py')
profile = importlib.util.module_from_spec(spec)
spec.loader.exec_module(profile)
loads = [line.strip().split(' (', 1)[0] for line in (stage / 'host-dependencies.txt').read_text().splitlines()[1:] if line.strip()]
if not loads or any(not value.startswith(('/usr/lib/', '/System/Library/')) for value in loads):
    raise SystemExit('原生宿主依赖非系统动态库')
minimum = profile.check_minimum((stage / 'host-load-commands.txt').read_text())
if minimum != ['15.0']:
    raise SystemExit('新ARM宿主必须明确部署15.0')
(stage / 'probe-structure.json').write_text(json.dumps({'profile': profile.PROFILE, 'minimum_macos': minimum,
    'host_sha256': profile.sha256(stage / 'hello-static'), 'dependencies': loads, 'product_gate_satisfied': False}, indent=2) + '\n')
PY
# 执行环境不保留Mono/DYLD覆盖；仅写本STAGE，并保存成功、失败或超时证据。
"$PYTHON_BIN" - "$ROOT" "$STAGE" <<'ARM_EXECUTION_PY'
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys


def load_validator(root):
    spec = importlib.util.spec_from_file_location('formatter_validator', root / 'scripts/validate-source-formatter-runtime.py')
    validator = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(validator)
    return validator


def execution_environment(stage, inherited):
    # 明确清除所有MONO_*和DYLD_*；其它构建环境也不进入执行子进程。
    cleaned = {key: value for key, value in inherited.items() if not key.startswith(('MONO_', 'DYLD_'))}
    return {'PATH': '/usr/bin:/bin:/usr/sbin:/sbin', 'LANG': cleaned.get('LANG', 'en_US.UTF-8'),
            'HOME': str(stage / 'execution-home'), 'TMPDIR': str(stage / 'execution-tmp'),
            'MONO_CONFIG': '/dev/null', 'MONO_LOG_LEVEL': 'debug', 'MONO_LOG_MASK': 'asm,dll'}


def validate_trace(trace, stage, validator):
    # 复用正式validator的逐行bundle记录解析与外部location拒绝规则。
    if not validator.has_bundled_mscorlib(trace) or 'Assembly Loader loaded assembly from location:' in trace:
        raise RuntimeError('[ARM_PROBE_EXTERNAL_ASSEMBLY] mscorlib未由bundle加载或存在外部程序集。')
    bundled = set()
    for line in trace.splitlines():
        match = validator.BUNDLED_MSCORLIB_RECORD.fullmatch(line)
        if match and (match.group(1).startswith('/') or '/' not in match.group(1)):
            bundled.add(PurePosixPath(match.group(1)).name)
    required = {'mscorlib.dll', 'System.dll', 'System.Core.dll', 'Hello.exe'}
    if not required.issubset(bundled):
        raise RuntimeError('[ARM_PROBE_BCL_NOT_BUNDLED] 缺少BCL加载证据：' + ','.join(sorted(required - bundled)))
    # dll trace证明实际P/Invoke加载；dyld image记录核实展开后的真实绝对路径。
    loads = re.findall(r"^Mono: DllImport loaded library '([^'\r\n]+)'\.$", trace, re.M)
    expected = stage / 'libMonoPosixHelper.dylib'
    posix = [value for value in loads if PurePosixPath(value).name == expected.name]
    if not posix or any(value not in (str(expected), '@executable_path/' + expected.name) for value in posix):
        raise RuntimeError('[ARM_PROBE_POSIX_LOAD_INVALID] Posix加载轨迹缺失或指向外部SDK。')
    images = re.findall(r'^ARM_RUNTIME_POSIX_IMAGE=([^\r\n]+)$', trace, re.M)
    if images != [str(expected)] or expected.is_symlink() or not expected.is_file():
        raise RuntimeError('[ARM_PROBE_POSIX_IMAGE_INVALID] dyld实际Posix路径不是本STAGE辅助库。')
    if 'ARM_RUNTIME_STATIC_PROBE_OK: Hello/BCL/中文/异常/Posix' not in trace:
        raise RuntimeError('[ARM_PROBE_SUCCESS_MISSING] 缺少最小功能成功记录。')
    return {'bundled_assemblies': sorted(bundled), 'posix_loaded_image': images[0], 'posix_dll_trace': posix}


def execute(root, stage):
    stage = stage.resolve(strict=True)
    for name in ('execution-home', 'execution-tmp', 'execution-cwd'):
        (stage / name).mkdir(mode=0o700)
    receipt = {'passed': False, 'timeout_seconds': 30, 'product_gate_satisfied': False}
    stdout, stderr = '', ''
    try:
        result = subprocess.run([str(stage / 'hello-static')], cwd=stage / 'execution-cwd',
                                env=execution_environment(stage, os.environ), stdin=subprocess.DEVNULL,
                                capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=30)
        stdout, stderr = result.stdout, result.stderr
        receipt['returncode'] = result.returncode
        if result.returncode != 0:
            raise RuntimeError('[ARM_PROBE_EXECUTION_FAILED] 宿主退出码=' + str(result.returncode))
        receipt.update(validate_trace(stdout + '\n' + stderr, stage, load_validator(root)))
        receipt['passed'] = True
    except subprocess.TimeoutExpired as error:
        stdout = error.stdout or ''
        stderr = error.stderr or ''
        receipt['error'] = '[ARM_PROBE_TIMEOUT] 原生宿主执行超过30秒。'
    except (OSError, RuntimeError) as error:
        receipt['error'] = str(error)
    finally:
        stdout = stdout.decode('utf-8', 'replace') if isinstance(stdout, bytes) else stdout
        stderr = stderr.decode('utf-8', 'replace') if isinstance(stderr, bytes) else stderr
        (stage / 'probe.stdout').write_text(stdout, encoding='utf-8')
        (stage / 'probe.stderr').write_text(stderr, encoding='utf-8')
        (stage / 'probe-trace.txt').write_text(stdout + '\n' + stderr, encoding='utf-8')
        (stage / 'probe-execution.json').write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    if not receipt['passed']:
        raise SystemExit(receipt['error'])
    print('ARM_RUNTIME_EXECUTION_EVIDENCE_OK: bundle BCL + STAGE Posix image')


if __name__ == '__main__':
    execute(*map(Path, sys.argv[1:]))
ARM_EXECUTION_PY
printf '[ARM_RUNTIME_PROBE_OK] %s\n' "$STAGE"
