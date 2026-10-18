"""本轮撤旧的固定身份及真实生产/四附件回读门槛。"""
from pathlib import Path
import re
import release_driver as release

HERE = release.HERE
OLD_WINDOWS = {'file_name': 'PartyOps_1.4.5-rc.6_windows_amd64.exe', 'bytes': 443690896,
    'sha256': 'a7c044b0b830ce416fdf02c84daa2e7aa4929c0833d9b530bc47486d861a81be', 'magic_hex': '4d5a5000', 'content_type': 'application/octet-stream'}
HELD_WINDOWS = {'file_name': 'PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe', 'bytes': 449978846,
    'sha256': '3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27', 'magic_hex': '4d5a5000', 'content_type': 'application/octet-stream'}
GH_OLD = [{'id': 606128919, 'name': OLD_WINDOWS['file_name'], 'size': 443690896, 'digest': 'sha256:' + OLD_WINDOWS['sha256']},
          {'id': 606131252, 'name': OLD_WINDOWS['file_name'] + '.sha256', 'size': 105,
           'digest': 'sha256:b81d7b11fa23a0809c13e801650702a3e151cbc9455142c08ca196f2acace868'}]

def github():
    return release.load('post_switch_github_protocol', HERE / 'github_windows_assets.py')

def fixed_plan():
    module = github()
    before = release.read(HERE / 'github-current-before.json')
    release.require(before['release']['id'] == module.RELEASE_ID and before['release']['tag_name'] == module.TAG
        and before['tag']['object']['type'] == 'commit' and before['tag']['object']['sha'] == module.TAG_SHA, 'WITHDRAWAL_GITHUB_BASELINE_REQUIRED')
    rows = {r['id']: module.asset_identity(r) for r in before['release']['assets']}
    release.require(all(rows.get(a['id']) == a for a in GH_OLD), 'EXACT_OLD_WINDOWS_ASSET_IDENTITIES_REQUIRED')
    return {'status': 'planned-not-executed', 'root': '/workspace', 'downloads_root': '/workspace/downloads',
            'old_assets': [OLD_WINDOWS, HELD_WINDOWS], 'github_old_assets': GH_OLD,
            'retained_count': 10, 'final_windows_name': release.NAME,
            'required_proofs': ['production-website-readback', 'four-github-anonymous-full-get'], 'network_write_performed': False}

def proofs(website_path, github_path):
    website_path, github_path = Path(website_path).resolve(strict=True), Path(github_path).resolve(strict=True)
    release.require(website_path == HERE / 'production-website-readback.json'
        and github_path.parent == HERE / 'github-windows-assets-state' and re.fullmatch(r'readback-[a-f0-9]{12}\.json', github_path.name), 'CURRENT_BATCH_PROOF_PATHS_REQUIRED')
    module = github()
    payload = module.checked()
    domestic_sha = module.domestic()
    web, gh = release.read(website_path), release.read(github_path)
    production_website(web)
    release.require(web.get('phase') == 'production' and web.get('base_url') == 'https://partyops.cn' and web.get('all_passed') is True
        and isinstance(web.get('checks'), list) and len(web['checks']) > 0, 'PRODUCTION_WEBSITE_REQUIRED')
    wanted = {'/releases/v1.4.5-rc.6/' + release.MANIFEST: release.digest(HERE / 'batch' / release.MANIFEST),
              '/releases/v1.4.5-rc.6/windows-installation-20261018.zh-CN.md': release.digest(HERE / 'docs-draft/docs/windows-installation-20261018.zh-CN.md')}
    for route, sha in wanted.items():
        rows = [r for r in web['checks'] if r.get('route') == route]
        release.require(len(rows) == 1 and rows[0].get('status') == 200 and rows[0].get('matches_local') is True
            and rows[0].get('sha256') == sha, 'NEW_PRODUCTION_MANIFEST_GUIDE_REQUIRED')
    release.require(gh.get('status') == 'passed' and gh.get('repository') == module.REPO and gh.get('release_id') == module.RELEASE_ID
        and gh.get('tag') == module.TAG and gh.get('package_sha256') == module.SHA and gh.get('manifest_sha256') == module.MANIFEST_SHA
        and gh.get('expected_sha256') == release.digest(module.EXPECTED) and gh.get('domestic_readback_sha256') == domestic_sha
        and gh.get('all_four_anonymous_full_get_verified') is True and gh.get('old_27_preserved') is True and gh.get('tag_unchanged') is True
        and gh.get('missing') == [] and len(gh.get('assets', [])) == 4, 'NEW_FOUR_ASSET_READBACK_REQUIRED')
    for asset in payload['assets']:
        rows = [a for a in gh['assets'] if a.get('filename') == asset['filename']]
        release.require(len(rows) == 1 and rows[0].get('bytes') == asset['bytes'] and rows[0].get('sha256') == asset['sha256']
            and rows[0].get('public_readback_verified') is True and type(rows[0].get('asset_id')) is int, 'EXACT_NEW_ASSET_READBACK_REQUIRED')
    return module, payload, gh, {'website_proof': str(website_path), 'website_proof_sha256': release.digest(website_path),
        'github_readback': str(github_path), 'github_readback_sha256': release.digest(github_path), 'domestic_readback_sha256': domestic_sha}

def production_website(web):
    """复用部署入口的实际inventory，绑定同批生产intent/result与完整产物。"""
    inventory_file = HERE / 'website-frozen-files.json'
    result_file = HERE / 'website-production-result.json'
    inventory = release.read(inventory_file)
    result = release.read(result_file)
    intent = release.read(HERE / 'website-production.intent.json')
    sha = release.digest(inventory_file)
    deployed = release.load('post_switch_actual_website_inventory', HERE / 'deploy-website.py')
    release.require(isinstance(inventory, dict) and inventory and 'edgeone.json' in inventory
        and any(p.startswith('cloud-functions/') for p in inventory) and deployed.inventory() == inventory,
        'CURRENT_FROZEN_WEBSITE_REQUIRED')
    release.require(result.get('success') is True and result.get('returncode') == 0
        and isinstance(result.get('deployment_id'), str) and re.fullmatch(r'\w+', result['deployment_id'])
        and intent.get('inventory_sha256') == sha and web.get('inventory_sha256') == sha
        and web.get('deployment_result_sha256') == release.digest(result_file),
        'SAME_BATCH_PRODUCTION_DEPLOYMENT_REQUIRED')
