# Win10 timeout 修复后最小验收控制器

准备日期：2026-10-18。当前仅完成本地控制器及离线测试；未连接 Guest、未安装、未重启，未声明新候选验收通过。新包 SHA 尚待正式构建生成。

前置说明：使用会话现有 `apply_patch`、本地 Python 与只读检索，不调用网络写入或远端 CLI。所有新增文件仅位于本目录；旧控制器、原业务基线、旧备份和产品文件不修改。回滚本轮控制器变更只需撤销本目录新增源码；执行后产生的证据应归档保留，禁止删除 attempt 来重试。无迁移，无系统重装。

## 起点及复用边界

- 原 VM 身份和唯一实际系统盘由旧 `retention_contract.bind_guest` 校验：UUID `f47cf265-72a2-4d51-9cf1-b1f5ae960cb2`，`D:/PartyOps-VM-Lab/vms/win10-x64/system.qcow2`。不调用创建 VM、安装系统、恢复快照或删库流程。`existing_vm.py` 仅作为原 VM 身份来源参考，不调用其 start 分支。
- 原 `../windows-startup-20261016/retention-baseline.json` 和工作区 `.qa/windows-retention/before-overlay.partyops-backup` 继续作为唯一原业务及备份依据。只读检查备份大小、摘要、verified 标记，不复制、改写或追认基线。
- 当前安装起点为旧完整候选 `3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27`，449978846 字节。期望文件哈希来自 `D:/PartyOps-VM-Lab/state/builds/3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27.json`。控制器核对旧成功保留报告封存的回执摘要、正式构建日志和 manifest，再核对 Guest 实际 manifest、PartyOps.exe、PartyOpsWizard.exe 三个摘要。不会把原基线的最初公开包 manifest 当作当前安装起点。
- 新候选只接受本工作区 `artifacts/PartyOps_1.4.5-rc.6_windows_amd64.exe` 和调用者明确提供的 SHA256；复用旧 `candidate()`，要求实际包、当前产品源码 fingerprint、正式构建回执、构建日志、封存 manifest 全部绑定一致。产品源码变更后不可沿用旧 fingerprint。`scripts/verify-full-function-gate.py` 的 package 范围不包含 `qa/acceptance`，本控制器变更本身不改变产品包指纹。
- 覆盖复用 `install-candidate-once.py` 的 `installer.install_modern`；停止及启动复用 `runtime-transition.py`，运行时只重定向本目录证据及明确的新起点。正常重启和运行身份复用 `retention_contract.py`；业务读取复用 `verify-retained-business.py` 使用的同一 `WindowsRun.check_business_data`、日历摘要算法与原个人目录常量。
- `restore-personal-qa.py` 的旧成功恢复证据由旧 `retention-after-host-restore.json` 引用，保留历史证据。因本次修改涉及配置向导事务，新候选还须经 `host-personal.py` 完成一次真实主机配置、恢复原个人模式及原数据保留验证；执行顺序见 [HOST_PERSONAL.md](HOST_PERSONAL.md)。旧成功证据只证明历史起点，不能替代本次专项。当前起点仍须通过原个人目录和原合成业务实际读取。

## 主控执行顺序

所有命令在 `C:/PartyOps-VM-Lab/windows-startup-repair-20261016` 中运行。将下面 `$sha` 设置为**新正式完整包**的真实 SHA；示例不填入占位哈希。Python 使用当前本地可用解释器。

```powershell
Set-Location -LiteralPath 'C:/PartyOps-VM-Lab/windows-startup-repair-20261016'
$controller = 'qa/acceptance/windows-timeout-repair-20261018/controller.py'
$package = 'artifacts/PartyOps_1.4.5-rc.6_windows_amd64.exe'
# $sha 由新正式构建回执提供，执行前须已设置。
python $controller preflight --package $package --sha256 $sha
python $controller record-start --package $package --sha256 $sha
python $controller stop-current --package $package --sha256 $sha --allow-qa-process-termination
python $controller install --package $package --sha256 $sha
python $controller launch --package $package --sha256 $sha
python $controller verify-overlay --package $package --sha256 $sha
python $controller normal-reboot --package $package --sha256 $sha --execute-normal-reboot
python $controller verify-reboot --package $package --sha256 $sha
```

逐条执行并检查退出码及新证据；前一步失败不继续。preflight 不连接 Guest。record-start 只读取现有原个人模式业务并独占记录当前启动和安装起点，不生成新账号、事项、附件、日历或备份。业务读取会沿用已有合成账号登录 API，不输出凭据正文。

`stop-current` 复用已绑定的单个普通用户进程停止流程；显式参数允许必要时只终止该精确进程，不代表 GUI 正常退出验收。`launch` 复用一次普通用户交互任务，不运行 Prepare 或重新配置。若安装器已经自动启动产品，launch 会阻断；先 inspect，不自行重复启动。

安装及重启先独占创建 attempt，再发出唯一动作。结果未知、断线、超时或已有 attempt 时，只运行：

```powershell
python $controller inspect --package $package --sha256 $sha
```

inspect 仅采集实际启动时间、三个已安装哈希、进程和 HTTP 监听归属，写本目录 observation；不启动程序、不安装、不重启、不补造 completion，不将未知状态判为通过。缺失真实完成回执时后续验证继续阻断，由主控依据真实安装日志/启动证据决定后续处理；不得删 attempt、换目录逃避次数限制或直接重装。

## 新证据及验收标准

本目录独占生成 `retention-boot-before.json`、`runtime-stop-old.*.json`、`overlay-install/attempt.json`、`overlay-install/completed.json`、`runtime-launch-candidate.*.json`、`retention-after-overlay.json`、`normal-reboot-attempt.json`、`normal-reboot-completed.json`、`retention-after-reboot.json`。不覆盖旧目录同名证据。

通过条件：新正式完整包身份一致；覆盖退出码 0 且已安装布局完整；同一 VM、同一系统盘、同一原合成账号 ID、事项 ID、附件 SHA、日历 ID 和日历 SHA 在覆盖后及一次实际正常关机启动后都与原基线一致；新运行进程和 HTTP 监听归属正确，正常重启由实际 boot 变化与 QMP 正常关机证据证明。该最小控制不重复 WPS、首台主机或完整系统生命周期验收。

本地验证命令：`python -m unittest discover -s qa/acceptance/windows-timeout-repair-20261018 -p test_controller.py -v`。测试只模拟动作、不操作 Guest，正例读取现有正式起点回执与原备份；负例覆盖回执篡改、哈希不符、起点链接错误、旧包误用、重复安装/重启 attempt、候选替换和未显式授权重启。最后验证日期：2026-10-18。新包 preflight 与 Guest 实测尚未执行。
