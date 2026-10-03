# ADR：公文排版源码宿主的跨平台适配边界

日期：2026-09-01｜状态：Windows、Linux amd64 已通过；其余目标禁止出包

## 背景与输入

- 唯一功能规格：`E:\paiban\PartyOps.DocumentFormatter.Source`，已同步到
  `vendor/document-formatter-source`。
- 用户确认的正确金样：`backend/tests/fixtures/document-formatter-source/expected-source-formatted.docx`。
- 目标版本：`1.4.5-rc.6`；所有界面操作必须留在 PartyOps 页面内，不显示独立排版窗口。
- 结论时效：北京时间 2026-09-01。WPS 平台能力以当天官方文档和当前源码实测为准。

## 已验证事实

1. 原工具使用 .NET Framework 4.8、Word/WPS COM 和 154 个直接操作 Word
   对象模型的业务源码文件，不能把 Windows 二进制直接复制到 Linux/macOS。
2. 此前集成偏差来自两处：一是快照内三个上游业务文件曾被加入原独立版不存在的
   “选区/汇编”分支；二是 Windows COM 执行链曾被另一套 WPS 桥接机制替代。两者都会
   让 PartyOps 与独立版走不同代码路径，现已全部撤销。
3. 当前纳入构建的功能快照与 `E:\paiban\PartyOps.DocumentFormatter.Source` 逐文件
   归一化比对一致：898 个功能/构建文件，聚合 SHA-256 为
   `15c21b886f6a958fb61a3b106266b446a2b959b0085510015eeb790efaa770d3`（898 个受控文件）。
4. Windows x64/x86 已使用未改写的原 `StandaloneBatchProcessor` 和真实本机 WPS 完成：
   六功能 10 场景、配置字节级恢复、源文件 SHA-256 不变，以及用户金样的结构和三页
   像素一致性验证。正文样式同时包含 `firstLine=420` 和 `firstLineChars=200`，即原源码
   的 2 字符首行缩进。
5. Linux amd64 已把同一 AnyCPU 规则程序集与 Mono 运行时封装成单一 ELF，
   通过 528/528 WPS RPC 虚表核验、三页像素金样和六功能 10 场景；运行时不再
   依赖外装 Mono，也不依赖 JSAPI 加载项中继。
6. [WPS 客户端开发概述](https://open.wps.cn/documents/app-integration-dev/wps365/client/wpsoffice/wps-integration-mode/wps-client-dev-introduction)
   概括支持 Windows、Linux、macOS，但具体技术的支持范围不同。
7. [WPS C++ 集成示例](https://open.wps.cn/documents/app-integration-dev/wps365/client/wpsoffice/wps-integration-mode/c-app-integration-wps-guide/create-the-first-example)
   明确给出 Linux、中标麒麟和银河麒麟路径；官方示例要求 WPS 专业版和 Qt4/Qt5。
8. [WPS 加载项概述](https://open.wps.cn/documents/app-integration-dev/wps365/client/wpsoffice/wps-integration-mode/wps-addin-development/addin-overview)
   当前明确写明已适配 Windows/Linux，没有把 macOS 列入该具体能力。
9. WPS 宏编辑器文档展示了跨平台动态库后缀和 JavaScript 宏能力，但当前官方资料
   没有给出 macOS 上由外部本机进程静默启动 WPS、注入任务、等待宏完成并可靠关闭
   的受支持接口；不能据此假定 macOS 已经具备无人值守适配能力。
10. 金山官方开源示例仓库 `zouyf/wps` 的提交
   `5bf5c1b26245c4b3e07737b14984fd7810b4fcad` 明确列出 WPS 2019 的
   Windows、Linux 与 Mac 加载项支持，并提供本机 58890 中继协议。PartyOps 已按
   该协议实现令牌绑定、后台调用和“2 字符首行缩进”持久化探针；探针通过只说明
   对象模型链路可用，不说明六功能源码适配已经完成。

## 决策

- Windows：把原 `PartyOps.DocumentFormatter.AddIn` 业务工程原样编译进无窗口宿主，
  只新增请求/进度/取消的薄桥；宿主直接调用原 `StandaloneBatchProcessor`，不复制、
  改写或重实现任何排版规则。WPS 优先，只有用户选择自动且 WPS 宿主不可用时才回退
  Word。宿主使用 GUI 子系统且不显示窗口，PartyOps 通过私有任务目录交换任务信息。
- Linux：适配器固定为 WPS 官方 RPC；宿主只做 ABI 封送，仍执行原
  `StandaloneBatchProcessor`。每个架构必须在真实 UOS/麒麟目标机重新通过
  三页金样和六功能 10 场景，amd64 证据不能替代 arm64。
- macOS：不把“WPS 客户端总体支持 macOS”误写成“外部静默自动化接口已支持”。
  必须先在 Apple Silicon 与 Intel 原生 Mac 上完成 API 探针；探针和金样未通过前，
  ARM64/x86_64 PKG 构建均失败关闭。
- Windows 开发机：可以运行同一 JSAPI 探针来提前验证协议实现，但证据中的平台
  必须记录为 `windows`，且 Linux/macOS 构建器明确拒绝把它当作目标平台证据。
- 禁止回退到 PartyOps 旧 Python OOXML 排版器、LibreOffice UNO 近似排版或仅修改
  DOCX XML 的替代实现。LibreOffice 仅保留旧格式转换/渲染职责，不作为排版规则引擎。

## 发布硬门禁

所有平台的 `formatter-host/source-host.json` 使用 schema 2，必须包含平台、架构、
适配类型、六功能、25 项能力、北京时间时区、源码快照指纹和宿主 SHA-256。安装包自检
会实际执行宿主 `--self-test` 并核对返回契约。缺文件、错架构、错平台、哈希变化、
能力不完整或无法启动都会阻断构建/安装，不再返回 `not-applicable`。

Linux/macOS 本机宿主还必须分别提供环境变量：

- `PARTYOPS_LINUX_FORMATTER_RUNTIME`
- `PARTYOPS_MACOS_FORMATTER_RUNTIME`

环境变量只接受已完成真实 WPS 金样验收的当前架构目录。不得用空壳程序、固定成功
JSON 或 Windows 结果代替本平台实测。

## 回滚

这些改动只收紧候选包构建和安装自检，不修改业务数据。若门禁自身误判，可回退
`package_selftest.py`、各平台构建脚本及 schema 2 清单；不得通过恢复跨平台
`not-applicable` 来绕过排版功能缺失。
