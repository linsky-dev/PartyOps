# Win10 x64 启动修复记录

最后更新：2026-10-17 14:06。正式完整包及覆盖、重启、原数据保留已通过；用户已同意 QA WPS 新许可和当前用户测试 CA 信任。WPS 正在同一 Guest 安装，主机配置待随后执行已独立审查的 approved-existing-root 单次续接；尚未发布。

## 当前恢复点（优先于下方历史状态）

- 14:06：本轮 WPS 图形安装器已正常退出可见窗口；官方外层与自解压内层各自有效签名/版本及摘要已核验。QA 脚本前置核验和残留退出延迟均发生在 Start-Process 前，回执保留。当前唯一实际 `/S` 安装命令在运行，不重复启动。当前用户已明确批准本机 QA WPS 许可与 PartyOps Local Root 信任，原待确认文字为历史状态。
- 排版验收补充只改 QA 控制器，不改冻结产品。WorkBuddy 当前5.8.2与归档桥版本不一致，发布适配仅本地准备，旧麒麟部署意图不得重放。
- 本地补丁工具未实际创建文件，已使用当前健康工作区 PowerShell/Python 保存 QA 文件并读回；不改变产品包，可随 QA 中间产物回退。

- 符合限制的独立主机 QA 目录 `C:\PartyOps-QA-Host-20261017\主机 数据` 已通过真实 UAC；主机进程曾到 ready（`root-host-observation-e6fd052b0f9d.json`），但随后 CA 信任确认超时，因此不能记主机配置通过。`first-host-root-directory-submit.result.json` 记录 HTTP 500 / host-first-run-failed；已完成产品的恢复 UAC，`host-rollback-final-state.json` 实测 user personal / system missing、pending=false / journal=false、主机服务停止、仅普通个人端口 18775/18777 存活，原个人新 PID 2448。`retention-after-host-rollback.json` 再次核对原账号、事项、日历、附件通过。
- 当前没有构建、安装或配置提交控制器继续运行。WPS 安装器仍等待许可；证书确认问题与 WPS 新许可问题保持待用户答复。证书已超时关闭，不要盲点旧截图；下轮依据新页面及真实状态执行。不得原样重放 root-directory case（目录已存在且旧 attempt/result 必须保留）；没有证书阶段独立 resume，不删除新 QA 库或旧证据。
- 下一步：取得上述确认后，核对原 QA 主机目录身份和残留，再通过正式产品入口完成一个有独立回执的主机配置事务；完成 WPS 安装/六功能及金样。通过后才按回收的 UPLOAD_WORKFLOW 发布并撤下过时包。GovPulse 已恢复，E 盘继续不访问。
- 13:26 首次主机表单真实 UAC 已由主控按既有授权确认，但 QA 误选用户文档子目录，违反 `setup_wizard.py` 的主机数据位置限制；提权子进程明确拒绝，父页面 HTTP 400 / `ADMIN_CHILD_FAILED`。`first-host-submit.result.json` 与 `host-qa-directory-rejected.png` 保留失败，不算主机通过。随后只读 `host-wizard-inspect-3bc980ec35d4.json` 证明原 personal PID 6116 未变，pending/journal 均 false、system mode missing。只纠正 QA 路径后续接原向导，不修改产品限制或重复安装。
- `win10-qa-fonts-stage.completed.json`：三份授权字体在原普通用户 HKCU/字体目录完成摘要登记及独立读回，作用域仅 QA；未进入安装包，不代表 WPS 排版通过。
- `host-form-invalid-address.result.json`：实际安装向导一次拒绝无效地址，返回 `RUNTIME_CONFIGURATION_INVALID`；原 personal 模式保留，未遗留 pending/journal。不是地址专属错误码通过，也不是主机配置通过。首次新主机须使用普通用户的 `--initial-role host`，已有数据的 `--reconfigure` 路径不可冒充新库初始化。
- WPS 官方签名安装器 12.1.0.28488 x64 已传入同一 Guest 并打开，当前等待用户确认新许可，未勾选、未安装。`wps-staged.json`、`wps-license-screen.png` 为证据；凭据仅在内存使用且不入日志。
- `publication-windows-20261017/prepared/preparation.json` 仅为本地准备，保持其他九包原始签名记录；主机/WPS 未通过前不得发布。官网/GitHub没有改动。
- `overlay-install/completed.json`：唯一一次覆盖安装退出 0，完整安装布局一致；`runtime-launch-candidate.result.json`：原普通用户交互会话启动候选，实际 HTTP PID 归属匹配。
- `retention-after-overlay.json`、`retention-after-reboot.json`：原合成账号、事项、日历 ID/内容摘要、附件摘要分别通过真实安装版 HTTP 核对。`normal-reboot-completed.json`：一次 Guest 正常关机再启动、相同 UUID/系统盘、新 PID/boot_id。不得重复安装或重启来制造新证据。
- 正常重启后仅重定向原本机 noVNC 查看服务到同一 Guest 的新 VNC 端口 55328；未另建 VM 或测试库。接下来完成真实向导主机配置/UAC、安装版排版；历史源码测试不能替代。
- 新完整包 `artifacts/PartyOps_1.4.5-rc.6_windows_amd64.exe`，449978846 字节，SHA-256 `3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27`。Inno 实际成功，正式 build receipt 为 `D:/PartyOps-VM-Lab/state/builds/3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27.json`，preflight 通过。
- GovPulse 于 12:54 完成恢复后实际读回原四 worker 六队列，状态 restored；不是只有恢复命令确认。构建资源临时暂停已解除。
- 停旧 QA 进程遇到两项控制器差异：WMI UUID 大写（动作前拒绝）；SSH/cmd 超长 EncodedCommand（原 PID 及创建时间未变）。仅修 QA 控制器大小写规范化与 SFTP 固定摘要脚本传输，6 项离线测试通过。原失败/attempt 保留；显式 `recover-stop-transport-once.py` 核原对象后成功受控终止原 PID，不计作 GUI 正常退出通过。
- 12:57 首次执行候选覆盖；唯一意图 `overlay-install/attempt.json`。进程运行期间不得再次安装；完成后依次 launch-candidate、after-overlay、normal-reboot、after-reboot。`runtime-stop-old-transport-recovery.result.json` 记录停止结果。
- 12:38:32 正式全套通过：2194 passed、5 skipped、3 subtests passed；后端行覆盖率 96.25%、分支 92.03%。`.release-gates/full-function-tests.json` package 指纹 `6f879341ef736ffd1ca41da9ff171976c5cb17985058fa1af206fa97eecdb702`。完整日志 `full-function-gate-20261017-122655.log`。
- 首次构建在冻结前退出：Python 中转启动 PS5 继承 PS7 模块环境，`Get-FileHash` 不可见。失败回执 `D:/PartyOps-VM-Lab/reports/build-ceaa60359988/build.json` 保留；QA 包装器改为绝对路径启动同一 PowerShell，实测 Python→同一 shell 可见 `Get-FileHash`/`Get-AuthenticodeSignature`。未改产品或重跑全套。正式续构建日志 `candidate-build-20261017-123952.log`。
- 首次退出的 finally 已恢复 GovPulse，随后 status 实际读回原四 worker 六队列。真正续构建前再次按授权暂停；续构建的 finally 仍负责恢复，结束后必须核对实际回读。
- runtime-transition 精度修复完成，Reviewer 独立5项离线测试通过、P1关闭；尚未调用 Guest 的 stop-old/launch-candidate。
- 用户手工清理的 `pytest-3-cleanup.json` 已记录 removed；只读确认原目录不存在，不再重复清理。工作区凭据扫描已通过。
- 12:26:55 启动 `run-windows-gate.ps1`，日志 `full-function-gate-20261017-122655.log`。排版 x64/x86 源码、金样、六功能、安全扫描、前端 221 测试与构建、官网覆盖率/真实 PostgreSQL 测试与构建已通过；后端全量仍在运行，不能提前判定整套通过。
- 同一主控进程在门禁成功后调用 `build-candidate.ps1`，并在 finally 恢复 GovPulse 原队列。构建正在占用资源期间 GovPulse 已重新暂停；结束后必须回读原四 worker 六队列，不能只看控制命令确认。
- 本轮仍使用同一 Win10 系统盘，无覆盖安装、验收重启或新建虚拟机。`runtime-transition.py` 的停止/启动控制器正在定点独立审查，发现 CIM 与 Get-Process 创建时间精度差，修复前不执行 stop-old；产品源码不受影响。
- 既有一次 `boot-before`、原合成业务基线及覆盖前备份保留。下一步按真实候选 SHA 和正式构建回执绑定一次覆盖，再核对重启和原数据；不得重复旧安装或用源码结果代替 Guest 验收。

