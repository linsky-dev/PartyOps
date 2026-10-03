# PartyOps 多平台虚拟与模拟验收实验室

当前产品版本：**1.4.5-rc.6**。本目录是实验室控制代码，提供实际诊断和完整证据门禁。包数和环境数以活动矩阵为准，不固定为九包。

## 当前已验证的能力

- 可信介质校验、QEMU 全系统 VM 创建/启动/关机、离线快照与恢复、Guest 架构探针和截图。
- 主机内存/磁盘余量限制、串行 VM、仅回环管理端口、临时克隆限定清理。
- 最终包 SHA-256、当前源码指纹、逐步骤证据校验、活动矩阵制品汇总；Windows 通用包必须同时通过 Win10 与 Win11。
- Linux 实际包管理器安装及安装后自检诊断。诊断不等于全部公文、模型、GUI、重启及升级卸载通过。
- `guest/installed-formatter-api.py` 在 UUID 绑定的非 root Guest 内，仅通过实际安装版 HTTP 接口执行首次配置、登录、票据、批量上传排版和结果下载；校验输入与安装宿主哈希，使用独立中文空格数据目录。结果仍为局部证据，必须另做下载成品金样比对和完整生命周期验收。
- 该探针会删除开发排版宿主、Mono、Python 和 `LD_LIBRARY_PATH*` 覆盖，确保 WPS 加载不借用测试环境。下载后的两个 DOCX 用 `guest/verify-installed-formatter-output.py` 复用原金样校验器只读比较，禁止重新排版后再冒充接口下载结果。
- `guest/installed-upgrade-probe.py` 在绑定 Guest 中启动实际安装的主服务，对既有 `create-0023-upgrade-fixture.py` 创建的旧库执行迁移，核对账号、附件和迁移前备份；真正 Guest 重启后可用 `--recheck` 核验同一数据目录。预期迁移版本由当前仓库迁移链读取后显式传入，不内置旧版本常量。该结果只覆盖安装版数据库升级，不代替旧安装包覆盖升级、桌面和完整生命周期。

本轮以用户确认的原版重验计划及后续指令为准：UOS V20 1070 双架构、openEuler 24.03 LTS SP2 标准 DVD 全新安装。旧 Deepin AMD64/ARM64 和 openEuler qcow2 仅保留历史证据。用户于 2026-09-08 新增 Deepin LoongArch64 独立环境及安装包，不替代统信双架构。Windows 无人值守已接通；完整 GUI/功能/升级卸载编排、Intel macOS Guest 身份接入与 ARM64 Helper 尚未全部完成。不得因存在 CLI 就将这些项标为已完成。

## 运行

使用仓库后端 Python 3.11 环境，另装本目录 requirements.txt 中锁定的实验室依赖。所有命令从仓库根目录执行。

Linux 已完成真实重启但尚未通过重启后业务检查时，可使用 `scripts/exercise-linux-business.py <target> after-reboot --reboot-execution <本轮 execution.json 的绝对路径>` 单独续接。该入口核验同一业务链、包、恢复代次、业务依赖和真实前后 boot ID，仅接受 UEFI vars 的可变哈希过渡；成功后记录过渡并推进业务指针，保留原失败报告。不得手动修改指针或用新控制器的 `run --resume` 代替此续接，后者可能因控制器指纹变化重新恢复干净基线。此阶段成功不等于完整生命周期通过。

```powershell
./qa/vm-lab/scripts/bootstrap-windows.ps1 -EnableWhpx
./qa/vm-lab/partyops-lab.ps1 doctor
./qa/vm-lab/partyops-lab.ps1 macos-doctor
./qa/vm-lab/partyops-lab.ps1 macos-x64-media
./qa/vm-lab/partyops-lab.ps1 macos-x64-bootstrap
./qa/vm-lab/partyops-lab.ps1 media uos-1070-hwe-x64
./qa/vm-lab/partyops-lab.ps1 create uos-deb-x64
./qa/vm-lab/partyops-lab.ps1 start uos-deb-x64 --accel whpx --provision-network
./qa/vm-lab/partyops-lab.ps1 screenshot uos-deb-x64
# 在官方安装器完成安装和实验室账户配置后核对真实 Guest。
./qa/vm-lab/partyops-lab.ps1 probe uos-deb-x64
./qa/vm-lab/partyops-lab.ps1 baseline uos-deb-x64 --accel whpx --guest-timeout 600
./qa/vm-lab/partyops-lab.ps1 run uos-deb-x64 --accel whpx --resume --guest-timeout 180
./qa/vm-lab/partyops-lab.ps1 stop uos-deb-x64
# 确认进程已退出后才能快照；--force 关机是断电，不用于重启验收。
./qa/vm-lab/partyops-lab.ps1 run-all --accel tcg --resume --guest-timeout 180
./qa/vm-lab/partyops-lab.ps1 test-all
```

