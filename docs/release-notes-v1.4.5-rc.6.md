# PartyOps 1.4.5-rc.6

本批发布 8 个已冻结并获对应范围批准的 Windows、Linux 和龙芯安装包。Win7 x86 沿用 b3，按用户确认的本机字体读取和实际排版范围验收。此版本为候选版本；不宣称全部系统、全部功能或完整安装升级生命周期通过。

Intel Mac 已在 macOS 15.7.9（24G830）与 WPS 12.1.29166 内核查签名、沙盒、真实桌面启动和本地接口，并检索 WPS/Apple 官方资料。WPS 能正常打开，但自动排版接口仍未监听，未找到适用的官方修复办法；本批暂停。M 系列等待远程实机。两个 Mac 不包含本批 RC6 新包，原 RC4 记录继续保留。

## 实际验证环境

| 包与版本 | 实际验收环境 | 范围 | 未覆盖 |
| --- | --- | --- | --- |
| windows_amd64 · 1.4.5-rc.6 | Windows 10 22H2（10.0.19045） / x86_64 / QEMU / WPS 版本未记录 | 安装、普通用户启动、数据保留、健康与中文OCR | 完整生命周期、全部映射系统和正式旧包升级未覆盖 |
| windows7_amd64 · 1.4.5-rc.6 | Windows 7 SP1（6.1.7601） / x86_64 / QEMU / WPS 版本未记录 | 安装、普通用户启动、中文OCR、WPS转换与补字体后排版、冷启动 | 未追加完整生命周期；保留字体名称检查的非阻断问题 |
| linux_amd64 · 1.4.5-rc.6 | UOS 桌面专业版 20（1070） / x86_64 / QEMU / WPS 版本未记录 | 安装、普通用户桌面启动、健康与原业务数据保留 | 完整生命周期与全部国产发行版未覆盖 |
| linux_arm64 · 1.4.5-rc.6 | UOS 桌面专业版 20（1070） / aarch64 / QEMU / WPS 版本未记录 | 普通业务、OCR、同Guest独立会话协作 | 完整生命周期、跨机器协作、WPS版本及旧包升级未覆盖 |
| rpm_x86_64 · 1.4.5-rc.6 | openEuler 24.03 LTS-SP2 / x86_64 / QEMU / WPS 版本未记录 | 安装、普通用户桌面启动、健康与原业务数据保留 | 完整生命周期与全部RPM发行版未覆盖 |
| rpm_aarch64 · 1.4.5-rc.6 | openEuler 24.03 LTS-SP2 / aarch64 / QEMU / WPS 版本未记录 | 普通业务、OCR、同Guest独立会话协作 | 完整生命周期、跨机器协作、WPS版本及旧包升级未覆盖 |
| linux_loong64 · 1.4.5-rc.6 | Deepin 25.2.0 / loongarch64 / QEMU / WPS 版本未记录 | 原版Deepin安装、业务/OCR、真实Firefox UI、WPS九项与三页金样、独立重启后检查 | 完整生命周期原blocked；旧版升级无基线；其他龙芯发行版未覆盖 |
| windows7_x86 · 1.4.5-rc.6 | Windows 7 SP1（6.1.7601） / i686 / QEMU / WPS 12.1.0.28505；Windows 10 22H2（10.0.19045） / i686 / QEMU / WPS 12.1.0.28505 | Win7：用户批准本机字体读取与排版；Win10：普通业务、OCR、重启、同包恢复、浏览器UI与WPS排版 | Win7严格三PDF金样与剩余生命周期未完成；Win10模型409、跨机器协作、旧升级及ARM未覆盖 |
| macos_x86_64 · 1.4.5-rc.4 | 本批未验收（当前RC4旧包） | 当前链接为RC4旧包；本批未完成该架构macOS验收 | Intel Mac本批暂停：接口未监听，权限排查未找到受支持修复 |
| macos_arm64 · 1.4.5-rc.4 | 本批未验收（当前RC4旧包） | 当前链接为RC4旧包；本批未完成该架构macOS验收 | M系列等待用户提供远程机器，本批尚未完成新验收 |


