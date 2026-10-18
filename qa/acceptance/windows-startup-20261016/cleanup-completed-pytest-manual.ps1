# 仅由用户手工执行；自动审批拒绝过此目录清理，助手不执行本脚本。
# 只删除已经结束的 pytest-3 合成数据，不删除安装包、虚拟机或业务数据。
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$python = Join-Path $root 'backend/.venv/Scripts/python.exe'
$previousOutputEncoding = $OutputEncoding
$OutputEncoding = [Text.UTF8Encoding]::new($false)
Push-Location $root
try {
@'
import errno, json, os, shutil, stat, sys
from datetime import datetime, timezone
from pathlib import Path

root = Path.cwd().resolve(strict=True)
relative = Path('.test-temp/pytest-of-Administrator/pytest-3')
target = root / relative
evidence = root / 'qa/acceptance/windows-startup-20261016'
if sys.version_info < (3, 12):
    raise SystemExit('需要已有 Python 3.12，保证 Windows junction 删除行为明确。')
if not target.exists():
    print('目标已不存在，不重复删除。')
    raise SystemExit(0)
current = root
for component in relative.parts:
    current = current / component
    if current.is_symlink() or current.is_junction():
        raise SystemExit('临时目录路径包含链接，停止。')
if target.resolve(strict=True) != target or not target.is_relative_to(root):
    raise SystemExit('目标越过当前工程边界，停止。')
if (target / '.lock').exists():
    raise SystemExit('测试目录仍有锁，停止；不自动解除锁。')
if not (evidence / 'backend-final-20261016-194447.xml').is_file():
    raise SystemExit('缺少目录外保存的原测试结果，停止。')
count = size = 0
for folder, directories, files in os.walk(target, followlinks=False):
    directories[:] = [n for n in directories if not (Path(folder)/n).is_junction()]
    for name in files:
        item = Path(folder)/name
        if not item.is_symlink():
            count += 1
            size += item.stat().st_size
receipt = {'schema': 1, 'path': str(target), 'files': count, 'bytes': size,
           'time': datetime.now(timezone.utc).isoformat(), 'status': 'pending',
           'scope': '仅已完成全量测试的临时合成数据；保留日志、JUnit、覆盖率和源代码'}
record = evidence/'pytest-3-cleanup.json'
record.write_text(json.dumps(receipt, ensure_ascii=False, indent=2), encoding='utf-8')

def retry_readonly_file_once(func, path, error):
    # 只处理Windows普通单链接文件的只读位；不改ACL，不重试目录或其他错误。
    if (os.name != 'nt' or not isinstance(error, PermissionError)
            or not (error.errno == errno.EACCES or getattr(error, 'winerror', None) == 5)
            or func not in (os.unlink, os.remove)):
        raise error
    candidate = Path(os.path.abspath(path))
    if candidate == target or not candidate.is_relative_to(target):
        raise error
    # resolve前逐级检查，拒绝任何链接/junction/reparse，避免越界改属性。
    for ancestor in (candidate, *candidate.parents):
        info = ancestor.lstat()
        if (ancestor.is_symlink() or ancestor.is_junction()
                or getattr(info, 'st_file_attributes', 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT):
            raise error
    if candidate.resolve(strict=True) != candidate:
        raise error
    info = candidate.lstat()
    if (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1
            or not (info.st_file_attributes & stat.FILE_ATTRIBUTE_READONLY)):
        raise error
    # Windows的os.chmod只改变只读属性；S_IWRITE解除该位，不改变ACL。
    os.chmod(candidate, info.st_mode | stat.S_IWRITE)
    func(path)  # 仅一次直接重试；再次失败直接交外层保存failed回执。

# Python 3.12 的 Windows rmtree 不递归进入 junction 目标。
try:
    shutil.rmtree(target, onexc=retry_readonly_file_once)
except OSError as error:
    receipt['status'] = 'failed'
    receipt['error'] = {
        'type': type(error).__name__, 'errno': error.errno,
        'winerror': getattr(error, 'winerror', None),
        'path': error.filename, 'message': str(error),
    }
    record.write_text(json.dumps(receipt, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f'清理失败，具体错误已保存到 {record}；只读普通文件最多重试一次，未改ACL。')
    raise SystemExit(1) from error
receipt['status'] = 'removed'
record.write_text(json.dumps(receipt, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'已删除本轮临时文件 {count} 项，释放 {size} 字节。')
'@ | & $python -
    if ($LASTEXITCODE -ne 0) { throw '临时目录未清理，请保留错误信息。' }
} finally {
    Pop-Location
    $OutputEncoding = $previousOutputEncoding
}
