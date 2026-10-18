最后验证日期：2026-10-18。

## 当前执行状态（优先于下方历史准备说明）

Win10 安装、覆盖、重启、原账号/事项/日历/附件保留、真实主机配置，以及 WPS 六项排版、五字体实际渲染、三页金样和 GUI 保存重开均已完成对应范围验收。最终 WPS 回执为 `../win10-formatter-595d684e3890/summary.json`；索引为 `acceptance-evidence.json`。不宣称完整生命周期或其他平台通过。

签名清单及 `batch` 已准备完成，`batch/binding.json` 中 `formal_release_ready=true`。这是本地发布准备就绪，不是公开上传完成。尚无 `batch/deployment-receipt.json` 或 `batch/windows-domestic-readback.json`；不能启动 GitHub 上传、切换官网或删除旧包。

当前阻塞仅为 WorkBuddy 窗口最小化且电脑工具恢复失败，已请求用户手工恢复窗口。恢复后先观察原发布任务，再按官方入口一次部署本批 receiver，真实回执绑定既有域名和 sandbox 后才执行 upload；结果未知只恢复读取，不重发部署。GovPulse 已恢复，无需再暂停或重装 Win10。

下方 2026-10-17 的待验收、未创建令牌和未签名说明为历史准备记录，保留追溯，不代表当前状态。

本目录只做本地发布准备。Windows候选SHA为 `3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27`，449978846字节。真实主机安装/UAC与WPS排版验收仍待主控完成；所有生成记录保持 `network_write_allowed=false`、`formal_release_ready=false`。未读取上传凭据、创建token或部署intent，未执行Guest、上传、部署、GitHub写入和删除。

本轮正式全套门禁已于 `2026-10-17T12:38:32+08:00` 通过，证据为当前C工作区 `.release-gates/full-function-tests.json`。其package源码指纹 `6f879341ef736ffd1ca41da9ff171976c5cb17985058fa1af206fa97eecdb702` 与正式build回执source_before/source_after相等，build回执同时绑定本轮3c81…安装包SHA。当前 `formal_gate=FULL_FUNCTION_GATE_PASSED`，不能继承历史麒麟批次的STALE例外。`formal_release_ready=false` 的当前原因仅是本轮Guest主机/WPS事实验收待完成；用户已经授权这些验收通过后发布，不需要新增人工审批。

在当前C工作区运行：

```powershell
& .\backend\.venv\Scripts\python.exe .\qa\acceptance\windows-startup-20261016\publication-windows-20261017\prepare.py preflight
& .\backend\.venv\Scripts\python.exe .\qa\acceptance\windows-startup-20261016\publication-windows-20261017\prepare.py prepare
& .\backend\.venv\Scripts\python.exe .\qa\acceptance\windows-startup-20261016\publication-windows-20261017\test_prepare.py
```

`preflight`只读核对正式build回执、载荷清单、安装/重启/原数据保留回执、历史发布签名和当前十包接收元信息。`prepare`独占创建 `prepared`，给原Windows包创建本地硬链接，生成SHA sidecar和 `preparation.json`。不会复制麒麟或其它平台二进制。已有目录会阻断重复准备；恢复时读取现有JSON和重新执行preflight，不删除或覆盖证据。

本轮基线为麒麟发布目录 **batch** 内实际发布的签名清单，SHA `b52807452932502a5f05a10eb609c87345fabe4addb17d3ffeae3a673e0d0e57`。它与官网public副本、post-switch接收器元信息绑定一致。该历史目录顶层同名清单SHA为95b2…，是旧预审稿，不能用作当前基线。脚本验证整体签名与全部十个asset签名，仅拟替换 `windows_amd64`，其它九个asset和acceptance对象原值保留。所有原签名清单只记录路径/摘要，原文件不改。

后台接口入口已记录在 `preparation.json.reusable_interfaces`：

- 国内上传复用历史 `publication.py.release_client()` 加载的 `cloudstudio-scoped-upload.py.upload_one`。现有官方WorkBuddy后台为 connector-proxy / `workbuddy_sites_deploy`；需要当前只读预检及新批次独占intent，旧intent不可重放。
- 新 `windows_receiver_factory.py` 复用已钉住原字节的接收协议，独立profile为 `windows-startup-20261017-v1`，封存前缀为 `.partyops-windows-startup-20261017-`，基线绑定当前麒麟发布后的十包清单。旧十文件启动时逐一全量核哈希，均只能GET/HEAD；唯一可POST的文件是本轮Windows新分发名。现有桥只接受固定八/九/十包profile，未声称兼容Windows新批次。
- GitHub复用 `github_publication.py` 的已有gh认证机制、匿名回读、附件核对和独占intent方式。旧入口固化麒麟payload及main，不直接执行。

待准备的事实材料为真实主机/UAC及WPS排版回执、独立Windows签名清单、当前后台/sandbox/域名绑定、GitHub当前release/tag/main及附件快照、完整官网候选与预览回执。已有用户发布授权持续有效。现有同名Windows资产禁止直接clobber：新分发名固定使用 `startup-20261017` 后缀，先追加并全量回读，再切换入口。最初 `prepared/` 保留原正式构建文件名，后续 `draft/` 使用新分发名；旧包保留回滚。

