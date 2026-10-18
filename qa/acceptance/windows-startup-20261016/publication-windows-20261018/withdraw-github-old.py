"""切换及四附件完整回读后只撤两个固定旧Windows附件；默认仅计划。"""
import argparse
import json
import subprocess
from pathlib import Path
import post_switch_common as common

r = common.release

def observe(module, payload, readback, retained):
    value = module.gh_api('repos/' + module.REPO + '/releases/' + str(module.RELEASE_ID))
    tag = module.gh_api('repos/' + module.REPO + '/git/ref/tags/' + module.TAG)
    r.require(value.get('id') == module.RELEASE_ID and value.get('tag_name') == module.TAG and value.get('draft') is False
        and tag['object'].get('type') == 'commit' and tag['object'].get('sha') == module.TAG_SHA, 'WITHDRAWAL_RELEASE_TAG_CHANGED')
    current = {a['id']: module.asset_identity(a) for a in value.get('assets', [])}
    r.require(len(current) == len(value.get('assets', [])) and all(current.get(a['id']) == a for a in retained), 'WITHDRAWAL_OTHER_ASSETS_CHANGED')
    for expected in payload['assets']:
        found = module.find_asset(value, expected)
        proof = next(a for a in readback['assets'] if a['filename'] == expected['filename'])
        r.require(found is not None and found['id'] == proof['asset_id'], 'WITHDRAWAL_NEW_ASSET_CHANGED')
    for old in common.GH_OLD:
        if old['id'] in current: r.require(current[old['id']] == old, 'WITHDRAWAL_OLD_ASSET_CHANGED')
        r.require(not any(a.get('name') == old['name'] and a.get('id') != old['id'] for a in value['assets']), 'WITHDRAWAL_OLD_NAME_REPLACED')
    return value

def run(action, website_proof, github_readback):
    plan = common.fixed_plan()
    module, payload, readback, links = common.proofs(website_proof, github_readback)
    state = r.HERE / 'github-windows-assets-state'
    snapshot = r.read(r.HERE / 'github-current-before.json')['release']
    retained = [module.asset_identity(a) for a in snapshot['assets'] if a['id'] not in {x['id'] for x in common.GH_OLD}]
    value = observe(module, payload, readback, retained)
    # 当前额外附件也按原值保留，绝不只保护本轮已知附件。
    retained = [module.asset_identity(a) for a in value['assets'] if a['id'] not in {x['id'] for x in common.GH_OLD}]
    results = []
    for old in common.GH_OLD:
        value = observe(module, payload, readback, retained)
        present = any(a['id'] == old['id'] for a in value['assets'])
        if action == 'recover':
            results.append({'asset': old, 'result': 'still-present' if present else 'absent-observed'}); continue
        if not present:
            results.append({'asset': old, 'result': 'already-absent'}); continue
        intent = state / (str(old['id']) + '.withdraw-intent.json')
        r.require(not intent.exists(), 'WITHDRAWAL_OUTCOME_UNKNOWN_READ_ONLY_ONLY')
        r.exclusive(intent, {'asset': old, 'authorized_scope': 'replace-only-old-windows-exe-and-sidecar', **links,
            'created_at': module.utc(), 'snapshot_sha256': r.digest(r.HERE / 'github-current-before.json')})
        # 复用正常gh认证；单次DELETE，无令牌读取/输出，无自动重试。
        try:
            command = subprocess.run([str(module.GH), 'api', '--method', 'DELETE', 'repos/' + module.REPO + '/releases/assets/' + str(old['id'])],
                                     capture_output=True, timeout=120)
            returned = command.returncode
        except subprocess.TimeoutExpired:
            returned = None
        after = observe(module, payload, readback, retained)
        r.require(not any(a['id'] == old['id'] or a['name'] == old['name'] for a in after['assets']), 'DELETE_OUTCOME_UNKNOWN_READ_ONLY_ONLY')
        results.append({'asset': old, 'result': 'deleted-observed', 'command_returncode': returned})
        r.exclusive(state / (str(old['id']) + '.withdraw-result.json'), {'asset': old, 'result': 'deleted-observed', **links, 'confirmed_at': module.utc()})
    final = observe(module, payload, readback, retained)
    target = state / ('withdrawal-readback-' + __import__('uuid').uuid4().hex[:12] + '.json')
    receipt = {'status': 'passed' if all(x['result'] != 'still-present' for x in results) else 'partial', 'results': results,
               'other_assets_unchanged': True, 'tag_unchanged': True, 'remaining_asset_count': len(final['assets']),
               'confirmed_at': module.utc(), 'network_write_performed': action == 'withdraw', **links}
    r.exclusive(target, receipt)
    return {'status': receipt['status'], 'receipt': str(target), 'network_write_performed': action == 'withdraw'}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['plan', 'withdraw', 'recover'], nargs='?', default='plan')
    parser.add_argument('--website-proof', type=Path)
    parser.add_argument('--github-readback', type=Path)
    parser.add_argument('--execute-network', action='store_true')
    parser.add_argument('--execute-read-only', action='store_true')
    args = parser.parse_args()
    r.require(args.execute_network == (args.action == 'withdraw'), 'EXPLICIT_WITHDRAW_FLAG_REQUIRED')
    r.require(args.execute_read_only == (args.action == 'recover'), 'EXPLICIT_RECOVERY_FLAG_REQUIRED')
    if args.action == 'plan': result = common.fixed_plan()
    else:
        r.require(args.website_proof is not None and args.github_readback is not None, 'REAL_POST_SWITCH_PROOFS_REQUIRED')
        result = run(args.action, args.website_proof, args.github_readback)
    print(json.dumps(result, ensure_ascii=False))

if __name__ == '__main__': main()