WPS 版本未记录的项目保持未知，不借用其他目标的版本。所有环境来自原实际虚拟机记录；映射兼容的其他系统不等于实测系统。Win7 严格金样、Win10 模型 409 和其他未覆盖范围仍保留，未把原 failed/partial/blocked 记录提升为完整通过。

## 校验与来源

下载后请使用附件的 SHA-256 文件核对安装包；签名 `release-manifest.json` 保存逐包摘要、构建及验收证据。源码 HEAD 与各包构建来源不同，清单中未证实的 source_commit 保持 null，不把 Release 的目标提交当作八包共同构建来源。

国内主下载已进入官网；GitHub 附件为同一冻结批次。国内 8 个安装包已经在同一 CloudStudio 受管应用封存，并经新路由逐包完整公网回读，字节数与 SHA-256 全部匹配签名清单。确认时间：2026-10-03（北京时间）。官网已于 2026-10-03 完成正式部署及公开内容回读。

升级前在系统内备份。遇到问题可恢复原包和备份；旧 Release 保留，不静默覆盖同名附件。此次发布同步未额外新增迁移；RC.4 源码升级 RC.6 已有 `0024 → 0025 → 0026` 迁移，应沿既有备份、迁移及失败恢复流程，逐包升级未覆盖项仍保留。

## 国内下载（已完整回读）

- [PartyOps-1.4.5-0.rc.6.1.aarch64.rpm](https://partyops-rc6-win7-x86.app.workbuddy.host/downloads/PartyOps-1.4.5-0.rc.6.1.aarch64.rpm)
- [PartyOps-1.4.5-0.rc.6.1.x86_64.rpm](https://partyops-rc6-win7-x86.app.workbuddy.host/downloads/PartyOps-1.4.5-0.rc.6.1.x86_64.rpm)
- [PartyOps_1.4.5-rc.6_linux_amd64.deb](https://partyops-rc6-win7-x86.app.workbuddy.host/downloads/PartyOps_1.4.5-rc.6_linux_amd64.deb)
- [PartyOps_1.4.5-rc.6_linux_arm64.deb](https://partyops-rc6-win7-x86.app.workbuddy.host/downloads/PartyOps_1.4.5-rc.6_linux_arm64.deb)
- [PartyOps_1.4.5-rc.6_linux_loong64.deb](https://partyops-rc6-win7-x86.app.workbuddy.host/downloads/PartyOps_1.4.5-rc.6_linux_loong64.deb)
- [PartyOps_1.4.5-rc.6_windows7_amd64.exe](https://partyops-rc6-win7-x86.app.workbuddy.host/downloads/PartyOps_1.4.5-rc.6_windows7_amd64.exe)
- [PartyOps_1.4.5-rc.6_windows7_x86.exe](https://partyops-rc6-win7-x86.app.workbuddy.host/downloads/PartyOps_1.4.5-rc.6_windows7_x86.exe)
- [PartyOps_1.4.5-rc.6_windows_amd64.exe](https://partyops-rc6-win7-x86.app.workbuddy.host/downloads/PartyOps_1.4.5-rc.6_windows_amd64.exe)

新版 WorkBuddy 发布入口会按应用 ID 复用 sandbox；本批采用同一原 CloudStudio 应用内固定 8 文件接收器，每文件独立元数据、令牌、续传状态与封存，发布后台未更换。旧单文件部署失败记录保留；后续新接收器的八包完整回读已重新执行。

## 默认分支与源码说明

默认分支已同步 RC.6 已公开开发提交和本批发布文档，未包含本地未提交源码；rc.6 开发源码见 [release/1.4.5-rc.6 分支](https://github.com/linsky-dev/PartyOps/tree/release/1.4.5-rc.6)。该分支与标签不替代逐包构建来源，具体来源和未知值以签名发布清单为准。
