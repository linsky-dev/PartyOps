"""官方5.8.2只读连接预检；明确工作区，不部署、不输出授权头。"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import types

HERE = Path(__file__).resolve().parent
SOURCE = Path('C:/PartyOps-VM-Lab/records/kylin-20261012/acceptance/publication-recovery-20261016/workbuddy-cloudstudio-bridge.py')

def bridge(workspace):
    source = SOURCE.read_text(encoding='utf-8-sig')
    # 只在内存中扩展旧discover所需的三个版本判断；旧桥文件保持原样。
    identity = "{'5.6.2.0', '5.7.6.0', '5.7.7.0'}"
    context = "{'5.7.6.0', '5.7.7.0'}"
    if source.count(identity) != 1 or source.count(context) != 2:
        raise RuntimeError('ORIGINAL_DISCOVERY_VERSION_TEMPLATE_CHANGED')
    source = source.replace(identity, "{'5.8.2.0'}").replace(context, "{'5.8.2.0'}")
    module = types.ModuleType('official_workbuddy_582_readonly')
    module.__file__ = str(SOURCE)
    exec(compile(source, str(SOURCE), 'exec'), module.__dict__)
    module.TARGET_WORKSPACE = workspace
    return module

def run(workspace, discover_only=False):
    workspace = Path(workspace).resolve(strict=True)
    if not workspace.is_dir(): raise RuntimeError('EXPLICIT_EXISTING_WORKSPACE_REQUIRED')
    original_sha = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
    module = bridge(workspace)
    uri, headers = module.discover()
    result = {'status': 'discovered', 'official_version': '5.8.2.0', 'workspace': str(workspace),
              'old_bridge_sha256': original_sha, 'authorization_output': False, 'deploy_called': False,
              'network_write_performed': False, 'allowed_operations': ['discover']}
    if not discover_only:
        client = module.Client(uri, headers)
        client.request('initialize', 1, {'protocolVersion': '2025-06-18', 'capabilities': {},
                                       'clientInfo': {'name': 'PartyOps-readonly-preflight', 'version': '1.0'}})
        client.request('notifications/initialized')
        listed = client.request('tools/list', 2)
        tools = listed.get('tools')
        module.require(isinstance(tools, list) and all(isinstance(t, dict) and isinstance(t.get('name'), str) for t in tools), 'MCP_TOOLS_LIST_INVALID')
        known = [t['name'] for t in tools if t['name'] in {module.NEW_TOOL, module.OLD_TOOL}]
        module.require(len(known) == len(set(known)), 'DEPLOY_TOOL_AMBIGUOUS')
        result.update(status='readonly-initialized-listed', tool_count=len(tools), deployment_tool_names=known,
                      allowed_operations=['discover', 'initialize', 'notifications/initialized', 'tools/list'])
    module.require(hashlib.sha256(SOURCE.read_bytes()).hexdigest() == original_sha, 'ORIGINAL_BRIDGE_CHANGED')
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workspace', required=True, type=Path, help='主控已在官方UI核对的工作区，不自动猜测')
    parser.add_argument('--discover-only', action='store_true')
    args = parser.parse_args()
    try:
        print(json.dumps(run(args.workspace, args.discover_only), ensure_ascii=False)); return 0
    except Exception as error:
        code = str(error)
        print(json.dumps({'status': 'blocked', 'code': code if re.fullmatch('[A-Z][A-Z0-9_]{1,100}', code) else type(error).__name__,
                          'deploy_called': False, 'authorization_output': False, 'network_write_performed': False})); return 2

if __name__ == '__main__': raise SystemExit(main())
