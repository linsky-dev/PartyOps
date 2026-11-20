# 1.4.6-rc.1 验收报告

| 范围 | 状态 | 证据 |
| --- | --- | --- |
| 工程恢复阶段本地回归 | PASS | `37f4efc` 在 Windows x64 主机运行两份实验室测试，83 项通过。PowerShell、Bash 语法及 Git 差异检查通过。 |
| 六种标准托管 Runner | PASS | [探测运行](https://github.com/linsky-dev/PartyOps/actions/runs/44439294911) 覆盖源检出、系统和架构、磁盘余量、Python、Node.js、Git。 |
| 首轮完整 PR CI | FAIL | [运行 44469509485](https://github.com/linsky-dev/PartyOps/actions/runs/44469509485) 记录 Windows 注册表模块导致 Linux 测试收集失败，前端 Blob 跨运行环境断言失败，以及 Pillow 12.3.0 告警。 |
| 已锁定的现代运行时依赖审计 | PASS | Windows x64、Python 3.12.10，以 `uv export --frozen` 导出运行时锁定依赖并执行 `pip-audit --no-deps --disable-pip`。更新 Pillow、certifi、filelock、urllib3 后无已知漏洞。Win7 回移轮子另行审计。 |
| 真实回环双向 TLS | PASS | Windows 开发运行时启动 Uvicorn mTLS，两张合成设备证书各自只出现在自己的连接上下文中。 |
| 账号、时间轴、通知与迁移源码专项 | PASS | Windows x64 开发环境的定向批次通过 79 项。新的集成提交继续由 PR 验证。 |
| 实际前端操作 | PASS | Windows Edge、1366×768，隔离源码服务验证登录信息修改、旧会话失效和一次性离线恢复，通知定位高亮实际评论，实际日期与参考日期共同展示。使用合成数据。 |
| Windows 云端全量后端基线 | FAIL | [运行 44527709198](https://github.com/linsky-dev/PartyOps/actions/runs/44527709198)，`f15480b` 得到 2204 通过、32 失败、13 跳过。问题包括新模式测试夹具、通知回归、Windows 路径和缺失的外部构建输入。 |
| 回滚、升级包模式及 生产 CSR 入网 | PASS | Windows x64 定向回归 101 项通过，沿正式 RC6 模式 `0026` 升级到 `0028`。 |
| 文件、AI、对象关联与 PKI 专项 | PASS | Windows x64 115 项通过，使用合成数据验证 HTTP 访问域和 PKI 异常路径。独立只读复核通过上轮两项权限泄露修复。 |
| 发展时间轴与提醒收敛 | PASS | Windows x64 55 项通过，覆盖确认实际节点、保留参考日期、撤销旧提醒及重新进入提醒窗口。后续 36 项专项独立验证法规和参考窗口、同日双提醒、旧通知已读状态及定位。 |
| 前端本地候选专项 | PASS | 244 项通过，行覆盖率 96.54%，分支覆盖率 93.04%。类型检查、生产构建及 4 项 Sites 测试通过。 |
| 六架构真实国密运行时 | PARTIAL | [运行 44527709086](https://github.com/linsky-dev/PartyOps/actions/runs/44527709086)。Windows x64、Ubuntu x64/ARM64 和 macOS Apple Silicon 通过真实 SM3/SM4-GCM 探测。Windows ARM64、macOS Intel 已安装原生 Python 3.12，缺本版锁定的 cryptography wheel，标记 BLOCKED。 |
| Windows .NET 前置输入 | PASS | 本地调用既有封包校验，微软离线前置包的大小、固定 SHA-256 和 Authenticode 通过，8 项 PE 静态测试通过。此处未安装 .NET。 [微软官方来源](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48) |
| Windows 全量后端源码（`edafb5d`） | 测试 PASS，门槛 FAIL | 本机 2292 项通过、15 跳过，行 96.12%、分支 91.77%；云端 2290 项通过、17 跳过，行 95.91%、分支 91.58%，低于原分支门槛 92%。云端 mypy 106 个源文件通过。 [云端运行](https://github.com/linsky-dev/PartyOps/actions/runs/44678047627) |
| Linux 后端与前端源码（`edafb5d`） | PASS | Linux 149 项通过、1 跳过，锁定依赖与静态审计通过。前端 244 项测试、4 项静态入口测试、类型检查、生产构建和生产依赖审计通过，覆盖率为行 96.54%、分支 93.04%。 [云端运行](https://github.com/linsky-dev/PartyOps/actions/runs/44678047627) |
| Windows x86 与 x64 上游排版项目 | BUILD_ONLY | 在标准 Windows Runner 上通过各架构上游快照校验、原始方案重新构建及三个随附回归程序。托管宿主脱离开发目录自检、排版金样及安装器另行验收。 [云端运行](https://github.com/linsky-dev/PartyOps/actions/runs/44678047627) |
| Windows x64 修复版 LibreOffice 输入 | PASS | 从上游 MSI 只读解包 26.2.7.3。官方 SHA-256、MSI 及两个启动程序的 Authenticode、x64 PE、许可证和现有打包输入门禁通过。隔离启动及一页合成 DOCX 转 PDF 的回读通过。 [官方来源与校验和](https://download.documentfoundation.org/libreoffice/stable/26.2.7/win/x86_64/LibreOffice_26.2.7_Win_x86-64.msi.mirrorlist) |
| 五个托管平台国密源码探测（`edafb5d`） | PARTIAL | macOS Intel 新增原生验证，锁定 OpenSSL 3.5.7 源构建与 SM3、SM4-GCM 已知答案、AAD 和标签拒绝通过。其余四个已通过的 Runner 复验通过。Windows ARM64 仍缺锁定 wheel。 [云端运行](https://github.com/linsky-dev/PartyOps/actions/runs/44678047573) |
| 十包构建与逐包真实安装 | NOT_RUN | 尚无本次冻结制品。 |
| 特殊目标原生安装、升级及真实 WPS | BLOCKED | 需要对应目标环境和合法可用的 WPS 输入。 |
| 双官网预览及下载回读 | BLOCKED | EdgeOne 控制台可见内容缺少可重建的原始工程。 |

验收证据以各次运行记录的执行系统、架构、命令、依赖和 SHA 为准。安装验收需追加包 SHA-256、日期和未覆盖范围。
GitHub PR 运行使用该次合并提交作为检出和附件名的一部分。例如，候选分支头为 `edafb5d` 时，工作流检出为 `ddcdd56f45aa`。