## 基线与范围

- 健康盘工作区：`C:\PartyOps-VM-Lab\windows-startup-repair-20261016`。
- 分支：`fix/windows-host-startup-20261016`；起点：`b58cd0505ebd8bc187d40857e92d19c88b2a2b56`（公开 main）。该起点不等同于 E 盘历史未提交修改或所有已发布冻结程序。
- 用户确认故障设备为 Windows 10 x64 / AMD Ryzen 5 4500U。客户安装包的准确哈希尚未知。
- 客户原始输入在桌面：`partyops-host-status.json`、`partyops-host-service.log`、`partyops.log`。不纳入公开源码；不上传完整日志。
- 本次只使用现有 Win10 x64 系统盘，不重装 Windows、不还原旧快照、不复制整个 VM。目标是一次候选覆盖安装，再验证启动、配置和数据保留。
- E 盘已有真实读错误，本次在 C 盘编辑构建，不清理或再次访问 E 盘。

## 已证实

1. 客户服务日志显示 Agent 安全端口 18766 绑定失败，主业务端口尚未启动。仅凭日志不能断言是端口占用、旧 IP、驱动或 CPU 问题。
2. 两处提权调用把参数追加到 PowerShell `-Command` 后，却依赖 `$args` 接收；真实 PowerShell 替身复现收到空参数。属于代码缺陷。
3. `network-settings.json` 可覆盖环境配置，修复必须考虑其与用户显式配置操作的一致性；不得静默扩大监听范围。
4. 原分支后端归档中的相关 6 项测试通过，结果见 `baseline-tests.xml` / `baseline-tests.log`；这些 mock 测试未覆盖真实 PowerShell 传参。

## 既有 Guest 与资源

- UUID：`f47cf265-72a2-4d51-9cf1-b1f5ae960cb2`。
- 磁盘：`D:\PartyOps-VM-Lab\vms\win10-x64\system.qcow2`，无 backing file；现有快照仅核对存在，不执行还原。
- 历史记录：Windows 10 22H2 / 19045 / x86_64；须启动后重新读取，不能算本次通过。
- 旧 PID 已退出。镜像迁移并校验后，使用同一系统盘成功启动 Guest（当前进程 PID 25216、QMP 8400）；没有重装、还原或复制系统盘。
- 实际读回 Windows 10 专业版 19045 / 64 位；已安装 RC.6，路径 `C:\PartyOps QA\中文 程序`，个人进程监听 127.0.0.1:18775/18777，原主机服务停止。详见 `guest-before-identity.json`。这是旧包状态，不是新包验收。
- D 盘原余约 3.85 GiB。三份当前未挂载的 ISO 迁至 C 盘并逐份校验哈希（`media-relocation.json`）；两个无进程占用、无现代码引用的旧测试安装器已清理，共 585412735 字节（`obsolete-installer-cleanup.json`）。保留所有系统盘、业务数据、快照与证据。启动时 D 盘约 22 GiB 可用，未降低资源门槛。
- Inno Setup 6 实际位于当前用户 LocalAppData\Programs。微软签名的官方 Build Tools 已安装到本工作区 `.build-kit/vs2022`，退出码 0；未对 E 盘旧实例执行 repair/modify。安装记录为 `buildtools-*.json`。
- 前端类型检查与构建通过（`frontend-build.log`）。原排版源码 x64 构建及 138 项功能断言通过（`formatter-build-x64.log`），不代表新安装包排版通过。
- Win10 内无业务数据副作用的套接字测试确认：不可用地址返回 10049，占用端口返回 10048，释放后可重新绑定（`guest-bind-baseline.result.json`）。

## 当前修复审查

第二批修复定向测试 196 项通过，但不作为最终发布结论。独立审查确认普通父进程不再读写受保护配置；继续发现事务过早 finalize、进程中断遗漏用户模式恢复、GUI 提权子进程缺少业务错误返回三项问题。正在延长事务至主机健康检查结束、补本用户恢复 journal/同 ID 结果记录与固定业务退出码。审查关闭前不覆盖安装。

## 本轮新增实际证据

- `retention-baseline.json`：在既有 Win10 安装版中复用原合成账号、事项及附件完成 API 核对，补一条唯一 QA 日历记录；通过产品备份接口生成、校验并下载覆盖前备份。备份仅在被 Git 忽略的 `.qa/windows-retention`，未触及真实客户业务库。该结果是覆盖前基线，不能算覆盖/重启通过。
- SSH 失联后发送一次正常 ACPI 关机，确认 QEMU 退出后重用原系统盘启动；未强制关机、重装或还原快照。当前 PID 27436、QMP 9528、VNC display 22100；SSH 已恢复且上述 API 验证成功。
- 重启前 D 盘余量不足，额外将未挂载 Win10 x64 ISO 搬至 C 盘并逐字节 SHA 校验后删除旧位置，记录 `win10-iso-relocation.json`；不删系统盘。后续安装介质引用须使用新路径。
- `windows-formatter-source-recovery.json`：公开 main 的 vendor 为 Mac 字体修订；从已有 Git 历史恢复 Windows 独立构建快照，898 文件聚合 SHA 精确符合原 Windows 门禁，原 Mac 目录未修改。构建需设置 `PARTYOPS_DOCUMENT_FORMATTER_SOURCE=.qa/windows-formatter-upstream`（绝对路径）。
- 原宿主复制依赖路径遗漏 x64/x86 子目录，已修 `scripts/build-document-formatter-host.ps1`，保留 AnyCPU 原路径。x64/x86 两套脱离源码自检、金样及六功能证据均通过，保存在 `.release-gates/formatter-*.json`。
- 原现代 OCR ZIP 缺失，已增加固定公开安装器与逐文件清单的恢复路径；未伪造原 ZIP 哈希，原 ZIP 存在但损坏仍拒绝。恢复验证、PS5/PS7 和真实 junction 拒绝测试通过；详见 `packaging/windows/OCR-PUBLIC-RECOVERY.md`。
- 前端/私有官网依赖审计的高危项已作最小修补；Python 发布依赖审计通过。全套 `scripts/test.ps1` 仍在验证，未产生新的有效全门禁通过结论。
- noVNC 1.7.0 / websockify 0.13.0 准备完成，但启动本机查看服务被自动审批返回 `blocked by policy`，未运行该服务、未换工具绕过；现有 SSH 可继续，真实 GUI/UAC 验收仍待具备可操作界面。当前截图只证明桌面可显示。

## 待完成

- 修复独立审查、定向回归、可执行完整构建门禁。
- Guest 当前安装身份与隔离 QA 数据确认；候选包覆盖安装、双 TLS 端口、配置和重启数据保留验收。
- 验证通过后依既有 UPLOAD_WORKFLOW 发布，最后清理确定已被替代的包与中间产物。
- 不套用此前麒麟候选包发布例外，不将源代码单测算作 Win10 安装包验收。

## 工具说明

采用会话可用的本地 shell / apply_patch；无需更改 Multi-Agent 配置。架构判断、复杂实现及独立审查各限定范围。源码变更可按本次 diff 回退，不回退用户原目录。

## 2026-10-16 晚间审查补充

