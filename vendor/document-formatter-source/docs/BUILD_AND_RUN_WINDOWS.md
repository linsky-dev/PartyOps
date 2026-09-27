# Windows 构建、发布与运行

最后验证日期：2026-08-29（Asia/Hong_Kong）

## 当前推荐：独立桌面版

本机 `Word.Application` 由 WPS 接管，推荐使用无需注册加载项的独立桌面版：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1 -Configuration Release -Platform x86
.\src\PartyOps.DocumentFormatter.Desktop\bin\x86\Release\PartyOps.DocumentFormatter.Desktop.exe
.\tools\Publish-Standalone.ps1 -Configuration Release -Platform x86 -SkipBuild
```

独立版复用五大功能业务核心，只对输出副本操作。本机 32 位 WPS 已通过只读打开、另存 DOCX、完整性校验、关闭宿主及源文件 SHA-256 不变验证。详见 `docs\STANDALONE_DESKTOP.md` 与 `artifacts\standalone-validation.json`。

## 1. 环境要求

- Windows 10/11 x64。
- Visual Studio 2022 或 Build Tools 2022，包含 MSBuild 与 Roslyn。
- .NET Framework 4.8 Developer Pack/Targeting Pack；本机缺少该开发包时，构建脚本会明确告警并使用现有 4.x 运行时引用作为兼容回退。
- Microsoft Visual Studio Tools for Office Runtime（仅 VSTO 加载项路线必需）。
- 独立版需要已安装且位数匹配的 WPS 文字或 Microsoft Word；VSTO 实际加载验收必须安装桌面版 Microsoft Word。

当前恢复机的 MSBuild 位于 E 盘；脚本会自动定位。当前 `Word.Application` COM 注册被 WPS 接管且没有可定位的 `WINWORD.EXE`，宿主盘点脚本会把这种状态识别为“Word 不可用”，不会误装到 WPS。

## 2. 一键结构检查、构建与回归

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1 -Configuration Debug -Platform AnyCPU
.\build.ps1 -Configuration Release -Platform x86
.\build.ps1 -Configuration Release -Platform x64
```

`build.ps1` 会依次执行：

1. 全源码反编译残留门禁。
2. 解决方案 Restore/Rebuild。
3. `RecognitionRegressionTests`。
4. `SignatureLayoutHostSmoke`。
5. `FeatureParityRegressionTests`。

成功标准是 0 警告、0 错误，且三套回归程序退出码均为 0；当前最新 Release 配置共 165 项断言（26+11+128）。

覆盖率在隔离副本中采集，不会直接改写最终构建输出：

```powershell
.\tools\Collect-Coverage.ps1 -Configuration Release -Platform x64
```

## 3. 生成签名 VSTO 包

开发机本地验收：

```powershell
.\tools\Publish-Vsto.ps1 -Platform x86 -SkipBuild
.\tools\Publish-Vsto.ps1 -Platform x64 -SkipBuild
```

正式签名示例：

```powershell
.\tools\Publish-Vsto.ps1 `
  -Platform x64 `
  -CertificatePath 'E:\certificates\organization-code-signing.pfx' `
  -CertificatePassword '<从安全输入获取>' `
  -TimestampUrl 'https://<组织批准的时间戳服务>' `
  -SkipBuild
```

发布脚本会重建应用/部署清单、计算每个文件的长度与 SHA-256、更新部署清单对应用清单的绑定并完成 XML 签名。`assets` 中的原始清单仅是结构模板；它们的旧哈希和旧签名绝不能直接用于新 DLL。

最终目录：

- `artifacts/publish/x86`
- `artifacts/publish/x64`

## 4. 发布包只读验收

```powershell
.\artifacts\publish\x64\Install-Local.ps1 -Action Diagnose
```

只读诊断会验证：

- 两级清单密码学签名。
- 37 个应用文件的 SHA-256 与长度。
- 清单引用闭合和部署绑定。
- `ThisAddIn`、Word `appAddIn`、`Ribbon1` 入口。
- PDF 转 Word 与修复工具。
- Microsoft Word 是否存在、Word PE 位数与包位数是否匹配，以及 Word COM 是否被 WPS 接管。

## 5. 在装有 Microsoft Word 的机器安装

先选择与 Word 位数一致的包，关闭所有 `WINWORD.EXE`。开发证书本机验收：

```powershell
.\Install-Local.ps1 -Action Install -TrustPublisher -LaunchWord
```

正式受信任证书发布包：

```powershell
.\Install-Local.ps1 -Action Install -LaunchWord
```

安装器只写当前用户的 `HKCU\Software\Microsoft\Office\Word\Addins\partyops.documentformatter` 注册，Manifest 使用包内绝对 `file:` URI 与 `|vstolocal`。卸载只移除该加载项注册：

```powershell
.\Install-Local.ps1 -Action Uninstall
```

## 6. Word 实机验收矩阵

Word 启动后依次确认：

1. 加载项列表中 `partyops公文排版助手` 的 LoadBehavior 保持为 3，Ribbon 可见。
2. 一键排版验证全文、选区、模板切换、页边距/网格/页码、图片和表格。
3. 一键替换验证普通文本、通配符、正则、格式替换、多方案和批量规则。
4. 一键套红验证下行文、上行文、便函，以及份号、密级、紧急程度、机关、文号、签发人、红线和版记。
5. 一键命名验证标题、发文字号、副标题、日期和自定义文字组合，并确认文档保持打开。
6. 一键转换验证 DOCX/PDF/TXT、分页图片/长图、全部/范围/指定页、DPI、保存位置、同名策略和本地 PDF 转 Word。
7. 取消、只读/受保护文档、未保存文档、输出冲突与异常回滚路径符合提示。
8. 功能无需用户中心、账号登录或 WebView2，关闭 Word 后无残留 `WINWORD.EXE`。
9. 运行 `application\PartyOps.DocumentFormatter.AddInRepair.exe /diagnose <报告路径>`，检查 VSTO 运行时、注册、禁用项和加载日志。

若 Word 加载失败，先在修复器中开启 VSTO 日志，再复现并检查 `PartyOps.DocumentFormatter.AddIn.vsto.log`；不要先修改业务代码来掩盖环境或证书错误。

WPS 需要单独验收：保存所有文档并关闭 WPS 后，再使用专门的兼容性步骤验证 COM 加载、Ribbon 和上述功能。不要直接运行 Word 安装动作来覆盖 WPS 注册，也不要把 `Word.Application` 指向 WPS 当成 Word VSTO 已加载。

## 7. 回滚

- 源码级总回滚：保留的 `E:\paiban\PartyOps.DocumentFormatter.Source.zip`。
- 安装级回滚：运行 `Install-Local.ps1 -Action Uninstall`，发布目录和证书文件不会被删除。
- 中间实验制品和历史日志已可恢复地归档到 `artifacts/experimental-archive` 与 `artifacts/recovery-logs`。
