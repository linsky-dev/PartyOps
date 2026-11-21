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
| Windows 全量后端源码（`3c672a3`） | 测试 PASS，门槛 FAIL | 本机 2308 项通过、15 跳过，行 96.18%、分支 91.93%；云端 2306 项通过、17 跳过，行 95.97%、分支 91.75%。保留 95% 行、92% 分支门槛。 [云端运行](https://github.com/linsky-dev/PartyOps/actions/runs/44679679022) |
| Linux 后端与前端源码（`edafb5d`） | PASS | Linux 149 项通过、1 跳过，锁定依赖与静态审计通过。前端 244 项测试、4 项静态入口测试、类型检查、生产构建和生产依赖审计通过，覆盖率为行 96.54%、分支 93.04%。 [云端运行](https://github.com/linsky-dev/PartyOps/actions/runs/44678047627) |
| Windows x86 与 x64 上游排版项目 | BUILD_ONLY | 在标准 Windows Runner 上通过各架构上游快照校验、原始方案重新构建及三个随附回归程序。托管宿主脱离开发目录自检、排版金样及安装器另行验收。 [云端运行](https://github.com/linsky-dev/PartyOps/actions/runs/44678047627) |
| 脱离源码目录的 Windows 排版宿主 | BUILD_ONLY | `3c672a3` 的 x64、x86 和 AnyCPU 原生构建及搬迁自检通过。三个宿主 EXE 与对应清单的 SHA-256 一致。AnyCPU 产物通过 Linux 封装的上游快照和两项托管二进制校验。真实 WPS、金样及 Linux 原生封装待验。 [云端运行](https://github.com/linsky-dev/PartyOps/actions/runs/44679679022) |
| Windows 句柄占用与换库补偿 | PASS | 用真实 Windows 文件句柄占用隔离的 SHM。换库报权限错误后，原 SQLite 主库摘要及两份旁路文件保持一致。另通过五类故障注入，含补偿二次失败时保留同批文件并要求停服。 |
| Windows x64 修复版 LibreOffice 输入 | PASS | 从上游 MSI 只读解包 26.2.7.3。官方 SHA-256、MSI 及两个启动程序的 Authenticode、x64 PE、许可证和现有打包输入门禁通过。隔离启动及一页合成 DOCX 转 PDF 的回读通过。 [官方来源与校验和](https://download.documentfoundation.org/libreoffice/stable/26.2.7/win/x86_64/LibreOffice_26.2.7_Win_x86-64.msi.mirrorlist) |
| 五个托管平台国密源码探测（`edafb5d`） | PARTIAL | macOS Intel 新增原生验证，锁定 OpenSSL 3.5.7 源构建与 SM3、SM4-GCM 已知答案、AAD 和标签拒绝通过。其余四个已通过的 Runner 复验通过。Windows ARM64 仍缺锁定 wheel。 [云端运行](https://github.com/linsky-dev/PartyOps/actions/runs/44678047573) |
| 云端后端全量覆盖率（`e51d1235`） | PASS | [运行 44699646896](https://github.com/linsky-dev/PartyOps/actions/runs/44699646896)：Windows 2324 通过、16 跳过、3 个子测试通过；行覆盖率 96.21%、分支覆盖率 92.00%，达到原 95%/92% 门禁。Linux 专项 170 通过、4 跳过，前端及三个 Windows 排版宿主构建作业均成功。这是 `e51d1235` 的证据，后续安全补丁仍须单独复测。 |
| 云端分架构探测（`e51d1235`） | PARTIAL | [运行 44699646844](https://github.com/linsky-dev/PartyOps/actions/runs/44699646844)：Windows x64、Ubuntu x64/ARM64、macOS Intel/ARM64 国密运行时探测成功，macOS Intel/ARM64 LibreOffice 独立 headless 探测成功。Windows ARM64 对锁定 `cryptography==50.0.0` 缺 wheel，维持失败/阻塞，不视为已适配。 |
| Windows 全量源码与覆盖率（`1282ffd0`） | PASS | [GitHub Actions #44704652175](https://github.com/linsky-dev/PartyOps/actions/runs/44704652175)：Windows 2,332 项通过、16 项跳过、3 项子测试通过；行覆盖率 96.22%、分支覆盖率 92.01%，均超过 95%/92% 门槛；Linux 176 通过、4 跳过，前端 246 项、生产构建及原有 Windows 排版宿主 x86/x64/AnyCPU 构建均通过。此为源码与 CI 门禁，不是发布包验收。 |
| Windows ARM64 原生国密运行时（`1282ffd0`） | PASS（运行时） | [GitHub Actions #44704652210](https://github.com/linsky-dev/PartyOps/actions/runs/44704652210) 实际使用 ARM64 Windows Runner、Python 3.12、MSVC/Rust，从固定 SHA-256 的 OpenSSL 3.5.7 及 cryptography 50.0.0 sdist 构建 wheel，绑定 OpenSSL 3.5.7；通过 SM3、RFC 8998 SM4-GCM、标签与 AAD 篡改拒绝。该运行时探测未测试完整 PartyOps ARM64 安装包。 |
| 原生六平台国密与 macOS LibreOffice（`1282ffd0`） | PASS（专项探测） | 同一 [运行 #44704652210](https://github.com/linsky-dev/PartyOps/actions/runs/44704652210) 的 8 个作业均成功。macOS Intel 从锁定 OpenSSL 3.5.7 编译的原生密码库通过向量；LibreOffice 两架构为 headless 独立冒烟，不等于 WPS。 |
| ARM64 密码库离线 wheel 审查附件（`1470c710`） | PASS（BUILD_ONLY） | [GitHub Actions #44710662726](https://github.com/linsky-dev/PartyOps/actions/runs/44710662726) 8/8 作业通过，原生 Windows ARM64 由固定源码构建 cryptography 50.0.0 wheel，离线安装和 SM3/SM4-GCM 已知答案通过，留存哈希、来源与显式未签名信息。 [临时构建附件](https://github.com/linsky-dev/PartyOps/actions/runs/44710662726/artifacts/14616598687)。此结果不等于 ARM64 完整 PartyOps 应用已验收。 |
| PR 源码与锁定依赖 SBOM（`f9b526ca`） | PASS（BUILD_ONLY） | [GitHub Actions #44712221242](https://github.com/linsky-dev/PartyOps/actions/runs/44712221242)：PR 测试合并提交的源码归档、Python/前端 CycloneDX、大小与 SHA-256 核验均通过；[审查附件](https://github.com/linsky-dev/PartyOps/actions/runs/44712221242/artifacts/14616984364)。没有生产发布能力与签名。 |
| 2026-11-20 最近稳定源码基线（`5c3086e7`） | PASS（源码 CI） | [完整源码 #44748807004](https://github.com/linsky-dev/PartyOps/actions/runs/44748807004)、[八项跨平台 #44748806963](https://github.com/linsky-dev/PartyOps/actions/runs/44748806963)、[真实 BGE ARM64 #44748807098](https://github.com/linsky-dev/PartyOps/actions/runs/44748807098) 和 [SBOM #44748807133](https://github.com/linsky-dev/PartyOps/actions/runs/44748807133) 全部成功；此基线未包含后续新增模型包签名验证。 |
| ARM64 测试密钥签名模型包（新增专项） | NOT_RUN | 原生 ARM64 在已有 `cryptography==50.0.0` 与 OpenSSL 3.5.7 源构建完成后，以一次性测试 Ed25519 密钥调用原始 `build-model-pack.py`，再从原始 `model_packs.py` 读取验证函数，检查签名正确、错误外部信任根、载荷篡改、清单篡改及 Zip 路径穿越拒绝。仅合成非可执行载荷，密钥与测试包仅在临时目录，附件只保留脱敏结果。**不得将该专项或真实 BGE 第三方权重通过误记为正式 RC6 签名模型包已导入。** |
| ARM64 测试专用签名模型包校验（`db133a62`） | PASS（BUILD_ONLY） | [原生云端 #44752117478](https://github.com/linsky-dev/PartyOps/actions/runs/44752117478) 在原生 Windows ARM64 以固定 OpenSSL 3.5.7 + cryptography 50.0.0 执行原始 `build-model-pack.py` 与原始签名验证函数，测试钥匙签名接受，错误外部信任公钥、模型载荷 SHA-256 篡改、清单篡改、ZIP 路径穿越全部拒绝；仅合成不可执行载荷，正式签名包未验收。 |
| ARM64 合成签名包与原始 EmbeddingRuntime 连续验证（新增） | NOT_RUN | 计划在同一原生 ARM64 Runner 使用锁定 cryptography 50.0.0 与隔离 ONNX Runtime 候选，以一次性钥匙签发**可运行的合成 ONNX/tokenizer**模型包；先验证签名与全部载荷 SHA，再解包并调用原始 `EmbeddingRuntime.encode()`，同时验证篡改包在提取前被拒绝。此项仍非正式 RC6 签名包、完整应用上传/激活或真实模型权重验收，只有本轮独立作业实际成功才能标记 PASS。 |
| Windows ARM64 原始签名包→向量推理连续链路（`3c37901c`） | PASS（BUILD_ONLY） | [原生运行 #44758117279](https://github.com/linsky-dev/PartyOps/actions/runs/44758117279) 8/8 作业成功；原始构建器、签名/哈希验证及原始 `EmbeddingRuntime` 接力完成合成向量推理，并拒绝篡改包。未使用 RC6 正式模型包。 |
| Windows ARM64 原始 SQLAlchemy 模型包导入、激活与损坏拒绝（新增） | NOT_RUN | 新增独立的临时 SQLite 原生测试：调用**未修改的** `install_model_pack()`、`verify_installed_pack()`、`activate_model_pack()`、`active_model_pack()` 及原始向量推理，测试重复安装、损坏文件重激活拒绝。数据只在一次性内存数据库/临时目录内，**非正式 RC6 包、完整 API 服务或安装包验收**。 |
| RC2 历史官方 BGE 签名包原生来源、签名与载荷验收 | PASS（BUILD_ONLY） | [原生 Windows ARM64 #44862849895](https://github.com/linsky-dev/PartyOps/actions/runs/44862849895) 下载固定大小 94,965,776 字节、SHA-256 为 `57df3669…` 的原始 RC2 资产，使用未修改的原签名校验器和仓库外置正式公钥验证 Ed25519 签名及全部载荷。签名清单明确声明 Windows ARM64；尚未证明正式 HTTP 导入、原生安装或实际 WPS。 |
| 官方签名 BGE 原生 ARM64 导入、激活、真实推理（新增专项） | NOT_RUN | 本次新增与上一步不同的隔离测试：以原始发布包和正式外部公钥调用真实 `app.model_packs`，在临时 SQLite 和目录中完成导入、验签、启用、真正 BGE 中文向量推理、重复导入拒绝；仅上传脱敏 JSON。待新 CI 的独立作业日志确认后才能标记 PASS。 |
| 2026-11-21 Windows 全量回归 #44864858094 | FAIL（两项测试判定问题）/ 新版待复测 | [原始作业](https://github.com/linsky-dev/PartyOps/actions/runs/44864858094) 2,361 passed、16 skipped、2 failed。文件上传事件循环测试用绝对 80 ms 断言，Runner 负载下误伤（0.569 s），现改为检查真正 SHA-256 函数的执行线程与事件循环线程不同；排版本地票据原测试误把随机 token 字符串包含 `path` 判为路径泄露（正式响应 schema 没有路径字段），现只校验结构。**两项均不修改业务执行路径或 WPS 自动排版实现，须待新版 CI 重测。** |
| 十包构建与逐包真实安装 | NOT_RUN | 尚无本次冻结制品。 |
| 特殊目标原生安装、升级及真实 WPS | BLOCKED | 需要对应目标环境和合法可用的 WPS 输入。 |
| 双官网预览及下载回读 | BLOCKED | EdgeOne 控制台可见内容缺少可重建的原始工程。 |

验收证据以各次运行记录的执行系统、架构、命令、依赖和 SHA 为准。安装验收需追加包 SHA-256、日期和未覆盖范围。
GitHub PR 运行使用该次合并提交作为检出和附件名的一部分。例如，候选分支头为 `edafb5d` 时，工作流检出为 `ddcdd56f45aa`。