- 第二轮事务修复定向回归为 246 passed、3 subtests；不等同于最终安装版通过。
- 19 点左右独立复核又确认四项必须在冻结前解决的缺口：单 IP 监听却固定回环健康探测；普通用户不能读数据树内 CA；pending/outcome 原子替换后缺少普通用户读权限；提权 prepare 前中断遗留 journal 缺少受控恢复出口。已统一交原写入者修复，避免重复生成和安装候选。
- 使用健康 C 盘 Git 后，真实扫描 267 个提交及工作目录均未发现凭据。此前旧 E 盘 Git 扫描 0 个提交不计通过。
- `run-windows-gate.ps1` 已于 18:59 启动完整门禁；本轮后续源码修订必须重新绑定最终门禁，不能复用修订前结果。
- `build-candidate.ps1` 仅封装健康盘工具和原正式构建入口，强制先核验全套门禁，不绕过门禁；尚未执行候选构建或覆盖安装。
- 前端最终依赖状态：221 项 Vitest、4 项静态入口测试、类型检查及生产构建通过（`frontend-final-regression.log`）。官网 165 项 Vitest 通过，但原覆盖率门禁失败，缺失集中在未纳入 Vitest 的评论后台测试；补齐中，不降低阈值（`website-final-regression.log`）。
- 18:59 的全套门禁在 Bandit 停止：单 IP 保留逻辑中的通配地址字符串比较被 B104 命中；精确说明比较用途后修复，不关闭全局规则。该次全套未通过。
- `install-candidate-once.py` 已准备并仅完成语法与帮助入口检查，复用既有安装执行器，独占 attempt 记录防止重复安装；未执行安装。`verify-retained-business.py` 覆盖后阶段强制核对候选清单摘要，并拒绝旧清单冒充新包。
- `guest-office-baseline.result.json`：本次 Guest 的机器/已加载桌面用户卸载登记、常见 WPS 路径及 WPS/Word COM 均未找到；系统字体登记存在仿宋、楷体、黑体、宋体、Times New Roman。这是只读登记核查，不能计作排版通过。既有 `WPS_Setup_x64_28488.exe` 与 2026-09-07 记录摘要一致，金山签名 Valid（`wps-cached-installer.json`），未安装到 Guest。
- 全量源测试需要的两份 lxml 6.1.1 Linux cp311 wheel 已从 PyPI 按原准备脚本的架构下载，摘要为 `lxml-wheel-inputs.json`，原失败单项现已通过。只恢复本项测试输入，不宣称 Linux 离线依赖闭包齐全。
- 产品与测试于 19:19:44 冻结；19:21:04 开始的统一回归 XML 为 304 tests / 0 failures / 0 errors / 0 skipped。Reviewer 定点复核关闭前述四项阻断，允许进入安装版验收；尚不等于可发布。`real-public-acl.json` 是宿主受限 Windows 线程令牌及真实单 IP TLS 测试，不能冒充 Win10 Guest 或 UAC 验收。
- 保留两项非阻断边界：无账号数据库叠加既有单 IP 配置的管理员 bootstrap 回环访问尚未覆盖；普通账号读取受保护日志受限时细分诊断可能降为通用健康超时。已有账号重配置与默认通配监听不因此判为失败。
- 当前全套日志 `full-function-gate-20261016-192137.log`。官网 Vitest 已恢复到 206 测试、原阈值通过；Node 静态/集成脚本仍在恢复本地测试依赖和环境。正式门禁尚未通过，不生成假通过记录。
- 该次全套在既存 Mac 测试导入排序处被 Ruff 阻断；仅调整 `test_mac_format_product_entry.py` 导入顺序。随后全目录 Ruff 通过。全目录 mypy 暴露 `package_selftest.py` 字典读取无法收窄类型，改为本地变量再检查，行为不变；101 个源码文件类型检查通过（`backend-full-mypy.log`）。后端全量 coverage/pytest 独立执行中，日志 `backend-full-tests.log`，未声称已通过。
- 创建新的隔离 PostgreSQL QA 实例被自动审批返回 `blocked by policy`，命令未执行；未换工具/委派绕过，也未连接或启停已有 PostgreSQL 业务服务。用户手工入口为 `start-local-qa-services.ps1`（仅语法检查通过，助手不执行），同时可启动前述被拒绝的本机 VM 查看服务。已请求用户运行；正式门禁中的真实 PG 测试仍保留失败状态。

## 全量回归收尾（2026-10-16）

- 独立后端全量结果：2011 passed、63 failed、5 skipped、3 subtests passed；原失败日志完整保留为 `backend-full-tests.log`。该次独立运行未设置 Windows 排版源码环境变量，与正式 `run-windows-gate.ps1` 不同，正在按正确环境复验，不能把该次结果写成通过。
- 失败分组包括旧排版摘要/平台 profile 的测试基线、缺失 OCR 合成 fixture、依赖未入库历史文件的凭据扫描测试、PKI 测试替身缺字段、Mac 打包过时断言及 Mac 工作目录测试。分组修复由互不重叠的写入者负责，产品门禁和失败断言均保留。
- 原源码 profile 测试现明确核对 Mac 引号修订，并确认默认上游 profile 会拒绝它；Windows 构建仍由正式脚本校验独立 7ae0… 上游快照。两组运行时测试中的旧摘要更新为当前锁定摘要；34 项定点测试通过，见 `formatter-test-contracts.log`。
- 此次失败运行的覆盖率只用于诊断：行 95.75%、分支 90.63%，未达到原 95%/92% 门槛。补真实包自检的成功/异常契约测试，不降低阈值、不排除代码。
- 继续复用同一个 Win10 x64 Guest；没有卸载旧 PartyOps、重复安装系统或执行候选覆盖。55432/4337 尚未监听，用户手工启动步骤仍待完成。
- 进一步复现确认 Mac 工作目录的 12 项失败是 Windows `MAX_PATH`：确认回执路径 278 字符，`Path.rename` 返回 winerror=3；不是 Mac 产品错误，也不是排版源码环境变量导致。测试仅用短临时根目录，仍保留中文/空格用户与全部清理断言，20 项通过。Mac 包装 15 项静态契约也按现存版本与架构最低系统要求对齐；没有改 Mac 产品或支持范围。
- 缺历史输入的两组测试经独立 Reviewer 核对，改为只在 `tmp_path` 和独立导入模块中使用合成输入；生产 OCR 固定摘要、凭据扫描历史批准范围与验证器均未更改，未补造历史 PNG/manifest。新增未替换生产常量时拒绝合成输入及篡改负例，16 项契约单测通过；不计 Loong64 实测或历史批准真实性。
- 本轮测试基线修订后全目录 Ruff、产品 Python 编译与 diff 格式检查通过。完整包自检契约测试补充中，最终后端全套与覆盖率尚待重跑。
- 新增 `test_package_selftest_contracts.py` 的 99 项定点测试通过，真实执行来源/资源摘要/候选限制/回执校验；仅外部运行库调用使用受控替身。独立 Reviewer 对这份测试及两份合成输入测试的最终 diff 静态复核无阻断；不独立背书运行数量，也不代替 Guest。测试于约 19:45 冻结，`run-backend-regression.ps1` 开始完整复验；它不写正式全功能门禁。
- 私有官网生产构建通过，当前原有 10 个主包的发布元数据校验通过，记录 `website-production-build.log`。没有改公开下载项或部署官网；新 Windows 包仍未生成。
- 完整后端复验 `backend-final-20261016-194447.log/xml`：2176 passed、5 skipped、3 subtests passed，无失败；当时分支 91.86%。随后新增 4 项包自检与 14 项主机诊断/公开控制文件边界测试，两份文件定向重跑 117 passed。使用 coverage append 合并，行 96.25%、分支 92.03% 达到原后端阈值；这不是第二次完整全功能门禁。绑定源码指纹与两次 JUnit 摘要的记录为 `backend-final-evidence.json`。
- 新的源码/测试 Ruff 通过。267 个 Git 提交秘密扫描仍无发现；工作区扫描发现 98 项，全在刚结束的 `.test-temp/pytest-of-Administrator/pytest-3` 合成临时密钥/配置。脱敏记录 `final-secrets-redacted.json`，没有打印密钥内容。清理该精确临时目录被自动审批再次返回 `blocked by policy`，未执行、未绕过；后续定向测试使用全新显式 basetemp，避免借默认轮换删除被拒绝的目录。
- 用户手工清理入口 `cleanup-completed-pytest-manual.ps1` 已准备，助手不执行。它只删除上述已结束测试的临时目录，保留源码、日志、JUnit、覆盖率、安装包、VM、业务数据，并记录清理回执。隔离测试服务仍由此前 `start-local-qa-services.ps1` 手工启动；截至 19:53 两个端口仍未监听。
- 当前阻塞仍是完整门禁中的真实 PostgreSQL 测试、工作区扫描恢复及可操作 VM 查看服务。没有生成正式门禁通过记录，没有新安装包/覆盖安装/发布；原 Win10 Guest 和覆盖前备份保留。解除后先核对服务与清理回执，再跑正式全套、构建、一次覆盖和重启保留验收，最后按原流程发布及清理被替代旧包。
- 用户再次授权电脑操作后，按 computer-use 技能初始化 `@oai/sky` 并读取真实窗口清单。工具可用，但当前没有 Win10/QEMU 或虚拟机查看窗口；没有操作无关窗口、没有通过 UI 执行此前被拒绝的命令，也未新增 VM 或重复安装。仅授权文字不证明两个本机服务已启动。

