import sys,struct,json,collections,os
sys.path.insert(0,'/mnt/data/work')
from clrmini import PECLR
SRC='/mnt/data/work/partyops.documentformatter.constants-clean.dll'
OUT='/mnt/data/work/partyops.documentformatter.controlflow-clean.dll'
LOG='/mnt/data/work/controlflow_clean_log.json'
p=PECLR(SRC); data=bytearray(p.data); mo=p.method_owner_map()

def ldc_val(op,a):
    if op=='ldc.i4': return a
    if op=='ldc.i4.s': return a
    if op=='ldc.i4.m1': return -1
    if op.startswith('ldc.i4.'):
        z=op.rsplit('.',1)[1]
        if z.isdigit(): return int(z)
    return None

def loc_idx(op,a,prefix):
    if op in (prefix,prefix+'.s'):return a
    if op.startswith(prefix+'.'):
        z=op.rsplit('.',1)[1]
        if z.isdigit():return int(z)
    return None

def dispatchers(ins):
    out=[]
    for i in range(len(ins)-6):
        k=ldc_val(ins[i][1],ins[i][2]);n=ldc_val(ins[i+4][1],ins[i+4][2]);li=loc_idx(ins[i+3][1],ins[i+3][2],'stloc')
        if k is not None and n is not None and ins[i+1][1]=='xor' and ins[i+2][1]=='dup' and li is not None and ins[i+5][1]=='rem.un' and ins[i+6][1]=='switch' and len(ins[i+6][2])==n:
            out.append({'i':i,'start':ins[i][0],'key':k,'loc':li,'n':n,'targets':ins[i+6][2],'end_i':i+6})
    return out

def u32(x):return x&0xffffffff
def mul32(a,b):return u32(u32(a)*u32(b))
def xor32(a,b):return u32(a)^u32(b)
def case_raw(state,d):
    raw=u32(state)^u32(d['key']); return raw%d['n'],raw

def ins_succ(ins):
    om={o:i for i,(o,_,_) in enumerate(ins)};succ={};cond={'brfalse','brfalse.s','brtrue','brtrue.s','beq','beq.s','bne.un','bne.un.s','bge','bge.s','bgt','bgt.s','ble','ble.s','blt','blt.s','bge.un','bge.un.s','bgt.un','bgt.un.s','ble.un','ble.un.s','blt.un','blt.un.s'}
    for i,(o,op,a) in enumerate(ins):
        nx=ins[i+1][0] if i+1<len(ins) else None
        if op in ('ret','throw','rethrow','endfinally'):ss=[]
        elif op in ('br','br.s','leave','leave.s'):ss=[a] if isinstance(a,int) else []
        elif op=='switch':ss=list(a)+([nx] if nx is not None else [])
        elif op in cond:ss=([a] if isinstance(a,int) else [])+([nx] if nx is not None else [])
        else:ss=[nx] if nx is not None else []
        succ[o]=[x for x in ss if x in om]
    return succ

def case_reach(ins,d):
    succ=ins_succ(ins);om={o:i for i,(o,_,_) in enumerate(ins)}; endpoint_cases=collections.defaultdict(set);case_nodes={}
    for ci,t in enumerate(d['targets']):
        stack=[t];seen=set()
        while stack:
            o=stack.pop()
            if o in seen or o==d['start']:continue
            seen.add(o);i=om.get(o)
            if i is None:continue
            op,a=ins[i][1],ins[i][2]
            if op in ('br','br.s') and a==d['start']:
                endpoint_cases[o].add(ci);continue
            stack.extend(succ.get(o,[]))
        case_nodes[ci]=seen
    return endpoint_cases,case_nodes,succ

cond_ops={'brfalse','brfalse.s','brtrue','brtrue.s','beq','beq.s','bne.un','bne.un.s','bge','bge.s','bgt','bgt.s','ble','ble.s','blt','blt.s','bge.un','bge.un.s','bgt.un','bgt.un.s','ble.un','ble.un.s','blt.un','blt.un.s'}