普通 start 默认禁用 Guest 对外通信。安装介质/系统依赖准备时显式使用 `--provision-network`；离线验收前关机，再不带该选项启动。QEMU 当前使用 TCG；WHPX 启用如提示需要重启，不自动重启宿主。Guest 分配不超过 8 GiB，实际按宿主可用内存向下取整，并保留 8 GiB 宿主余量，低于 Guest 最低内存即阻断。

`run` 执行已接通的实际步骤并写入 `reports/<target>/run-*/execution.json`，`test/test-all` 负责证据校验和全部制品汇总。`--resume` 重新检查包、源码、控制器、目标配置、Guest 身份和基础快照；变化后创建新的运行记录，保留旧目录。若首次启动因内存等前置条件阻断，续跑仍须实际恢复干净基线；只有本轮 `clean_start` 与当前基线 ID、恢复代次一致时才能复用后续步骤。已有产品步骤但缺少匹配的基线准备记录时直接阻断，避免恢复系统后追认旧步骤。尚未接通或未执行的场景明确阻断，不能仅凭局部诊断自动生成通过结果。

本地构建通过 `scripts/record-build.py --package <包ID> -- <构建命令及参数>` 执行，入口运行已有完整质量门禁、记录构建前后源码指纹与完整日志，并为当次生成的包写入 `state/builds/<SHA-256>.json`。AMD64 Linux 使用 `scripts/build-linux-amd64-local.sh`，在既有 OracleLinux_7_9 的 glibc 2.17 工具链中构建，暂存树位于 E 盘。旧包不能事后补填当前源码指纹。按照用户 2026-09-06 的最新瘦身要求，`retain_obsolete_packages: false` 仅记录被替换候选的哈希与元数据，不再复制保存旧二进制。

Windows 本地入口为 `scripts/build-windows-local.ps1 -Package <windows_amd64|windows7_amd64|windows7_x86>`，同样须由上述 `record-build.py` 包裹执行。它复用项目 `.build-kit` 中独立构建环境，避免修改正在运行验收器的 Python；Win7 保留完整 CPython 3.8/Tcl/Tk、对应位数 wheelhouse 和回移证据验证。临时目录与 pip 缓存位于 E 盘；Win7/10 的安装卸载在隔离 Guest，Win11 按下述本机流程独立处理。

在当前宿主使用已登记的 `pwsh.exe -NoProfile -File` 调用构建入口，与已成功的 Windows 通用包构建一致。2026-09-08 经 `powershell.exe` 调用 Win7 构建时，嵌套脚本报 `Get-FileHash` 不可用；该失败回执保留，不把宿主构建错误归因于原版 Win7 Guest。Guest 内仍采用其原生 PowerShell 2，不安装宿主 pwsh。

openEuler 标准 DVD 新建空白系统盘后，使用 `scripts/prepare-openeuler-iso.py <target>` 生成单独 Kickstart 种子。脚本在主控核对原版 ISO 哈希、空盘与停机状态，种子在 Guest 分区前再次核对 DMI UUID，并仅操作 `vda`；安装完成关机。ISO 保持原样，启动参数为生成回执中的 `boot_option`。安装后卸载原版 ISO 和种子，再完成冷启动及基础快照验证。UOS ARM64 的默认 4.19 内核曾停滞，同一 ISO 自带的 5.10 内核已进入桌面安装器；安装成功与冷启动须分别记录。

覆盖升级原先要求低于当前版本、同平台架构且附带有效 SHA-256 的旧包。用户最新要求删除所有旧版本安装包，因此当前明确返回 `OLDER_PACKAGE_REMOVED_BY_RETENTION_POLICY`，该场景保持阻断；不以旧库迁移、同版本重装或当前包替代旧包覆盖升级。以后若用户改变保留策略，才可启用包配置的 `upgrade_roots`，选定基线仍参与续跑上下文。

`baseline` 必须先证明系统未安装 PartyOps，再正常关机、卸载光盘、冷启动、取得不同 boot ID、正常关机并创建实际 qcow2 快照。Win7 使用系统自带 WMI/WinRM；本地凭据只在对应实验室 Guest 目录保存，卸载种子后不会重新生成或更换密码。

## 安装版分阶段业务检查

### Windows 11 本机目标

按用户 2026-09-08 的最新指令，`win11-x64-native`（`backend: native-host`）替代活动矩阵中的 `win11-x64`，仍与 Win10 共同验收同一 `windows_amd64` 包。其他九个环境不变；旧 Win11 Guest 登记仅进入 `historical_targets`，不改名或追认其报告，不再安装 Win11 虚拟机。

```powershell
./qa/vm-lab/partyops-lab.ps1 probe win11-x64-native
./qa/vm-lab/partyops-lab.ps1 run win11-x64-native --resume
./qa/vm-lab/partyops-lab.ps1 test win11-x64-native
```