## 2026-10-17 服务恢复与空间核对

- 用户手工运行服务脚本后，已实际确认 55432 隔离 PostgreSQL 与 4337 noVNC 监听。仅调整该独立测试库连接上限至 64，以承载原 20 并发用例；官网 `test:sites` 的 42 项 Node 测试与 21 项 Vitest 均通过，见 `website-sites-postgres-final-20261017.log`。人工启动脚本同步该容量设置，不再次执行初始化。
- noVNC 已连接原 Win10 Guest，看到桌面并完成右键菜单及 Escape 无副作用交互；不等于新包或 UAC 已验收，未安装候选。
- 用户授权卸载驻留模型后曾释放约 14.39 GiB 提交额度，但 GovPulse 再次加载同一模型，目前只剩约 1 GiB；等待是否暂停其调用任务，未结束其他任务进程。
- C/D 盘只读盘点未发现可直接认定可删除的 VM：D 盘 Win10 正运行；C 盘麒麟原盘与独立 render-probe 诊断盘用途不同，仍需核对最后验收记录。E 盘已确认介质错误，本轮不递归扫描或删除。
- 用户清理脚本回执 `pytest-3-cleanup.json` 停在 pending，目录仍存在；等待具体错误，未换工具执行此前被拒绝的删除。保留原数据、所有测试结果与源码。
- 空间定点复核：麒麟 `system.qcow2` 为后续验收盘克隆前的首次安装旧基线（15,454,830,592 字节）；克隆时 compare 相同，后续业务及排版全部在独立 `render-probe.qcow2` 完成，两者无 backing 关系。用户新授权清理重复 VM，故旧基线可列退休候选，保留最终验收盘、原业务备份和官方 ISO。两份 OCR 临时目录各 62 个文件、114,801,597 字节，逐文件 SHA 与最终 vendor 保留副本匹配。候选合计约 14.6 GiB，见 `duplicate-inventory-20261017.json`，不计作已释放。
- 本轮精确删除两份 OCR 临时目录再次被自动审批拒绝 `blocked by policy`，未执行、未绕过。已准备 `cleanup-verified-duplicates-manual.ps1`，默认仅检查，`-Apply` 才执行；先局部调整麒麟入口使用现有验收盘并拒绝旧入口重建，留小脚本备份后才移除旧基线。PS5/PS7 静态证据已生成，独立 Reviewer 复核中。主控没有执行整个清理脚本（包括默认模式）。
- `cleanup-completed-pytest-manual.ps1` 仅补 UTF-8 stdin 与失败回执，未运行；只读检查发现待清目录内有 Git 合成对象的 ReadOnly 属性，但未获用户原始错误前不将其确定为根因，不自动修改 ACL 或重试删除。
- C 盘当前约 10.36 GiB、D 盘 25.86 GiB 空闲；宿主 pagefile 当前约 21 GiB，内存提交余量最近约 5.8 GiB，仍低于完整构建要求。没有新安装包、覆盖、发布；尚待资源/临时扫描目录阻塞解除后继续正式门禁与原 Win10 Guest 验收。
- 独立 Reviewer 已完成清理入口复核，审查 SHA `064918bfb2878948d64ef5799cee74b5da3886667bde270ca9b875837987df39`，无已确认阻断。实测 PS5/7 内存补丁生成、OCR 62 文件/61 不同哈希保留句柄、默认保留盘绑定及共享函数不变；未执行实际入口落盘、删除或启动。执行期间须保持这两个 OCR 临时目录及麒麟 VM 目录无其他写入。删除后失去干净 OS 旧基线，不失去保留盘或独立业务备份；回退入口备份不能恢复旧 qcow2。

## 2026-10-17 用户授权临时暂停 GovPulse

- 用户明确允许暂时暂停 GovPulse 模型调用任务，PartyOps 构建完成后恢复。先使用其 UI 暂停“宣汉阶段监测”，界面成功确认后又变回运行，故不作为有效暂停证据；全国目标原本已暂停，不修改其监测配置。
- 不读取 E 盘代码或配置，不终止工作进程。通过本机 Redis 的运行元数据确认 Celery broker 位于 loopback 6379/db2；使用隔离安装于 `.qa/govpulse-control-client` 的 Celery 5.6.3 官方客户端控制四个已确认 GovPulse worker，六个队列均 `cancel_consumer` 并收到确认，随后 `active_queues` 逐一读回空列表。没有清空任务、revoke、杀进程或停止 Redis/Ollama 服务。原队列及 exchange/routing 配置保留在 `govpulse-consumers-before.json`。
- 可恢复入口为 `govpulse-consumer-pause.py restore`（使用本工作区 backend/.venv/Scripts/python.exe）；只向原四 worker 恢复原六队列，不启动原本停止的监测目标。**当前仍处于 paused；后续 PartyOps 构建完成必须执行恢复并核对回执。** 真实操作证据为 `govpulse-consumer-pause-receipt.json`。原 UI 暂停未保持，恢复时仍需确认宣汉原运行态、全国原暂停态。
- Ollama ps 已无驻留模型；实际空闲提交额度约 13.09 GiB。正式 package 门禁仍返回 FULL_FUNCTION_GATE_STALE，尚未启动冻结。新扫描 `source-gate-after-memory-pause.log`：267 Git 提交无命中，工作区 84 项均位于原 pytest-3 合成测试目录；脱敏逐项记录 `source-gate-after-memory-pause-redacted.json`。不降低扫描规则、不删除失败回执、不重复运行已知会在此处失败的全套测试。
- 官方控制依据（2026-10-17 查询）：https://docs.celeryq.dev/en/stable/reference/celery.app.control.html ，`cancel_consumer`/`add_consumer` 定点控制，恢复带原 exchange、routing_key 和队列参数。此为外部运行态控制，不是 PartyOps 产品修改。
- 手工 pytest 清理入口新增精确 Windows 只读普通单链接文件处理：仅 PermissionError/EACCES 或 winerror5，限定原目录、无 reparse、os.unlink/remove；只解除只读属性并重试一次，不改 ACL，失败继续写回执。实现者 13 个纯模拟用例及 Reviewer 独立 9 类路径验证通过；审查 SHA `73364130098d7a1904f6cf6d6ef5336806c55cd333a0899943e8c44bda9ab73e`。实际删除未执行，84 项命中仍保留。用户需要手工执行修正后的 `cleanup-completed-pytest-manual.ps1`；若做空间清理，另有已审查的 `cleanup-verified-duplicates-manual.ps1 -Apply`，均不能由助手绕过此前自动审批拒绝。
- 11:25 左右再次回读四 worker 消费队列为空，Ollama ps 无模型；GovPulse 的临时暂停继续保持，恢复命令/原配置/回执都已保留。下一步先读取用户手工清理回执，再正式全门禁、构建、原 Win10 Guest 一次覆盖/重启/数据保留；不得把本轮控制资源与静态验证写成安装包通过。

