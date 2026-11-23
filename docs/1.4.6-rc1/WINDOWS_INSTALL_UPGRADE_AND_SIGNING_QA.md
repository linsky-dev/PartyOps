# PartyOps 1.4.6-rc.1 Windows 安装、升级、回滚与签名验收

## 一、用户实际截图中的 INSTALL_DIR_NOT_FIXED

安装界面提示 `[INSTALL_DIR_NOT_FIXED] 程序目录必须位于本机固定磁盘`。对应源码 `packaging/windows/validate-install-path.ps1` 在任何文件冻结前检查 `DriveInfo.DriveType`；这不是 WPS 排版或 PDF 引擎报错。所选盘符未显示在截图中，不能据此断言所有电脑误报。

现在已完成两处最小改进：

1. 独立安装目录校验清楚展示实际根盘符与 Windows 报告的磁盘类型，并明确拒绝 UNC 网络共享、盘符相对路径、磁盘根目录、目录联接、可移动磁盘和危险 ACL。
2. Inno Setup 在安装向导选择程序目录后就调用相同只读校验；路径错误时停留在目录选择页，直接更换路径，不再只能等到“正在准备安装”失败并退出。真正安装前仍会重复原检查并收敛 ACL，防止检查后路径被置换。

**用户立即可用的处理方式：** 在安装器选择 `C:\Program Files\PartyOps` 或其它 Windows 确认为 `DriveType=Fixed` 的本机盘符目录。数据目录可另选符合安全要求的磁盘；不要为了消除报错关闭 ACL/完整性安全检查。查看本机磁盘：

```powershell
Get-CimInstance Win32_LogicalDisk | Select-Object DeviceID,DriveType,ProviderName
```

`DriveType=3` 表示 Windows 固定磁盘，`2` 可移动，`4` 网络盘，`5` 光驱。若自认为内置的 D/E 盘被报告为其他类型，先检查挂载和安全策略。无真实盘符信息时不擅自扩大允许范围。

## 二、真实 Windows QA 与适用范围

- 原版 `PartyOps_1.4.6-rc.1_windows_amd64_BUILD_ONLY_UNVERIFIED.exe` 在 GitHub 托管 Windows Server 2025 的固定 C 盘上已实际安装、核对 `release-manifest.json` 和主程序 SHA-256、卸载成功；相关日志和 JSON 见 CI `45101084560`，这是真正执行安装 EXE，但仍不等同 Windows 10/11 交互桌面 WPS 测试。
- 新加 `scripts/rc1-windows-upgrade-rollback-smoke.ps1` 与原版 RC6 安装器及上述候选 EXE 固定哈希绑定。在全新、无旧 PartyOps 的一次性 Windows Runner 上实际执行 RC6 初装、RC1 覆盖与 RC6 程序回滚，对比安装后主程序哈希和 ProgramData 合成文件标记，生成真实日志。**程序文件恢复不能替代 SQLite 0026→0028 的生产数据库安全回滚**。
- 新增单独路径矩阵真实 Windows PowerShell 5.1 测试。项目全量前后端测试及隔离 PDF 回归仍作为代码级证据，和安装状态分开记录。

## 三、正式数据回滚边界

在有正式业务数据的电脑上不应直接以旧 RC6 EXE 覆盖已升级到 RC1 迁移版本的数据库。正确过程：先做好数据库及其 WAL/SHM、附件、证书/密钥、服务模式和配置的完整一致性备份；停止服务并校验备份 SHA-256；升级并验证结构迁移、数据保留、登录和审计；发生故障时仅在对应迁移事务尚未对外提供写入的安全阶段恢复**配套的旧程序与旧数据库快照**。如果已经有新版本写入，走新版本兼容修复或人工审查的恢复路线，不得直接降级使用旧库。参考 `docs/1.4.6-rc1/ROLLBACK.md`。

## 四、数字签名验收与正式发布证书

