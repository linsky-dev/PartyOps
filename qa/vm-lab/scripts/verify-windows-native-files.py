"""核验本机实际安装文件与已绑定候选清单，结果仅限安装完整性。"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, sha256, write_json
from lab import fingerprint, load_configuration
from native_install import verify as verify_binding
from providers import QemuLab
from windows_build_payload import expected_payload


def verify(run: Path) -> dict:
    run = run.resolve()
    root = Path('D:/PartyOps-VM-Lab/reports/win11-x64-native').resolve()
    if not run.is_relative_to(root):
        raise RuntimeError('NATIVE_RUN_OUTSIDE_LAB')
    if (run / 'installed-files.json').exists():
        raise RuntimeError('NATIVE_INSTALLED_FILES_EVIDENCE_EXISTS')
    context = json.loads((run / 'install-result.json').read_text(encoding='utf-8'))
    if context.get('target') != 'win11-x64-native' or context.get('installer_exit_code') != 0:
        raise RuntimeError('NATIVE_INSTALL_NOT_COMPLETED')
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix['defaults']['primary_root']), media)
    source = fingerprint()
    binding = verify_binding(lab, run, source)
    package, payload, entries = expected_payload(lab, context['package'], source)
    if (context['package'] != binding['package'] or context.get('expected_payload') != payload
            or context.get('install_binding_sha256') != sha256(run / 'install-binding.json')):
        raise RuntimeError('NATIVE_INSTALL_RESULT_BINDING_MISMATCH')
    installed = Path(context['install_dir']).resolve()
    if installed != Path('E:/PartyOps1/PartyOps').resolve():
        raise RuntimeError('NATIVE_INSTALL_PATH_UNEXPECTED')
    actual_manifest = installed / 'release-manifest.json'
    if payload['manifest']['sha256'] != sha256(actual_manifest):
        raise RuntimeError('NATIVE_INSTALLED_MANIFEST_MISMATCH')
    errors, expected = [], set()
    for item in entries.values():
        relative = item['path']
        path = (installed / relative).resolve()
        if not path.is_relative_to(installed) or relative in expected:
            raise RuntimeError('NATIVE_MANIFEST_PATH_INVALID')
        expected.add(relative)
        if not path.is_file():
            errors.append({'path': relative, 'reason': 'missing'})
        elif path.stat().st_size != item['size'] or sha256(path) != item['sha256']:
            errors.append({'path': relative, 'reason': 'hash_or_size_mismatch'})
    result = {
        'at': now(), 'target': 'win11-x64-native', 'environment_type': 'native-host',
        'scope': 'installed-manifest-files-only', 'package': package,
        'install_dir': str(installed), 'checked_files': len(expected),
        'manifest_sha256': sha256(actual_manifest), 'errors': errors,
        'expected_payload': payload, 'install_binding_sha256': sha256(run / 'install-binding.json'),
        'installed_files_match': not errors, 'runtime_environment_passed': False,
        'limitations': ['未以文件哈希替代实际功能', '未声明同版本替换为旧版本升级'],
    }
    write_json(run / 'installed-files.json', result)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run-directory', type=Path, required=True)
    arguments = parser.parse_args()
    report = verify(arguments.run_directory)
    print(json.dumps({key: report[key] for key in ('target', 'checked_files', 'errors',
                                                  'installed_files_match', 'runtime_environment_passed')}, ensure_ascii=False))
    raise SystemExit(0 if report['installed_files_match'] else 2)