## 2026-10-17 清理回读与验收脚本补强

- 用户运行的重复制品清理已完成，回执 `duplicate-cleanup-manual-20261017-114252-03a6528820db4ac3931192f4e2a9a0eb/receipt.json`。移除旧麒麟初装盘和两份已核对的 OCR 临时副本，共 15,684,433,786 字节；实际再次确认旧盘不存在，最终 `render-probe.qcow2` 与 OCR vendor 副本仍在。不重复执行清理。
- 覆盖前备份再次核对 SHA-256 为 `7a7ff62357138d5b79083fc3afbb92dd7aa5f1967ba7b1a9f55abb8a55796bba`；没有覆盖安装 attempt。原 pytest 清理回执仍为旧 pending，目录仍在，已向用户指出两份清理脚本的区别。此前被自动审批拒绝的删除不改工具执行，不放宽扫描规则。
- 11:49 实时进程复核发现原 QEMU 与隔离 PostgreSQL 已退出，不能沿用旧 PID。恢复的是已存在的隔离 PostgreSQL 数据目录及原 Win10 磁盘，没有初始化数据库、重装系统、恢复快照或新建 VM。PostgreSQL 当前 PID 21572；Win10 当前 PID 30508、UUID 不变、QMP 8606、VNC display 4174。端口和 PID 仅是当时观测，后续必须重新查询。
- 独立 Reviewer 发现 QA 控制器三项证据缺口：重启后阶段没有证明 boot ID 变化；包 SHA 与实际运行文件没有闭合绑定；安装前没有核对 QMP 实际系统盘。已交单一 Complex Worker 修改本次 QA 控制器及构建包装器，保留原基线、备份和产品源码；待定向测试与独立复核，不计作 Guest 已通过。
- GovPulse 四 worker 的消费者再次读回为空，仍为本次授权的临时暂停。完成构建后恢复原六队列；未改变全国/宣汉监测目标配置。
- 后续补强已完成：`install-candidate-once.py`、`verify-retained-business.py`、`build-candidate.ps1`，新增 `retention_contract.py`、`record-candidate-build.py`、`test_retention_contract.py`。实现者 8 组正负契约通过；Reviewer 独立运行 6 组只读测试通过，另 2 组只审源码，三个原 P1 均关闭。此结论只针对控制器，不代替实际候选安装。
- 主控已实际执行一次只读 `boot-before`，通过 QMP 固定 UUID、登记盘、实际系统盘检查并记录原启动时间，见 `retention-boot-before.json`。原 `retention-baseline.json` 和已校验备份保持不变；新启动桌面右键菜单与 Escape 交互正常。没有覆盖安装或为验收执行重启。
- 11:57 因构建仍等待手工清理而先恢复 GovPulse，避免无构建期间一直暂停。六条恢复命令均收到确认；第一次聚合状态回读不完整，未重发控制命令。随后实际读回全部原队列，并逐项核对 queue/exchange 参数，4 worker、6 queue、零差异，见 `govpulse-restoration-verified.json`。`govpulse-consumer-pause.py status` 已修复为按实际读回推导状态，避免沿用上次 paused。**当前 GovPulse 为 restored，后续真正开始构建前可按已有授权再暂停，构建后仍须恢复。**
- 当前唯一已知正式构建前置阻塞是原 pytest-3 目录：回执仍停在旧 pending，已请求用户运行已修正的 `cleanup-completed-pytest-manual.ps1`；助手没有执行此前被审批拒绝的删除。未通过的全门禁保持原状态，未生成/发布新 Windows 包。待清理后顺序为：核清理回执→暂停原 GovPulse 消费队列并卸载驻留模型→完整 `run-windows-gate.ps1`→`build-candidate.ps1`→恢复 GovPulse→候选 preflight→一次覆盖→after-overlay→显式 normal-reboot→after-reboot→真实主机/GUI专项→发布流程。已有 boot-before 若现场启动状态变化，先核查而不直接覆盖该证据。

## 2026-10-17 实际包验收续接（14:34）

- 本轮固定候选SHA256：3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27；正式全功能源码门禁及构建已通过（12:38门禁），不再重复构建或安装。一次覆盖、一次正常重启及原账号/事项/日历/附件保留已通过，见overlay-install/completed.json、retention-after-overlay.json、retention-after-reboot.json。
- GovPulse已恢复原4 worker/6队列，先前paused记录是历史状态。
- 真实主机配置已成功，first-host-approved-existing-root-submit.result.json为HTTP200/管理员初始化页/Host ready/用户与系统host。真实UAC两次及现有测试CA信任由主控通过CUA在已获准的QA范围完成；回执中的uac_acceptance=not-verified为该自动控制器自身范围，不能抹去历史失败回执。
- host-qa-live-tls-verified.json：18765严格CA验证TLS1.3及HTTP200；18766严格CA验证TLS1.3，未提供设备身份的后续HTTP连接关闭，不宣称设备业务鉴权通过。
- 官方WPS外层/S已安装在QA管理员账户，不能作为普通用户可用证据。已确认官方内层真实manifest为asInvoker，现通过ordinary用户LUA任务安装同一签名原文件，尚未判排版通过。所有新许可和QA证书信任已有用户专项授权。
- 恢复personal的QA控制器先后暴露旧实例复用、合法Host-only状态与Host/Guest时钟混用问题，均属于QA脚本；未重复PartyOps安装。原prepare已关闭绑定旧向导，recover-prepare已启动Wizard340/49733，实际任务Running；Guest时间早于Host导致误判超时，正补只读观察恢复。失败attempt/result全部保留，不重放已启动步骤。
- 发布驱动独立审查发现通用passed回执可冒充专项、外部acceptance覆盖派生事实两项P1，修正前不签名或上传。WorkBuddy5.8.2暂无运行中的官方connector，已请求用户通过正常UI打开健康C盘工作区；不用旧E盘绑定或伪造认证context。
- 所有本轮操作仅健康C/D盘，未访问故障E盘，未修改官网/GitHub下载。

## 2026-10-18 续验现场

- 上轮个人模式已真实恢复，独立只读结案restore-personal-observed-completion.json及retention-after-host-restore.json通过；HTTP提交轮询曾误判双向导进程，原失败保留。
- 本轮原VM处于关闭状态，恢复同一D盘UUID/磁盘，未安装、未还原快照。因D盘低于22GiB启动预留，把206615928字节历史QEMU安装文件迁到本工作区.qa/relocated-tools-20261018并核SHA，见tool-installer-relocation-20261018.json。
- 现有WPS普通用户版本12.1.0.28488和五字体读取通过。尚未完成安装版排版金样验收；原2812c92ce569、续验0384b2bd39d5失败回执保留。QA已增加受控异常码、脱敏sidecar及原生JSON显式UTF8读取，产品与冻结包未修改。
- 临时目录误判已纠正：HKU目标用户Environment原始TEMP/TMP均为ExpandString %USERPROFILE%\AppData\Local\Temp；管理员上下文GetValue展开为管理员目录，不能当作目标进程环境。不修改用户环境。
- 第三次87b0e65401ae在HTTP阶段失败，正在增加状态码/路由模板诊断；未把失败改写通过。
- 本轮通过computer-use插件@oai/sky正常启动WorkBuddy5.8.2，沿用已授权的原部署任务，仅发送C健康工作区的只读连接器预检；没有部署或上传。此前“native不可用”仅指CUA入口，当前专用电脑插件可用。

## 2026-10-18 安装版排版实测（12:31）

