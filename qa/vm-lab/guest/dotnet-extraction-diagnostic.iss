; 最小 Inno 提取诊断：不安装任何软件、不登记卸载项、不修改 ACL、不重启。
#ifndef DiagnosticSource
  #error DiagnosticSource must name the controlled fixture or verified offline runtime
#endif
#ifndef DiagnosticOutput
  #error DiagnosticOutput must be inside the laboratory reports directory
#endif
#ifndef DiagnosticCompression
  #define DiagnosticCompression "lzma2/ultra64"
#endif
#ifndef DiagnosticName
  #define DiagnosticName "dotnet-extraction-probe"
#endif

[Setup]
AppName=PartyOps Extraction Diagnostic
AppVersion=1.0
AppId=PartyOps-Disposable-Extraction-Diagnostic
CreateAppDir=no
Uninstallable=no
CreateUninstallRegKey=no
PrivilegesRequired=admin
MinVersion=6.1sp1
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#DiagnosticOutput}
OutputBaseFilename={#DiagnosticName}
Compression={#DiagnosticCompression}
SolidCompression=yes
LZMANumBlockThreads=1
SetupLogging=yes
DisableWelcomePage=yes
DisableReadyPage=yes

[Files]
#ifdef DiagnosticPrefix
Source: "{#DiagnosticPrefix}"; DestName: "diagnostic-prefix.bin"; Flags: dontcopy
#endif
Source: "{#DiagnosticSource}"; DestName: "diagnostic-payload.bin"; Flags: dontcopy

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Payload, Hash, OriginalError: String;
begin
  NeedsRestart := False;
  Log('PARTYOPS_DIAGNOSTIC_ONLY: no prerequisite execution, no product install, no ACL change');
  Log('DIAGNOSTIC_TEMP=' + ExpandConstant('{tmp}'));
  try
    ExtractTemporaryFile('diagnostic-payload.bin');
    Payload := ExpandConstant('{tmp}\diagnostic-payload.bin');
    Hash := GetSHA256OfFile(Payload);
    Log('DIAGNOSTIC_EXTRACT_SUCCEEDED_SHA256=' + Hash);
  except
    OriginalError := GetExceptionMessage;
    Log('DIAGNOSTIC_EXTRACT_EXCEPTION=' + OriginalError);
  end;
  { 准备阶段明确中止，安装步骤永远不执行；失败日志由控制器另存。 }
  Result := 'PARTYOPS_DIAGNOSTIC_COMPLETE_NO_INSTALL';
end;
