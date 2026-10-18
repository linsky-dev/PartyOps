"""对指定官网部署做公开只读回验；不登录、不投稿、不写业务数据。"""
from pathlib import Path
import urllib.request, urllib.error, hashlib, json, re, sys, time
from datetime import datetime, timezone

root=Path(__file__).resolve().parent
base=sys.argv[1].rstrip('/')
phase=sys.argv[2]
assert phase in {'preview','production'}
assert base == 'https://partyops.cn' if phase == 'production' else re.fullmatch(r'https://partyops-cn-overseas-dp[a-z0-9]+[.]edgeone[.]dev',base)
dist=Path('C:/PartyOps-VM-Lab/website-recovered-20261014/dist/edgeone')
inventory=root/'website-frozen-files.json'
inventory_sha=hashlib.sha256(inventory.read_bytes()).hexdigest()
expected_files=json.loads(inventory.read_bytes())
actual_files={p.relative_to(dist).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in dist.rglob('*') if p.is_file()}
assert actual_files==expected_files,'FROZEN_WEBSITE_CHANGED'
deployment_result=root/('website-'+phase+'-result.json')
deployment=json.loads(deployment_result.read_bytes())
intent=json.loads((root/('website-'+phase+'.intent.json')).read_bytes())
assert deployment['success'] and intent['inventory_sha256']==inventory_sha,'ACTUAL_DEPLOYMENT_REQUIRED'
if phase=='preview': assert base=='https://partyops-cn-overseas-'+deployment['deployment_id']+'.edgeone.dev'
result=[]
def fetch(route):
    for i in range(2):
        try:
            with urllib.request.urlopen(urllib.request.Request(base+route,headers={'User-Agent':'PartyOps-release-verification','Cache-Control':'no-cache'}),timeout=45) as r:
                return r.status,r.read()
        except urllib.error.HTTPError as e:
            if i==0 and (e.code==429 or e.code>=500): time.sleep(20 if e.code==429 else 2); continue
            return e.code,e.read()
        except (TimeoutError,urllib.error.URLError):
            if i: raise
            time.sleep(2)
status,html=fetch('/')
assert status==200
assert html== (dist/'index.html').read_bytes()
paths=['/','/admin','/changelog','/releases/v1.4.5-rc.6/release-manifest.json','/releases/v1.4.5-rc.6/release-manifest-kylin-arm64-20261016.json','/releases/v1.4.5-rc.6/kylin-installation-20261016.zh-CN.md','/releases/v1.4.5-rc.6/release-manifest-windows-startup-20261018.json','/releases/v1.4.5-rc.6/windows-installation-20261018.zh-CN.md']
paths += re.findall(r'(?:src|href)="(/assets/[^\"]+)"',html.decode())
for route in paths:
    status,body=fetch(route)
    expected=(dist/'index.html') if route in {'/','/admin','/changelog'} else dist/route.lstrip('/')
    assert status==200 and body==expected.read_bytes(),f'PUBLIC_BYTE_MISMATCH:{route}:{status}'
    result.append(dict(route=route,status=status,bytes=len(body),sha256=hashlib.sha256(body).hexdigest(),matches_local=True))
for route in ['/api/reviews','/api/sponsors','/api/download-count','/api/admin/session','/api/admin/sponsors']:
    status,body=fetch(route)
    data=json.loads(body)
    assert status==(401 if route=='/api/admin/sponsors' else 200),(route,status)
    if route=='/api/admin/session': assert data.get('authenticated') is False
    assert b'ADMIN_NOT_CONFIGURED' not in body and b'SUPABASE_NOT_CONFIGURED' not in body
    result.append(dict(route=route,status=status,json_keys=list(data),body_sha256=hashlib.sha256(body).hexdigest(),expected_status=True))
receipt=dict(base_url=base,phase=phase,confirmed_at=datetime.now(timezone.utc).isoformat(),all_passed=True,checks=result,operations='public GET only',inventory_sha256=inventory_sha,deployment_result_sha256=hashlib.sha256(deployment_result.read_bytes()).hexdigest())
path=root/(phase+'-website-readback.json')
assert not path.exists()
path.write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(phase=phase,all_passed=True,checks=len(result)),ensure_ascii=False))