- `win10-formatter-aac5c77979a7` 已真实完成九个功能用例、两份规范排版 DOCX 语义检查，以及参考/两份结果的三页 PDF 逐页像素一致检查。对应安装包仍为 `3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27`，没有重新安装或修改冻结载荷。
- 两份 `native-output-supplement-*-3fc69ef18be2.json` 只读补齐该次规范排版与 PDF 作业的原生输出 SHA/大小/扩展名与 HTTP 下载绑定。补证控制器最后因字体作业临时目录已清理而失败，保留其失败总回执；不能把整个补证过程写成通过。
- 最后字体断言 `PDF_ACTUAL_FONT_GLYPHS_NOT_PROVEN` 保留。四种字体存在真实 PDF 文本字形，第五行小标宋视觉存在但为位图。实际已授权 QA TTF 摘要 `f6154b97ee71bad19efa7f78edc152cf515c1f943220fc6e98360005d53d1c62`，名称正确，OS/2.fsType=0x0002。独立字体渲染与 PDF SMask 字形比对高度匹配；正在加入严格正负测试和最小续验，不删除限制、不修改字体、不仅凭有图片判通过。
- 官方字段依据：https://learn.microsoft.com/en-us/typography/opentype/otspec190/os2 ，访问日期 2026-10-18。受限嵌入标记与 PDF 位图结果为已观察事实；不外推为全部 WPS 版本的处理契约。
- 原 `summary.json` 保持 failed；续验只补字体作业及最终运行环境，按摘要复用已有效的九功能与金样，未通过前不发布。普通用户 WPS GUI 保存/重开检查同步进行，仅使用合成文档。

## 2026-10-18 字体续验与通信契约核对（12:43）

- `wps-gui-save-reopen-20261018.json`：真实 WPS 界面打开金样结果、观察第 1/3 与 3/3 页、保存关闭、从最近列表重开成功；从 Guest 取回保存副本，语义与原结果一致。此前文件选择器路径输入失败截图保留，不计为产品文档处理失败，也不冒充重开成功截图。
- `win10-formatter-d85d956b91f5` 在创建会话阶段收到 HTTP 201 后正文截断（0/136 字节），未提交任何字体作业；不重放该票据/请求。原失败 summary 保持。
- Architect 定点核对发现：正式前端 `OfficialFormatView.vue:238` 的 session POST 无正文，QA `shared.session` 却发送 `{}`，而 helper 相应分支不消费正文。正在将 QA 对齐正式前端，并记录无敏感内容的收发字节数。未读请求体与关闭竞态导致 RST 是强候选机制，尚无本机抓包确认，不写成已证实根因；没有改冻结产品或追加 HTTP 重试。
- Reviewer 独立通过字体/resume/共享入口/publication 共 16 项定点测试。共享 x86 原入口的长命令也已改为短暂存执行，未执行 x86 Guest 或扩大其历史验收范围。

## 2026-10-18 排版验收完成与发布准备

- `win10-formatter-595d684e3890/summary.json` 于 04:43:08 UTC 完成：六项功能、九个功能用例、五种字体实际渲染、三页金样及原生宿主绑定通过；严格按摘要复用已通过的旧作业，仅续验字体和最终运行状态。正式前端无正文的会话契约对齐后，实测 POST 201 的 136 字节正文完整返回，DELETE 204 无正文。没有重新安装、改动冻结产品或重放失败票据。
- `wps-gui-save-reopen-20261018.json` 独立证明普通用户 WPS GUI 保存、关闭、重新打开及文档语义保留。Reviewer 已独立复核最终验收推导与字体、金样及 GUI 证据。只适用于 Win10 Pro 22H2 19045 x64 / QEMU / WPS 12.1.0.28488；`full_acceptance_passed=false`、完整生命周期未全覆盖的限制保留。
- 完整安装包 449978846 字节，SHA-256 `3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27`。新公开名称为 `PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe`，与冻结包相同字节；没有再次构建。
- 已单次只读使用原发布私钥完成独立清单签名，未复制私钥、输出密钥或更换信任链。签名清单 SHA-256 `41a126b135097255e224192ca12fe6be479d077d3842f122e67b34b5c3657d1c`，其他九包签名及验收对象保持原值。`publication-windows-20261017/batch/preparation-receipt.json` 已生成；尚未部署、上传或修改公开下载。
- 发布阻塞：WorkBuddy 官方窗口最小化；get_window_state 返回 `window is minimized`，重新绑定后 activate_window 返回 `user input was detected in this window; call get_window_state before continuing`。已停止重复恢复，等待用户仅手工恢复窗口；不存在本轮部署回执，不伪造或重放旧部署。现有下载及全部回退记录保留。
- GovPulse 已恢复，覆盖安装、正常重启和原账号/事项/日历/附件保留证据继续有效。发布完成前不删除旧包或中间证据。

## 2026-10-18 发布前独立审查发现事务超时缺陷

- 用户恢复 WorkBuddy 后，官方接收器唯一部署成功，原 sandbox/域名不变；独立 health 核验旧十包原哈希保留，新 Windows 待上传。新包上传已启动，实际结果另存，尚未修改 GitHub 或官网公开入口。
- Reviewer 对准备提交的产品源码发现 P1：提权包装进程超时后，尚无 pending 时直接删除用户 journal；Windows 只读子进程实验确认包装进程超时不能证明其子助手终止。Architect 独立确认迟到助手仍可能写主机配置，现有通过测试没有覆盖此异常路径。
- `publication-windows-20261017/PUBLICATION_HOLD.json` 永久封存 `3c81…` 候选，不再作为最终发布包；其签名清单中的准备状态是发现缺陷前的历史事实，不构成当前发布许可。官网本地数据仍为 pending，GitHub 没有上传、提交或推送。
- 由单一 Complex Worker 最小修改 `host_configuration_transaction`：复用同 ID 的 abort/mutex/outcome，在恢复结果未确认时保留 journal；不改变 WPS、超时时间或新增进程终止机制。新哈希必须重新构建和绑定专项升级/恢复验证，不把旧 WPS 验收冒充新包实测。

## 2026-10-18 超时修复与候选下载回退

- 源码修复已完成，38 项专项测试及 Ruff 通过，Reviewer 独立 13 项分支验证通过。证据为 `../windows-timeout-repair-20261018/source-fix-review.json`；目前尚无包含修复的新安装包，旧全功能门禁不能用于新源码。
- 新候选验收控制器已通过实现者 13 项离线测试、Reviewer 9 项只读离线测试及其余四项静态复核。`../windows-timeout-repair-20261018/controller-review.json` 仅是控制器准入，不是 Guest 验收结论。继续使用同一 Win10 UUID、同一 D 盘系统盘和原业务基线。
- 旧候选上传实际已完成，但全文件回读被发布阻断中止。随后通过同一 WorkBuddy 官方部署恢复原十包接收器；独立匿名 health 核得原清单 `b5280745…`、十包全部 ready；旧候选 `startup-20261017.exe` HEAD 为 404，现官网旧 Windows 包 HEAD 为 200。见 `publication-windows-20261017/batch/withdraw-timeout-candidate.verified.json`。只撤下路由，旧候选实物和历史证据保留，不改网站或 GitHub。
- 本地官网已恢复当前公开基线，候选改动封存于 `website-candidate-held/`；下载卡片 11 项测试及十包 metadata 校验通过。公开文档仍为待更新草稿，不得提交其中旧候选哈希。
- 新全门禁暂被上次全测生成的 `.test-temp/pytest-of-Administrator/pytest-3` 合成数据阻塞，扫描 100 项均在该目录。此前自动审批拒绝过对此精确目录的删除，助手不换工具绕过；已请用户运行 `cleanup-completed-pytest-manual.ps1`。旧 12:25 清理回执早于目录重建，不能作为本次清理完成依据。
- 下一步：核实际清理 → 正式全门禁 → 保护旧包硬链接后构建新完整包 → 新控制器一次覆盖/正常重启/原业务核验 → 按实际组件及包身份补验 → 新发布批次。旧发布 hold 保持，不重放旧上传或部署 intent。GovPulse 当前已恢复，本轮未再次暂停。

