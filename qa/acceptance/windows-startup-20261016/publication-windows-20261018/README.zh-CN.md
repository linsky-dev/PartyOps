# Windows 20261018 新候选发布准备

最后本地验证：2026-10-18。此目录独立于永久 hold 的 20261017 批次。新正式候选为 `e3e2ea45ccd38acff059b5713826a78f998a56ccef75341633f67bd4e17e57da`；后续发布仍须全部真实专项证据齐备，不因控制器准入或部分验收通过而放行。本轮适配开发未读取凭据、连接 Guest、上传、部署或提交。

当前正式构建及完整门禁已完成；新包唯一覆盖、正常关机后的单次开机恢复、原账号/事项/附件/日历保留均已通过。新包完整 WPS 实测 `win10-formatter-7de930093bcc/summary.json` 已通过六项入口、9 个功能用例、五字体及三页金样，无历史 resume。独立新轮次的真实主机配置、恢复个人和最终保留均已结案；保留原失败提交及只读观察结案，`http_terminal_observed=false`不改写。签名及bundle已完成，公开部署与上传由主控进行；不声明整体全部通过。

最初主机提交已失败，原 attempt/result 保留。发布新增独立成功轮次选择：只有 `host-journal-recovery-verified.json` 实证同事务 rollback、清除 journal/pending 及个人业务保留后，才允许 `host-after-journal.py` 发出新请求。新回执以 `after-journal-` 为前缀；发布同时绑定原失败、受控回收的未知中断、只读诊断、独立 resume-launch 和恢复验证，不覆盖或伪造旧结果。恢复尚未确认、成功新轮次或最终个人保留缺失时均不发布。

本轮用本地 Python/文件工具处理 QA 文本文件；没有适用的专用 MCP 编辑接口。改动仅限本目录，回滚为停用本目录；不修改旧回执或解除旧 hold。

## 可恢复检查点

`release_driver.py` 实现只读正式构建/当前完整门禁绑定、新候选覆盖/正常重启/保留/主机配置/恢复回执链、完整实际 WPS 验证、原九包签名和 acceptance 保留、签名输入及本地 receiver bundle。

`formatter_validation.py` 复制成熟严格验证器，只将固定旧 build receipt 改为显式输入。要求新候选完整实际 9 feature、金样、五字体、原生宿主输出及下载字节绑定；本轮拒绝旧候选及历史 resume。`installed_uac_timeout_live_tested` 始终明确为 false，不能把单测写成安装版真实超时实测。

`github_windows_assets.py` 复用成熟的四附件追加/读取恢复实现，运行前重新验证新候选证据和签名。无 clobber、无旧附件删除、无正文/main/tag 更新；单文件独占 intent，未知写入结果只读取恢复。匿名完整 GET 核所有附件；保留原 27 附件。所有网络动作由主控后续执行。

按用户只保留最终摘要的要求，已删除废弃调研快照、转换回执及转换脚本，释放 3,170,576 字节。保留小型 research-status，明确该调研子集未覆盖完整 PYZ、CArchive 入口和运行钩子，不获准用于等价复用；新候选必须完整实际排版验收。正式驱动所需 `frozen_domain.py` 与其他实际证据保留；发布逻辑无快照运行时依赖。

## 离线命令

在 `C:/PartyOps-VM-Lab/windows-startup-repair-20261016` 执行；不使用 E 盘。以下变量必须替换成实际新构建值，未知时不要运行 prepare。

```powershell
$pub = 'qa/acceptance/windows-startup-20261016/publication-windows-20261018'
backend/.venv/Scripts/python.exe -X utf8 -B -m unittest discover -s $pub -p 'test_*.py' -v
backend/.venv/Scripts/python.exe -X utf8 -B "$pub/release_driver.py" prepare --package $actualPackage --sha256 $actualSha --build-receipt $actualFormalReceipt --wps-summary $actualNewFullFormatterSummary
backend/.venv/Scripts/python.exe -X utf8 -B "$pub/release_driver.py" verify
```

