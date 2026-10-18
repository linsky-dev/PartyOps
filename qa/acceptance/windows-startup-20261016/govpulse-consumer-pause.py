"""用户授权的临时队列暂停/原样恢复；不终止任务、不清队列、不访问 E 盘。"""
from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / '.qa/govpulse-control-client'))
from celery import Celery  # noqa: E402

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('action', choices=('pause', 'restore', 'status'))
args = parser.parse_args()
app = Celery('partyops-authorized-pause', broker='redis://127.0.0.1:6379/2')
before = json.loads((HERE / 'govpulse-consumers-before.json').read_text(encoding='utf-8'))
allowed = {'govpulse-collection-national', 'govpulse-collection-xuanhan', 'govpulse-analysis', 'govpulse-reports'}
if len(before) != 4 or {name.split('@')[0] for name in before} != allowed:
    raise SystemExit('WORKER_IDENTITY_MISMATCH')
record = HERE / 'govpulse-consumer-pause-receipt.json'
state = json.loads(record.read_text(encoding='utf-8')) if record.exists() else {'operations': []}

def save() -> None:
    record.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding='utf-8')

def queues() -> dict:
    result = app.control.inspect(destination=list(before), timeout=6).active_queues()
    if result is None or set(result) != set(before):
        raise RuntimeError('WORKER_REPLY_INCOMPLETE')
    return {worker: [q['name'] for q in rows] for worker, rows in result.items()}

if args.action != 'status':
    current = queues()
    state['requested_action'] = args.action
    state['requested_at'] = datetime.now(timezone.utc).isoformat()
    state['before_worker_queues'] = {w: [q['name'] for q in qs] for w, qs in before.items()}
    save()
    for worker, originals in before.items():
        if set(current[worker]) - {q['name'] for q in originals}:
            raise SystemExit('QUEUE_CONFIGURATION_CHANGED')
        for queue in originals:
            name = queue['name']
            if args.action == 'pause' and name in current[worker]:
                reply = app.control.cancel_consumer(name, destination=[worker], reply=True, timeout=8)
            elif args.action == 'restore' and name not in current[worker]:
                exchange = queue['exchange']
                options = {k: queue[k] for k in ('durable', 'exclusive', 'auto_delete', 'queue_arguments', 'binding_arguments') if k in queue}
                reply = app.control.add_consumer(name, exchange=exchange['name'], exchange_type=exchange['type'], routing_key=queue['routing_key'], options=options, destination=[worker], reply=True, timeout=8)
            else:
                continue
            state['operations'].append({'action': args.action, 'worker': worker, 'queue': name, 'reply': reply, 'time': datetime.now(timezone.utc).isoformat()})
            save()
            if not reply or any('error' in item.get(worker, {}) for item in reply):
                raise SystemExit('CONTROL_REPLY_FAILED_CHECK_RECEIPT')

current = queues()
state['current_worker_queues'] = current
state['checked_at'] = datetime.now(timezone.utc).isoformat()
expected = {w: ([] if args.action == 'pause' else [q['name'] for q in qs]) for w, qs in before.items()}
if args.action != 'status':
    passed = all(set(current[w]) == set(expected[w]) for w in before)
    state['status'] = ('paused' if args.action == 'pause' else 'restored') if passed else 'incomplete'
    save()
    if not passed:
        raise SystemExit('CONTROL_VERIFICATION_FAILED')
else:
    # 状态以本次实际读回为准：控制已成功但上次回读超时时，不沿用旧 paused。
    original_queues = {w: {q['name'] for q in qs} for w, qs in before.items()}
    if all(set(current[w]) == original_queues[w] for w in before):
        state['status'] = 'restored'
    elif all(not current[w] for w in before):
        state['status'] = 'paused'
    else:
        state['status'] = 'incomplete'
    save()
print(json.dumps({'status': state.get('status'), 'queues': current}, ensure_ascii=False))
