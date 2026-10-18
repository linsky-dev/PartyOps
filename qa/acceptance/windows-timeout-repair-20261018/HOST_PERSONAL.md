# 新候选主机配置及个人恢复专项

准备日期：2026-10-18。入口 `host-personal.py`，默认只做本地证据检查；本轮开发及离线测试均未连接 Guest、未读取 Guest 凭据、未启动向导或提交配置。此说明不代表专项验收通过。

本次续接：最初 host-submit 已失败，原 attempt/result 不覆盖。受控回收旧 QA 向导后，原 reopen 在任务注册前失败，旧 stderr 未持久化，未知根因不追认为确定原因；只读诊断证明旧向导已退出、任务未注册、原个人进程保留。主控已唯一执行 `recover-host-journal.py resume-launch`，只启动正常个人重配置入口以恢复同一 journal 事务，不重复回收。尚无 `host-journal-recovery-verified.json` 时保持阻断，不重发任何旧命令或猜测口令。

主控完成正常恢复 UAC 后，仅执行 `backend/.venv/Scripts/python.exe -X utf8 -B qa/acceptance/windows-timeout-repair-20261018/recover-host-journal.py verify --package artifacts/PartyOps_1.4.5-rc.6_windows_amd64.exe --sha256 <实际新包SHA> --execute`。该命令只读确认同 ID rollback、journal/pending 清除、无残留 helper/consent、原个人实例及业务保留。

恢复成功后改用 `host-after-journal.py`，沿用下述 check、host-prepare、host-submit、restore-prepare、restore-submit、verify-retention 动作及显式 `--execute`，使用同一虚拟环境解释器。默认 check 也会验证上述恢复成功证据；所有新动作回执以 `after-journal-` 为前缀，任务名含 After-Journal，旧 failed 回执只作为证据来源。host-prepare 仅精确回收已绑定的恢复向导并启动正常 host 初始入口，不能停止原个人进程或服务；时间用 tick 比较。发布验证显式消费成功的新轮次、原失败和完整恢复链，不将新请求当成旧未知请求的重放。

前置说明：仅在当前新验收目录新增薄适配及测试，不修改产品、网站、`controller.py`、旧 QA 控制器或旧证据。使用现有 `apply_patch` 和本地 Python。回滚开发变更可撤销本轮新增文件；执行后产生的 attempt/结果须保留，不能删除来重复动作。

## 前置条件及执行

先用 `controller.py` 完成新正式完整包的一次覆盖、一次正常重启及原数据保留验证。本入口要求新目录 `retention-after-reboot.json` 通过且绑定当前新包、安装完成回执及原基线摘要。新正式包 SHA 由正式构建回执提供。

在 `C:/PartyOps-VM-Lab/windows-startup-repair-20261016` 中逐条执行并检查退出码，前一步失败不继续：

```powershell
$hostController = 'qa/acceptance/windows-timeout-repair-20261018/host-personal.py'
$package = 'artifacts/PartyOps_1.4.5-rc.6_windows_amd64.exe'
# $sha 必须事先设置为新正式包的真实 SHA256。
python $hostController --package $package --sha256 $sha
python $hostController host-prepare --package $package --sha256 $sha --execute
python $hostController host-submit --package $package --sha256 $sha --execute
python $hostController restore-prepare --package $package --sha256 $sha --execute
python $hostController restore-submit --package $package --sha256 $sha --execute
python $hostController verify-retention --package $package --sha256 $sha --execute
```

`host-prepare` 只启动一次安装后的向导，不提交配置、不重新 Prepare 用户。`host-submit` 在原普通用户向导上提交一次真实主机配置，主控通过 GUI 完成必要 UAC；不创建管理员账号或测试业务。成功必须观察 HTTP 200 管理员初始化页、Host ready、用户/系统 host。`restore-prepare` 精确关闭已绑定的主机向导并启动一次个人重新配置向导；`restore-submit` 经正式异步事务一次恢复原个人目录，主控负责必要 UAC。`verify-retention` 比较原合成账号 ID、事项 ID、附件 SHA、日历 ID/SHA，证明同一原个人运行实例恢复且数据保留。

假设沿用已登记 Win10 VM、原普通用户桌面、原 QA 主机根目录 `C:\PartyOps-QA-Host-20261017\主机 数据` 与个人目录。主机准备要求用户和系统模式均为上轮实际恢复后的 `personal`，不能删除系统配置伪装成 `missing`。根目录只能通过旧真实创建时间、单个 SQLite 文件头/硬链接、无重解析点、公开 CA 指纹及停止服务检查证明所有权；不创建另一目录或数据库。旧创建证据只证明目录来源，不能充当本次新候选主机验收。

