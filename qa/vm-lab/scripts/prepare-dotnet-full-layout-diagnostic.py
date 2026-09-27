"""只生成原文件布局的诊断封装配方，不执行产品构建或安装。"""
import hashlib
import json
import re
import shutil
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
ROOT = Path("E:/codex/PartyOps/.partyops-vm-lab/reports/win7-dotnet-extraction-20260908")
BUNDLE = REPO / "artifacts/PartyOps-1.4.5-rc.6-windows7-amd64"
ORIGINAL = REPO / "packaging/windows/PartyOps.iss"
EXPECTED_ISS = "71af22509a2685e4ad81d378f349ab61f412c2ad043a154970b6c01836bbf9f6"


def main():
    for drive, gib in (("D:/", 20), ("E:/", 32)):
        if shutil.disk_usage(drive).free < gib * 1024**3:
            raise RuntimeError("DIAGNOSTIC_DISK_RESERVE:" + drive)
    digest = hashlib.sha256(ORIGINAL.read_bytes()).hexdigest()
    if digest != EXPECTED_ISS:
        raise RuntimeError("DIAGNOSTIC_INSTALLER_BASE_CHANGED")
    raw = ORIGINAL.read_text(encoding="utf-8")
    sections = re.split(r"(?m)^(\[[A-Za-z]+\])\s*$", raw)
    kept = sections[0]
    for index in range(1, len(sections), 2):
        if sections[index] in {"[Setup]", "[Languages]", "[Messages]", "[Files]"}:
            kept += sections[index] + "\n" + sections[index + 1]
    kept = "#define PartyOpsLegacy\n" + kept
    kept = kept.replace('AppId={{1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A}', 'AppId=PartyOps-Disposable-Extraction-Diagnostic-Full')
    kept = kept.replace('OutputBaseFilename={#PartyOpsOutputBase}', 'OutputBaseFilename=full-layout')
    kept = kept.replace('SolidCompression=yes', 'SolidCompression=yes\nLZMANumBlockThreads=1\nCreateAppDir=no\nUninstallable=no\nCreateUninstallRegKey=no')
    kept = kept.replace('{#SourcePath}', str(REPO / 'packaging/windows'))
    kept += r'''
[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Payload, OriginalError: String;
begin
  NeedsRestart := False;
  Log('PARTYOPS_DIAGNOSTIC_ONLY: original Files layout, no installation or ACL mutation');
  try
    ExtractTemporaryFile('ndp48-x86-x64-allos-enu.exe');
    Payload := ExpandConstant('{tmp}\ndp48-x86-x64-allos-enu.exe');
    Log('DIAGNOSTIC_EXTRACT_SUCCEEDED_SHA256=' + GetSHA256OfFile(Payload));
  except
    OriginalError := GetExceptionMessage;
    Log('DIAGNOSTIC_EXTRACT_EXCEPTION=' + OriginalError);
  end;
  Result := 'PARTYOPS_DIAGNOSTIC_COMPLETE_NO_INSTALL';
end;
'''
    ROOT.mkdir(parents=True, exist_ok=True)
    (ROOT / 'full-layout.iss').write_text(kept, encoding='utf-8', newline='\n')
    (ROOT / 'preparation.json').write_text(json.dumps({
        'scope': 'inno-full-layout-extraction-only', 'original_iss_sha256': digest,
        'product_source_baseline': 'fc9082e1f6ae324967163ea643a0accfcbf8b501a6f1e7a03f1f7d9531cc1d21',
        'payload_bundle_source': '1dedcc3bb0611b8549fc7bc9b858c17b6f2bc4ca2df844a40f53dba664854750',
        'bundle': str(BUNDLE), 'diagnostic_source_sha256': hashlib.sha256(kept.encode()).hexdigest(),
        'rebuildable': True, 'runtime_environment_passed': False,
        'changes': '保留原文件顺序及压缩字典，单压缩任务；替换为无安装副作用的提取回调'}, ensure_ascii=False, indent=2), encoding='utf-8')
    print(str(ROOT / 'full-layout.iss'))


if __name__ == '__main__':
    main()