def parse_tail(ins,j,d):
    if j>=1:
        v=ldc_val(ins[j-1][1],ins[j-1][2])
        if v is not None:return {'kind':'direct_const','start_i':j-1,'state':u32(v),'raw_dep':False}
    if j>=5:
        a=ins[j-5:j];li=loc_idx(a[0][1],a[0][2],'ldloc');c=ldc_val(a[1][1],a[1][2]);k=ldc_val(a[3][1],a[3][2])
        if li==d['loc'] and c is not None and a[2][1]=='mul' and k is not None and a[4][1]=='xor':
            return {'kind':'direct_raw_mul_xor_const','start_i':j-5,'c':u32(c),'k':u32(k),'raw_dep':True}
    suffix_start=j;trans_c=None
    if j>=4:
        a=ins[j-4:j];li=loc_idx(a[0][1],a[0][2],'ldloc');c=ldc_val(a[1][1],a[1][2])
        if li==d['loc'] and c is not None and a[2][1]=='mul' and a[3][1]=='xor':trans_c=u32(c);suffix_start=j-4
    if suffix_start>=6 and ins[suffix_start-1][1]=='pop':
        popi=suffix_start-1
        if ins[popi-1][1]=='dup':
            vb=ldc_val(ins[popi-2][1],ins[popi-2][2]);bri=popi-3
            if vb is not None and bri>=3 and ins[bri][1] in ('br','br.s') and ins[bri][2]==ins[popi][0] and ins[bri-1][1]=='dup':
                va=ldc_val(ins[bri-2][1],ins[bri-2][2]);ci=bri-3
                if va is not None and ci>=0 and ins[ci][1] in cond_ops and ins[ci][2]==ins[popi-2][0]:
                    return {'kind':'conditional_diamond','start_i':ci,'op':ins[ci][1],'fall_base':u32(va),'taken_base':u32(vb),'c':trans_c,'raw_dep':trans_c is not None}
    return {'kind':'unknown','start_i':None,'raw_dep':False}

def eval_formula(t,raw=None,which=None):
    if t['kind']=='direct_const':return t['state']
    if t['kind']=='direct_raw_mul_xor_const':
        if raw is None:return None
        return xor32(mul32(raw,t['c']),t['k'])
    if t['kind']=='conditional_diamond':
        base=t['fall_base'] if which=='fall' else t['taken_base']
        if t['c'] is None:return base
        if raw is None:return None
        return xor32(base,mul32(raw,t['c']))
    return None
LONG_OP={'br':0x38,'brfalse':0x39,'brtrue':0x3A,'beq':0x3B,'bge':0x3C,'bgt':0x3D,'ble':0x3E,'blt':0x3F,'bne.un':0x40,'bge.un':0x41,'bgt.un':0x42,'ble.un':0x43,'blt.un':0x44}
def enc_br(op,src,target):
    op=op[:-2] if op.endswith('.s') else op;return bytes([LONG_OP[op]])+struct.pack('<i',target-(src+5))
def add_patch(ps,start,end,blob,kind,meta):
    if end-start<len(blob):raise ValueError((kind,start,end,len(blob)))
    ps.append({'start':start,'end':end,'payload':blob+b'\0'*(end-start-len(blob)),'kind':kind,'meta':meta})