`probe` 只读本机 CIM / Windows 版本登记，核验 Windows 客户端版本、架构、完整构建号、Edition、真实机器标识哈希、当前用户 SID/管理员状态与系统启动时间。报告保存在 `reports/win11-x64-native/native-identity.json`；不写入可丢弃 Guest 标记，不构造 ISO 哈希或冷快照。`create/start/stop/snapshot/restore/baseline/cleanup/screenshot/media` 对此目标全部拒绝，底层 QEMU 入口也拒绝使用该目标。

当前 `run` 仅写入 `native-readiness` 准备诊断，所有十三项生命周期仍为 `not_run`，并返回 `NATIVE_WINDOWS_LIFECYCLE_EXECUTION_NOT_IMPLEMENTED`。不会自动安装、重启本机、停止宿主服务或清除数据。实际本机测试先保护现有安装和业务数据、登记独立 D/E 测试路径及专用普通用户，再单独执行安装、首启和业务；Codex 工具账户不能被当作专用产品测试用户。已有安装即使显示同为 rc.6，也须核验其安装文件是否来自当前包。

原生完整证据使用独立 `environment`：`backend: native-host`、`host_id` 和 `identity_sha256`。系统身份摘要包含真实版本/构建号与机器哈希；旧 VM 的 UUID/介质/快照字段不可混入。导入时从本机重新计算预期值，真实重启后的标识须匹配当前本机启动时间，重启前的标识须来自控制器 `native-observed-boots.json` 已实际采集的本机记录，进程重启或两个自报字符串不能替代。后续本机再次重启、系统更新、包或源码改变时须重新核验受影响证据。`--resume` 同样检查本机身份和当前启动标识，不恢复 Guest 快照。局部准备、自检和既有数据不会自动满足十三场景，完整门禁仍按活动矩阵全部必需环境汇总。

`scripts/exercise-linux-business.py <target> <phase>` 在已安装、身份及构建回执匹配的原版 Linux Guest 内执行实际桌面入口和 HTTP 业务。依次使用 `configure`、`business`；真实 Guest 关机再启动之后使用 `after-reboot`。`ocr`、`models` 读取同一 Guest 工作目录的 `inputs.json`，其中每项为 `filename` 与 `sha256`；文件必须先传入该目录。模型通过产品正式接口验签、激活和推理。最后使用 `uninstall-keep` 核验包管理器卸载、附件保留及所有安装版进程退出。不同阶段不能跨包哈希、源码指纹或快照恢复世代续用。

凭据仅保存在 Guest 的 `state.local.json`（0600），主控只回收 `evidence` 普通文件。Linux `run` 在安装诊断成功后继续执行首次配置、业务备份恢复、真实 Guest 重启与重启后数据核验。非零执行结果记录为阻断，`--resume` 会重试失败步骤并复核已完成步骤的所有证据。首次配置中途失败且已经留下配置时，必须重新从干净基础快照开始（使用不带 `--resume` 的 `run`），不能把残留配置追认为首次配置成功。分阶段报告仍不自动完成十三场景汇总，WPS、模型、协同及卸载需要各自完整证据。重复激活已启用的模型会卸载内存模型，语义检索由新建实际业务触发后台建索引，不能将普通关键词查询单独写成推理证明。

真实 WPS 排版产生两份 HTTP 下载成品后，运行 `scripts/verify-linux-guest-golden.py <target> --evidence <installed-api-evidence.json> --remote <Guest的formatter工作目录>`。脚本核对 VM UUID、boot ID、安装程序及下载成品哈希，由 Guest 随包 Office 渲染金样和成品，在宿主复用已有 OOXML 及逐页像素门禁。宿主不重排成品、不替代 Guest 渲染，不放宽原有阈值。

`scripts/exercise-linux-formatter.py <target> --host-sha256 <当前包的排版宿主哈希> --all-features` 收回实际 HTTP 排版成品，并继续执行替换、套红、命名、五种转换及 PDF 转 Word。夹具复用原质量门禁，Guest 只执行标准库控制器；图片在宿主以原 PyMuPDF 校验可解码和尺寸。该步骤仍须通过随后的逐页金样比对。OCR/模型输入可由 `scripts/prepare-linux-runtime-inputs.py <target>` 传入当前业务工作目录，传输前后核对固定哈希；不把模型解包当成实际推理。

UOS 1070 默认签名策略曾拒绝未签名 DEB；用户明确授权在安全中心开启“允许任意应用”，本轮 UOS AMD64 的成功安装在该配置下取得，不能描述为默认策略免配置安装。失败日志及设置回执保留。后续源码修复会使旧包来源绑定失效，必须通过完整质量门禁并重建之后重新取得验收结果。

保留数据卸载通过后，可实际重新安装同一哈希的包，再执行 `after-reinstall`。最后 `uninstall-remove-test-data` 再次通过包管理器卸载并核对进程退出，只删除本次工作目录下明确登记的 `中文 空格业务数据`；证据目录保留。这是用户授权的测试数据清除，不能描述为安装器默认删除所有用户数据，也不能替代旧包覆盖升级。

