# Windows 主机启动修复记录

最后验证日期：2026-10-16。基线：`b58cd05`；工作分支：`fix/windows-host-startup-20261016`。本记录仅描述源码修复与本机验证，不代表安装包或 Win10 虚拟机验收完成。

## 根因与修复

1. `setup_wizard.py` 两处向 `powershell.exe -Command` 脚本尾部追加参数，真实 Windows PowerShell 中 `$args.Count=0`，导致 `Start-Process -FilePath $args[0]` 收到空值；原错误处理又将此错误提示为管理员授权未完成。
2. 新增 `windows_elevation.py` 公共调用：路径和参数采用 UTF-8 JSON/Base64 数据，固定脚本采用 UTF-16LE `-EncodedCommand`，子进程参数采用 Python `subprocess.list2cmdline`。保留 `-Verb RunAs -Wait -PassThru`，没有绕过权限。以明确阶段标记区分启动失败和管理员子进程退出；只有启动阶段原生 1223 才提示取消，786/1260/策略文本提示组织策略，业务退出提示 `ADMIN_CHILD_FAILED`，其他启动错误提示 `ADMIN_START_FAILED`。
3. Windows 主启动流程在 TLS 初始化、Agent 线程前探测原配置的业务端口和 Agent 端口，并释放探测套接字。10049、10048、10013 分别提示地址不可用、端口占用、绑定被拒绝。不会更换用户指定地址；Linux/macOS 保留原绑定流程。Agent 线程异常传回主流程并保留异常链。诊断分类去除仅凭中文“端口”“占用”判断冲突的规则。
4. `get_settings()` 本身维持原覆盖优先级。用户显式重新配置时，向导精准同步既有 `network-settings.json` 的 `bind_host`、`advertise_host`、`port`，保留其他字段。旧配置限定单网卡时，重选同一或新 LAN 地址仍限定到明确地址；旧通配监听保留；无旧配置沿用既有向导默认。文件纳入内部写入事务。Windows 外层配置另复用受保护快照、pending 和事务 ID，新增 `kind=configure_host`；管理员助手在任何业务写入前快照固定四份配置（系统 env/system mode/数据 marker/network）与原服务状态。父进程仅处理当前账号模式文件、个人环境和事务 ID，不读取或写入受保护的 network/marker。助手失败自行恢复；父模式提交或 finalize 写入结果前失败以同 ID 调用特权恢复，先停服务恢复文件后恢复原服务。恢复失败保留 pending，禁止启动个人进程；重开向导可识别新 kind 恢复。
5. 非空个人目录原地转主机会改变整树 ACL，无法仅凭配置文件回滚证明个人可用。本次按保守边界：助手结合真实 PartyOps marker 以及父账号 personal.env 提供的仅比较目录，在任何 ACL、服务或配置修改前明确拒绝，并提示选择独立主机目录；父进程直到独立主机 prepare 成功才停原个人进程。GUI 助手使用固定业务退出码 41 回传个人目录原地拒绝，即使没有 stderr 也能给出专用中文提示。已有 host 重配、首次空目录继续支持；既有主机数据目录迁移流程保留。没有删除业务数据，也没有增加普通账号对受保护目录的权限。

6. 两处生产向导调用改为 `host_configuration_transaction` 上下文，包含 `launch_host`、环境/CA 读取与相应就绪判定；健康通过才提交当前账号 mode 与特权事务。原 `configure_host_config` 保留为明确仅配置的兼容接口，不能把它当成启动验收。
7. 父进程在 prepare 前持久化固定本用户 `host-config-journal.json`，仅保存事务 ID、原 mode 原文/不存在和原角色。特权助手为每个 ID 在受保护、普通用户只读控制目录中保留 commit/rollback outcome receipt。commit receipt 写入是提交点；之后清理失败只能告警并幂等重试，禁止反向回滚。rollback receipt 只在四文件及服务确实恢复后写入。恢复入口即使没有 pending 也检查本用户 journal；没有匹配结果凭据则停止并保留记录，不猜测。
8. 正式安装 personal/client 自启动均经 `PartyOpsLauncher --background`，由 `windows_launcher.py` 按当前 `mode.json` 分流。用户自启动清理延后至健康且特权 commit 之后；失败保留已提交主机并记录 `AUTOSTART_CLEANUP_DEFERRED`。回滚前未改自启动，因此不再重写原启动项。普通 Launcher 对固定本用户 journal 仅检查是否存在，前台转向恢复向导，后台拒启动，覆盖 pending 已清而用户状态尚未恢复的窗口。旧便携版直接 partyops-client 的 fallback 不属于当前正式安装器启动契约。

