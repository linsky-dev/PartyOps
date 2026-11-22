# PartyOps 1.4.6-rc.1 Windows 10/11 x64 真机验收与打包接续

## 已完成、保留不动

旧版 WPS 排版源码继续保持 Git 原始字节；全平台原有的生产 Python 依赖锁也不擅自升级。当前 Windows x64 云端已验证 SQLite 3.53.4、官方 LibreOffice 26.2.7.3、.NET 4.8、62 个中文 OCR 运行时文件和 25 个本地 AI 文件。原有 WPS 核心逻辑不重写。

安全候选 PyMuPDF 2.1 已在原始 Windows x64 全后端通过 2,395 项测试（另 19 项跳过，3 子测试通过），并通过原覆盖率门槛和依赖差异审计。**候选并非生产安全升级。**

## 为什么不能简单把 PyMuPDF 2.1 写入全平台 `backend/uv.lock`

官方 2.1 二进制发行包只包含 Windows x64、Linux x64/ARM64、macOS ARM64 预编译包；没有 Windows ARM64、macOS Intel、龙芯相应完整版 wheel。Windows 7 的 Python 3.8 不能使用 2.1 的 cp311-abi3 wheel。因此必须先找到对应平台经过验证的替代实现或安全构建路线，避免使原本正常的其他系统无法安装。脚本 `scripts/rc1_pymupdf_21_wheel_matrix.py` 使用真实隔离 uv 锁，显式区分“官方 wheel 存在”和“目标平台真实运行通过”；**不把源码包等同于已验收的预编译包**。

## 真机原版 WPS 验收入口

在**专用 Windows 10/11 x64 QA 机器或虚拟机**、用户真实交互桌面、已安装 WPS 的相同 Windows 用户会话内操作。请不要在有真实业务数据的生产 PartyOps 安装目录直接试验。

使用独立干净检出的 `feature/1.4.6-rc1-cloud-rebuild`，并事先按照已通过的 `cloud-windows-x64-runtime-input-closure.yml` 流程还原所有经签名/哈希锁定的打包依赖。保证 Python 3.12 后端 `.venv`、Node.js/Corepack、有效的原始上游排版源码均齐全。

PowerShell 7 内运行：

```powershell
$sha = (git rev-parse HEAD).Trim()
pwsh -NoProfile -File scripts/rc1-windows-native-wps-gate.ps1 -Mode Preflight -ExpectedSourceSha $sha
```

预检可能因生产 PyMuPDF 仍为旧版而返回退出码 2，**这属于预期的阻断，不是 WPS 成功**。只有环境完整，且获得专用测试机操作许可时，才可运行原始真实 WPS 与全功能测试：

```powershell
pwsh -NoProfile -File scripts/rc1-windows-native-wps-gate.ps1 -Mode NativeTests -ExpectedSourceSha $sha -ConfirmDedicatedTestMachine -ConfirmInteractiveWpsAcceptance
```

只有在安全依赖已通过正式生产范围验证、`scripts/test.ps1` 全量测试与当前源码指纹一致，且希望生成**本机候选**而非对外发布时：

```powershell
pwsh -NoProfile -File scripts/rc1-windows-native-wps-gate.ps1 -Mode BuildCandidate -ExpectedSourceSha $sha -ConfirmDedicatedTestMachine -ConfirmInteractiveWpsAcceptance
```

两个模式均保留原始 WPS 金样与六功能测试；但 **NativeTests 不验证签名、安装包安全及完整功能门禁**。只有 BuildCandidate 才会在全部安全和全功能门禁通过后调用原 `packaging/windows/build-windows.ps1`。任何失败直接停止并留存相应测试证据；没有运行就不得登记通过。

候选 EXE 即使生成也必须继续执行 Windows 10、Windows 11 两套原生安装、重启、权限、已有安装升级与回滚、备份恢复、哈希、数字签名和人工发布审批。没有这些证据，不得合并、打标签、上官网或称作正式版。

## 原生环境与本轮 CI 边界（2026-11-21）

