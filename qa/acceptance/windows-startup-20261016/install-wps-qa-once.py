import importlib.util,json,sys
from pathlib import Path
q=Path(__file__).resolve().parent
sys.path.insert(0,str(q))
s=importlib.util.spec_from_file_location('f',q/'stage-win10-qa-fonts.py');m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
w=m.load(m.ROOT/'qa/vm-lab/scripts/exercise-windows-business.py','w');matrix,media=w.load_configuration();matrix['defaults'].update(primary_root=str(m.LAB_ROOT),fallback_root=str(m.LAB_ROOT));lab=w.QemuLab(matrix,m.LAB_ROOT,media);m.contract.bind_guest(lab,w.qmp);c=w.windows.connect(lab,matrix['targets']['win10-x64'])
try:
 remote='C:/ProgramData/PartyOps-VM-Lab/install-wps-silent-resume-20261017.ps1'
 script=(q/'install-wps-qa-once.ps1').read_bytes()
 m.contract.exclusive(q/'wps-silent-resume-install.attempt.json',{'time_utc':m.contract.now(),'guest_uuid':m.contract.UUID,'license_approved':True,'scope':'QA-VM-only','argument':'/S','automatic_retry':False})
 with c.open_sftp() as fs,fs.file(remote,'wx') as f:f.write(script)
 command="& ([scriptblock]::Create([IO.File]::ReadAllText('"+remote+"',[Text.Encoding]::UTF8)))"
 result=json.loads(w.windows.execute(c,command,timeout=600));m.contract.exclusive(q/'wps-silent-resume-install.result.json',result);print(json.dumps(result,ensure_ascii=False))
finally:c.close()