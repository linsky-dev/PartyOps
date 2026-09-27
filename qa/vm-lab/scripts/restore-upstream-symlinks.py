"""在 WSL 中恢复固定上游仓库的 Git 符号链接，修复 Windows 检出形式。"""
import os
import subprocess
from pathlib import Path

root = Path('/mnt/e/codex/PartyOps/.partyops-vm-lab/tools/darwin-vm/qemu-sptm').resolve()
assert str(root).startswith('/mnt/e/codex/PartyOps/.partyops-vm-lab/tools/')
records = subprocess.check_output(['git', '-C', str(root), 'ls-files', '--stage', '-z']).decode().split('\0')
count = 0
for record in filter(None, records):
    metadata, name = record.split('\t', 1)
    mode, blob, _ = metadata.split()
    if mode != '120000':
        continue
    path = root / name
    expected = subprocess.check_output(['git', '-C', str(root), 'cat-file', 'blob', blob]).decode()
    if path.is_symlink():
        assert os.readlink(path) == expected
        continue
    assert path.is_file() and path.read_text() == expected
    assert path.resolve().is_relative_to(root)
    path.unlink()
    path.symlink_to(expected)
    count += 1
print({'restored_git_symlinks': count})