- [`aaf01d8b` 的原生门禁 #44917466139](https://github.com/linsky-dev/PartyOps/actions/runs/44917466139) 中，标准 `windows-2025` 通过 **拒绝伪装 Windows 10/11 用户端** 测试；真正 `windows-11-arm` 通过 **拒绝把 ARM64 当作 x64** 测试。两个 job 的 PASS 均是**阻断机制正确**，而非真实 WPS 功能 PASS。
- [Windows 全量源码 CI #44917466125](https://github.com/linsky-dev/PartyOps/actions/runs/44917466125) **2,414 passed / 17 skipped**；[隔离 PyMuPDF 2.1 全回归 #44917466041](https://github.com/linsky-dev/PartyOps/actions/runs/44917466041) **2,412 passed / 19 skipped**，且两者均通过原 95% 行、92% 分支覆盖门槛。这些证据不代替真实 WPS 交互操作。
- PR 早期确有 `EastAsianPunctuationFontService.cs` 新文件、Windows/Linux 封装构建与源码暂存调整；本轮未改动这些文件。执行真实验收前须以冻结 SHA 确认原始上游快照及本机 WPS 版本，并验证六功能金样；不得仅依据 Windows Server 构建通过声明排版功能通过。
- 生产 PyMuPDF 尚未取得完整跨架构安全版本方案，因此 `BuildCandidate` 的阻断必须保留。此文中的可执行命令只适用于获得授权的专用 QA 测试机。

## 仅验证 Windows 安装包范围，不阻塞于独立官网源码（2026-11-22）

- 原 `scripts/test.ps1` 默认 `-Scope Full` **保持不变**，仍运行原有排版、后端、前端、官网及安全完整质量门禁，记录 `full/package/website` 三范围指纹。
- 专用 Windows 10/11 x64 的 `BuildCandidate` 入口调用原测试脚本的 `-Scope Package`，仅跳过**不随 PartyOps 安装包分发的官网**审计、测试和构建；原始 WPS、后端、前端、依赖安全审查和包级证据仍全部执行。`NativeTests` 现已改为专用 `-Scope WpsOnly`，只保留真正的 WPS 验收，不声称通过完整打包门禁；详见下节。
- 该模式的 `.release-gates/full-function-tests.json` 只保存 `package` 指纹，并显式标注 `record_scope=package`；`verify --scope website/full` 必须拒绝。待最新版 EdgeOne 官网源码恢复并完成官网测试后，仍需运行原 `scripts/test.ps1 -Scope Full` 获取独立完整证据，且需双站真实预览与人工发布批准。
- **仅为测试链路解耦，不解除 PDF 生产锁安全阻塞、不代替真实 Windows 10/11 WPS 用户桌面，更不授权打包或正式发布。** 当前云端普通 Windows Server Runner 不具备真实用户桌面和 WPS；不能把源码/合同测试误写成原生功能通过。

## Windows WPS 先行真机验证（2026-11-22）

**独立的 NativeTests 只测试真实 WPS，不签发安装包资格。** 之前 NativeTests 实际会调用 `test.ps1 -Scope Package`，其 `pip-audit` 因旧版生产 PyMuPDF 被阻断，妨碍先独立完成 WPS 金样。现在 `NativeTests` 显式调用原始 `scripts/test.ps1 -Scope WpsOnly`，只执行原先开头的 Windows x64/x86 排版源码构建、搬迁宿主自检、三页视觉金样及真实 WPS 六功能 12 场景。它不调用依赖安全审计、后端/前端、官网或生成全功能凭据的 `record`，也不会调用安装包构建器。实际执行成功才记录 `PASS_NATIVE_WPS_ONLY` 和 `native_wps_passed=true`；`native_full_function_passed`、签名、升级/回滚、`release_ready` 一律为 `false`。

此模式可先行在专用 Windows 10/11 x64 交互桌面使用：须安装 WPS，具备原始排版源码、后端 Python 测试环境及可信 LibreOffice 渲染环境；不用为 WPS-only 验证提前准备打包才需要的本地 AI、OCR、.NET、SQLite 与 Corepack。仍使用前述 `-Mode NativeTests` 命令及两个明确确认开关。

**BuildCandidate 严格不变：** 继续要求生产 PyMuPDF 安全升级，调用原 `scripts/test.ps1 -Scope Package` 完成排版、前后端及所有安全审查并核验包级凭据，最后才能调用原始 Windows 打包脚本。最终网站和全量发布仍需原 `-Scope Full` 及双官网批准。未在真实 Windows 10/11+WPS 用户桌面运行时，不得把 CI 的拒绝型 PASS 或静态测试认定为 WPS 功能验收成功。

