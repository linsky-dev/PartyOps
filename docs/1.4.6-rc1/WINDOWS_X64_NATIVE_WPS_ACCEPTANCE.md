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

后两个模式调用的是原来的 `scripts/test.ps1` / `packaging/windows/build-windows.ps1`，绝不跳过真实 WPS 金样、六功能证据、签名输入、完整性校验或版本门禁。任何失败直接停止且保留 `artifacts/rc1-native-wps-*/native-wps-candidate.json`；没有运行就不得登记通过。

候选 EXE 即使生成也必须继续执行 Windows 10、Windows 11 两套原生安装、重启、权限、已有安装升级与回滚、备份恢复、哈希、数字签名和人工发布审批。没有这些证据，不得合并、打标签、上官网或称作正式版。