prepare 的新候选必须来自本工作区 artifacts，正式 receipt 固定为 `D:/PartyOps-VM-Lab/state/builds/<实际SHA>.json`，成功构建前后源码指纹、当前 `scripts/test.ps1` 完整门禁及当前源码必须相等。新 Guest 证据固定来自 `qa/acceptance/windows-timeout-repair-20261018`，不接受外部验收覆盖。WPS summary 固定来自原 QA 控制器的新随机 run 目录，candidate 必须完全一致，且不得有 resume_sources。

## 本次中断重启的真实恢复证据

原正常关机后，启动前磁盘预留门槛阻断；原 attempt 保留，QMP 关机事件未持久化，不补造 `normal-reboot-completed.json`。本目录发布验证现显式二选一：标准路径仍保留原 QMP completion、实际 restart、boot 变化及阶段摘要全部门禁；恢复路径选择 `reboot-recovery-completed.json`、`recover-start.attempt.json`、`recover-start.result.json` 和该 completion 绑定的实际事件文件。

事件文件必须先满足 basename 正则 `reboot-recovery-events-[a-f0-9]{12}.json` 且解析后仍在固定 QA 目录，才允许读取。所有新文件 candidate 都必须等于新正式 binding；共享 `reboot_evidence.validate_retention` 再核原正常 attempt、恢复 start/事件摘要、实际 Windows 系统事件、实际 boot/probe boot，以及 `retention-after-reboot.json` 的原基线/备份和安装链。事件 wrapper 还核采集来源、候选及采集运行时的三哈希、UUID、用途、boot/probe；允许后续合法运行进程 PID 改变，不据此改写采集历史。

正常/恢复 completion 同时存在或都缺失均阻断。恢复路径明确保留 `qmp_shutdown_event_persisted=false`；以 Windows UTC 事件时序及本次异常 41/6008 拒绝证明恢复，不把跨 provider 的 RecordID 当成时间全序。实际保留报告的 `reboot_evidence` 必须链接所选真实 completion；缺少报告、摘要改变或混用旧候选不能发布。

helper 载入只在验证期间临时绑定 QA 的 `sys.path`、`controller`、`reboot_evidence`；正常退出和异常退出都恢复原状态，避免影响其他发布模块。该适配仅离线消费主控真实证据，不启动、关机、安装或访问 Guest。

prepare 全部检查通过后独占生成 `candidate-inputs.json`、`acceptance-evidence.json`、`wps-actual-acceptance.json` 与 `manifest-signing-input.json`；不填假候选、不覆盖旧 attempt。主控签名后：

```powershell
backend/.venv/Scripts/python.exe -X utf8 -B "$pub/release_driver.py" bundle --signed-manifest $actualSignedManifest
```

新公开名固定为 `PartyOps_1.4.5-rc.6_windows_amd64_startup-20261018.exe`；新 manifest 固定为 `release-manifest-windows-startup-20261018.json`。原十包基线仍为 `b52807452932502a5f05a10eb609c87345fabe4addb17d3ffeae3a673e0d0e57`，原十文件先全部保留，仅追加新 Windows 文件。旧 Windows 删除是后续独立阶段。

## 后续主控阶段与剩余输入