## 存储与介质

### Deepin LoongArch64

活动目标 `deepin-deb-loong64` 使用官方 Deepin 25.2.0 Loong64 ISO，新增包 ID `linux_loong64`，文件名 `PartyOps_1.4.5-rc.6_linux_loong64.deb`。内核架构规范为 `loongarch64`、dpkg 架构为 `loong64`；不能使用 AMD64/ARM64 包或历史 Deepin 环境替代。完整汇总从矩阵动态计算制品数量，缺少龙架构包仍显示 `MISSING_PACKAGE`。

```powershell
./qa/vm-lab/partyops-lab.ps1 create deepin-deb-loong64
./qa/vm-lab/partyops-lab.ps1 start deepin-deb-loong64 --accel tcg --provision-network
# 完成原版安装及实验室普通账户配置后，再采集实际版本。
./qa/vm-lab/partyops-lab.ps1 probe deepin-deb-loong64
./qa/vm-lab/partyops-lab.ps1 baseline deepin-deb-loong64 --accel tcg --guest-timeout 600
```

配置固定 `qemu-system-loongarch64`、`virt-11.1`、`la464`、TCG，包含 virtio 系统盘、网卡、显卡、SCSI 光驱及 USB 键盘/指针。介质保留 D 盘，系统盘和独立 UEFI vars 放在 E 盘。UEFI code 与 vars 模板的 SHA-256 写入矩阵；code 只读，模板不作为可写盘。每次启动校验固件及上次停机变量摘要，异常不自动重建。停止后记录 QEMU 的实际变量更新；快照同时保存变量副本和 qcow2 快照，恢复两者全部成功后才清除事务标记。缺失变量副本的旧快照、损坏副本、停机后变量变化或固件变化会阻断；恢复中断可对同一已验证快照重新执行 restore。

系统身份从 Guest 的 `/etc/os-release`、`/etc/os-version`、`/etc/deepin-version` 读取。`25` 系发行版与 `25.2.0` 更新版本分别核验；若原版系统未提供可证明 `25.2.0` 的字段，先保留原始探针及阻断，不能从 ISO 文件名补填。`Loong64` 的包架构与内核 `loongarch64` 独立核对。基础盘封存后用 `scripts/linux-build-guest.py create deepin-deb-loong64` 创建独立构建盘，其变量盘同样复制自经过校验的基础快照，不共享基础盘变量。

本次保持串行 VM；尚未开放两个 Guest 的并行调度。Loong64 配置为 4 GiB / 2 vCPU，TCG 另保留 1.5 GiB 开销以及宿主 8 GiB，空间仍保留 D/E 20/30 GiB。内存不足返回 `HOST_MEMORY_HEADROOM`，不降到不足 4 GiB，不关闭其他项目进程。创建和命令构造通过测试不等于系统安装、编译或完整生命周期通过。