- 原 BUILD_ONLY 安装包没有正式 Authenticode 签名。云端签名专项只使用一次性的**临时 QA 自签名证书**对单独复制的文件测试 Authenticode 验签和修改文件后的篡改拒绝，不覆盖原包、不上传签名私钥或 QA 签名 EXE；这不属于可信正式发行签名。
- 新增只读 `scripts/rc1-verify-windows-authenticode.ps1` 正式签名门禁：安装包需匹配预期的**签名后 SHA-256**、Windows 信任验证为 Valid、经用户确认的代码签名证书 SHA-1 指纹、非自签发 CA 签名、Code Signing EKU 和可信时间戳。只要缺少任一条件或使用测试证书即拒绝正式签名通过。
- 正式签名还需用户提供可用的可信代码签名证书/硬件密钥及授权签名环境，且通过候选安装、升级、回滚和当前平台验收后再签；不能通过在 CI 临时生成一个证书来冒充官方发布者。

## 五、交付与状态隔离

目前已成功的旧候选 EXE：`https://github.com/linsky-dev/PartyOps/actions/runs/45096682908/artifacts/14784506862`；这是截图报错发生时的先前版本，不含后续路径向导改进。修改后的程序需以新 GitHub CI 构建成功且 SHA-256 核对完成后另行分发。保留 `main`、RC6 原包、全部生产官网和未涉及的 WPS 源码不变。

本轮所有内部候选均应继续显示 `BUILD_ONLY_UNVERIFIED_NATIVE_WPS`、`signed=false`、`release_ready=false`；不得将 Windows Server 的托管环境和历史 RC6 WPS 证据自动升级成新版 Windows 10/11 正式验收。

## 六、原有用户覆盖升级操作与验收优先级（2026-11-23）

**默认路线是直接安装新版覆盖旧版，不需要卸载。** 新旧 Inno `AppId` 保持一致，启用 `UsePreviousAppDir=yes`。新版安装向导进一步核对 64 位原安装记录以及 32 位注册表历史位置，将程序目录预填原路径，不允许不知情地换到第二套目录造成服务、快捷方式和旧数据失联。

原数据目录按 `partyops.env`、`install-data-dir.txt` 的既有优先级恢复。升级时若用户选择不同数据目录，提示原路径与迁移要求，不自动把原数据库、证书或账号迁往新位置。检测到冲突的两套旧版安装注册时停止并留存旧版，不擅自删除任何一个安装。目录异常 ACL 仍拒绝在未知可写二进制上执行 LocalSystem 服务，不要求卸载作为修复前提。

数据库恢复/失败回滚：本次补强了被强制终止时遗留的 `install-data-dir.txt.previous` 恢复机制。每次新事务先恢复未完成的原目录标记，再进行新事务切换；不能恢复即拒绝安装，不丢弃最后一份标记。

**已证明**：在 GitHub 托管 Windows Server，真实 RC6 安装器经显式 `/DIR` 安装 → 真实 RC1 BUILD_ONLY 原位覆盖 → RC6 程序文件回滚、主程序 SHA-256 以及 ProgramData 合成标记未丢失，完整作业 `45101363940` 为 SUCCESS。

**更严格的专项新增**：只在首次 RC6 安装指定 `/DIR`，后续 RC1 和 RC6 回滚不传 `/DIR`，验证默认旧路径查找；另在 RC6 初装后创建原业务数据目录的 SQLite 合成核验库及附件，检查升级/程序回滚后目录标记、SQLite 的 `PRAGMA quick_check`、内容和字节哈希都保留。合成 SQLite 并不是 PartyOps `0026→0028` 真正数据库迁移，此项仍需另行运行真实旧库夹具和服务/界面验收。

**升级验收检查顺序**：旧版识别 → 原程序路径确认 → 原业务数据路径确认 → 备份校验 → 安全停止现有 PartyOps 服务 → 文件覆盖 → 主程序与旧用户桌面启动探针 → 服务恢复与账号权限 → 新版本数据库升级及健康状态 → 保存新的安装器缓存与回滚凭据。安装失败不意味着可以在生产数据库已经写入新版数据后任意重装旧版。

为防止把 PascalScript 语法错误留到整包构建，另有快速 Windows Inno 编译专项，只使用明确标记的无功能 QA 载荷、不上传可执行样包，编译真实 `PartyOps.iss` 的目录向导、升级分支与中断恢复逻辑；此编译成功不代表正式 EXE 完整封装或真实用户测试。
