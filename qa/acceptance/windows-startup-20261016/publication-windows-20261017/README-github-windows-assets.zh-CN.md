# Windows GitHub 四附件驱动

驱动为 `github_windows_assets.py`，只追加四附件：本批新 exe、其 `.sha256`、已签名 Windows manifest、Windows 安装说明。仓库固定 `linsky-dev/PartyOps`，release 固定 `v1.4.5-rc.6` / `402021905`，tag commit 固定 `a8aaf44984c1d9ce85981bd4756e71372c6b8cb8`。使用 `C:/Program Files/GitHub CLI/gh.exe` 的现有正常认证，不读取或输出凭据。

在 `C:/PartyOps-VM-Lab/windows-startup-repair-20261016` 运行：

```powershell
backend/.venv/Scripts/python.exe -X utf8 -B qa/acceptance/windows-startup-20261016/publication-windows-20261017/github_windows_assets.py prepare
backend/.venv/Scripts/python.exe -X utf8 -B qa/acceptance/windows-startup-20261016/publication-windows-20261017/test_github_windows_assets.py
```

`prepare` 为默认动作，只核本地文件并独占保存 `github-windows-assets-expected.json`。同一计划再次准备只接受逐字段相同的绑定；文件变动不会自动覆盖旧计划。准备不要求国内回执已出现，不调用 gh 或网络。已实际执行本地准备，结果为 `local-prepared`。

下面两个入口供主控后续执行，本次准备未执行：

```powershell
backend/.venv/Scripts/python.exe -X utf8 -B qa/acceptance/windows-startup-20261016/publication-windows-20261017/github_windows_assets.py append --execute-network
backend/.venv/Scripts/python.exe -X utf8 -B qa/acceptance/windows-startup-20261016/publication-windows-20261017/github_windows_assets.py recover
```

`append` 首先要求本批 `batch/windows-domestic-readback.json` 的真实完整回读、文件大小/SHA、manifest SHA及候选 binding 一致。该原生回执的通过事实为 `result.finalized=true`、`result.public_readback_verified=true`，并保留原九包及旧 Windows。只有这些事实成立，才会创建本驱动自己的独占 intent 并调用 gh 上传。

四附件逐文件一次调用，不使用 `--clobber`。同名附件只有大小、上传终态和 SHA 全部相同才可视为已存在，否则阻断。上传超时或失败之后只重新读取 release/tag/附件，找不到已完成结果则标记未知并停止。同一文件已有 intent 但远端未确认时，不允许重发；不能删除 intent 绕过。主控可执行 `recover` 获取只读回执，再决定未开始的后续文件是否继续。429 退避20秒，5xx/超时读取退避2秒且最多重试一次；上传写入本身不自动重试。

`recover` 仅查询远端和匿名完整下载已存在的新附件，全部四份都完整核 SHA后才记录 `status=passed`。exe 是449978846字节完整 GET，不以HEAD、GitHub元数据摘要或部分Range替代。缺文件时只记录 `partial`，不会上传。每次只读观察用不同文件名保存，不覆盖历史。记录目录为 `github-windows-assets-state/`。

每次远端观察均核对固定 release/tag 及快照中的原27附件，匿名回读结束后再次核对附件身份。先追加阶段不删除任何旧附件，包括旧Windows。旧Windows撤下必须由主控完成独立验收后另行执行。本脚本没有删除、正文编辑、main提交、tag修改或部署入口。main的 `b58cd0505ebd8bc187d40857e92d19c88b2a2b56` 仅绑定2026-10-18本地快照；main和发布正文由主控单独处理。

本地输入证据：`github-current-before-20261018.json`、`batch/binding.json`、四份实际附件以及运行时要求的国内回读。manifest 固定 SHA 为 `41a126b135097255e224192ca12fe6be479d077d3842f122e67b34b5c3657d1c`，exe固定 SHA 为 `3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27`。本轮仅本地准备与离线测试，不形成远端发布通过声明。
