# PartyOps 1.4.5-rc.6 本地验收记录

最后验证日期：2026-09-02（北京时间，UTC+08:00）

状态：开发候选，未发布；生产回滚点为 `v1.4.5-rc.4`。

## 2026-09-02 制品验收补充（优先于下文历史记录）

- 当前功能测试门禁为 `.release-gates/full-function-tests.json`：`2026-09-01T22:16:47+08:00`；安装包范围源码指纹 `00751c8ab15131d244b97853bca6f78169141a59ed0e4addab54f62c09cc7731`。2026-09-02 再次验证匹配。
- Windows 原排版源码快照为 898 文件，摘要 `15c21b886f6a958fb61a3b106266b446a2b959b0085510015eeb790efaa770d3`。x64/x86 宿主使用原 AddIn 规则；构建时绑定真实 WPS、3 页金样、6 类功能的 10 个执行用例与 25 项能力清单证据。

| 候选 | 构建/二进制校验 | 安装器文件验证 | 运行/数据升级 | 当前边界 |
| --- | --- | --- | --- | --- |
| Win7 amd64 | 最新构建，450 项 PE 通过 | 20,123 个压缩条目、20,119 个清单文件通过 | 构建目录与解包目录的启动、全新库、0023→0026 均通过 | 当前主机是 Win11；不等于真实 Win7 安装测试；未签名 |
| Win7 x86 | 最新构建，429 项 PE 通过 | 20,093 个压缩条目、20,089 个清单文件通过 | 构建目录与解包目录的启动、全新库、0023→0026 均通过 | 当前主机是 Win11；不等于真实 Win7 安装测试；未签名 |
| Win10/11 amd64 | 已按上述指纹重建 | 15,313 个压缩条目、15,309 个清单文件通过 | 构建目录与解包目录的依赖自检、启动、全新库、0023→0026 均通过 | Win11 管理员环境；真实安装/标准用户/Win10 未验；未签名 |

| 制品 | 字节数 | SHA-256 |
| --- | ---: | --- |
| `PartyOps_1.4.5-rc.6_windows7_amd64.exe` | 489774755 | `6134267928cfe7fdb305bc62b126086b8a744d1a75ddd86c96a115f3dcfb312e` |
| `PartyOps_1.4.5-rc.6_windows7_x86.exe` | 457447847 | `5b42abbbcc95c2582596f3c58b27410e47cedd59cb9693ff339c531b80a01f8b` |
| `PartyOps_1.4.5-rc.6_windows_amd64.exe` | 443644944 | `d4cb594a04bcfea2ab95de47b79e01968c90794bb996d400749d13a0d55e8b63` |

本地证据：`.release-gates/win7-x86-extracted-verification-20260902.json`、`win7-x86-installer-integrity-20260902.log`、`win7-x86-installer-fresh-20260902/`、`win7-x86-installer-upgrade-20260902/`，以及 2026-09-01 的 `win7-amd64-*` 目录。所有数据升级测试使用独立目录，旧管理员、附件和迁移前备份均验证保留。

补充安装包落地后的对抗检查：

