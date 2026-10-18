# 党建智办 PartyOps 1.4.5-rc.6 发布说明

## Windows x64 启动修复候选（2026-10-18）

- 本批验收记录更新于 2026-10-18。Windows 10 x64 安装包为 `PartyOps_1.4.5-rc.6_windows_amd64_startup-20261018.exe`（449988807 字节；SHA-256 `e3e2ea45ccd38acff059b5713826a78f998a56ccef75341633f67bd4e17e57da`；独立签名清单 `release-manifest-windows-startup-20261018.json`）。
- 修复 Windows 管理员进程启动参数传递和启动错误诊断；TLS 服务启动前检查配置的业务端口和设备安全端口，按实际错误提供诊断。
- 修复配置向导超时后过早删除事务 journal 的问题，保留恢复所需证据并维护互斥边界。
- 验证环境：Windows 10 专业版 22H2（10.0.19045）/ x86_64 / QEMU。覆盖安装、正常关机后的开机恢复、原账号/事项/日历/附件保留，以及真实管理员确认的主机配置、恢复个人模式和最终业务保留均通过。
- WPS 12.1.0.28488 x64 新安装版验收通过：六项排版功能、五字体读取、三页金样及保存后重新打开核验均通过。操作步骤见 [Windows 安装说明](https://github.com/linsky-dev/PartyOps/blob/main/docs/windows-installation-20261018.zh-CN.md)。
- 安装版真实 UAC 超时未实测；该修复由正式源码测试、构建与安装载荷绑定验证，不宣称其他平台或完整生命周期全部通过。

## 麒麟 ARM64 完整候选（2026-10-16）

- 修复旧数据升级、启动配置检查和排版运行库依赖问题，提供完整安装包，接替旧 ARM64 主包与诊断修复包。其余九个安装包及其验收范围保持不变。
- 验证环境：官方银河麒麟桌面 V10 SP1 2503 / aarch64 / QEMU / WPS 12.8.2.1119.AK.preload.sw。覆盖安装、重启和原合成账号、事项、日历、附件保留通过；六项排版、字体和三页金样通过。
- 新包与已安装验收版本的程序和安装脚本一致，仅更新验收记录与校验清单；新包解包后的程序、排版宿主自检通过。麒麟 9000C 实机及其他系统版本仍需复验。
- **先备份，再覆盖安装，不要先卸载或删库。安装后核对原账号、事项、日历和附件，并再次启动确认。**


## 本次更新

- 新增本机智能编排与步骤确认，支持模型规划、权限校验和规则回退。
- 修复 Windows 启动诊断误读历史日志及管理员启动错误反馈问题；启动修复候选的安装版验收范围见专用说明。
- 修复个人模式配置读取和数据目录处理，保留已保存的个人数据目录。
- 修复同一电脑不同账号的公文排版实例互相干扰，分别绑定排版端口和访问票据。
- 缩短公文排版临时路径，保留原始中文文件名，并提示超长路径问题。
- 完善 Windows 系统及当前用户字体读取，支持识别仅为当前用户安装的字体，并在排版时重新检查。
- 修复国产 Linux 用户配置解析及桌面启动，配置异常时提供明确诊断和修复向导，保留原业务数据。
- 新增龙芯 LoongArch64 DEB 安装包，并完善 Windows 7 与 Windows 10 的 32 位安装包适配。
- 新增 Intel x86_64 macOS RC6 安装包；有限验收范围内的排版与批量格式功能可用，分页结果需人工核对。
- 增补 Apple Silicon arm64 macOS 安装包，最低支持 macOS 15.0；在 macOS 15.7.9 / arm64 GitHub 原生主机完成安装后资源签名校验和四项 CLI 自检。GUI/Finder 启动、WPS 六功能、业务功能、OCR 模型、协同与生命周期未验证。

## 安装包验证环境

| 安装包 | 实际验证环境 |
| --- | --- |
| PartyOps_1.4.5-rc.6_windows_amd64_startup-20261018.exe | Windows 10 专业版 22H2（10.0.19045） / x86_64 / QEMU / WPS 12.1.0.28488；六项排版、五字体、三页金样及保存重开通过 |
| PartyOps_1.4.5-rc.6_windows7_amd64.exe | Windows 7 SP1（6.1.7601） / x86_64 / QEMU / WPS 版本未记录 |
| PartyOps_1.4.5-rc.6_linux_amd64.deb | UOS 桌面专业版 20（1070） / x86_64 / QEMU / WPS 版本未记录 |
| PartyOps_1.4.5-rc.6_linux_arm64_kylin-full-20261016.deb | 官方银河麒麟桌面 V10 SP1 2503 / aarch64 / QEMU / WPS 12.8.2.1119.AK.preload.sw |
| PartyOps-1.4.5-0.rc.6.1.x86_64.rpm | openEuler 24.03 LTS-SP2 / x86_64 / QEMU / WPS 版本未记录 |
| PartyOps-1.4.5-0.rc.6.1.aarch64.rpm | openEuler 24.03 LTS-SP2 / aarch64 / QEMU / WPS 版本未记录 |
| PartyOps_1.4.5-rc.6_linux_loong64.deb | Deepin 25.2.0 / loongarch64 / QEMU / WPS 版本未记录 |
| PartyOps_1.4.5-rc.6_windows7_x86.exe | Windows 7 SP1（6.1.7601） / i686 / QEMU / WPS 12.1.0.28505；Windows 10 22H2（10.0.19045） / i686 / QEMU / WPS 12.1.0.28505 |
| PartyOps_1.4.5-rc.6_macos_x86_64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg | macOS 15.7.9 / x86_64 / VMware / WPS 12.1.29166 |
| PartyOps_1.4.5-rc.6_macos_arm64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg | macOS 15.7.9 / arm64 / GitHub 原生托管主机；WPS 未测试 |

Intel Mac 为有限验收：分页统计前端 2 页、WPS 3 页，需人工核对正文、字体和页码；14 处引号的 Times New Roman 字体检查通过。未使用 Developer ID、未公证，AI 未配置模型；完整生命周期、RC4 升级、首次设置及跨机迁移未验证。国内全文件回读确认时间为 2026-10-08 18:42:10（北京时间，UTC+8）。

macOS 支持范围：Intel x86_64 为 macOS 11.0 及以上；Apple Silicon arm64 为 macOS 15.0 及以上。Apple Silicon 应用使用 ad-hoc 签名，安装器未签名、未公证；AI 模型需另行配置。

### Apple Silicon arm64 macOS 候选包

- PartyOps_1.4.5-rc.6_macos_arm64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg 在 macOS 15.7.9 / arm64 GitHub 原生主机完成安装后资源签名校验和四项 CLI 自检。
- GUI/Finder 启动、WPS 六功能、业务功能、OCR 模型、协同及生命周期未验证。
- RC.6 共十个安装包；其他平台包的验证范围及 Intel Mac 回执沿用原记录。