## 2026-10-18 16:08 清理确认后恢复正式构建门禁

- 用户已执行手工清理。实际目录不存在；新 `pytest-3-cleanup.json` 为 08:08:15 UTC、removed、7333 文件、315131159 字节。此前旧回执不再作为本次依据。
- 已启动正式 `run-windows-gate.ps1`，日志 `full-function-gate-20261018-160846.log`；只在当前进程 PATH 前置健康 C 盘 Git/Gitleaks，不访问故障 E 盘、不改门禁。
- 本轮开工空闲提交内存约 17 GiB，C 盘约 30 GiB、D 盘约 22 GiB，因此不暂停已恢复的 GovPulse。新包构建和 Guest 操作尚待全门禁完成。
- 新发布工具只在 `publication-windows-20261018/` 编写，旧批次保持 hold。主机配置/个人恢复因相关源码改变，要求新包实际验收；WPS 仅在冻结模块、源码和运行组件严格一致时允许明确标注的历史证据复用。

- 本次全门禁在官网 PostgreSQL 集成阶段停止：`ECONNREFUSED 127.0.0.1:55432`；实际无该端口监听，`pg_ctl status` 确认既有 QA data 未运行。系统另有 PostgreSQL 业务进程，未触碰它们。此前自动审批曾拒绝启动此隔离库，已请求用户仅运行 `pg_ctl` 恢复现有目录；不重建库、不删除旧 postmaster.pid、不改扫描或测试门槛。此前已完成的静态、前端、官网单元结果保留在同一失败日志，不冒称全门禁通过。

- 门禁实际统计：前端 221 项、官网单元 206 项通过；官网接口 42 项中 36 通过、6 项因 55432 连接拒绝失败。为避免浪费，QA 包装入口已增加只读既有数据库服务预检，PowerShell 语法检查通过；不自动启动任何数据库，不改正式门禁。
- 原 3c81 包的 artifacts 固定名称已同卷移动到 `artifacts/candidate-history/3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27/PartyOps_1.4.5-rc.6_windows_amd64.exe`，前后 SHA 核验一致；原发布目录的其余硬链接不变，无重复大文件复制。这是避免新构建截断旧共享 inode，非清理历史证据。
- WPS 证据策略更新：独立审查发现 `historical-wps-domain.json` 的模块范围未覆盖完整 PYZ 及 CArchive 运行钩子，不能作为运行等价通过证据。停止扩大依赖推导框架，后续新包直接复用已有修正验收脚本做真实 WPS 回归，生成绑定新 SHA 的 summary；不重装 WPS 或系统。旧域快照只保留为调研，旧包 WPS 结论不改写。

- 用户首次手工启动时把 `20261016/.qa` 误复制成 `20261016.qa`，未执行数据库变更；改用正斜杠命令后已实际恢复。`pg_ctl status` 和 loopback 55432 均确认 PID 23684 为本 QA data（仅该时点身份），server.log 表示 ready。主控先单独运行原失败的 `node --test tests/supabase-postgres.test.mjs`，6 项全部通过，再继续已启动的正式全门禁（新执行会话 87407），不沿用失败门禁。

## 2026-10-18 中间摘要清理

- 按用户“只保留最终 SHA-256”的要求，精确删除 publication-windows-20261018 下废弃调研快照、转换回执和转换脚本三文件，释放 3,170,576 字节。移除唯一依赖已弃用快照的离线测试；18 项仍适用的发布工具测试通过。
- 保留不获准复用的结论、正式发布 helper、真实安装及数据保护证据；不删除正式签名清单的校验字段，不篡改历史失败回执。用户交付只展示最终包摘要。
- 使用原扫描规则重跑 scripts/scan-secrets.ps1，通过 Git 历史与当前工作区扫描；记录 ../windows-timeout-repair-20261018/scan-after-research-cleanup.log。此前中间快照误报不再阻塞。
- 既有 QA PostgreSQL 已确认运行；继续正式门禁，日志 full-function-gate-20261018-163244.log。没有重装 Guest、WPS 或开始新的覆盖安装。
## 2026-10-18 继续新包构建与验收

- 现有 Win10 桌面正常，未重装系统、WPS或重复覆盖安装。
- 正式门禁 full-function-gate-20261018-163244.log 完整结束：后端2205通过、2失败、5跳过、3子测试通过。失败为 test_windows_host_role_uses_uac_helper_for_protected_config 与 test_configure_host_direct_and_elevation_failure_matrix；门禁未通过，禁止打包发布。
- 定点定位为旧fixture缺少stdout/stderr、多个未知提权结果场景共用配置目录。新增保护正确保留journal；由Complex Worker仅修正两测试fixture，产品实现不改、权限和错误断言不降低。
- 本次失败运行的覆盖率另存 ../windows-timeout-repair-20261018/coverage-failed-gate-analysis.json，行96.26%、分支92.04%，仅作分析，不替代成功门禁。
- 全测再次生成pytest-3合成临时目录；由于此前自动审批拒绝过精确目录删除，已请用户执行原手工清理脚本。未换工具删除或增加扫描例外。等待清理期间继续定向测试和独立复核。
## 2026-10-18 16:50 定向回归与临时目录准入

- 两fixture修正后，在独立短临时目录两次40项通过，Ruff通过；Reviewer审查本次相对暂存区增量，无阻断，不改产品。未知事务journal保留、场景隔离、策略拒绝仅一次UAC及同ID rollback收据断言均保留。
- 额外深QA目录试跑有20项事务失败，内层错误未完整保存，不能宣称根因已确证或已排除产品长路径问题。临时控制文件266字符与正式默认路径237字符仅支持路径长度候选。正式门禁仍要求默认目录下全量通过，不以短目录40项替代。
- 用户已清理正式pytest-3，实际目录不存在，新回执08:47:59 UTC，7429文件、315162245字节。QA库仍运行。
- 定向测试四目录遗留14条合成配置扫描项，均在qa/hf3、qa/hf4及pytest-host-fixture-20261018-1722；hf2亦为同轮结束场景。只读报告fixture-scan-redacted.json，扫描规则未变。
- 主控精确清理这四目录时自动审批拒绝，未执行命令，只返回blocked by policy。未换工具绕过；已准备cleanup-fixture-temp-manual.ps1并请用户执行。不得把原pytest-3清理回执当成此四目录完成。新包/Guest覆盖/发布仍未开始。
- 用户完成修正后的四目录手工清理，主控逐路径确认不存在；凭据扫描重新通过（scan-clean-final-preflight.log）。清理脚本对94个pytest current目录符号链接先删链接本身，用户实际执行完成；未由助手执行拒绝过的删除。
- 按用户最新要求，后续不穿插清理，先完成新包构建及验收，测试数据统一留待最后处理。正式门禁新日志full-function-gate-20261018-165337.log已运行；测试修正已暂存，未提交、推送或发布。
## 2026-10-18 17:35 新包完成与唯一覆盖安装观察

- 正式门禁 full-function-gate-20261018-165337.log 于17:05完成：后端2207通过、5跳过、3子测试通过，行覆盖96.27%、分支92.05%；前端221项及官网完整测试、接口、构建等通过。
- 正式构建 candidate-build-20261018-170531.log 成功，新完整包 artifacts/PartyOps_1.4.5-rc.6_windows_amd64.exe 大小449988807字节；对应正式回执见 ../windows-timeout-repair-20261018/overlay-install/attempt.json 引用。最终摘要暂不对外重复展示。
- 新控制器preflight、record-start、stop-current成功；17:21仅发起一次覆盖安装，尚无completed回执。业务基线及备份保持原样。
- 17:25仍有安装日志写入，17:26末尾安装office-runtime帮助文件；此后Guest交互及SSH失去响应。17:30至17:33 QMP三次记录显示running、无块I/O错误，但磁盘无新增读写；单vCPU约94.5%忙，桌面停在17:29。30秒纯SSH banner探测超时。不能宣称安装成功或据此归因产品。
- Architect只读复核确认安装会话上限3600秒，超时不终止Guest安装器；正在评估保留原内存/磁盘的最小诊断，未强制重启、重复安装或终止QEMU。GovPulse保持已恢复状态。按用户要求本轮测试临时数据最后统一清理。

