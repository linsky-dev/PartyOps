# 本次正常关机后启动门槛中断恢复

日期：2026-10-18。适用于新候选 `e3e2ea45ccd38acff059b5713826a78f998a56ccef75341633f67bd4e17e57da` 已覆盖且 `verify-overlay` 通过、原 `normal-reboot-attempt.json` 已产生，但正常关机后的 `lab.start` 在启动前被 `DISK_HEADROOM` 阻断的本次情况。

前置证据 `reboot-start-interruption.json` 记录主控实际看到的 session 52973 traceback。原 QMP 正常关机返回事件仅在内存中，未持久化；不得补造 `normal-reboot-completed.json`。原 QEMU PID 2900 来自主控及 Architect 的当次实际工具观察，原历史文件没有封存该 PID，恢复 attempt 明确标记此来源。

## 操作顺序

工作目录 `C:/PartyOps-VM-Lab/windows-startup-repair-20261016`。以下命令均由主控执行，开发者未操作 Guest。

```powershell
$recovery = 'qa/acceptance/windows-timeout-repair-20261018/reboot-recovery.py'
$package = 'artifacts/PartyOps_1.4.5-rc.6_windows_amd64.exe'
$sha = 'e3e2ea45ccd38acff059b5713826a78f998a56ccef75341633f67bd4e17e57da'
python $recovery --package $package --sha256 $sha
python $recovery recover-start --package $package --sha256 $sha --old-qemu-pid 2900 --execute
# 等原 Guest 和原产品自然启动后：
python $recovery observe --package $package --sha256 $sha --execute
python qa/acceptance/windows-timeout-repair-20261018/controller.py verify-reboot --package $package --sha256 $sha
```

如果主控已经执行过 recover-start，绝不能再次执行。recover-start 独占写 intent 后只调用一次 `lab.start`，不调用关机、安装、快照或数据变更。它要求原 UUID/盘、stopped/pid=null、安装介质已拆除、PID 2900 已不存在、QMP 不通且未找到同 UUID/盘的 QEMU。磁盘容量及内存门槛沿用原 `QemuLab.start`，不降低预留。

只读 observe 可在 Guest 尚未就绪、SSH 或产品尚未启动时失败后重做，绝不启动产品、不再开机。它采集 System 日志的 provider、ID、RecordID、UTC，不保存事件 Message/Properties、账户或正文。每次取到的事件集合独占保存在 `reboot-recovery-events-*.json`；验证成功才保存独立 `reboot-recovery-completed.json`。

## 证据判据及消费接口

恢复证明要求原 overlay 运行进程创建后存在 Kernel-General 13 与 EventLog 6006 正常停止事件、实际新 boot 对应唯一 Kernel-General 12、随后 EventLog 6005，核对 UTC 先后、唯一 RecordID 和正确 provider，并拒绝该区间的 Kernel-Power 41、EventLog 6008 和额外启动/关机。Kernel provider 可能延迟落盘，因此不要求跨 provider 的 RecordID 随事件 UTC 递增。User32 1074 若不存在就明确记录未观察到，不虚构 ACPI 所不产生的事件。

实际首轮只读采集保存在 `reboot-recovery-events-82c20dedd363.json`：Kernel 13/12 的 RecordID 2502/2503 大于 EventLog 6005 的 2499，但其 UTC 时序完整。首轮 `ORDERED_SYSTEM_LOG_START_REQUIRED` 失败保留；本次修正的是 QA 跨 provider 全序假设，原始文件不改写。该次 Get-WinEvent StartTime 查询仍返回了旧 boot 的事件，验证器对原始结果显式按 UTC 作用域筛选；04:00 的历史 41/6008 不属于 09:37 正常关机及 09:43 恢复开机，本次区间如出现同类异常仍拒绝。

`reboot-recovery-completed.json` 明确 `kind=windows-system-event-recovery`、`qmp_shutdown_event_persisted=false`，绑定原正常重启 attempt、单次恢复 start attempt/result、事件文件摘要、实际 boot/probe boot。它只证明事件恢复，不代替业务保留验证。

本目录 `reboot_evidence.py` 提供：

- `validate(binding, actual, state, validate_restart, output)`：正常路径仍验证原 QMP 标准证据；恢复路径验证上述独立真实证据，两个 completion 同时存在即阻断。返回 `{kind, file, sha256, normal_attempt_sha256}`。
- `validate_retention(binding, report, state, validate_restart, output)`：再核 `retention-after-reboot` 的新包、原基线/备份、安装完成摘要及 `reboot_evidence` 链接。

`controller.py verify-reboot` 已仅替换证据验证调用，原运行身份及账号/事项/附件/日历检查继续执行；新报告带 `guest_uuid`、`disk`、`reboot_evidence`。已有 `retention-after-overlay.json` 不改写、不补顶层字段，身份仍由原 boot/attempt/runtime 链提供。`host-personal.py` 已调用同一保留报告验证适配。

发布控制器由主控协调修改消费点，不在本执行者文件范围；应调用 `validate_retention`，不能强制要求缺失的原 QMP completion，更不能将恢复事件改写成 QMP 事件。旧基线、原 attempt、产品、旧 QA、数据库均不修改。回滚本次代码可撤销新适配及本目录消费改动；已执行 attempt/证据必须保留，不能撤销来重发动作。

离线测试：`python -m unittest discover -s qa/acceptance/windows-timeout-repair-20261018 -p test_reboot_recovery.py -v`。本执行者不宣称 Guest 事件、恢复开机或业务保留已通过；以主控实际生成的回执为准。