新增限域驱动为本目录 `publication.py`，不调用旧麒麟reserve/adopt/upload入口，不连接WorkBuddy，也不编辑官网或GitHub。其默认 `draft` 只独占创建本目录的 `draft/`：原包硬链接为 `PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe`、匹配新名字的SHA sidecar、blocked未签名输入和桥独立receiver。`draft` 不生成上传令牌；正式门禁标为PASSED，Guest证据未齐时发布执行仍blocked，网络写入不允许。未签名输入不能当作公开清单或部署输入。

```powershell
python -B .\qa\acceptance\windows-startup-20261016\publication-windows-20261017\publication.py draft
python -B .\qa\acceptance\windows-startup-20261016\publication-windows-20261017\test_publication.py
```

后续显式入口：`signing-input`（Guest事实证据未齐时仍只生成blocked输入）、`prepare-signed --manifest <主控签名文件> --evidence <验收证据索引>`、`upload --evidence <同索引> --execute-network`、`recover --evidence <同索引> --execute-read-only`。这些后续步骤本轮没有执行。签名只能由主控完成，本工具不读取私钥。`prepare-signed` 会核对受信任Ed25519整体及十个asset签名、原九包和acceptance原值，并只为本轮新文件生成本地随机令牌；上传必须先有本轮当前后台真实部署回执。未齐的事实前置不能被签名代替。

证据索引是普通事实汇总，不是授权文件或额外审批。默认 `acceptance-evidence.json` 必须只有四个不同文件的 `{path,sha256}` 引用：

- `host_acceptance`：`first-host-approved-existing-root-submit.result.json`，必须是 HTTP200、`bootstrap-page-observed`、Host ready且用户/系统host模式的真实终态，绑定候选和原attempt。
- `personal_restore`：`restore-personal-submit.result.json` 的 `restored-ready`，或独立 `restore-personal-observed-completion.json`。后者必须完整绑定已提交attempt、原失败回执、observed-prepared及Host成功证据，现场核个人模式/原目录/普通用户PID与监听/Guest动作时间/服务停止/无pending；明确 `http_terminal_observed=false`，不把未观察到的HTTP终态冒称通过，保留原失败。
- `retained_business`：`retention-after-host-restore.json`，必须是本次恢复后的独立业务保留通过回执，绑定所选恢复证据SHA、当前原个人实例和原baseline账号/事项/附件/日历摘要。旧host-rollback阶段不能替代。
- `wps_acceptance`：本次 `win10-formatter-<12位hex>/summary.json`，必须实际 `formatter_acceptance_passed=true`，具备九作业覆盖六入口、原生宿主与候选、五字体实际PDF字形、金样语义/视觉、图片解码及真实下载输出摘要证据，partial/blocked或通用passed不能替代。

索引拒绝任何外部 `windows_acceptance` 对象。Windows摘要只由上述绑定证据和当前正式门禁/build回执派生；目标固定Win10 x64，`full_lifecycle_verified=false`，不得扩展平台或冒称完整生命周期。真实证据齐备后沿用用户已有授权，`formal_release_ready` 才可转为true；其他九包保留历史验收范围。使用只读结案时，业务入口为 `verify-retained-business.py after-host-restore --restore-evidence observed-completion --package <正式包> --sha256 <正式SHA>`。

最初draft误继承的STALE状态已通过 `refresh-draft-status` 本地修正；改前五份控制文件原字节及SHA保存在 `formal-gate-correction-20261017/`，包链接不变，不覆盖历史正式清单。该修正不读取认证、不执行网络写入。

正式 `batch/deployment-receipt.json` 必须由主控保存本轮真实部署结果，要求 `status=received`、完整 `binding` 与本地 `batch/binding.json` 相同、`base_url` 为既有下载域名、`current_backend_preflight_verified=true`。旧WorkBuddy桥的历史intent、旧麒麟intent或未知部署结果都不能代替这份回执。当前5.8.2桥未适配，这条前置仍阻断。

`upload` 独占写入 `batch/windows-upload.intent.json` 后，仅调用已钉住客户端上传新Windows文件；每次health须证明旧十文件均实际ready/reused且SHA/大小原值不变。客户端继续使用原有429退避20秒、5xx/超时退避2秒和单次重试规则。结果未知不得重发upload，使用 `recover`：只GET health、必要时匿名完整GET新文件核SHA，不读取上传令牌、不POST、不部署、不自动切换官网。恢复观察不会把本批标为正式发布通过。

国内完整回读通过后，才进入独立GitHub追加/回读及官网完整候选预览流程；本驱动不执行这两阶段，也不删除旧Windows附件。receiver保留旧十文件加新Windows，共11文件；新活动签名清单仍为10包，仅Windows对象替换，其他9包原值保留。

回滚依据包括旧Windows SHA/大小/签名、原九包、历史签名清单、上一官网部署 `dpgtwf7htv3j`、历史GitHub main `b58cd0505ebd8bc187d40857e92d19c88b2a2b56`。这些是2026-10-16的记录；本轮未重新核查远端现状，不宣称旧二进制可直接恢复。实际网络切换前须更新只读快照。本轮没有修改官网源码、公开docs或正式发布清单。