- 17:35:51经Architect定点判断，执行一次QMP stop→paused→cont→running，保留同一QEMU和Guest内存；未reset/重启。随后桌面时钟恢复、原右键输入得到响应、磁盘读写继续；原唯一安装会话于17:36返回0并生成completed.json。根因仅确认与虚拟机停滞有关，不能断言具体WHPX缺陷；继续新包及原数据实测。证据install-pause-resume-once.json、install-after-resume-qmp.json。

- 新候选launch与verify-overlay通过；原账号、事项、日历、附件保留。正常关机已返回，但再次开机被D盘20GiB+2GiB余量门槛阻断。原QMP shutdown事件只在内存，未持久化；不补造正常重启回执、不重复关机。reboot-start-interruption.json如实记录中断。两份非活动历史缓存无损移动到本工作区.qa/deferred-cache，D空余22.321GiB，源/目标哈希一致；不是测试数据删除。正在为本次已关机Guest建立一次继续启动及真实Windows事件核验。

- 原VM单次恢复开机成功，QEMU PID5560，实际boot为2026-10-18T09:43:26.5Z。原QMP事件未落盘，因此以独立Windows System事件恢复回执证明正常关机与启动；原normal-reboot-attempt保留，未造标准completion。首轮因跨provider RecordID顺序假设失败，修正为UTC时序后通过；旧04:00的异常关闭历史与本次范围严格分开。
- Windows事件12/13可延迟写入，RecordID不等于跨provider发生时间；真实原始失败事件与修正后回执均保留。Reviewer只读复核通过事件时序，补强wrapper候选/运行身份关联后继续独立复核。
- 原websockify查看服务仍运行且未重启；官方QMP display-update只将同VM VNC重新绑定原127.0.0.1:23995，恢复现有查看连接，不开放外网、不改认证。viewer-port-restored.json保存前后信息。
- 排版QA入口增加显式--installation-evidence，避免新候选读取旧overlay完成记录；只允许两份已知QA目录，仍按candidate严格核安装身份，排版输出仍写原随机run。23项离线回归通过，Reviewer实物确认旧回执对新binding被拒绝。新完整实测run为win10-formatter-7de930093bcc，目前九个功能案例通过，金样/五字体尚在执行；未复用旧候选排版证据。

## 2026-10-18 18:05 新包专项通过与主机授权等待

- 新包完整WPS实测win10-formatter-7de930093bcc已通过六入口九例、五字体、17输出与三页金样；无历史resume。Reviewer独立重算两DOCX语义及三PDF逐像素一致，正式包/构建/源码指纹及2207项门禁绑定通过。
- retention-after-reboot.json已通过原账号、事项、日历和附件保留；正常重启仍使用真实Windows事件恢复链，保留原QMP事件未落盘的限定，不补造回执。
- host-prepare成功；host-submit仅发起一次，09:55:34 UTC提交。UAC等待期间个人模式及原进程保留，journal存在。10:03:31 UTC客户端记录HOST_HTTP_FAILED_INSPECT_WITHOUT_RESUBMIT，不能据此判定Guest动作失败或成功，不重发。
- 用户明确要求查找并输入已有Win10 QA密码。只读取同UUID的本地guest-credential.local.json，通过noVNC实际输入并点击本次确认；未改密码。浏览器无障碍回读意外带出QA临时凭据，已清空浏览器输入；本记录不保存凭据，后续密码框仅截图观察。
- 当前UAC框处于灰色等待，主机切换与恢复未通过。Architect只读检查进程/事务状态中；不重复安装、不清理测试数据、不发布或使用旧候选证据替代。
- 后续校正：noVNC坐标输入存在偏移，最初“是”的点击没有命中按钮，不能记录为授权已成功。实际键盘DOM输入已送达掩码框；在核对光标偏移后，仅正常点击“否”关闭残留UAC。旧提权PowerShell已不存在，未见host/abort/restore助手，原个人进程6208不变；Guest与宿主时钟相差约2分51秒，不混用绝对时间推导顺序。正常取消后向导1192和journal仍在，后续必须通过产品正式恢复入口确认同事务rollback，不手工删journal。

## 2026-10-18 管理员输入通道校正

- 首次受控reopen关闭旧QA向导后未完成新任务注册，失败原脚本和只读诊断保留；不能据此宣称恢复成功。resume-launch仅补齐缺失的正常用户启动，实际成功打开产品恢复UAC，未更改原个人进程或业务数据。
- 保存的同UUID Win10 QA凭据通过一次SSH密码认证，未修改凭据。UAC错误输入后已正常取消，未猜测或连续重试密码。
- 以非敏感样本在Guest浏览器地址栏实测：noVNC fill/pressSequentially未保持大写和Shift符号，且中文输入法会转换样本文字；显式组合键Shift+p和Shift+1正确显示P和!。样本未提交搜索，测试标签已关闭。后续密码不进入无障碍快照、文件或聊天，仅用已验证的组合键通道输入到本QA授权框。
- 当前产品页面仍明确提示上次模式切换待管理员恢复；主机模式及发布继续阻断。下一步只补同事务正常恢复，之后执行独立新host/restore轮次，不重复安装、不清理测试数据。

- 后续恢复已完成：显式组合键正确输入已有QA密码并提交UAC；产品恢复页警告消失。`host-journal-recovery-verified.json`证实同ID rollback、journal/pending清除、6208同实例以及原账号/事项/附件/日历保留。Reviewer独立核验通过；图片`../windows-timeout-repair-20261018/uac-recovery-returned.jpg`。
- 独立after-journal主机配置实际成功：`after-journal-first-host-approved-existing-root-submit.result.json`为bootstrap-page-observed/HTTP200、user/system host、host ready PID6624；两次同一配置流程的UAC均以现有QA密码完成。Guest浏览器实际HTTPS访问10.0.2.15:18765呈现“主机服务已就绪”和首位管理员配置页面，未新建账号、未绕过证书警告。图片`host-ready-browser.jpg`。
- 随后个人恢复prepare在回收旧QA向导后中断，原失败回执保留；一次只读检查证实旧7492已退出、原主机仍正常、新个人任务不存在。复用既有recover-prepare只补正常任务启动成功，正在执行唯一submit恢复原个人目录；未再回收、安装或重启系统。

## 2026-10-18 最终候选验收结案与发布进行中

- 个人恢复已由实际只读完成证据确认：PID3380、原目录、双方personal、服务Stopped/Manual、journal/pending清除；HTTP提交轮询失败原样保留，不补写HTTP成功。`after-journal-restore-personal-observed-completion.json`和`after-journal-retention-after-host-restore.json`证明原账号、事项、日历、附件保留。
- Reviewer独立核验43份回执及摘要、来源篡改反例、实际截图后准入。正式源码全测、最终包覆盖/重启/WPS均通过；真实安装180秒UAC超时及完整生命周期仍未验证。
- 最终完整包SHA-256为e3e2ea45ccd38acff059b5713826a78f998a56ccef75341633f67bd4e17e57da。新发布目录`publication-windows-20261018`已prepare、使用原Ed25519私钥签名并bundle；私钥仅从此前已明确定位的E盘文件只读使用，不复制/输出，不写E盘。
- WorkBuddy5.8.2只读后台预检仍CONNECTOR_MISSING_OR_AMBIGUOUS。已按用户既有授权在原任务提交一次接收器部署，尚未认定公网完成。主控发现复制原token映射的键仍为旧ARM包，已通知保留唯一调用结果、不得重试或改文件；等确定真实结果后修正映射。安装包本身不变，旧官网/GitHub未切换。
- 所有删除延后至发布验证完成；未重复安装系统或WPS，GovPulse保持恢复状态。