- x64/x86 第二个实例均以退出码 3 和 `INSTANCE_ALREADY_RUNNING` 退出，无 `DATABASE_SCHEMA_FAILED` 误分类，第一实例仍健康，SQLite `quick_check=ok`。证据：`.release-gates/win7-amd64-concurrent-20260902/result.json`、`win7-x86-concurrent-20260902/result.json`。
- x86 解包宿主真实 WPS 六类功能 10 个场景通过；金标准样本段落语义、分页、三页像素比对通过。证据：`.release-gates/win7-x86-installer-formatter-features-20260902.json`、`win7-x86-installer-parity-20260902.json`。这些测试在当前 Win11 主机执行，不是 Win7 原生环境测试。
- amd64 解包宿主同样通过六类功能 10 场景、金标准段落语义及三页像素比对；对应证据为 `.release-gates/win7-amd64-installer-formatter-features-20260902.json`、`win7-amd64-installer-parity-20260902.json`。
- 本轮权限探针使用当前终端身份。不得把管理员身份执行的 `--startup-desktop-user-self-test` 解释为已在标准用户下验证；真实标准用户安装/配置/启动仍待完成。下文历史记录中的“普通桌面用户”须按此边界理解。
- Win10/11 amd64 新安装包解出后，同样通过上述重复启动场景、六功能 10 场景及三页金样比对；额外通过包级 OCR、NumPy/ONNX/tokenizers、TLS/Ed25519、llama 启动及原排版宿主依赖自检。日志 `.release-gates/windows-amd64-candidate-acceptance-20260902.log` 汇总全流程，原始证据位于同目录的 `windows-amd64-installer-*20260902*`。
- 金样没有替换：工作树的 `expected-source-formatted.docx` 与用户提供的 `E:\paiban\PartyOps.DocumentFormatter.Source\src\PartyOps.DocumentFormatter.Desktop\bin\x86\Release\输出\测试_已排版.docx` 均为 SHA-256 `bef6831245bc5a064bcf4135a51252f4dfd0a926f79228c7a2487a9c02639133`。
- 模型复验为两个集成用例通过，涵盖正式包验签/导入/激活及真实推理；范围边界、版本与日志见 `docs/model-validation-1.4.5-rc.6.md`，不代表各平台模型已全部验收。
- Win10/11 候选全部运行测试后，再次核对安装文件：15,309 项清单哈希仍一致、无新增文件。证据 `.release-gates/windows-amd64-after-runtime-verification-20260902.json`；任务结束时未遗留本轮 PartyOps、排版宿主、llama 或 soffice 测试进程，现用 `E:\PartyOps1\PartyOps\PartyOps.exe` 保持运行。

本轮新增失败门禁：`backend/.venv/Scripts/python.exe -m mypy app --no-incremental` 返回 333 项 / 46 文件（受检 96 文件），日志 `.release-gates/mypy-current-20260902.log`。这项优先于下文 317 项的历史数字；功能测试门禁的 `passed` 不覆盖 mypy，且不能解释为“全部静态检查通过”。初步核查包含关键字参数类型推断、互斥分支变量复用、跨平台 API 类型和第三方声明缺失，需逐项治理，未采用全局忽略。当前制品只是用于二进制验收的未签名候选，不是最终可发布安装包。

两个 Win7 候选仍有明确能力限制：x86 为 `legacy-core`，不含语义重排及 llama.cpp；amd64 为 `legacy-smart`，不含可用 llama.cpp。此事实必须向用户公开，不能据此宣称所有平台内置模型验收完成。Linux/macOS 原生 WPS、模型、安装/启动验收及正式签名仍是未关闭门禁；不更新官网公开下载和 GitHub Release。

本轮未修改排版规则。上一轮 x86 构建的文件占用源于 2026-08-31 手工转换探针未携带输入文件而遗留的 `soffice` 进程；核验可执行路径后仅终止这两个测试进程，重建成功，未关闭用户 WPS 或现用 PartyOps。该操作不是对产品排版源码的修复，也不能代替产品取消/异常回收测试。

以下各节为 2026-08-27 历史基线，数字及候选状态不代表本轮最新结果。

## 1. 本轮修复

- 修复 Windows 10/11、Windows 7 启动前依赖检查缺失的问题：个人入口在配置前核验
  主程序、向导、Launcher、Python、SQLite/FTS5、UCRT、VC、Tcl/Tk、前端资源及清单
  SHA-256；发现缺失或混装时返回 `RUNTIME_DEPENDENCY_MISSING`，不再等子进程退出后
  才显示笼统错误。
- 修复 `RUNTIME_DEPENDENCY_MISSING` 之后关闭并重新配置又出现
  `MODE_SWITCH_ROLLBACK_FAILED / 自启动：未找到运行程序：PartyOpsLauncher` 的错误分类。
  自启动恢复现在是辅助步骤：入口被安全软件隔离或策略拒绝时只记录
  `AUTOSTART_RESTORE_DEFERRED`，保留个人/主机/协同核心模式；真实的运行时缺失统一返回
  `RUNTIME_EXECUTABLE_MISSING` 并给出同版本修复安装建议。
