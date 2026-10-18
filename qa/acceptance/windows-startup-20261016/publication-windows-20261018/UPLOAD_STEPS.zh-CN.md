# 主控发布执行步骤

以下命令均从 `C:/PartyOps-VM-Lab/windows-startup-repair-20261016` 执行。本次实现仅本地核对，没有执行上传、定稿POST或GitHub写入。

```powershell
$py = 'C:/PartyOps-VM-Lab/windows-startup-repair-20261016/backend/.venv/Scripts/python.exe'
$pub = 'C:/PartyOps-VM-Lab/windows-startup-repair-20261016/qa/acceptance/windows-startup-20261016/publication-windows-20261018'
& $py -X utf8 -B "$pub/domestic-release.py" verify
```

新 receiver 的四文件为 `_serve.js`、`index.html`、`upload-metadata.json`、`.upload-token`。最后一文件必须为只有新 Windows 文件名键的JSON对象，其值为原上传令牌的64位小写hex字符串。旧token文件原字节复制后即使仅一项，也不兼容新文件名；主控须在内存保留原唯一值、仅映射为本轮新文件名，不能输出值。实现者已只读报告该不兼容，未改令牌。主控通过官方WorkBuddy部署四文件；当前未知部署不得自动重发。

主控保存真实 `batch/deployment-receipt.json` 后再上传。必需字段：`status="received"`、`binding`与`batch/binding.json`完全相同、`base_url="https://partyops-rc6-win7-x86.app.workbuddy.host"`、`current_backend_preflight_verified=true`。不以旧intent或未知结果替代。

```powershell
& $py -X utf8 -B "$pub/domestic-release.py" upload --execute-network
```

复用旧 `upload_one` 的分块、单次finalize和匿名完整GET核SHA；原十文件需保持ready/reused且摘要/大小原值不变。新上传独占 `windows-upload.intent.json`，不删intent重发。若操作中断，仅执行：

```powershell
& $py -X utf8 -B "$pub/domestic-release.py" recover --execute-read-only
```

若远端实际已完成但 `windows-domestic-readback.json` 尚缺，可执行下列只读定稿。它只GET、全量核SHA并保存真实国内回执，不重发finalize POST。未ready则保持未知并停止。

```powershell
& $py -X utf8 -B "$pub/domestic-release.py" finalize --execute-read-only
```

GitHub直接复用本目录既有 `github_windows_assets.py`，不另建追加协议。已有 `github-current-before.json` 为固定release/tag/main/27附件快照；正式append会重新观察远端并核原27附件保持不变。文件发生变化必须阻断，不覆盖旧计划。

```powershell
& $py -X utf8 -B "$pub/github_windows_assets.py" prepare
& $py -X utf8 -B "$pub/github_windows_assets.py" append --execute-network
& $py -X utf8 -B "$pub/github_windows_assets.py" recover
```

append必须先有本轮真实国内完整回读，逐一追加exe、sidecar、签名manifest和最终安装说明；无clobber、旧附件删除或正文/main/tag修改。每个文件独占intent，未知结果只读recover，最后四附件均匿名完整GET匹配才通过。官网切换及正文/main由主控后续处理。

用户文档已在原有变更之上更新最终e3e包的文件名/字节/SHA、主机与个人恢复及原数据保留范围，并新增 `docs/windows-installation-20261018.zh-CN.md`；旧20261017说明保留。安装版真实UAC超时未实测、其他平台及完整生命周期的限定继续保留。未修改git index，主控负责审阅、提交和推送。