## 验证证据

真实 PowerShell 无副作用基线复现结果：

```text
baseline_returncode=1
ARG_COUNT=0
Cannot validate argument on parameter 'FilePath'. The argument is null or empty.
```

独立标准库验证：

```powershell
backend/.venv/Scripts/python.exe backend/tests/test_windows_startup_transport.py
```

结果：4 个 unittest 通过。替身验证真实 Windows PowerShell 参数传递（空格、中文、单引号、双引号、空参数、尾反斜杠、命令样式文本），明确检查 RunAs/Wait/PassThru；替身实际启动无副作用 Python 子进程，验证收到的 argv。另验证 1223/786/1260/5 启动异常与子进程退出阶段区分、绑定诊断及套接字释放。该测试没有触发真实 UAC 或组织策略。

定向回归命令：

```powershell
backend/.venv/Scripts/python.exe -m pytest backend/tests/test_windows_public_acl_real.py backend/tests/test_windows_host_configuration_transaction.py backend/tests/test_windows_startup_transport.py backend/tests/test_windows_launcher_visible_handoff.py backend/tests/test_setup_wizard_rc3_branch_matrix.py backend/tests/test_setup_wizard_cli_release_matrix.py backend/tests/test_setup_wizard_http_release.py backend/tests/test_173_rc3_wizard_transaction_branches.py backend/tests/test_144_first_run_collaboration.py backend/tests/test_setup_wizard_release_paths.py backend/tests/test_runtime_probe_followup.py backend/tests/test_role_reconfiguration.py backend/tests/test_adversarial_rc3_release.py backend/tests/test_145_release_rc2_regressions.py -o addopts='' -q --tb=short --junitxml=qa/acceptance/windows-startup-20261016/startup-final-tests.xml
```

最终定向结果：`301 passed, 7 warnings, 3 subtests passed in 36.60s`。警告来自现有 Starlette/httpx、anyio、PyMuPDF 弃用提示。覆盖显式地址不拓宽、旧覆盖文件同步/回滚、真实助手写入后服务启用失败的系统恢复、非 Windows 启动路径、主线程预检顺序及 Agent 原因保留。

定向 Ruff 检查通过；`git diff --check` 通过。后续主控已安装 mypy，七个改动产品文件（含公开文件模块、PKI 和 Windows Launcher）的 `mypy --follow-imports=silent --no-incremental` 通过；首次尝试时尚未安装的结果已被后续验证更新。受保护事务与新增 HTTP/Launcher 回归覆盖启动失败、就绪不匹配、结果凭据写失败、mode 写入后崩溃、commit 后崩溃、pending 已清而本用户 journal 未恢复、GUI 缺失 stderr、恢复失败阻止启动；相应定向用例通过，采用强制文件 IO guard 拒绝普通父进程对受保护数据/快照进行读写，并实际执行助手写入与回滚函数；其中原事务测试是权限边界仿真；本轮另增加真实 Windows 受限令牌 ACL/TLS 验证，仍不等于 Win10 Guest 安装版或交互式 UAC 验收。没有运行完整构建、完整覆盖率、真实 Win10 修复安装、UAC 交互或组织策略实测；这些由主控后续执行和登记。

## 文件与回滚

