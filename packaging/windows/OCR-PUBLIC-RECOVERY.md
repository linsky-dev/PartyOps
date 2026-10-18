# Windows 现代 OCR 的公开成品恢复

最后验证：2026-10-16，Windows PowerShell 5 与 PowerShell 7。

## 来源与边界

原自制 `tesseract-5.5.3-windows-amd64.zip` 缺失，且未找到可证明相同 ZIP 字节的配方。本路径直接恢复公开成品中的 OCR 目录；不重新生成原 ZIP，不修改原 `SOURCE.json`，不执行旧 PartyOps 安装器，不访问业务数据或旧 E 盘。

输入安装包：[PartyOps 1.4.5-rc.6 Windows amd64](https://github.com/linsky-dev/PartyOps/releases/download/v1.4.5-rc.6/PartyOps_1.4.5-rc.6_windows_amd64.exe)。大小 443690896 字节，SHA-256 `a7c044b0b830ce416fdf02c84daa2e7aa4929c0833d9b530bc47486d861a81be`，主控已对 GitHub release 资产 digest 核验。

解包器来自维护者 [innoextract_win tag 670](https://github.com/UserUnknownFactor/innoextract_win/releases/tag/670)，为第三方维护分支，并非原 innoextract 1.9 官方发行版。下载 [innoextract670.zip](https://github.com/UserUnknownFactor/innoextract_win/releases/download/670/innoextract670.zip)，ZIP SHA-256 `79b69b9b1fcd98f42ccd4b245efdf6a03bcfb674ba6af482f5a46891c9ed4d14`，与 GitHub Release API 资产 digest 一致。`--help` 显示 `1.12-dev + e561d8c`，支持 Inno Setup 至 6.7.0；实际安装包 data version 为 6.7.0。原官方 innoextract 1.9 及 7-Zip 26 无法正确读取此载荷，未用它们的失败结果推断包已损坏。

脚本固定解包器 `innoextract.exe` SHA-256 `0dab51583e403970065db49f87a6a2bf683ab6ebfdf27bede2bf633dd232a1a2` 与 `libbz2-1.dll` SHA-256 `b79cd3a7102e359ef6324e98966a67939e58ec2b651ae2847cae84a4dfa453f7`。工具目录只能包含这两个普通文件。

## 本地执行

将上述下载放到当前工作区内。先校验解包器 ZIP SHA-256，再用 `Expand-Archive` 展开至独立目录。恢复命令（路径按当前工作区替换）：

```powershell
pwsh -NoProfile -File packaging/windows/recover-ocr-from-public-installer.ps1 `
  -RepoRoot C:\PartyOps-VM-Lab\windows-startup-repair-20261016 `
  -InstallerPath C:\PartyOps-VM-Lab\windows-startup-repair-20261016\.qa\windows-existing-public\PartyOps_1.4.5-rc.6_windows_amd64.exe `
  -ExtractorDirectory C:\PartyOps-VM-Lab\windows-startup-repair-20261016\.qa\ocr-recovery-tools\innoextract670
```

若本机执行策略拒绝运行，保留并明确报告策略错误，由主控处理；本命令不默认绕过执行策略。恢复脚本只运行解包器的 `--extract --include 'app\ocr'`，临时文件留在工作区 `.qa/ocr-public-recovery-*`。输出为 `vendor/windows/ocr/tesseract-5.5.3-windows-amd64-recovered`。已有且完整的恢复目录直接复核，不重复提取；已有目录损坏时拒绝覆盖，须先保存证据后另行处理。

固定清单 `ocr-public-recovery-manifest.json` 记录全部 62 个文件；其 SHA-256 `fa4ef50093a52c72932550a48c01cdea7fe44056d44bcc6460fa40cdffdbdff3` 固定在验证器中。引擎、全部依赖 DLL、许可证、SOURCE 与三模型均按字节校验，额外/缺失文件及重解析点均拒绝。

`prepare-ocr-runtime.ps1` 仅在现代原 ZIP 缺失时采用恢复目录。原 ZIP 存在则仍必须匹配 `57825338CEAA141C617F66D2A2210B6BEF396436FFC83D242595E5F5F33BF462`，坏 ZIP 不可通过恢复目录绕过。Win7 两种架构保持原路径与固定哈希。恢复目录复制后再核验清单，随后继续原有必需文件、禁用训练工具、版本来源、引擎启动及中英文模型加载检查。

## 验证、回滚与限制

已在本机真实运行恢复命令，并分别以 PowerShell 5 与 7 调用 `Expand-VerifiedPartyOpsOcrRuntime`，产物位于 `artifacts/ocr-recovery-validation-ps5/ocr` 与 `artifacts/ocr-recovery-validation/ocr`。独立测试 `python -m pytest backend/tests/test_windows_ocr_public_recovery.py -q` 共 7 项通过；现有 `test_packaging_wait_policy.py` 的 `windows_ocr_uses_locked_minimal_runtime_and_runs_during_freeze` 定点检查通过。集成测试需要先恢复上述目录，否则明确跳过；不伪造成功。

负向测试覆盖改写模型、缺模型、混入训练工具、篡改清单和存在坏 ZIP，均在触碰目标前拒绝。测试时发现 Python 子进程继承 PS7 的 `PSMODULEPATH` 会使 PS5 无法加载 `Get-FileHash`；测试启动 PS5 时清除这一继承值，让 PS5 使用自身默认模块路径。恢复验证器同时设置 `ErrorActionPreference = 'Stop'`，哈希命令缺失或读取错误立即失败，禁止因非终止错误继续封包。

回滚时撤销本次恢复脚本、清单、测试与 `prepare-ocr-runtime.ps1` 的缺失 ZIP 分支；保留恢复目录和公开安装包供审计，不影响业务数据。恢复路径保证来源与文件字节完整性，未声称复现原 ZIP 哈希，也不替代最终安装包/虚拟机整体验收。

前置说明：当前会话无适用于 Inno Setup 的专用 MCP 提取能力，采用安全本地命令与 `apply_patch`；操作范围仅上述 OCR 文件、当前工作区 `.qa` 与 `artifacts` 的恢复/验证产物。公开检索关键词为 `innoextract official windows download 1.9`、`innounp official 2026 Inno Setup 6.7 download sourceforge`；访问日期 2026-10-16，最终采用维护者 release 与资产 API digest、实际帮助及提取结果。
