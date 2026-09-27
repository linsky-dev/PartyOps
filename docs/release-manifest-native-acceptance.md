# 本地完整活动矩阵验收与发布清单

最后核查：2026-09-08，Asia/Hong_Kong。

`scripts/generate-release-bundle-manifest.py` 的 schema 5 接口只接受控制器生成的最终报告与本地实验室。它不接受手工声明已验证平台，也不要求 GitHub Actions 运行编号。rc.4 已公开的清单和下载不随本工具改动。

本轮最终活动目录为 **10 个安装包、14 个必需环境**。目录在发布工具源码的 `RELEASE_PACKAGES`、`TARGET_REQUIREMENTS`、`REQUIRED_CASES` 中明确登记，参与产品源码冻结和完整质量门禁；不会从用户报告或可删改 YAML 的数量推导较小范围。新增或取消必需平台必须按正式源码变更审查、重跑质量门禁并重新绑定受影响包。

| 安装包 ID | 必需环境 |
| --- | --- |
| windows_amd64 | win11-x64-native、win10-x64、win11-arm64 |
| windows7_amd64 | win7-x64 |
| windows7_x86 | win7-x86、win10-x86、win10-arm64 |
| linux_amd64 / linux_arm64 | uos-deb-x64 / uos-deb-arm64（分别对应） |
| linux_loong64 | deepin-deb-loong64 |
| rpm_x86_64 / rpm_aarch64 | openeuler-iso-x64 / openeuler-iso-arm64（分别对应） |
| macos_x86_64 / macos_arm64 | macos-x64 / macos-arm64（分别对应） |

Windows 新兼容环境复用现有包，不新增虚构 Windows ARM64 原生包。Win10 ARM64 的 OS 为 ARM64、PartyOps 为 x86 且经 Windows x86 仿真；Win11 ARM64 的 OS 为 ARM64、PartyOps 为 x64 且经 Windows x64 仿真。原生系统缺失、TPM/UEFI 能力不足、旧 x86 包未捆绑 AI 或 Win7 x64 包未捆绑 LLM 都保留真实阻断，不能删除目标或必需场景后声称完整通过。

## 发布前顺序

1. 集中应用产品与发布工具修复，将产品源码冻结为提交。`scripts` 和 `backend/tests` 都在产品源码指纹范围内；发布工具变更也必须触发完整质量门禁和受影响制品重建。
2. 本地运行 `scripts/test.ps1`，确认完整质量门禁通过。记录 `.release-gates/full-function-tests.json`；如需提交该记录，记录文件本身不进入产品指纹。
3. 从冻结提交通过实验室 `record-build.py` 重建完整目录的最终安装包，逐包检查包内版本、架构与源码提交。保留控制器构建回执及非空构建日志。
4. 在矩阵规定的全部原版环境中完成十三项必需生命周期场景。升级基线缺失、缺包、环境能力不足或任何证据缺失必须保持阻断。
5. 运行实验室 `test-all` 重新读取原始结果与构建回执，确认本轮 `passed_packages=10` 且 `all_packages_runtime_gate=passed`。还须逐项满足 14 个必需目标，包数量相同不能替代完整目标集。不要编辑汇总 JSON 来制造通过。
6. 将完整目录的包及已确认需要公开的 SHA-256、SBOM、VEX、脱敏验收附件放进独立的冻结目录。目录不能包含私钥、调试输出或旧包。
7. 用下面的接口生成签名清单；成功后仍需执行平台签名/公证检查、受控下载上传与完整公网回读，以及 GitHub/官网发布流程。

## 调用接口

以下 PowerShell 脚本接收现有正式私钥的位置和冻结目录，不展示或复制私钥。`ReleaseDirectory` 应为独立发布暂存目录，不能直接使用混有构建工具和临时文件的项目 `artifacts` 根目录。