- 安装器和桌面 Launcher 在用户配置目录及 `ProgramData\\PartyOps` 写入当前安装根标记。
  标记只接受绝对目录，并且候选 Launcher 必须与 PartyOps 发布清单中的 SHA-256 完全一致，
  防止旧快捷方式或被篡改的标记执行非 PartyOps 程序。
- Win7 专用包继续强制 CPython 3.8、目标位数和 KB2533623/等效 Loader API；Win10/11
  通用包拒绝在 Win7 或错误架构上运行。

## 2. 自动验证结果

| 门禁 | 结果 | 证据 |
| --- | --- | --- |
| 后端全量回归 | 通过 | `scripts/test.ps1`：1481 个通过、4 个环境型用例跳过、零失败 |
| 后端覆盖率 | 通过 | `backend/coverage-release.json`：行 96.41%，分支 92.20% |
| 前端全量回归 | 通过 | 19 个测试文件、218 个测试通过 |
| 前端覆盖率 | 通过 | 行 96.51%，分支 93.08% |
| 前端类型检查与生产构建 | 通过 | `pnpm run build`；Vite 1296 modules transformed |
| 官网测试、覆盖率与构建 | 通过 | 90 个测试；行 98.85%、分支 93.21%；Sites/SPA/更新清单防回退测试通过 |
| Ruff | 通过 | 后端 `ruff check app tests`；Windows 修复专项测试通过 |
| 依赖与安全扫描 | 通过 | pnpm/pip audit、Gitleaks、Bandit（中高风险 0）、`pip check` 均通过 |
| Windows 依赖探针专项 | 通过 | `test_175_rc3_runtime_permission_preflight.py`、`test_setup_wizard_rc3_branch_matrix.py` |
| Windows 冻结运行时 | 通过 | 全新库、真实 `0023→0026` 覆盖升级、SQLite 3.53.4/FTS5、桌面普通用户探针 |
| rc.6 模型组合（Windows AMD64） | 通过 | DeepSeek、Needle、BGE、Qwen3 正式包验签、导入、激活与真实离线推理 |
| 后端 mypy | 未通过，阻断发布 | mypy 2.3.1：317 项、44 个文件；含第三方 stubs、跨平台分支及既有 ORM 类型问题，未使用全局忽略掩盖 |
| Win7 wheelhouse 与冻结构建 | 部分通过 | x64/x86 安全回移证据、依赖闭包、自检、全新库和 `0023→0026` 通过；尚无真实 Win7 SP1 虚拟机启动证据 |

## 3. Windows 候选包状态

已在本机分别生成 Windows 10/11 AMD64、Windows 7 SP1 AMD64 和 x86 三个中间候选，并通过
全新库、真实 `0023→0026` 覆盖升级、普通桌面用户、冻结 EXE 与模型运行时门禁。此后又合入
党委会议程层级识别、公文页码复检和公文 AI 只读诊断，三个候选的源码提交已过期；必须从
最终源码重新冻结、重跑非 C 盘/标准账号/自启动/卸载保留数据门禁，当前文件不得上传。

Win7 x64/x86 已完成安全回移证据验证，但尚未在真实 Win7 SP1 环境验证。Linux、UOS、麒麟
和 macOS rc.6 也尚未在对应原生环境完成构建与启动验证，不能通过跨平台改名宣称支持。

## 4. 发布判定与回滚

rc.6 不能进入 Cloud Studio、GitHub Release 或 EdgeOne Production。必须先补齐 mypy 存量错误
治理、Win7 真机/虚拟机证据、Linux/macOS 原生构建及跨平台模型签名/推理门禁，再按固化顺序发布。
任一门禁失败均保留 rc.4 生产版本，不替换官网清单，也不上传部分平台制品。
