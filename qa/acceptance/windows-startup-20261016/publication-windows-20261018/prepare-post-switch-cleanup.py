"""复用麒麟精确撤旧片段，仅准备本地receiver；不部署、不执行删除。"""
import argparse
import ast
import json
from pathlib import Path
import post_switch_common as common

r = common.release
SOURCE = r.PRIOR / 'prepare-post-switch-cleanup.py'

def receiver_source(source, meta, plan):
    old_baseline = meta['assets'][:-1]
    r.require(meta['assets'][-1]['file_name'] == r.NAME and len(old_baseline) == 10
        and [a for a in old_baseline if a['file_name'] == common.OLD_WINDOWS['file_name']] == [common.OLD_WINDOWS], 'EXACT_ELEVEN_TO_TEN_RECEIVER_REQUIRED')
    retained = [a for a in old_baseline if a['file_name'] != common.OLD_WINDOWS['file_name']]
    anchor = 'const baseline = ' + json.dumps(old_baseline, ensure_ascii=False) + ';'
    r.require(source.count(anchor) == 1, 'BASELINE_RECEIVER_ANCHOR_CHANGED')
    source = source.replace(anchor, 'const baseline = ' + json.dumps(retained, ensure_ascii=False) + ';', 1)
    template = SOURCE.read_text(encoding='utf-8')
    assignments = [node for node in ast.parse(template).body if isinstance(node, ast.Assign)
        and len(node.targets) == 1 and isinstance(node.targets[0], ast.Name) and node.targets[0].id == 'code']
    r.require(len(assignments) == 1 and isinstance(assignments[0].value, ast.Call)
        and isinstance(assignments[0].value.func, ast.Attribute) and assignments[0].value.func.attr == 'replace'
        and isinstance(assignments[0].value.func.value, ast.Constant), 'PINNED_WITHDRAWAL_TEMPLATE_CHANGED')
    # 复用原来逐文件hash/路径预检、全部预检后unlink及审计；不另造删除协议。
    code = assignments[0].value.func.value.value.replace('__ASSETS__', json.dumps(plan['old_assets'], ensure_ascii=False))
    code = code.replace('if (fs.realpathSync(ROOT) !== path.resolve(ROOT)',
        'if (path.resolve(ROOT) !== "/workspace" || fs.lstatSync(ROOT).isSymbolicLink() || fs.realpathSync(ROOT) !== path.resolve(ROOT)')
    for anchor in ('async function start() {', '  http.createServer(async(req,res)=> {', 'manifest_sha256:meta.manifest_sha256,\n        files:'):
        r.require(source.count(anchor) == 1, 'WITHDRAWAL_INSERTION_ANCHOR_CHANGED')
    r.require('async function withdrawSuperseded()' not in source, 'WITHDRAWAL_ALREADY_PREPARED')
    source = source.replace('async function start() {', code + '\nasync function start() {', 1)
    source = source.replace('  http.createServer(async(req,res)=> {', '  await withdrawSuperseded();\n  http.createServer(async(req,res)=> {', 1)
    source = source.replace('manifest_sha256:meta.manifest_sha256,\n        files:', 'manifest_sha256:meta.manifest_sha256,withdrawal:withdrawalResult,\n        files:', 1)
    return source, {**meta, 'assets': retained + [meta['assets'][-1]]}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['plan', 'prepare', 'observe'], nargs='?', default='plan')
    parser.add_argument('--website-proof', type=Path)
    parser.add_argument('--github-readback', type=Path)
    parser.add_argument('--execute-read-only', action='store_true')
    args = parser.parse_args()
    plan = common.fixed_plan()
    r.require(args.execute_read_only == (args.action == 'observe'), 'EXPLICIT_HEALTH_OBSERVATION_REQUIRED')
    if args.action == 'plan':
        target = r.HERE / 'post-switch-withdrawal-plan.json'
        if target.exists(): r.require(r.read(target) == plan, 'WITHDRAWAL_PLAN_CHANGED')
        else: r.exclusive(target, plan)
        print(json.dumps(plan, ensure_ascii=False)); return
    r.require(args.website_proof is not None and args.github_readback is not None, 'REAL_POST_SWITCH_PROOFS_REQUIRED')
    _, _, _, links = common.proofs(args.website_proof, args.github_readback)
    domestic = r.load('post_switch_checked_domestic', r.HERE / 'domestic-release.py')
    folder, binding = domestic.checked_batch()
    if args.action == 'observe':
        target = folder / 'post-switch-receiver'
        prepared = r.read(r.HERE / 'post-switch-preparation.json')
        r.require(prepared.get('receiver_sha256') == r.digest(target / '_serve.js')
            and all(prepared.get(k) == v for k, v in links.items()), 'PREPARED_POST_SWITCH_SOURCE_CHANGED')
        client = r.history().p.release_client()
        status, _, raw = client.request('GET', 'https://partyops-rc6-win7-x86.app.workbuddy.host/health')
        health = json.loads(raw)
        validate_health(health, r.read(target / 'upload-metadata.json'), plan, status)
        output = r.HERE / ('post-switch-health-' + __import__('uuid').uuid4().hex[:12] + '.json')
        r.exclusive(output, {'status': 'passed', 'health': health, 'binding': binding, **links, 'network_write_performed': False})
        print(json.dumps({'status': 'passed', 'receipt': str(output), 'network_write_performed': False})); return
    source, meta = receiver_source((folder / 'receiver/_serve.js').read_text(encoding='utf-8'), r.read(folder / 'receiver/upload-metadata.json'), plan)
    target = folder / 'post-switch-receiver'
    r.require(not target.exists(), 'POST_SWITCH_RECEIVER_EXISTS_INSPECT_ONLY')
    target.mkdir()
    (target / '_serve.js').write_text(source, encoding='utf-8', newline='\n')
    (target / 'index.html').write_bytes((folder / 'receiver/index.html').read_bytes())
    r.exclusive(target / 'upload-metadata.json', meta)
    r.exclusive(r.HERE / 'post-switch-preparation.json', {'status': 'prepared-not-deployed', 'binding': binding, **links,
        'old_assets': plan['old_assets'], 'retained_count': 10, 'receiver_sha256': r.digest(target / '_serve.js'),
        'source_cleanup_template_sha256': r.digest(SOURCE), 'token_copied': False, 'network_write_performed': False})
    print(json.dumps({'status': 'prepared-not-deployed', 'receiver': str(target), 'token_copied': False, 'network_write_performed': False}))