summary=collections.Counter();logs=[];conflicts=[]
for rid,m in enumerate(p.rows[6],1):
    if not m['RVA']:continue
    ins=p.disasm(m);ds=dispatchers(ins)
    if not ds:continue
    mb=p.method_body(m);om={o:i for i,(o,_,_) in enumerate(ins)};patches=[]
    for d in ds:
        epcases,case_nodes,succ=case_reach(ins,d)
        trans={o:parse_tail(ins,om[o],d) for o in epcases}
        if any(t['kind']=='unknown' for t in trans.values()):summary['unknown_parse']+=sum(t['kind']=='unknown' for t in trans.values())
        union=set().union(*case_nodes.values()) if case_nodes else set();pred=collections.defaultdict(set)
        for a,ss in succ.items():
            for b in ss:pred[b].add(a)
        states=[set() for _ in range(d['n'])]
        init_state=ldc_val(ins[d['i']-1][1],ins[d['i']-1][2]); assert init_state is not None
        init_case,init_raw=case_raw(init_state,d);states[init_case].add(init_raw)
        switch_off=ins[d['end_i']][0]
        for ci,tgt in enumerate(d['targets']):
            for pr in pred.get(tgt,()):
                if pr not in union and pr!=switch_off:states[ci].add(None)
        changed=True;iters=0
        while changed and iters<200:
            changed=False;iters+=1
            for o,cases in epcases.items():
                t=trans[o]
                for ci in cases:
                    for raw in list(states[ci]):
                        vals=[eval_formula(t,raw,'fall'),eval_formula(t,raw,'taken')] if t['kind']=='conditional_diamond' else [eval_formula(t,raw)]
                        for st in vals:
                            if st is None:continue
                            nc,nraw=case_raw(st,d)
                            if nraw not in states[nc]:
                                if len(states[nc])<128:states[nc].add(nraw);changed=True
                                else:summary['state_set_overflow']+=1
        if iters>=200:summary['fixpoint_limit']+=1
        istart=ins[d['i']-1][0];iend=d['start'];assert iend-istart==5
        add_patch(patches,istart,iend,enc_br('br',istart,d['targets'][init_case]),'initial_state',{'dispatcher':d['start'],'state':init_state,'case':init_case,'target':d['targets'][init_case]});summary['initial_states']+=1
        for o,cases in epcases.items():
            t=trans[o];j=om[o]
            if t['kind']=='unknown':continue
            known_context=[];unknown_dep=False
            for ci in cases:
                for raw in states[ci]:
                    if raw is None and t['raw_dep']:unknown_dep=True
                    else:known_context.append((ci,raw))
            if not known_context and not t['raw_dep']:known_context=[(-1,None)]
            if unknown_dep or not known_context:summary['transitions_left_fallback']+=1;continue
            start=ins[t['start_i']][0];end=ins[j+1][0] if j+1<len(ins) else mb['codesize']
            if t['kind']=='conditional_diamond':
                fcases=set();tcases=set()
                for ci,raw in known_context:
                    fs=eval_formula(t,raw,'fall');ts=eval_formula(t,raw,'taken');fcases.add(case_raw(fs,d)[0]);tcases.add(case_raw(ts,d)[0])
                if len(fcases)!=1 or len(tcases)!=1:summary['ambiguous_transition']+=1;continue
                fc=next(iter(fcases));tc=next(iter(tcases));ft=d['targets'][fc];tt=d['targets'][tc]
                if ft==tt:
                    popc=1 if t['op'].split('.')[0] in ('brtrue','brfalse') else 2
                    blob=b'\x26'*popc+enc_br('br',start+popc,ft);kind='opaque_condition_removed';summary[kind]+=1
                else:
                    blob=enc_br(t['op'],start,tt)+enc_br('br',start+5,ft);kind='conditional_diamond';summary[kind]+=1
                add_patch(patches,start,end,blob,kind,{'dispatcher':d['start'],'cases':sorted(cases),'fall_case':fc,'taken_case':tc,'fall_target':ft,'taken_target':tt,'contexts':len(known_context)})
            else:
                tcases=set()
                for ci,raw in known_context:tcases.add(case_raw(eval_formula(t,raw),d)[0])
                if len(tcases)!=1:summary['ambiguous_transition']+=1;continue
                tc=next(iter(tcases));target=d['targets'][tc]
                add_patch(patches,start,end,enc_br('br',start,target),t['kind'],{'dispatcher':d['start'],'cases':sorted(cases),'target_case':tc,'target':target,'contexts':len(known_context)});summary[t['kind']]+=1
        summary['dispatchers']+=1
    patches.sort(key=lambda x:(x['start'],x['end']));acc=[]
    for x in patches:
        if acc and x['start']<acc[-1]['end']:
            y=acc[-1]
            if x['start']==y['start'] and x['end']==y['end'] and x['payload']==y['payload']:summary['duplicates']+=1;continue
            conflicts.append((rid,y['start'],y['end'],x['start'],x['end']));summary['conflicts']+=1;continue
        acc.append(x)
    code=mb['offset']+mb['header']
    for x in acc:data[code+x['start']:code+x['end']]=x['payload']
    logs.append({'rid':rid,'type':p.typedef_name(mo.get(rid)),'method':p.get_string(m['Name']),'dispatchers':len(ds),'patches':len(acc)});summary['methods']+=1;summary['patches']+=len(acc)
with open(OUT,'wb') as f:f.write(data)
q=PECLR(OUT);summary['dispatcher_headers_retained']=sum(len(dispatchers(q.disasm(m))) for m in q.rows[6] if m['RVA'])
with open(LOG,'w',encoding='utf8') as f:json.dump({'summary':dict(summary),'conflicts':conflicts,'methods':logs},f,ensure_ascii=False,indent=2)
print(json.dumps(dict(summary),indent=2,ensure_ascii=False));print('conflicts',len(conflicts),OUT,os.path.getsize(OUT))