```powershell
param(
    [Parameter(Mandatory=$true)][string]$ReleaseDirectory,
    [Parameter(Mandatory=$true)][string]$PrivateKeyPath
)
$repo = 'E:\codex\PartyOps\.publish-github'
$lab = 'D:\PartyOps-VM-Lab'
$report = Get-Content -LiteralPath (Join-Path $lab 'state/latest-report.json') -Raw | ConvertFrom-Json
if ($report.passed_packages -ne $report.required_packages -or $report.all_packages_runtime_gate -ne 'passed') {
    throw '完整活动矩阵生命周期未通过，禁止生成可发布清单。'
}
# 此预检查只用于提前报错；生成器仍独立核对冻结目录，较小的自报分母无法通过。
$finalReport = Join-Path $report.report_path 'qa-report.json'
& (Join-Path $repo 'backend/.venv/Scripts/python.exe') `
    (Join-Path $repo 'scripts/generate-release-bundle-manifest.py') `
    --root $ReleaseDirectory `
    --output (Join-Path $ReleaseDirectory 'PartyOps_1.4.5-rc.6_release-manifest.json') `
    --source-root $repo --lab-root $lab --qa-report $finalReport `
    --generated-at (Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz') `
    --private-key $PrivateKeyPath `
    --public-key (Join-Path $repo 'packaging/uos/update-public-key.txt')
if ($LASTEXITCODE -ne 0) { throw '发布清单生成失败，禁止上传。' }
```

## 实际核对的证据

- 完整质量门禁与当前产品指纹一致，产品源码在 Git 中无未提交或未跟踪变更。源码提交由当前 Git HEAD 读取。
- 最终报告与控制器配置必须和冻结目录的包 ID、每包必需目标、目标 OS/ISA/版本/后端及全部十三项场景一致，且 `local_only=true`。删除 Loong64、删掉共用同一安装包的 Windows 兼容目标、换成相同数量的历史目标或删除模型场景均拒绝。
- 每个冻结安装包的版本、ID、字节数、SHA-256 与报告清单一致；重新读取构建回执、源码前后指纹和构建日志，拒绝修改或未完成回执。
- 重新调用实验室现有 `check_target`，核验发行版、版本、架构、介质哈希、Guest UUID、基础快照、十三项场景原始文件及真正系统重启；不能仅凭汇总表的 `passed` 标志通过。
- Loong64 同时复核固定 UEFI 配置和独立变量盘快照；Windows 新目标重新读取有哈希的进程证据，核对 OS ISA、实际 PartyOps PE/进程 ISA、普通用户 SID、安装版 manifest 和仿真层，不接受仿真程序自报为 ARM64 原生。
- 拒绝 hosted runner；由实际证据派生物理、硬件虚拟化、全系统模拟与实验环境分类。任何虚拟或模拟结果都不会被标成物理真机通过。
- 清单生成期间报告、产品源码或安装包变化时拒绝继续。

`acceptance` 保存固定目录的 `matrix_contract_sha256`、包/环境数、全部场景、最终报告、逐环境原始结果、构建回执和构建日志的摘要；每个环境分别记录包 ID、OS ISA、包 ISA 和应用执行方式。它不复制本地绝对路径、命令行或密钥内容到公开清单。

## 接口迁移

无旧接口回退，直接替换。删除 `--source-commit`、`--tooling-commit`、三个 `--macos-*` 以及三个 `--*-verified-platform` 手工追认参数，改传 `--source-root`、`--lab-root`、`--qa-report`。项目中现有生产脚本未发现调用旧生成器；对应单测随补丁更新。

schema 5 新增 `source_fingerprint`、`acceptance` 和 `virtualized_verified_platforms`；保留安装包清单、资产签名、`verified_platforms` 及模拟/物理平台列表。`native_machine_validation` 仅当完整目录所有安装包的全部必需环境均为 `native-host` 时为真。清单 Ed25519 签名不是 Windows/macOS 平台代码签名，也不能证明 Apple 公证完成。

## 失败与回滚

任何门禁失败时不得上传或更新挂载下载；保留当前线上版本。回滚本次工具代码应通过代码审查后的正常修改，并重跑质量门禁，不能回退到允许手填通过状态的旧生成器生成发布清单。当前实验室 0/10 时，本工具预期明确拒绝；历史 9/9 汇总也不能用于新目录。