def validate_health(health, meta, plan, status=200):
    r.require(status == 200 and health.get('ok') is True and health.get('version') == '1.4.5-rc.6'
        and health.get('receiver') == r.PROFILE and health.get('manifest_sha256') == meta['manifest_sha256']
        and len(meta['assets']) == 10 and set(health.get('files', {})) == {x['file_name'] for x in meta['assets']}, 'POST_SWITCH_TEN_HEALTH_REQUIRED')
    for item in meta['assets']:
        row = health['files'][item['file_name']]
        r.require(row.get('file_ready') is True and row.get('bytes') == item['bytes'] and row.get('sha256') == item['sha256'], 'POST_SWITCH_RETAINED_FILE_CHANGED')
    rows = health.get('withdrawal', [])
    r.require(len(rows) == 2, 'POST_SWITCH_TWO_WITHDRAWALS_REQUIRED')
    for old in plan['old_assets']:
        selected = [x for x in rows if x.get('filename') == old['file_name']]
        r.require(len(selected) == 1 and selected[0].get('sha256') == old['sha256'] and selected[0].get('bytes') == old['bytes']
            and selected[0].get('result') in {'deleted', 'already-absent'}, 'EXACT_OLD_WINDOWS_WITHDRAWAL_REQUIRED')

if __name__ == '__main__': main()
