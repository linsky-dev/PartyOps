# partyops公文排版助手独立桌面版

最后验证日期：2026-08-29（Asia/Hong_Kong）

## 目标与边界

独立版允许直接拖入 DOCX、DOC、WPS、RTF 或 PDF，选择功能后把结果导出到指定目录，不需要在 WPS/Word 中注册或显示加载项。它复用恢复工程中的统一功能注册表、命令、流水线、参数窗体、授权、转换和文件安全服务，不另写一套精简业务逻辑。

原始 Git 历史、未编译源码、原注释和原签名私钥不在发布二进制中，无法逐字恢复；当前目标是源码可维护、语义/行为尽可能等价并经本机验证。

## 已接入功能

- 一键排版：主/副标题、各级标题、正文、附件、落款、日期、图片、表格、页边距、网格和页码，继续使用现有多模板参数。
- 一键替换：普通文本、通配符、正则、纯格式和多规则方案。
- 一键套红：下行文、上行文、便函及份号、密级、紧急程度、机关、文号、签发人、红线和版记。
- 一键命名：标题、文号、副标题、日期、自定义文字和在线重命名规则。
- 一键转换：DOCX、PDF、TXT、分页图片和长图；页码范围、图片格式、DPI、保存位置和同名策略由“转换设置”控制。
- PDF 转 Word：继续使用恢复后的本地版面重建引擎。

## 安全执行模型

1. 独立版使用全生命周期 STA 后台线程管理 Word/WPS COM；OLE 消息筛选器处理宿主忙碌拒绝调用。
2. 启动隐藏宿主前检查已有文档；如果 COM 返回的是用户正在使用的 Word/WPS 会话，独立版拒绝接管，不隐藏、不关闭该会话。
3. DOCX 先通过原子事务复制到输出目录；DOC/WPS/RTF 以只读方式打开并另存 DOCX；业务命令只接触输出副本。
4. 未登录或授权不足在启动宿主前返回明确结果，不在后台线程弹出阻塞提示，也不绕过原授权逻辑。
5. 输出文件在提交前后执行格式完整性校验；WPS 异步写盘采用有限重试；失败输出只在已确认属于本次输出目录时删除。
6. “引擎自检”执行只读打开、另存、关闭、DOCX 包校验和源文件 SHA-256 前后比对，不执行收费业务。

## 构建与运行

本机 WPS 是 32 位，日常使用首选：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1 -Configuration Release -Platform x86
.\src\PartyOps.DocumentFormatter.Desktop\bin\x86\Release\PartyOps.DocumentFormatter.Desktop.exe
```

生成便携发布包：

```powershell
.\tools\Publish-Standalone.ps1 -Configuration Release -Platform x86 -SkipBuild
```

制品写入 `artifacts\publish\standalone` 下的新时间戳目录和 ZIP，不覆盖旧包，不向 C 盘安装程序文件。x64 Word/WPS 使用 `-Platform x64` 另行构建；位数不匹配时 COM 宿主无法启动。

## 本机实测证据

- `Release|x86` 与 `Release|x64`：均为 0 警告、0 错误；每个平台 26+11+128=165 项断言全部通过。
- WPS 12.1.0.28043：独立版成功通过 COM 启动隐藏宿主，源 DOCX 只读打开并另存为新 DOCX。
- 源文件 SHA-256 前后均为 `C26F8E6FC34F81B5D800F5B4907274A233C32B14C953D69F897E10BEB663F061`。
- 最终便携包再次实测，自检副本为 9,576 字节，包含 `word/document.xml`，通过 Open XML 包完整性校验；测试后可见 WPS 窗口为 0。
- 业务排版在未登录状态下正确被原授权逻辑阻止。登录后的全功能内容级 E2E 仍需使用用户自己的账号与代表性公文样本验收，不能用自检替代。

## 继续开发入口

- 桌面 UI：`src\PartyOps.DocumentFormatter.Desktop\MainForm.cs`
- 后台 STA：`src\PartyOps.DocumentFormatter.Desktop\Services\StaWorkQueue.cs`
- 批处理/输出安全：`src\PartyOps.DocumentFormatter.AddIn\DocumentRepository\Services\Hosting\Standalone\StandaloneBatchProcessor.cs`
- Word/WPS COM 生命周期：`src\PartyOps.DocumentFormatter.AddIn\DocumentRepository\Services\Hosting\Standalone\OfficeHostSession.cs`
- 五大功能统一执行：`src\PartyOps.DocumentFormatter.AddIn\DocumentRepository\Services\Features\FeatureTaskExecutor.cs`