1. 正式完整门禁及新候选构建已完成，数据库门槛已不再是当前阻塞；保留当时失败与恢复证据。
2. 新候选覆盖、开机恢复、原业务保留、完整实际 WPS、独立主机配置、恢复个人及最终数据保留均已完成，不重复安装、切换或整轮WPS。
3. prepare、签名新manifest及bundle已完成。后续每次重新核对应证据，缺失或变更均阻断；准确上传/只读恢复/GitHub命令见 `UPLOAD_STEPS.zh-CN.md`。
4. 主控复用成熟官方 WorkBuddy 客户端，先当前后台预检并建立本批次独占部署 intent，再一次部署本目录 receiver，设置仅新文件令牌并记录准确 binding 的部署 receipt。本轮不另建上传协议，不调用旧硬编码麒麟/Windows hold 批次写入口。
5. 主控用成熟上传/读取客户端将新文件上传一次并全 HTTPS 回读。须保存本批次 `batch/windows-domestic-readback.json`：binding 与 `batch/binding.json` 完全一致；result 含新 filename/bytes/sha256、finalized=true、public_readback_verified=true、固定 HTTPS 下载 URL；other_nine_preserved/old_windows_preserved/formal_release_ready=true，并有 completed_at。未知写入结果先只读恢复，不重发。
6. 本目录 `github-current-before.json` 保留实际 release/tag/main/27附件快照；用户指南 `docs-draft/docs/windows-installation-20261018.zh-CN.md` 已更新最终验收状态并保留限定。四附件为 exe、exe.sha256、新 manifest、新指南。主控执行附件驱动 prepare；明确授权网络动作时 append --execute-network；recover只读取并完整下载核SHA。仓库 `linsky-dev/PartyOps`、release id `402021905`、tag `v1.4.5-rc.6`/commit `a8aaf44984c1d9ce85981bd4756e71372c6b8cb8`，main仅快照 `b58cd0505ebd8bc187d40857e92d19c88b2a2b56`。
7. 官网预览与生产切换、正文/main 更新由主控另做；此工具不处理。

receiver bundle 不生成或读取上传令牌；正式官方部署/上传连接为后续主控阶段。候选形成不代表这些发布阶段已执行。原十包不改，回滚继续使用原十包路由；新附件保留追加态，不自动删除。

## 验证范围

既有定向测试覆盖未知/旧SHA/hold/缺输入阻断、不可覆盖attempt、原九包签名及验收保留、新候选实际WPS、标准/恢复重启证据、阶段摘要、临时模块恢复及GitHub追加/未知恢复。当前新上传薄适配新增2项局部测试通过，本地真实batch验证通过；未因此重复已完成的全套或Guest测试。主机/个人及原数据保留均已真实结案，公开网络发布仍以主控实际回执为准。

2026-10-18 此前使用原 `.gitleaks.toml` 对本目录独立 `gitleaks dir --redact --no-banner --config .gitleaks.toml --exit-code 1` 扫描通过，报告 `gitleaks-directory-redacted.json`；没有例外参数。该报告保留其当时文件范围，不冒充覆盖后续增量；最新发布目录单测为上述 23 项通过。

另外用复制后的严格验证器对既有成功 summary `win10-formatter-595d684e3890/summary.json` 做只读正对照，通过 17 个输出及 resume 原始九功能/金样重新核验；summary 摘要 `73a6874d83c078ff5ea3c24c2ba99d903e6dad8a3ac3b2b2290c40d5d073d610`。该对照没有经过新候选入口，不作为新包验收；新候选入口仍拒绝旧 SHA 和 resume。

本次恢复适配增量的 `test_release_driver.py` 共 15 项通过，覆盖标准门禁保留、恢复共享验证调用、completion 二选一、恶意事件路径拒绝、临时模块及路径异常恢复；关联恢复专项 12 项通过，含真实延迟落盘事件、事件 wrapper 反例及摘要关联。不以这些单测替代 Guest 实际证据。

恢复适配后的发布目录回归为 23 项通过，相关 Python 语法检查通过。另经临时 QA helper 对主控实际 `reboot-recovery-completed.json`、其绑定事件 wrapper 和本机 VM 登记进行离线正对照通过，确认共享恢复 validator 及临时模块恢复可消费现有真实回执。

主控生成实际 `retention-after-reboot.json` 后，再通过发布 `reboot_chain` 离线正对照：候选为上述 `e3e2…57da`，选择 `reboot-recovery-completed.json`、`recover-start.attempt.json`、`recover-start.result.json`、`reboot-recovery-events-81f0300cba0e.json`，共享 `validate_retention` 核验通过；退出时路径及两个临时模块绑定恢复。此过程未连接 Guest、未重跑已有测试，只消费主控实际报告。它不代表尚待齐备的主机配置、个人恢复及完整发布验收已通过。
