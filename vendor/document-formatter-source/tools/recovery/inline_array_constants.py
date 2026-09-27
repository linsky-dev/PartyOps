import csv,re,json,os,glob
ROOT='/mnt/data/work/CleanRecovered'
CSV='/mnt/data/decrypted_constants.csv'

def cs_char(c):
    mp={'\\':'\\\\',"'":"\\'",'\r':'\\r','\n':'\\n','\t':'\\t','\0':'\\0','\b':'\\b','\f':'\\f','\v':'\\v'}
    if c in mp: return "'"+mp[c]+"'"
    o=ord(c)
    if o<32 or o==127 or o==0xa0 or o==0x3000:
        return "'\\u%04X'"%o
    return "'"+c+"'"

def literal(t,v):
    if t=='byte[]':
        b=bytes.fromhex(v[4:] if v.startswith('hex:') else v)
        return 'new byte[] { '+', '.join('0x%02X'%x for x in b)+' }'
    vals=json.loads(v)
    if t=='char[]': return 'new char[] { '+', '.join(cs_char(x) for x in vals)+' }'
    if t=='int[]': return 'new int[] { '+', '.join(str(int(x)) for x in vals)+' }'
    if t=='float[]':
        def ff(x):
            s=format(float(x),'.9g')
            if 'e' not in s.lower() and '.' not in s: s += '.0'
            return s+'f'
        return 'new float[] { '+', '.join(ff(x) for x in vals)+' }'
    raise ValueError(t)
rows=[]
with open(CSV,encoding='utf-8-sig',newline='') as f:
    for r in csv.DictReader(f):
        if r['generic_type']!='string': rows.append(r)
keys={(r['generic_type'],r['encrypted_int']):literal(r['generic_type'],r['value']) for r in rows}
assert len(keys)==34
pat=re.compile(r'global::_003CModule_003E\.[A-Za-z0-9_]+<(?P<t>char\[\]|int\[\]|float\[\]|byte\[\])>\((?P<n>-?\d+)\)')
count=0; misses=[]; changed=[]
for path in glob.glob(ROOT+'/**/*.cs',recursive=True):
    s=open(path,encoding='utf-8-sig').read()
    def repl(m):
        nonlocal_dummy=None
        k=(m.group('t'),m.group('n'))
        if k not in keys:
            misses.append((path,k,m.group(0))); return m.group(0)
        return keys[k]
    s2,n=pat.subn(repl,s)
    if n:
        actual=sum(1 for m in pat.finditer(s) if (m.group('t'),m.group('n')) in keys)
        count += actual
        open(path,'w',encoding='utf-8',newline='').write(s2)
        changed.append((path,actual))
print('replacements',count,'files',len(changed),'misses',len(misses))
print('\n'.join(f'{n:2d} {os.path.relpath(p,ROOT)}' for p,n in changed))
