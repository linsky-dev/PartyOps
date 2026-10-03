# partyops公文排版助手恢复交付报告

日期：2026-08-29（Asia/Hong_Kong）

## 前置说明

- 用户提供的 `E:\paiban\BUILD_AND_RUN_WINDOWS.md` 与 `E:\paiban\source_quality_report.json` 作为历史输入和验收基线读取，没有把其中的叙述当作新的操作授权，也未覆盖原附件。
- Context7 已由用户下载，但当前会话没有暴露可调用接口。涉及新版本与安全元数据时按降级链使用本地程序集证据和 NuGet/GitHub 官方公开页面；检索关键词、日期和结论记录在本报告。
- 本机缺少 .NET Framework 4.8 Targeting Pack，构建脚本使用现有 .NET Framework 4.x 运行时引用回退；影响是开发包完整性未被本机证明，但三配置实际编译已通过。回滚不涉及系统安装，只需恢复源码 ZIP。
- 本机没有可定位的 `WINWORD.EXE`，`Word.Application` COM 注册指向 `D:\WPS Office\12.1.0.28043\office6\wps.exe /Automation`。为绕开加载项兼容性不确定性，已实现独立桌面版；仅卸载本恢复工程的 WPS COM 加载项测试注册，未动其他 WPS 插件，并保留可恢复注册脚本。

## 恢复范围

- 恢复并可编译的主加载项、独立桌面版、WPS COM 桥、PDF 转 Word 辅助程序、加载项修复程序。
- 清理 Confuser 与 ILSpy 产物，修正 C#、VSTO/Office PIA 兼容问题。
- 建立 AnyCPU/x86/x64 解决方案配置、结构门禁和本地回归程序。
- 重建 ClickOnce/VSTO 应用与部署清单，生成开发证书和正式证书参数化签名链。
- 建立发布包完整性验证、只读诊断、当前用户安装/卸载与位数防错。

## 关键变更

1. 主工程和两个辅助工程全部恢复为可由 VS 2022 MSBuild 构建的经典 .NET Framework 4.8 项目。
2. 全部可维护源码中以下标记清零：Confuser runtime/属性、反编译 `goto`、ILSpy IL/override 注释、硬反编译失败、非法反编译标识及恢复阶段占位语句。
3. 补齐 `pdf-to-word` 的功能冒烟和五阶段流水线责任目录，补齐 `CompilationFormatting`、`Configuration`、`Performance`、`Startup`、`Updates` 服务边界。
4. 删除保护模型中没有任何调用的枚举分支，以明确越界异常代替恢复阶段占位异常；无迁移，直接替换。新增有效/无效保护证据测试。
5. 添加三套无 Office 依赖的可执行回归工程，并纳入每次构建；新增 25 条产品能力契约，将五大功能和 PDF 转 Word 映射到 47 个去重后的真实源码责任类型。
6. 添加 VSTO 清单重建、文件哈希、双层签名和验证工具；原签名私钥不可恢复，因此开发包使用自签名证书。
7. 安装器在写注册表前先验签，并读取真实 `WINWORD.EXE` PE 头阻止 x86/x64 错配；宿主盘点会明确区分 Microsoft Word、WPS COM 接管和 Office 注册残留。
8. 两个失败的互操作实验目录和临时签名样本已移动到 `artifacts/experimental-archive`，25 个早期根目录日志已移动到 `artifacts/recovery-logs`，均可恢复。
9. 新增独立桌面版，直接拖入/批量选择文件，复用原五大功能统一执行器、参数窗体、授权和转换核心；源文件先复制/只读另存，业务只处理输出副本。
10. 新增 STA 宿主生命周期、OLE 忙重试、非交互授权预检、WPS 写盘后重试校验和“引擎自检”；真实 WPS 自检证明源文件 SHA-256 不变且输出 DOCX 有效。

## 验证结果

