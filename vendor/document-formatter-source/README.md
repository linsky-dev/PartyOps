# partyops公文排版助手 — NearSource 恢复工程

这是从授权持有的软件发布二进制中恢复、去混淆并重新工程化的源码版本。目标技术栈保持为 .NET Framework 4.8、Windows Forms 与 Word/WPS COM；除原 VSTO/WPS 加载项外，现已增加无需加载项的独立桌面版。

## 当前结论

截至 2026-08-29，工程已经达到“可维护源码 + 可重复构建 + 独立桌面运行 + 可验签加载项发布包”状态：

- 可维护源码共 858 个 `.cs` 文件、107,117 行；主加载项 834 个文件、104,079 行。
- Confuser 引用/标记、反编译 `goto`、ILSpy 诊断/override 注释、硬反编译失败标记、伪引用语法及恢复阶段占位标记均为 0。
- `Release|x86` 与 `Release|x64` 的独立版和整个解决方案均为 0 警告、0 错误。
- 一键排版、替换、套红、命名、转换及 PDF 转 Word 已固化为 25 条产品能力契约，并映射到真实源码责任类型。
- 最新 Release 配置均通过 26 项识别/目录/保护断言、11 项落款布局断言和 128 项功能/独立版生产契约，共 165 项。
- x86/x64 VSTO 包均通过应用清单签名、部署清单签名、37 个文件的长度与 SHA-256、清单闭合、部署绑定、入口与辅助程序等 8 项验证。
- 微软覆盖率工具在隔离副本中实测行覆盖率为 6.89%（3,484/50,550）、分支覆盖率为 4.92%（1,240/25,197），尚未达到 90% 目标。

本机当前没有可定位的 `WINWORD.EXE`；`Word.Application` COM 注册实际指向 WPS。原 VSTO 的 Microsoft Word 加载仍未闭环，但独立版已经通过 WPS 12.1.0.28043 的真实 COM 打开、另存、关闭、DOCX 完整性及源文件哈希不变验证，不再依赖 WPS 是否兼容 Word VSTO 清单。

## 工程结构

- `src/PartyOps.DocumentFormatter.AddIn`：Word VSTO 主加载项。
- `src/PartyOps.DocumentFormatter.Desktop`：直接拖入文件处理的独立桌面版。
- `src/PartyOps.DocumentFormatter.WpsShim`：保留的 WPS COM 加载桥源码（独立版无需注册它）。
- `src/PartyOps.DocumentFormatter.PdfToWord`：PDF 转 Word 辅助程序。
- `src/PartyOps.DocumentFormatter.AddInRepair`：加载项诊断与修复程序。
- `tests/RecognitionRegressionTests`：纯逻辑、注册目录与保护证据回归。
- `tests/SignatureLayoutHostSmoke`：落款布局宿主无关冒烟测试。
- `tests/FeatureParityRegressionTests`：五大功能参数、规则、能力契约及本地 PDF 转 Word 回归。
- `tools`：构建、清理、回归、覆盖率、Office 宿主盘点、清单签名、发布与包验证脚本。
- `artifacts/publish/standalone`：独立版 E 盘便携目录与 ZIP；`artifacts/publish/x86`、`artifacts/publish/x64` 为加载项包。

## 快速构建与发布

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1 -Configuration Debug -Platform AnyCPU
.\build.ps1 -Configuration Release -Platform x86
.\build.ps1 -Configuration Release -Platform x64
.\tools\Publish-Standalone.ps1 -Platform x86 -SkipBuild
.\tools\Publish-Vsto.ps1 -Platform x86 -SkipBuild
.\tools\Publish-Vsto.ps1 -Platform x64 -SkipBuild
```

发布脚本默认复用 `certificates/development` 下的自签名开发证书。对外发布必须传入组织持有的 PFX 与可用时间戳地址，不能冒充原发布者签名。

独立版说明见 `docs/STANDALONE_DESKTOP.md`；完整步骤见 `docs/BUILD_AND_RUN_WINDOWS.md`，功能闭环见 `docs/FEATURE_PARITY_MATRIX.md`，恢复与验收证据见 `docs/RECOVERY_COMPLETION_REPORT.md` 和 `artifacts/source-quality-final.json`。

## 不可恢复边界

发布二进制没有保存原始 Git 历史、源代码注释、未进入程序集的代码、绝大部分原局部变量名，也不包含原代码签名私钥。因此这里的 NearSource 指尽可能语义/行为等价、可编译、可维护、可发布的重建源码，不是逐字节伪造原开发仓库。
