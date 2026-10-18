"""沿用固定 EdgeOne CLI，部署同一完整网站产物；未知结果不重放。"""
from pathlib import Path
import argparse, datetime, hashlib, json, re, subprocess

HERE = Path(__file__).resolve().parent
SITE = Path('C:/PartyOps-VM-Lab/website-recovered-20261014')
DIST = SITE / 'dist/edgeone'

def read(path):
    return json.loads(path.read_text(encoding='utf-8'))

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def save(path, data):
    with path.open('x', encoding='utf-8') as stream:
        json.dump(data, stream, ensure_ascii=False, indent=2)

def inventory():
    return {p.relative_to(DIST).as_posix(): sha(p) for p in DIST.rglob('*') if p.is_file()}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('phase', choices=['freeze', 'preview', 'production'])
    args = parser.parse_args()
    # 正式函数与服务端配置必须维持原字节，不降级成静态站。
    before = read(HERE / 'website-backend-before.json')
    assert before and all((SITE / p).is_file() and sha(SITE / p) == value for p, value in before.items()), 'BACKEND_CHANGED'
    import github_windows_assets as github
    payload = github.checked()
    domestic_sha = github.domestic()
    gh = [read(p) for p in (HERE / 'github-windows-assets-state').glob('readback-*.json')]
    assert any(p.get('all_four_anonymous_full_get_verified') is True and p.get('status') == 'passed'
        and p.get('package_sha256') == payload['package_sha256']
        and p.get('manifest_sha256') == payload['manifest_sha256']
        and p.get('expected_sha256') == sha(github.EXPECTED)
        and p.get('domestic_readback_sha256') == domestic_sha
        and p.get('repository') == github.REPO and p.get('release_id') == github.RELEASE_ID
        for p in gh), 'GITHUB_READBACK_REQUIRED'
    current = inventory()
    assert current and 'edgeone.json' in current and any(p.startswith('cloud-functions/') for p in current), 'FULL_WEBSITE_REQUIRED'
    frozen = HERE / 'website-frozen-files.json'
    if args.phase == 'freeze':
        save(frozen, current)
        print(json.dumps({'status':'frozen', 'files':len(current), 'backend_files_unchanged':len(before)}))
        return
    assert read(frozen) == current, 'FROZEN_WEBSITE_CHANGED'
    if args.phase == 'production':
        proof = read(HERE / 'preview-website-readback.json')
        preview_result = read(HERE / 'website-preview-result.json')
        preview_intent = read(HERE / 'website-preview.intent.json')
        preview_url = 'https://partyops-cn-overseas-'+preview_result['deployment_id']+'.edgeone.dev'
        assert preview_result['success'] and preview_intent['inventory_sha256'] == sha(frozen), 'ACTUAL_PREVIEW_REQUIRED'
        assert proof['all_passed'] and proof['phase'] == 'preview' and proof['base_url'] == preview_url \
            and proof['inventory_sha256'] == sha(frozen) and proof['deployment_result_sha256'] == sha(HERE / 'website-preview-result.json'), 'PREVIEW_REQUIRED'
        visual = read(HERE / 'website-preview-visual.json')
        assert visual['passed'] and visual['new_windows_link_verified'] and visual['ten_packages'] \
            and visual['base_url'] == preview_url and visual['inventory_sha256'] == sha(frozen), 'VISUAL_REQUIRED'
    command = ['npx.cmd', '--offline', 'edgeone@1.6.28', 'makers', 'deploy', str(DIST), '-n', 'partyops-cn-overseas', '-e', args.phase]
    save(HERE / ('website-'+args.phase+'.intent.json'), {'command':command, 'inventory_sha256':sha(frozen), 'at':datetime.datetime.now(datetime.timezone.utc).isoformat(), 'repeat_allowed':False})
    run = subprocess.run(command, cwd=SITE, capture_output=True, timeout=600)
    output = (run.stdout+run.stderr).decode('utf-8', errors='replace')
    output = re.sub(r'\?eo_token=[^\s\x1b]+', '?[preview-access-redacted]', output)
    output = re.sub(r'\x1b\[[0-9;]*m', '', output)
    (HERE / ('website-'+args.phase+'.log')).write_text(output, encoding='utf-8')
    ids = re.findall(r'Deployment ID: (\w+)', output)
    result = {'returncode':run.returncode, 'deployment_id':ids[-1] if ids else None, 'success':run.returncode==0 and 'Deploy Success' in output}
    save(HERE / ('website-'+args.phase+'-result.json'), result)
    print(json.dumps(result))
    assert result['success'], 'DEPLOY_OUTCOME_UNKNOWN_READ_ONLY_RECOVERY_REQUIRED'

if __name__ == '__main__':
    main()
