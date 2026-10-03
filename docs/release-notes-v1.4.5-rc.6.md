# PartyOps 1.4.5-rc.6

### 相比 RC.4 的更新

- 新增本机智能编排与步骤确认，支持模型规划、权限校验和规则回退。
- 修复 Windows 启动诊断误读历史日志的问题，准确区分本次启动的权限、依赖和运行错误。
- 修复个人模式配置读取和数据目录处理，保留已保存的个人数据目录。
- 修复同一电脑不同账号的公文排版实例互相干扰，分别绑定排版端口和访问票据。
- 缩短公文排版临时路径，保留原始中文文件名，并提示超长路径问题。
- 完善 Windows 系统及当前用户字体读取，支持识别仅为当前用户安装的字体，并在排版时重新检查。
- 修复国产 Linux 用户配置解析及桌面启动，配置异常时提供明确诊断和修复向导，保留原业务数据。
- 新增龙芯 LoongArch64 DEB 安装包，并完善 Windows 7 与 Windows 10 的 32 位安装包适配。

### 安装包验证环境

| 安装包 | 实际验证环境 |
| --- | --- |
| PartyOps_1.4.5-rc.6_windows_amd64.exe | Windows 10 22H2（10.0.19045） / x86_64 / QEMU / WPS 版本未记录 |
| PartyOps_1.4.5-rc.6_windows7_amd64.exe | Windows 7 SP1（6.1.7601） / x86_64 / QEMU / WPS 版本未记录 |
| PartyOps_1.4.5-rc.6_linux_amd64.deb | UOS 桌面专业版 20（1070） / x86_64 / QEMU / WPS 版本未记录 |
| PartyOps_1.4.5-rc.6_linux_arm64.deb | UOS 桌面专业版 20（1070） / aarch64 / QEMU / WPS 版本未记录 |
| PartyOps-1.4.5-0.rc.6.1.x86_64.rpm | openEuler 24.03 LTS-SP2 / x86_64 / QEMU / WPS 版本未记录 |
| PartyOps-1.4.5-0.rc.6.1.aarch64.rpm | openEuler 24.03 LTS-SP2 / aarch64 / QEMU / WPS 版本未记录 |
| PartyOps_1.4.5-rc.6_linux_loong64.deb | Deepin 25.2.0 / loongarch64 / QEMU / WPS 版本未记录 |
| PartyOps_1.4.5-rc.6_windows7_x86.exe | Windows 7 SP1（6.1.7601） / i686 / QEMU / WPS 12.1.0.28505；Windows 10 22H2（10.0.19045） / i686 / QEMU / WPS 12.1.0.28505 |