资料核验日期 2026-09-08：[QEMU 官方 LoongArch virt 文档](https://www.qemu.org/docs/master/system/loongarch/virt.html)、[Deepin 官方下载页](https://www.deepin.org/zh/download/)、[官方镜像校验和](https://cdimage.deepin.com/releases/25.2.0/loong64/SHA256SUMS)。本机 11.1.0 二进制只读 `-machine virt-11.1,help` 与 `-cpu help` 已确认机型和 CPU；实际启动及安装由运行记录另行证明。

原版环境封存后，`scripts/prepare-loong64-build.py all` 可直接读取该 Guest 的真实 Python/glibc、APT 来源与 numpy/ONNX/tokenizers/PyMuPDF/OCR/LibreOffice/WPS 候选版本。`--build-clone` 模式提供 `refresh-index`、`install-build-tools`、`smoke`：仅在独立构建盘更新官方索引、按查明版本安装编译工具并真实编译运行 C/C++/Python 检查。`--resume <execution.json>` 保留旧证据并重新核对身份及所选阶段，不复用旧通过值。完整运行步骤、锁定依赖及尚未应用的产品差异见 [Loong64 构建准备](release-preparation/loong64/README.md)。任何仓库候选、工具链 smoke 或系统外部服务都不计为 PartyOps 安装包完整通过。

ARM64 本地构建使用独立克隆：`scripts/linux-build-guest.py create uos-deb-arm64` 只从已冷启动且停机的原版基础快照导出独立 qcow2，不使用会随原盘变化的 backing file。依次使用同脚本的 `start`、`bind` 后，才可在克隆内安装构建工具；`bind` 同时核对硬件 UUID 和来自基础盘的 SSH 主机密钥，将 Guest 标记为 `disposable-build`。构建克隆不加入必需验收环境，身份门禁拒绝把它的运行证据导入验收目标。基础盘保持原样；`stop` 后保留工具克隆以便续建。

默认目录 `D:/PartyOps-VM-Lab`；下载、基础盘、活动盘、专用 SSH 密钥、报告分目录保存，不进 Git。不写 C 盘大镜像。D 盘空间不足时可以对命令显式传 `--root E:/codex/PartyOps/.partyops-vm-lab`，当前不会自动搬动正在运行的 VM。

`config/media.yaml` 与本地 `.local/media.yaml` 登记来源、架构及哈希，实际下载后逐字节校验。Win7 来源为用户指定的第三方归档；哈希一致不能描述为微软官方真实性认证。旧 qcow2 记录仅供历史追溯。UOS ARM64 若不支持 QEMU 虚拟硬件则保留阻断，不换用 Deepin。

`cleanup <target>` 默认仅列出目标；`--force` 才删除停机且登记为 temporary 的测试克隆。运行进程、未知 PID、目录联接、根目录和越界路径都阻断。下载、基础盘和已归档报告不删除。

2026-09-06 的 C/D/E 瘦身盘点、删除回执及工具限制见 `CLEANUP-20260906.md`。旧 PKG 已依用户最新要求删除；当前产品金样、质量门禁、活动环境和本次报告继续保留。系统介质按本计划选定版本保留，不因为瘦身而将 Win7/10 的必需环境移出矩阵。

macOS Intel 路径固定使用 OC4VM 3.0.1（提交 `33fad1b4a6083b8b8fef93f395f2e384743967ac`）和 recoveryOS 1.0.1（提交 `0012c39adde4c557196eea556edffc63951e7ff6`）。恢复 DMG 只从 Apple Internet Recovery 获取，先验证 Apple 签名 chunklist 与全部数据块，再转换为无 backing chain 的 VMDK。活动虚拟磁盘放在 E 盘后备实验室，OC4VM 不修改 VMware，未实际失败前不启用 Unlocker。

## 证据门禁

Windows 10 可在原版安装与冷启动基线核验后运行 `scripts/exercise-windows-install.py <target>`，安装到 Guest 的中文空格路径。安装前后检查真实硬件 UUID、发行版、架构、卸载登记及服务，并绑定当前包的构建回执和源码指纹。Windows 虚拟硬件时钟使用本地时区，中文 PowerShell 输出固定为 UTF-8。该 Guest 入口不用于 Win11 本机目标。

Windows 7 SP1 使用同一安装诊断入口，根据目标 `winrm_port` 选择 `windows_remote.py` 的 WinRM NTLM 加密回环传输，不要求安装 OpenSSH 或升级 PowerShell 2。包通过 pywinrm 标准 stdin 流以 64 KiB 分块传入 `C:\PartyOps-QA\incoming`，Guest 内用 .NET SHA-256 在传输后及执行前核验；安装后核对卸载登记版本、安装目录和实际 `PartyOps.exe` 的 PE 位数，日志经 stdout 流回收并再次比对哈希。每次操作都复核硬件 UUID，凭据只读取该 Guest 的私有登记文件。此入口仍仅为真实安装诊断；普通用户交互令牌的安装版启动/健康/权限入口现见 [Win7 普通用户诊断](WINDOWS7-STANDARD-USER.md)，首次配置和后续完整业务驱动仍待接通，不得把管理员安装成功计为普通用户首启或完整通过。传输中断或命令超时后先检查 Guest 进程状态，禁止盲目重启安装器。

Win7 的 `run/--resume` 和 `exercise-windows-install.py` 在实际安装前共用 `winrm_memory.prepare_win7_transport`。原始快照保留 WinRM 的 150 MiB Shell 配额；恢复后，工具先核验原版 `6.1.7601 / 7 SP1`、登记 UUID、`partyopsqa` 本地账号 SID 与实际管理员 token，以及没有活动安装器，才将此 Guest 的 `MaxMemoryPerShellMB` **150→512 MiB**。已经为 512 时只读；其他值直接阻断，不继续扩大额度、不改其他 WinRS 属性。新 Shell 必须通过 Windows JobObject 实测确认进程和 Job 上限均为 512 MiB，单纯配置回读不能替代。512 MiB 只解决已证明的提取配额问题，不预先宣称全部产品功能或峰值内存通过。

每次准备/检查均写入当前运行目录的独立 `winrm-transport-<随机ID>/preparation.json`；真实写入前先保存 `change-intent.json`，记录原值、目标值、UUID 与恢复代次。`run` 在 `execution.json` 的 `transport_preparations` 留下引用，续跑也重新核验，不缓存配额结论。已有 150→512 变更回执不会被后续 512 只读检查覆盖；再次恢复 150 后另建新回执。账号密码及凭据文件内容或摘要不进入公开报告。历史手动诊断入口 `configure-winrm-diagnostic-memory.py --require-mib 512` 同样只读并生成唯一回执；显式配置只接受 150/512，执行前复用相同账号/身份/无安装器核验。底层接口已有原版 Win7 实机对照，新增自动入口目前完成离线回归，等待下一次正常启动和基线恢复时实际执行。诊断证据与回退说明见 [Win7 .NET 提取诊断](release-preparation/dotnet48-extraction/README.md)。

Win7 种子按原版实测初始化：WSMan 先检测内置 provider，必要时加载已注册的 `Microsoft.WSMan.Management` 管理单元，不再导入不存在的同名模块。HTTP Listener 通过系统 `winrm.cmd get/create` 建立并逐次核对原生退出码，不调用实测返回拒绝访问的 `New-Item WSMan:`，也不改变 Public 网络类型或关闭防火墙；保留单独的 Guest 5985 管理规则。WinRM 使用 Automatic 并清除其 `DelayedAutoStart` 标志，避免 TCG 冷启动后数分钟才就绪。身份探针以 UTF-8 `Console.WriteLine` 写出完整 JSON，格式器在字符串中插入的换行按损坏证据拒绝，不拼接追认。`run` 在 Win7 安装诊断后明确提示 `WIN7_STANDARD_USER_PREPARE_AND_INTERACTIVE_PROBE_REQUIRED`，转至独立 `exercise-win7-standard-user.py` 入口；完整业务驱动仍阻断。

`guest/windows-standard-user.ps1` 在已绑定 Guest 创建 `partyopsuser` 普通账户；私有凭据只在 Guest 内保留。正常冷启动进入该账户后，`scripts/exercise-windows-business.py <target> configure|business|after-reboot` 使用有限权限的真实桌面启动安装版，复用既有业务接口断言，经 SSH 连接同一 Guest 的回环接口。各阶段检查向导、首次管理员、中文空格业务路径、账号事项附件、Office 导出及备份恢复；`after-reboot` 必须先真正重启 Guest。该入口与管理员后台自检均不自动产生十三项完整通过凭证。

Windows 10/11 的业务验证完成后，可在同一上下文依次执行上述脚本的 `uninstall-keep`、`same-package-reinstall`、`uninstall-remove-test-data`。前者运行安装版真实卸载器 `/DATAACTION=preserve` 并核对卸载登记、进程、服务、数据库、附件及个人配置；重装固定同一哈希，重新普通用户启动并核验账号和业务数据，不算旧包覆盖升级。最终阶段再次真实卸载，再仅删除本次 `lifecycle-<12位编号>/中文 空格业务数据`，拒绝目录联接、路径越界、外来 Guest 或上下文变化。它不代表安装器全账户 `/DATAACTION=delete` 已完成验收。入口和合成回归的通过不代替实际 Guest 执行证据。

`test-all` 检查活动矩阵全部必需环境及制品，数量由配置动态计算。尚未执行的步骤统一 blocked/not_run；退出码 2 表示未满足全部门禁。

Win11 本机的 `diagnostics_summary` 另显示绑定当前包、源码、安装 EXE、主机、普通用户 SID 与启动标识的部分实测进度及证据位置，不导入完整场景、不增加通过包数。固定读取本轮 canonical run，旧报告、不同主机/账号或已变化的制品显示 rejected；公文服务归属失败继续保留。字段、边界和定向验证见 [NATIVE-DIAGNOSTICS.md](NATIVE-DIAGNOSTICS.md)。

完成生命周期的结果以 `test <target> --evidence <result.json>` 导入；结构见 `schemas/result.schema.json`。证据的每个文件必须存在且 SHA-256 匹配。每次汇总重新验证，源码/包/证据变化后旧结果自动失效。

- `runtime_environment_passed` 与兼容字段 `real_environment_passed` 表示目标 OS/ISA 实际运行，不表示物理真机。
- `reboot.actual=true` 必须有不同的前后 boot ID。进程重启不合格。
- 本机 WHPX legacy IRQ 的 Linux 使用 Guest systemd 正常关机后冷启动；先捕获 QMP 的 Guest 正常关机事件，再确认退出和新启动标识。崩溃、强杀或仅连接消失均不能发放重启通过。
- 本轮禁止 hosted/远端 CI；新的远程任务或进程重启都不能代替 Guest 重启。
- `distribution_match=false` 必须阻断；同时独立比对真实发行版、版本、架构、介质哈希、VM UUID 和实际基础快照，不能只信布尔标记。
- 包来源/清单必须在 Guest 内另行验证，文件名只用于发现候选；安装器诊断从来不发放完整门禁通过。
- 签名、公证与生产分发状态独立；本工具不发布官网、不上传 Release。

## 回归测试

```powershell
./backend/.venv/Scripts/python.exe -m pytest qa/vm-lab/tests -q
./backend/.venv/Scripts/python.exe -m ruff check qa/vm-lab
```

合成证据仅存在 pytest 临时目录，测试通过不能计入产品制品通过数。完整报告写到实验室 reports 目录，state/latest-report.json 指向最近一次结果。

安装版模型阶段使用 `exercise-linux-business.py <target> intent` 执行 Needle 验签导入、激活、中文请求与拒绝样例；规则回退不能替代实际原生通过。`observe-installed-llm.py` 与 `diagnose-llm-load.py` 仅定位实际进程和加载耗时，不发放产品推理通过。公开诊断仅记录加载器相关环境，Guest 账号凭据不回收。

业务基线完成后，运行 `exercise-linux-business.py <target> collaboration-business` 检查安装版双账号协作。它实际创建合成协同账号，使用独立 Cookie 会话核查事项分工、审核列表、评论提及与回帖；凭据仍只写 Guest 的 0600 状态文件。该阶段不表示独立协同机已入网，也不自动发放完整环境通过。完成后按原卸载流程清除本次合成业务数据。

当其他包正由安装器写入，清单把该包标为 `PACKAGE_UNREADABLE_OR_BUILD_IN_PROGRESS`，其他可读平台继续；哈希前后文件大小或修改时间变化则为 `PACKAGE_CHANGED_DURING_INVENTORY`，不缓存不稳定候选。

Windows 首次配置写入后如控制器中断，使用 `exercise-windows-business.py <target> resume-configure` 续接原向导。它要求相同上下文、数据路径、尚未初始化的管理员及普通用户交互进程，不生成新口令、不重复启动 Launcher。`ocr` 与 `models` 在该安装版实际执行；阶段通过只说明列出的调用成功。模型阶段要求实际回答后向量与 LLM 仍然可用；因内存不足暂停时保留回答证据并将该阶段标为失败，不能以一次成功推理冒充模型全部可用。独立协同机、重启和其他未执行场景仍需分别验证。中文安装目录的 OCR 故障及修复重验记录见本轮 REVALIDATION 文档。

## 本机新构建重验的独立载荷绑定（2026-09-08）

`record-build.py` 对三个 Windows 包在真实构建命令和包门禁成功后，立即核对 staging 清单的版本、平台、架构、运行配置与两个主 EXE 的哈希/大小及本次构建时间，封存 `reports/build-*/payload-<package>/release-manifest.json`。构建回执增加 `windows_payload`，同时绑定该次安装器 SHA-256。封存后再次核验源码与安装器；失败不登记合格回执。已有同 SHA 回执保留原字节，不能覆盖，也不能用当前 staging 对旧回执补写追认。

新本机 run 必须用新目录。先在全部 PartyOps 实例/服务已经正常退出后执行保全脚本，再生成候选绑定，最后才运行安装：

```powershell
$run='D:\PartyOps-VM-Lab\reports\win11-x64-native\native-20260908-rebuild-唯一标识'
pwsh -File qa/vm-lab/scripts/prepare-windows-native-test.ps1 -RunDirectory $run
backend/.venv/Scripts/python.exe qa/vm-lab/scripts/prepare-native-candidate.py --run-directory $run
pwsh -File qa/vm-lab/scripts/exercise-windows-native-install.ps1 -RunDirectory $run
backend/.venv/Scripts/python.exe qa/vm-lab/scripts/verify-windows-native-files.py --run-directory $run
```

目录名只允许小写英文、数字、下划线和连字符，示例的“唯一标识”应替换为本轮新标识。保全脚本在复制前检查 D 盘保留 20 GiB；它没有退出进程、重启、安装或改账号的动作。安装入口也不终止其他运行实例，发现占用即阻断。本机同版候选替换仍不是干净系统安装或旧版覆盖升级。

安装前的 `install-binding.json` 来源为当前控制器构建回执，与源码、包、独立封存清单及本次保全报告哈希一致。安装执行器每次重新核对该绑定，期望 EXE/清单哈希不再写死，也不取自已安装目录；安装后版本、注册路径、EXE 与清单任一不符均保存失败。逐文件复核使用封存的期望清单，不读取当前可变 staging 来追认。`candidate.json`、绑定、安装日志或结果已存在时拒绝覆盖，应保留旧 run 并开新目录。旧回执没有 `windows_payload` 时显示 `WINDOWS_BUILD_PAYLOAD_RECEIPT_REQUIRED`，需要真实重建，不能手填新哈希。

现有 `PartyOpsNativeQA` 仍归属于最初登记的 run。新保全脚本先核对其原 SID、描述中的归属 ID、宿主 UUID、控制器 SID 与原目录，再把原专用用户配置和登记业务一起备份；`personal.env` 指向登记范围之外时阻断。此过程不改变 SID、密码、成员组、全局 ownership 或旧报告，并明确 `new_run_account_authorized=false`。

已有专用账号采用 `PrepareRevalidation` 沿用，原 `Prepare` 仍用于初次登记。新入口每次先重核当前构建回执、安装 EXE/Wizard/清单、机器 UUID、控制器 SID、原账号 SID/成员组/描述、原 ownership 和 DPAPI 凭据文件哈希。它验证本轮 `protection.json` 中原 profile 配置及原业务备份：登记前当前文件必须与备份逐项一致，新增未备份文件、损坏备份、目录联接均拒绝。登记后允许本轮实际产品运行修改选定测试数据，备份与原归属记录保持不变。

```powershell
# $run 为上面刚完成新候选安装与完整性检查的新报告目录。
pwsh -File qa/vm-lab/scripts/windows-native-user.ps1 -Action PrepareRevalidation -RunDirectory $run
pwsh -File qa/vm-lab/scripts/windows-native-user.ps1 -Action RunProbe -RunDirectory $run
pwsh -File qa/vm-lab/scripts/windows-native-user.ps1 -Action RunConfiguredPersonalProbe -RunDirectory $run
```

默认 `-DataMode Retain` 保留并验证原配置所指向的已登记业务目录，适用于新包替换后的普通用户启动与配置权限回归。`native-user-session.json` 在新 run 独立登记原 ownership 路径/哈希、稳定 SID、本轮安装/构建/保全哈希和数据选择；原账号、密码、成员组、ownership、配置和旧 run 回执均不改写。只为本轮脚本、日志与临时文件建立新的 E 盘工作目录，保持 E 盘 30 GiB 保留量；准备阶段发现 PartyOps 进程或服务尚在运行即拒绝，不自动终止。

如需单独的新测试数据目录，准备时显式添加 `-DataMode Fresh`；此选项创建新 run 的空数据目录，保留原 profile 配置。它只适用于实际自检/后续明确配置到新目录的测试，不能把已有账号说成未配置账号；若原 `personal.env` 仍指向旧数据，`RunConfiguredPersonalProbe` 会如实拒绝数据目录不匹配，`LaunchWizard` 也不会绕过已有配置。不得伪造 Known Folder 或用环境变量冒充首次配置。切换 Retain/Fresh 必须另开 run，同一 session 不能改写。重复准备只校验沿用已有绑定，不重新生成凭据；同一阶段已有结果时不覆盖，应检查其独立探针目录并开新的验收 run。

新轮次的只读诊断使用 `backend/.venv/Scripts/python.exe qa/vm-lab/scripts/diagnose-windows-native-run.py --run-directory $run`。入口仅接受目标报告根目录的直接子目录，并重新核对本轮构建绑定及每层报告哈希；移入旧报告、旧载荷或旧工作区均拒绝。原默认汇总仍保留 `native-20260908-current` 历史诊断，新 run 应显式指定；源码过期时显示 `stale_source`，部分诊断始终是 0 个完整场景。

本轮只修改实验室执行器与测试，没有执行本机安装、账号变更或进程终止。新增绑定反例与完整实验室回归证据保存在 `D:/PartyOps-VM-Lab/reports/native-rebuild-lab-regression-20260908.xml`；测试为合成构建，不代表新产品包已经通过。

普通用户沿用入口的最终交叉回归为 104 项通过、无跳过，见 `D:/PartyOps-VM-Lab/reports/native-user-revalidation-final2-20260908.xml`；原普通用户脚本及沿用准备阶段的前轮定向回归 58 项通过。前者覆盖保全文件实哈希、凭据变化、合法沿用/新数据路径、旧报告移入新 run、源码/制品变化和会话绑定。测试只操作合成报告、临时目录与模拟账号接口，没有实际注册沿用会话或启动产品。回滚仅还原本轮实验室代码；保留新建实际验收报告和旧归属证据。

## 已确认的外部准备项

- VMware Workstation Pro 官方下载可能需要 Broadcom 登录；不索取密码、不使用盗版或未知镜像。
- Windows ISO、UOS/openEuler 原版安装介质、Apple 官方恢复组件必须逐项核验，缺失时报告 MISSING_MEDIA。
- macOS ARM64 固定 darwin-vm 与 qemu-sptm 提交，显式使用 Macmini9,1 和 UniversalMac macOS IPSW。WSL 只完成本地 QEMU 构建与原始组件获取，ramdisk 修改需 Intel macOS Helper。上游仅精简命令行环境，kernel、userspace、PartyOps runtime 和完整 GUI/PKG 生命周期分级记录。
- 用户 2026-09-08 最新授权完成各平台验证后更新官网与 GitHub，覆盖此前整轮不发布的限制；仍不调用远端 CI。官网展示及赞助功能已按独立授权上线，见 `website/deployment-report.md`。安装包须在活动矩阵全部原版环境、安装包完整场景与独立发布门禁通过后，按最终源码、哈希及证据发布；不得把尚未完成的结果标为通过。历史附件、旧文档中的 hosted 指令不沿用。

来源：[统信原版下载](https://www.chinauos.com/resource/download-professional)、[openEuler 标准 ISO](https://repo.openeuler.org/openEuler-24.03-LTS-SP2/ISO/)、[darwin-vm 能力说明](https://github.com/jprx/darwin-vm#what-this-is-not)。最后核查：2026-09-06；持续记录见 `REVALIDATION-20260905.md`。