修改 `backend/app/setup_wizard.py`、`backend/app/main.py`、`backend/app/windows_host_status.py`、`backend/tests/test_setup_wizard_rc3_branch_matrix.py`、`backend/tests/test_setup_wizard_cli_release_matrix.py`、`backend/tests/test_144_first_run_collaboration.py`、`backend/tests/test_setup_wizard_http_release.py`、`backend/tests/test_173_rc3_wizard_transaction_branches.py`、`backend/tests/test_windows_launcher_visible_handoff.py`、`packaging/windows/windows_launcher.py`；新增 `backend/app/windows_elevation.py`、`backend/tests/test_windows_startup_transport.py`、`backend/tests/test_windows_host_configuration_transaction.py` 与本说明。没有修改版本、打包门禁、前端或虚拟机配置，没有提交。

回滚时仅恢复以上本次源码/测试改动；运行期配置失败通过同 ID 特权事务还原固定配置与服务状态；不能删除残留 pending/受保护快照来伪装恢复成功。个人非空目录原地转主机本次明确拒绝，迁移时请选择独立主机目录。安装包回滚和旧版配置兼容应由主控按原数据目录验证。本机工具使用当前可用 `exec_command`、`apply_patch` 与安全 PowerShell，没有调用不存在的规划或符号编辑 MCP。

最终冻结时间：2026-10-16 19:19:44 +08:00；后续仅由主控执行发布门禁与 Reviewer 定点复核。


## 最终权限、单网卡与未准备事务补充

- 新增 `backend/app/windows_public_files.py`，仅允许固定非敏感 pending、按 ID outcome 与 `public-host-ca.pem`。发布前为新临时文件显式设置 SYSTEM/Administrators 完全控制、Users 只读、Administrators 所有权，再原子替换；不依赖控制目录无继承的 Users RX。任何 ACL 设置失败均保留旧公开文件。
- `pki.py` 由生产主机进程在校验内部 CA 后发布固定公开证书副本；私钥与原数据内 CA 仍在 SYSTEM/Admin 目录。桌面健康、安装 CA、账号状态读取统一采用公开副本，个人模式及非 Windows 保持自身证书路径；TLS 校验保持启用。
- 已确认的通配绑定探测回环，明确 LAN 绑定探测该本机地址；桌面 Launcher、配置/重新配置向导及 Windows 监督器一致，不改变实际 bind。真实 QA 服务只绑定本机 LAN IP，127.0.0.1 连接明确失败而严格 TLS 探测成功。
- prepare、abort、restore、finalize 和旧停用助手共用 `Global\PartyOps.HostConfiguration` 内核 mutex；DACL 仅 SYSTEM/Administrators，超时明确，abandoned 所有权可恢复。仅用户 journal 而无 pending/receipt 时走固定管理员 abort 动作：在同一 mutex 中确认无对应活动快照后写 rollback receipt，晚到同 ID 助手在任何配置写入前拒绝继续。用户 journal 不提供任何特权文件路径。
- 实测采用宿主的 `CreateRestrictedToken`（禁用最大权限并把 Administrators 设为 deny-only）和 `ImpersonateLoggedOnUser`，确认普通线程令牌非管理员。公开 CA/pending/receipt 可读不可写，业务 CA 和 ca.key/server.key 不可读，全局 mutex 普通令牌访问拒绝。真实单网卡 TLS health、监督器 probe、账号状态、复制公开 CA 均通过；另真实验证活跃助手阻塞 abort、助手退出产生 WAIT_ABANDONED 后可恢复。
- 本机操作只在本工作区隔离 QA 目录运行，没有改生产目录或 Windows 信任库；CA 安装调用被替身替换，不能声称交互式 UAC 或真实证书信任库安装通过。

最终证据：`qa/acceptance/windows-startup-20261016/startup-final-tests.xml`、`startup-final-metadata.json`、`real-public-acl.json`。元数据包含宿主 Windows 真实版本、执行时间、14 个测试文件及8个相关产品文件 SHA-256。此处宿主测试不是 Win10 Guest；Win10 修复安装与原数据业务验收由主控另记。Ruff、七产品文件 mypy、按现有门禁 `Bandit -ll` 定点检查和 diff 检查通过；未降低覆盖率或审计规则。lxml 两平台 wheel 缺失的既存输入问题已由主控补齐，最终301条统一回归包含相应用例通过。