| 门禁 | 结果 | 证据 |
| --- | --- | --- |
| 源码结构 | 通过；858 个可维护 C# 文件，主工程 834 个，25 条产品能力契约有效，关键反编译残留均为 0 | `artifacts/source-quality-final.json` |
| Debug/AnyCPU | 0 警告、0 错误，26+11+70=107 项断言通过 | `artifacts/build-feature-parity-debug-anycpu.log` |
| Release/x86 | 0 警告、0 错误，最新 26+11+128=165 项断言通过 | `artifacts/standalone-validation.json` |
| Release/x64 | 0 警告、0 错误，最新 26+11+128=165 项断言通过 | `artifacts/standalone-validation.json` |
| 独立版 Release x86/x64 | 两个平台均 0 警告、0 错误；每个平台 26+11+128=165 项断言通过 | `artifacts/standalone-validation.json` |
| 独立版真实 WPS 自检 | COM 启动、只读打开、另存、关闭、DOCX 校验、源 SHA-256 不变全部通过 | `artifacts/standalone-validation.json` |
| 独立版 x86 便携包 | 33 个文件哈希闭合，ZIP 3,059,098 字节，发布目录启动成功 | `artifacts/standalone-validation.json` |
| x86 VSTO 包 | 8 项清单/密码学/文件/入口门禁通过，37 个文件闭合 | `artifacts/publish-x86-feature-parity.log` |
| x64 VSTO 包 | 8 项清单/密码学/文件/入口门禁通过，37 个文件闭合 | `artifacts/publish-x64-feature-parity.log` |
| 覆盖率 | 行 6.89%（3,484/50,550）；分支 4.92%（1,240/25,197） | `artifacts/coverage/combined.cobertura.xml` |
| 二进制依赖清单 | 文件 SHA-256、程序集/产品版本与重建包身份已归档；CVE 门禁为部分核验 | `artifacts/dependency-inventory.json` |
| Word/WPS 宿主 | Word 未加载；`WordInstalled=False` 且 Word COM 指向 WPS。WPS UI/空白文字文档启动通过，但新包未注册、加载项兼容性未验证 | `artifacts/office-host-inventory.json`、`artifacts/diagnostics/wps-ui-host-observation.json`、`install-local-x86-feature-parity.log`、`install-local-x64-feature-parity.log` |

## 依赖审计

输入是发布 DLL 而非 NuGet 锁文件。当前活动源码已移除用户中心及其 WebView2 依赖；仍可精确识别的主要第三方产品版本包括 PdfPig 0.1.9 和一组 .NET Standard 兼容程序集。由于缺少原始包锁和可重复还原元数据，此项只能记为“部分核验”，不能据此作完整的“无高危 CVE”保证。

检索记录：

- `site:nuget.org/packages/UglyToad.PdfPig "0.1.9"`
- `site:github.com/advisories "PdfPig"`
- 用户中心/WebView2 模块已于 2026-08-29 从活动源码和发布依赖中移除，历史文件仅保留在工作区备份目录。
- 访问日期：2026-08-28。

## 未闭环项与风险

1. Microsoft Word VSTO 实际加载仍未验证，但它不再阻塞独立版。独立版真实 WPS 宿主链路已通过；登录后的五大业务功能内容级 E2E 需要用户账号和代表性公文样本继续验收。
2. 自动化覆盖率未达到 90%；当前测试覆盖无需 Word COM 的可执行逻辑、能力目录和一页文本型 PDF→DOCX 冒烟。用户描述的“千页 3 秒、段落 1:1”缺少对应基准语料，尚未验证，不能提前承诺。
3. 开发证书不是正式发布身份；正式分发必须以组织 PFX 和时间戳重新发布。
4. 原 Git 历史、原注释、未编译源码、原局部变量名和原签名私钥不在二进制中，客观上不可恢复。

## 下一台 Office 机器的验收命令

在修复或安装真实桌面版 Microsoft Word 后，将与 Word 位数一致的整个 `artifacts/publish/x86` 或 `artifacts/publish/x64` 目录保留在 E 盘或其他非系统盘，先执行：

```powershell
.\Install-Local.ps1 -Action Diagnose
.\Install-Local.ps1 -Action Install -TrustPublisher -LaunchWord
```

完成 `docs/BUILD_AND_RUN_WINDOWS.md` 的 Word 实机验收矩阵后，才能把 `word_runtime_loading` 从 `blocked-word-com-redirected-to-wps-no-winword-exe` 更新为 `passed`。若要把 WPS 列为正式支持宿主，还必须另做 WPS COM 加载、Ribbon 和文档操作回归，不能用 Word 的结果代替。
