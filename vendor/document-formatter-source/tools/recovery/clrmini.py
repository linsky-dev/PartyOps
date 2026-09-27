import struct
TABLE_NAMES=['Module','TypeRef','TypeDef','FieldPtr','Field','MethodPtr','MethodDef','ParamPtr','Param','InterfaceImpl','MemberRef','Constant','CustomAttribute','FieldMarshal','DeclSecurity','ClassLayout','FieldLayout','StandAloneSig','EventMap','EventPtr','Event','PropertyMap','PropertyPtr','Property','MethodSemantics','MethodImpl','ModuleRef','TypeSpec','ImplMap','FieldRVA','ENCLog','ENCMap','Assembly','AssemblyProcessor','AssemblyOS','AssemblyRef','AssemblyRefProcessor','AssemblyRefOS','File','ExportedType','ManifestResource','NestedClass','GenericParam','MethodSpec','GenericParamConstraint']
TABLE_ID={n:i for i,n in enumerate(TABLE_NAMES)}
CODED={
'TypeDefOrRef':(2,['TypeDef','TypeRef','TypeSpec']),
'HasConstant':(2,['Field','Param','Property']),
'HasCustomAttribute':(5,['MethodDef','Field','TypeRef','TypeDef','Param','InterfaceImpl','MemberRef','Module','DeclSecurity','Property','Event','StandAloneSig','ModuleRef','TypeSpec','Assembly','AssemblyRef','File','ExportedType','ManifestResource','GenericParam','GenericParamConstraint','MethodSpec']),
'HasFieldMarshal':(1,['Field','Param']),'HasDeclSecurity':(2,['TypeDef','MethodDef','Assembly']),
'MemberRefParent':(3,['TypeDef','TypeRef','ModuleRef','MethodDef','TypeSpec']),
'HasSemantics':(1,['Event','Property']),'MethodDefOrRef':(1,['MethodDef','MemberRef']),
'MemberForwarded':(1,['Field','MethodDef']),'Implementation':(2,['File','AssemblyRef','ExportedType']),
'CustomAttributeType':(3,[None,None,'MethodDef','MemberRef',None]),'ResolutionScope':(2,['Module','ModuleRef','AssemblyRef','TypeRef']),
'TypeOrMethodDef':(1,['TypeDef','MethodDef'])}
SCHEMAS={
0:[('Generation','u2'),('Name','str'),('Mvid','guid'),('EncId','guid'),('EncBaseId','guid')],
1:[('ResolutionScope','coded:ResolutionScope'),('Name','str'),('Namespace','str')],
2:[('Flags','u4'),('Name','str'),('Namespace','str'),('Extends','coded:TypeDefOrRef'),('FieldList','table:Field'),('MethodList','table:MethodDef')],
3:[('Field','table:Field')],4:[('Flags','u2'),('Name','str'),('Signature','blob')],5:[('Method','table:MethodDef')],
6:[('RVA','u4'),('ImplFlags','u2'),('Flags','u2'),('Name','str'),('Signature','blob'),('ParamList','table:Param')],7:[('Param','table:Param')],
8:[('Flags','u2'),('Sequence','u2'),('Name','str')],9:[('Class','table:TypeDef'),('Interface','coded:TypeDefOrRef')],
10:[('Class','coded:MemberRefParent'),('Name','str'),('Signature','blob')],11:[('Type','u2'),('Parent','coded:HasConstant'),('Value','blob')],
12:[('Parent','coded:HasCustomAttribute'),('Type','coded:CustomAttributeType'),('Value','blob')],13:[('Parent','coded:HasFieldMarshal'),('NativeType','blob')],
14:[('Action','u2'),('Parent','coded:HasDeclSecurity'),('PermissionSet','blob')],15:[('PackingSize','u2'),('ClassSize','u4'),('Parent','table:TypeDef')],
16:[('Offset','u4'),('Field','table:Field')],17:[('Signature','blob')],18:[('Parent','table:TypeDef'),('EventList','table:Event')],19:[('Event','table:Event')],
20:[('EventFlags','u2'),('Name','str'),('EventType','coded:TypeDefOrRef')],21:[('Parent','table:TypeDef'),('PropertyList','table:Property')],22:[('Property','table:Property')],
23:[('Flags','u2'),('Name','str'),('Type','blob')],24:[('Semantics','u2'),('Method','table:MethodDef'),('Association','coded:HasSemantics')],
25:[('Class','table:TypeDef'),('MethodBody','coded:MethodDefOrRef'),('MethodDeclaration','coded:MethodDefOrRef')],26:[('Name','str')],27:[('Signature','blob')],
28:[('MappingFlags','u2'),('MemberForwarded','coded:MemberForwarded'),('ImportName','str'),('ImportScope','table:ModuleRef')],29:[('RVA','u4'),('Field','table:Field')],
30:[('Token','u4'),('FuncCode','u4')],31:[('Token','u4')],32:[('HashAlgId','u4'),('MajorVersion','u2'),('MinorVersion','u2'),('BuildNumber','u2'),('RevisionNumber','u2'),('Flags','u4'),('PublicKey','blob'),('Name','str'),('Culture','str')],
33:[('Processor','u4')],34:[('OSPlatformID','u4'),('OSMajorVersion','u4'),('OSMinorVersion','u4')],
35:[('MajorVersion','u2'),('MinorVersion','u2'),('BuildNumber','u2'),('RevisionNumber','u2'),('Flags','u4'),('PublicKeyOrToken','blob'),('Name','str'),('Culture','str'),('HashValue','blob')],
36:[('Processor','u4'),('AssemblyRef','table:AssemblyRef')],37:[('OSPlatformID','u4'),('OSMajorVersion','u4'),('OSMinorVersion','u4'),('AssemblyRef','table:AssemblyRef')],
38:[('Flags','u4'),('Name','str'),('HashValue','blob')],39:[('Flags','u4'),('TypeDefId','u4'),('TypeName','str'),('TypeNamespace','str'),('Implementation','coded:Implementation')],
40:[('Offset','u4'),('Flags','u4'),('Name','str'),('Implementation','coded:Implementation')],41:[('NestedClass','table:TypeDef'),('EnclosingClass','table:TypeDef')],
42:[('Number','u2'),('Flags','u2'),('Owner','coded:TypeOrMethodDef'),('Name','str')],43:[('Method','coded:MethodDefOrRef'),('Instantiation','blob')],44:[('Owner','table:GenericParam'),('Constraint','coded:TypeDefOrRef')]}
# opcode mapping sufficient for parsing all ECMA CIL operands
OP={
0x00:('nop','none'),0x01:('break','none'),0x02:('ldarg.0','none'),0x03:('ldarg.1','none'),0x04:('ldarg.2','none'),0x05:('ldarg.3','none'),0x06:('ldloc.0','none'),0x07:('ldloc.1','none'),0x08:('ldloc.2','none'),0x09:('ldloc.3','none'),0x0A:('stloc.0','none'),0x0B:('stloc.1','none'),0x0C:('stloc.2','none'),0x0D:('stloc.3','none'),0x0E:('ldarg.s','u1'),0x0F:('ldarga.s','u1'),0x10:('starg.s','u1'),0x11:('ldloc.s','u1'),0x12:('ldloca.s','u1'),0x13:('stloc.s','u1'),0x14:('ldnull','none'),0x15:('ldc.i4.m1','none'),0x16:('ldc.i4.0','none'),0x17:('ldc.i4.1','none'),0x18:('ldc.i4.2','none'),0x19:('ldc.i4.3','none'),0x1A:('ldc.i4.4','none'),0x1B:('ldc.i4.5','none'),0x1C:('ldc.i4.6','none'),0x1D:('ldc.i4.7','none'),0x1E:('ldc.i4.8','none'),0x1F:('ldc.i4.s','i1'),0x20:('ldc.i4','i4'),0x21:('ldc.i8','i8'),0x22:('ldc.r4','r4'),0x23:('ldc.r8','r8'),0x25:('dup','none'),0x26:('pop','none'),0x27:('jmp','token'),0x28:('call','token'),0x29:('calli','token'),0x2A:('ret','none'),0x2B:('br.s','br1'),0x2C:('brfalse.s','br1'),0x2D:('brtrue.s','br1'),0x2E:('beq.s','br1'),0x2F:('bge.s','br1'),0x30:('bgt.s','br1'),0x31:('ble.s','br1'),0x32:('blt.s','br1'),0x33:('bne.un.s','br1'),0x34:('bge.un.s','br1'),0x35:('bgt.un.s','br1'),0x36:('ble.un.s','br1'),0x37:('blt.un.s','br1'),0x38:('br','br4'),0x39:('brfalse','br4'),0x3A:('brtrue','br4'),0x3B:('beq','br4'),0x3C:('bge','br4'),0x3D:('bgt','br4'),0x3E:('ble','br4'),0x3F:('blt','br4'),0x40:('bne.un','br4'),0x41:('bge.un','br4'),0x42:('bgt.un','br4'),0x43:('ble.un','br4'),0x44:('blt.un','br4'),0x45:('switch','switch'),
0x46:('ldind.i1','none'),0x47:('ldind.u1','none'),0x48:('ldind.i2','none'),0x49:('ldind.u2','none'),0x4A:('ldind.i4','none'),0x4B:('ldind.u4','none'),0x4C:('ldind.i8','none'),0x4D:('ldind.i','none'),0x4E:('ldind.r4','none'),0x4F:('ldind.r8','none'),0x50:('ldind.ref','none'),0x51:('stind.ref','none'),0x52:('stind.i1','none'),0x53:('stind.i2','none'),0x54:('stind.i4','none'),0x55:('stind.i8','none'),0x56:('stind.r4','none'),0x57:('stind.r8','none'),0x58:('add','none'),0x59:('sub','none'),0x5A:('mul','none'),0x5B:('div','none'),0x5C:('div.un','none'),0x5D:('rem','none'),0x5E:('rem.un','none'),0x5F:('and','none'),0x60:('or','none'),0x61:('xor','none'),0x62:('shl','none'),0x63:('shr','none'),0x64:('shr.un','none'),0x65:('neg','none'),0x66:('not','none'),0x67:('conv.i1','none'),0x68:('conv.i2','none'),0x69:('conv.i4','none'),0x6A:('conv.i8','none'),0x6B:('conv.r4','none'),0x6C:('conv.r8','none'),0x6D:('conv.u4','none'),0x6E:('conv.u8','none'),0x6F:('callvirt','token'),0x70:('cpobj','token'),0x71:('ldobj','token'),0x72:('ldstr','token'),0x73:('newobj','token'),0x74:('castclass','token'),0x75:('isinst','token'),0x76:('conv.r.un','none'),0x79:('unbox','token'),0x7A:('throw','none'),0x7B:('ldfld','token'),0x7C:('ldflda','token'),0x7D:('stfld','token'),0x7E:('ldsfld','token'),0x7F:('ldsflda','token'),0x80:('stsfld','token'),0x81:('stobj','token'),0x82:('conv.ovf.i1.un','none'),0x83:('conv.ovf.i2.un','none'),0x84:('conv.ovf.i4.un','none'),0x85:('conv.ovf.i8.un','none'),0x86:('conv.ovf.u1.un','none'),0x87:('conv.ovf.u2.un','none'),0x88:('conv.ovf.u4.un','none'),0x89:('conv.ovf.u8.un','none'),0x8A:('conv.ovf.i.un','none'),0x8B:('conv.ovf.u.un','none'),0x8C:('box','token'),0x8D:('newarr','token'),0x8E:('ldlen','none'),0x8F:('ldelema','token'),0x90:('ldelem.i1','none'),0x91:('ldelem.u1','none'),0x92:('ldelem.i2','none'),0x93:('ldelem.u2','none'),0x94:('ldelem.i4','none'),0x95:('ldelem.u4','none'),0x96:('ldelem.i8','none'),0x97:('ldelem.i','none'),0x98:('ldelem.r4','none'),0x99:('ldelem.r8','none'),0x9A:('ldelem.ref','none'),0x9B:('stelem.i','none'),0x9C:('stelem.i1','none'),0x9D:('stelem.i2','none'),0x9E:('stelem.i4','none'),0x9F:('stelem.i8','none'),0xA0:('stelem.r4','none'),0xA1:('stelem.r8','none'),0xA2:('stelem.ref','none'),0xA3:('ldelem','token'),0xA4:('stelem','token'),0xA5:('unbox.any','token'),0xB3:('conv.ovf.i1','none'),0xB4:('conv.ovf.u1','none'),0xB5:('conv.ovf.i2','none'),0xB6:('conv.ovf.u2','none'),0xB7:('conv.ovf.i4','none'),0xB8:('conv.ovf.u4','none'),0xB9:('conv.ovf.i8','none'),0xBA:('conv.ovf.u8','none'),0xC2:('refanyval','token'),0xC3:('ckfinite','none'),0xC6:('mkrefany','token'),0xD0:('ldtoken','token'),0xD1:('conv.u2','none'),0xD2:('conv.u1','none'),0xD3:('conv.i','none'),0xD4:('conv.ovf.i','none'),0xD5:('conv.ovf.u','none'),0xD6:('add.ovf','none'),0xD7:('add.ovf.un','none'),0xD8:('mul.ovf','none'),0xD9:('mul.ovf.un','none'),0xDA:('sub.ovf','none'),0xDB:('sub.ovf.un','none'),0xDC:('endfinally','none'),0xDD:('leave','br4'),0xDE:('leave.s','br1'),0xDF:('stind.i','none'),0xE0:('conv.u','none')}
OP2={0x00:('arglist','none'),0x01:('ceq','none'),0x02:('cgt','none'),0x03:('cgt.un','none'),0x04:('clt','none'),0x05:('clt.un','none'),0x06:('ldftn','token'),0x07:('ldvirtftn','token'),0x09:('ldarg','u2'),0x0A:('ldarga','u2'),0x0B:('starg','u2'),0x0C:('ldloc','u2'),0x0D:('ldloca','u2'),0x0E:('stloc','u2'),0x0F:('localloc','none'),0x11:('endfilter','none'),0x12:('unaligned.','u1'),0x13:('volatile.','none'),0x14:('tail.','none'),0x15:('initobj','token'),0x16:('constrained.','token'),0x17:('cpblk','none'),0x18:('initblk','none'),0x1A:('rethrow','none'),0x1C:('sizeof','token'),0x1D:('refanytype','none'),0x1E:('readonly.','none')}
class PECLR:
 def __init__(self,path):
  self.path=path;self.data=open(path,'rb').read();self.sections=[];self.streams={};self.tables={};self.rows={};self.parse_pe();self.parse_metadata()
 def u16(self,o):return struct.unpack_from('<H',self.data,o)[0]
 def u32(self,o):return struct.unpack_from('<I',self.data,o)[0]
 def rva_to_off(self,rva):
  for va,vsz,rawsz,raw in self.sections:
   if va<=rva<va+max(vsz,rawsz):return raw+(rva-va)
  if rva<self.size_headers:return rva
  raise ValueError(rva)
 def parse_pe(self):
  d=self.data;pe=self.u32(0x3c);coff=pe+4;nsec=self.u16(coff+2);optsz=self.u16(coff+16);opt=coff+20;magic=self.u16(opt);self.size_headers=self.u32(opt+60);dd=opt+(96 if magic==0x10b else 112);clr_rva=self.u32(dd+14*8);sec=opt+optsz
  for i in range(nsec):
   o=sec+i*40;vsz=self.u32(o+8);va=self.u32(o+12);rawsz=self.u32(o+16);raw=self.u32(o+20);self.sections.append((va,vsz,rawsz,raw))
  self.clr_header_off=self.rva_to_off(clr_rva);self.metadata_rva=self.u32(self.clr_header_off+8);self.metadata_size=self.u32(self.clr_header_off+12)
 def parse_metadata(self):
  d=self.data;m=self.rva_to_off(self.metadata_rva);self.metadata_off=m;verlen=self.u32(m+12);p=(m+16+verlen+3)&~3;nstreams=self.u16(p+2);p+=4
  for _ in range(nstreams):
   off=self.u32(p);size=self.u32(p+4);q=p+8;end=d.index(0,q);name=d[q:end].decode('ascii','replace');p=q+((end-q+1+3)//4)*4;self.streams[name]=(m+off,size)
  t,_=self.streams['#~' if '#~' in self.streams else '#-'];p=t;self.heap_sizes=d[p+6];p+=8;valid=struct.unpack_from('<Q',d,p)[0];p+=16;self.row_counts={i:0 for i in range(64)}
  for i in range(64):
   if (valid>>i)&1:self.row_counts[i]=self.u32(p);p+=4
  cur=p
  for i in range(64):
   n=self.row_counts.get(i,0)
   if n:
    if i not in SCHEMAS:raise ValueError(f'unknown table {i}')
    sz=self.row_size(i);self.tables[i]=(cur,sz,n);cur+=sz*n
  for i,(off,sz,n) in self.tables.items():
   arr=[]
   for rid in range(1,n+1):
    o=off+(rid-1)*sz;row={'rid':rid}
    for name,kind in SCHEMAS[i]:val,used=self.read_kind(o,kind);o+=used;row[name]=val
    arr.append(row)
   self.rows[i]=arr
 def heap_index_size(self,k):return 4 if (self.heap_sizes>>{'str':0,'guid':1,'blob':2}[k])&1 else 2
 def table_index_size(self,name):return 4 if self.row_counts[TABLE_ID[name]]>=65536 else 2
 def coded_index_size(self,name):
  bits,tabs=CODED[name];mx=max((self.row_counts[TABLE_ID[t]] if t else 0) for t in tabs);return 4 if mx>=(1<<(16-bits)) else 2
 def row_size(self,i):
  s=0
  for _,k in SCHEMAS[i]:
   if k=='u2':s+=2
   elif k=='u4':s+=4
   elif k in ('str','guid','blob'):s+=self.heap_index_size(k)
   elif k.startswith('table:'):s+=self.table_index_size(k[6:])
   elif k.startswith('coded:'):s+=self.coded_index_size(k[6:])
  return s
 def read_kind(self,o,k):
  if k=='u2':return self.u16(o),2
  if k=='u4':return self.u32(o),4
  if k in ('str','guid','blob'):
   z=self.heap_index_size(k);return (self.u16(o) if z==2 else self.u32(o)),z
  if k.startswith('table:'):
   z=self.table_index_size(k[6:]);return (self.u16(o) if z==2 else self.u32(o)),z
  if k.startswith('coded:'):
   z=self.coded_index_size(k[6:]);return (self.u16(o) if z==2 else self.u32(o)),z
 def get_string(self,idx):
  if not idx:return ''
  o,_=self.streams['#Strings'];o+=idx;e=self.data.find(b'\0',o);return self.data[o:e].decode('utf-8','replace')
 def typedef_name(self,rid):
  if not rid:return ''
  r=self.rows[2][rid-1];ns=self.get_string(r['Namespace']);n=self.get_string(r['Name']);return (ns+'.' if ns else '')+n
 def method_owner_map(self):
  out={};types=self.rows.get(2,[]);nm=len(self.rows.get(6,[]))
  for i,t in enumerate(types):
   a=t['MethodList'];b=(types[i+1]['MethodList'] if i+1<len(types) else nm+1)
   for rid in range(a,b):out[rid]=i+1
  return out
 def method_body(self,row):
  rva=row['RVA']
  if not rva:return None
  o=self.rva_to_off(rva);b=self.data[o]
  if b&3==2:
   size=b>>2;return {'offset':o,'header':1,'codesize':size,'code':self.data[o+1:o+1+size]}
  if b&3==3:
   fs=self.u16(o);hsize=((fs>>12)&15)*4;cs=self.u32(o+4);return {'offset':o,'header':hsize,'codesize':cs,'code':self.data[o+hsize:o+hsize+cs]}
  return None
 def disasm(self,row):
  mb=self.method_body(row)
  if not mb:return []
  code=mb['code'];out=[];pos=0
  while pos<len(code):
   start=pos;b=code[pos];pos+=1
   if b==0xFE:
    b2=code[pos];pos+=1;name,kind=OP2.get(b2,(f'UNKNOWN_FE{b2:02X}','none'))
   else:name,kind=OP.get(b,(f'UNKNOWN_{b:02X}','none'))
   a=None
   if kind=='u1':a=code[pos];pos+=1
   elif kind=='i1':a=struct.unpack_from('<b',code,pos)[0];pos+=1
   elif kind=='u2':a=struct.unpack_from('<H',code,pos)[0];pos+=2
   elif kind=='i4':a=struct.unpack_from('<i',code,pos)[0];pos+=4
   elif kind=='i8':a=struct.unpack_from('<q',code,pos)[0];pos+=8
   elif kind=='r4':a=struct.unpack_from('<f',code,pos)[0];pos+=4
   elif kind=='r8':a=struct.unpack_from('<d',code,pos)[0];pos+=8
   elif kind=='token':a=struct.unpack_from('<I',code,pos)[0];pos+=4
   elif kind=='br1':rel=struct.unpack_from('<b',code,pos)[0];pos+=1;a=pos+rel
   elif kind=='br4':rel=struct.unpack_from('<i',code,pos)[0];pos+=4;a=pos+rel
   elif kind=='switch':
    n=struct.unpack_from('<I',code,pos)[0];pos+=4;base=pos+4*n;a=[base+struct.unpack_from('<i',code,pos+4*j)[0] for j in range(n)];pos+=4*n
   out.append((start,name,a))
  return out