出现已有 attempt/result、超时或结果未知时，不重放动作、不删记录。只运行以下检查，然后由主控评估真实状态：

```powershell
python $hostController inspect --package $package --sha256 $sha --execute
```

inspect 不提交产品配置、不启停程序或服务；沿用旧 transport 机制，在 Guest 固定 QA 目录独占暂存只读探针，结果写新目录 `host-personal-observation-*.json`。它不能补造成功回执。默认离线 check 不连接 Guest，也不暂存探针。

## 复用与证据格式

- 复用旧 `first-host-qa.py` 的表单解析、Wizard 身份、既有 QA 根目录所有权及只读探针；不调用其强绑定旧负例/PID/失败恢复链的 `main_run`，不伪造那些前置。
- 新主机 case 为 `retained-existing-root`；主机证据名为 `first-host-prepare.attempt/result.json`、`first-host-approved-existing-root-submit.attempt/result.json`。保留后者旧文件名是为了薄适配恢复入口，case 明确区别于旧失败后恢复路径。
- 个人恢复直接复用旧 `restore-personal-qa.py:run`，只注入新输出目录、当前主机成功契约、原基线校验和模块路径。任务改为 `PartyOps-QA-First-Host-20261018`、`PartyOps-QA-Restore-Personal-20261018`，不覆盖或运行 20261017 旧任务；同名新任务存在即阻断。
- 恢复证据继续为 `restore-personal-prepare.attempt/result.json`、`restore-personal-submit.attempt/result.json`。成功结果 `status=restored-ready`，包含新 candidate、绑定 attempt 摘要、runtime、两项停止且 Manual 的服务、个人目录、user_mode、无 pending/journal；可直接复用旧 `verify-retained-business.py:restored_personal` 校验。
- 最终 `retention-after-host-restore.json` 包含 `status=passed`、新 candidate、原 baseline/backup inputs、UUID/磁盘/runtime、原 retained/calendar，以及 `personal_restore_completed_sha256`、`host_success_sha256`。旧基线和备份只读引用。
- 所有新回执留在本目录。未知/失败不会自动补发或以旧 host 成功证据替代；本适配不暴露 recover-prepare 等额外写入恢复动作。

## 本地验证

`python -m unittest discover -s qa/acceptance/windows-timeout-repair-20261018 -p test_host_personal.py -v`

离线测试覆盖默认不连接、所有 Guest 动作显式开关、重复 attempt 阻断、新候选及 Host ready 契约、模块输出重定向、旧真实目录证据、新任务防重复、原恢复入口注入、系统实际个人模式准入。最后验证日期 2026-10-18：9 项通过，语法编译通过。真实主机配置、UAC、个人恢复及最终数据保留尚待主控执行。
# 本次取消后的仅启动补全

`cancelled-relaunch` 在 recycle 阶段返回 `OTHER_LIVE_WIZARD_APPEARED`，旧 attempt/result 保留且不可重发。一次只读诊断 `host-journal-cancelled-relaunch-readonly-diagnosis.json` 确认旧 Wizard4532 已不存在、无监听及其他 writer，原个人6208/boot/HTTP和原 journal 不变，原任务 Ready，新 After-Cancel 任务不存在。

`resume-cancelled-start.py` 默认只核本地来源；显式 `--execute` 才重新核对现场并独占记录 `host-journal-cancelled-start.attempt/result.json`，只注册启动正常 RunLevel0 personal reconfigure，不再回收。恢复验证及发布显式消费这个启动来源，同时保留此前失败和诊断链。启动请求成功不等于 rollback/保留已验证，仍须主控处理 GUI UAC 后执行 `recover-host-journal.py verify`。

主机新请求已成功后，本轮 `after-journal-restore-personal-prepare` 在向导回收后失败。一次只读诊断确认原向导7492已退出、主机6624及服务仍运行、无新个人任务和事务；实际脚本保留，原stderr未保存，未断言唯一根因。`restore-after-journal.py check` 离线核来源，`recover-prepare --execute` 薄复用旧 `restore-personal-qa.py` 的仅启动恢复路径，`submit --execute` 显式消费该成功准备。两动作均须带本轮 `--package` 和 `--sha256`，保持旧失败回执。发布消费同时核失败、实际诊断、recover-prepare及submit来源链；最终仍须原 `verify-retention` 证明个人及业务保留。

