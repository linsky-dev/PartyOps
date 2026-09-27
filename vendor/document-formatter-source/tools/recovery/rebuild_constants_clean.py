import csv,struct,collections,json,sys,os
sys.path.insert(0,'/mnt/data/work')
from clrmini import PECLR
SRC='/mnt/data/work/orig/#U601d#U4eab#U6392#U7248#U52a9#U624b/partyops.documentformatter.dll'
CSV='/mnt/data/decrypted_constants.csv'
OUT='/mnt/data/work/partyops.documentformatter.constants-clean.dll'
LOG='/mnt/data/work/constants_clean_patch_log.json'
def align(x,a):return (x+a-1)//a*a
def cuint(n):
    if n<=0x7f:return bytes([n])
    if n<=0x3fff:return bytes([0x80|(n>>8),n&255])
    if n<=0x1fffffff:return bytes([0xC0|(n>>24),(n>>16)&255,(n>>8)&255,n&255])
    raise ValueError(n)
def us_entry(s):
    payload=s.encode('utf-16le')+b'\x01';return cuint(len(payload))+payload
def find_stream_headers(data,metadata_off=0):
    u16=lambda o:struct.unpack_from('<H',data,o)[0];u32=lambda o:struct.unpack_from('<I',data,o)[0]
    m=metadata_off;verlen=u32(m+12);p=align(m+16+verlen,4);n=u16(p+2);p+=4;out={}
    for _ in range(n):
        hs=p;off=u32(p);size=u32(p+4);q=p+8;end=data.index(0,q);name=data[q:end].decode('ascii');p=q+align(end-q+1,4);out[name]=(hs,off,size)
    return out
p=PECLR(SRC);data=bytearray(p.data)
rows=[]
with open(CSV,encoding='utf-8-sig',newline='') as f:
    for r in csv.DictReader(f):
        if r['generic_type']=='string':
            for k in ('method_rid','call_il_offset','source_il_offset'):r[k]=int(r[k])
            rows.append(r)
# preserve old #US exactly and append decrypted strings
old_us_off,old_us_size=p.streams['#US'];heap=bytearray(p.data[old_us_off:old_us_off+old_us_size])
if not heap:heap=bytearray(b'\0')
usoff={}
for r in rows:
    s=r['value']
    if s not in usoff:usoff[s]=len(heap);heap+=us_entry(s)
by=collections.defaultdict(list)
for r in rows:by[r['method_rid']].append(r)
patched=0;skipped=[];plog=[]
for mrid,rr in by.items():
    m=p.rows[6][mrid-1];mb=p.method_body(m);ins=p.disasm(m)
    if not mb:continue
    imap={o:i for i,(o,_,_) in enumerate(ins)};code_file=mb['offset']+mb['header']
    for r in rr:
        so=r['source_il_offset'];co=r['call_il_offset']
        if so not in imap or co not in imap:
            skipped.append((mrid,so,co,'offset'));continue
        ci=imap[co];end=ins[ci+1][0] if ci+1<len(ins) else mb['codesize'];length=end-so
        if length<5:
            skipped.append((mrid,so,co,'short'));continue
        tok=0x70000000|usoff[r['value']];pos=code_file+so
        data[pos:pos+5]=b'\x72'+struct.pack('<I',tok);data[pos+5:pos+length]=b'\0'*(length-5)
        patched+=1;plog.append({'method_rid':mrid,'type':r['type'],'method':r['method'],'start':so,'end':end,'us_token':hex(tok),'plaintext':r['value']})
# relocate whole metadata root into expanded .reloc section
pe=struct.unpack_from('<I',data,0x3c)[0];coff=pe+4;nsec=struct.unpack_from('<H',data,coff+2)[0];optsz=struct.unpack_from('<H',data,coff+16)[0];opt=coff+20;magic=struct.unpack_from('<H',data,opt)[0];dd=opt+(96 if magic==0x10b else 112);sec_align=struct.unpack_from('<I',data,opt+32)[0];file_align=struct.unpack_from('<I',data,opt+36)[0];sec=opt+optsz
reloc=None
for i in range(nsec):
    o=sec+i*40;name=bytes(data[o:o+8]).split(b'\0')[0]
    if name==b'.reloc':reloc=o
if reloc is None:raise RuntimeError('no reloc')
vs,va,rs,rp=struct.unpack_from('<IIII',data,reloc+8);cert_off,cert_size=struct.unpack_from('<II',data,dd+4*8);append_raw=rp+rs
data=data[:cert_off if cert_off and cert_off>=append_raw else append_raw]
new_meta_raw=len(data);new_meta_rva=va+(new_meta_raw-rp);orig_meta=bytearray(p.data[p.metadata_off:p.metadata_off+p.metadata_size]);mh=find_stream_headers(orig_meta,0);us_rel=align(len(orig_meta),4)
if len(orig_meta)<us_rel:orig_meta+=b'\0'*(us_rel-len(orig_meta))
us_hdr=mh['#US'][0];struct.pack_into('<II',orig_meta,us_hdr,us_rel,len(heap));orig_meta+=heap;new_meta_size=len(orig_meta);data+=orig_meta
new_raw_size=align(len(data)-rp,file_align);data+=b'\0'*(rp+new_raw_size-len(data));new_vs=(new_meta_raw-rp)+new_meta_size;struct.pack_into('<I',data,reloc+8,new_vs);struct.pack_into('<I',data,reloc+16,new_raw_size)
ch=struct.unpack_from('<I',data,reloc+36)[0];ch&=~0x02000000;ch|=0x40000000|0x40;struct.pack_into('<I',data,reloc+36,ch);struct.pack_into('<I',data,opt+56,align(va+max(new_vs,new_raw_size),sec_align));struct.pack_into('<II',data,dd+4*8,0,0);struct.pack_into('<II',data,p.clr_header_off+8,new_meta_rva,new_meta_size)
open(OUT,'wb').write(data)
q=PECLR(OUT)
json.dump({'patched':patched,'skipped':skipped,'unique_strings':len(usoff),'original_us_bytes':old_us_size,'combined_us_bytes':len(heap),'patches':plog},open(LOG,'w',encoding='utf8'),ensure_ascii=False,indent=2)
print({'patched':patched,'skipped':len(skipped),'unique_strings':len(usoff),'old_us':old_us_size,'new_us':len(heap),'size':len(data),'streams':q.streams})
