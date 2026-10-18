# 官网 Windows x64 更新最小方案（待真实发布回读后执行）

目标制品：`PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe`，449978846 字节，SHA-256 `3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27`。独立签名清单：`release-manifest-windows-startup-20261017.json`。

当前仅准备本地草稿。官网下载源和 GitHub Release 尚未完成本批发布回读；WorkBuddy 窗口恢复失败，正等待用户打开。不得提前切换公开下载链接或写入上传/回读时间，也不得将现有九个平台包及评论后台纳入本批修改。

## 最小变更范围

1. `qa/vm-lab/release-preparation/release-validation-environments-20261002.json`：只替换 `package_id=windows_amd64` 的现有一条记录，维持十包总数及其余九条原值。更新文件名、大小、SHA、Win10 Pro 22H2（19045）/x64/QEMU/WPS 12.1.0.28488 环境、六项排版/五字体/三页金样/保存重开范围及未覆盖项；`full_lifecycle_verified` 继续为 `false`。`confirmed_at` 和 `time_meaning` 只能在真实上传并完成完整回读后按回执填写。
2. `website/src/siteContent.js`：更新 Windows SHA 文件链接；Windows x64 卡片单独指向 `release-manifest-windows-startup-20261017.json`。为它增加独立 `windowsStartupManifest` 链接键，仅替换 Windows x64 卡片的清单链接；保留共用 `windowsManifest` 和麒麟 `product.manifestPath`，避免其他平台改指新清单。补一条简短 RC.6 Windows 验收更新，日期写验收更新日 2026-10-18，不改从现有发布证据派生的 `releasedAt`。
3. 仅在真实发布回读后，把独立 Windows 清单放入 `website/public/releases/v1.4.5-rc.6/` 并启用对应路径；GitHub / 国内下载 URL 的切换、SHA 文件回读和站点发布按既有发布驱动执行。当前草稿不包含虚构的上传或回读时间。
4. `website/src/siteContent.test.js`：将 Windows x64 旧包名断言更新为新包名，断言 SHA/下载地址/Windows 专属清单地址；断言 Windows 7、x86、Linux、Mac 的既有映射保持原值。
5. `website/src/components/download/ValidationEnvironment.test.jsx`：断言 Windows 行显示真实 Win10 x64/WPS 环境与六项排版、五字体、三页金样范围，且完整生命周期仍未验证。
6. `website/src/components/download/ArtifactIntegrity.test.jsx`：断言 Windows 新文件名、字节数、SHA 与逐包清单绑定。上传完成与回读时间断言必须取真实匿名 GET/HEAD 全文件核验回执，不能填计划时间。

## 执行顺序与验收

取得用户打开后的 WorkBuddy 窗口后，按既有发布流程完成国内与 GitHub 上传，逐个完整 GET 核对目标字节数和 SHA；成功后再落入实际确认时间并启用 Windows 专属清单链接。随后运行定点测试：

```powershell
npm exec -- vitest run src/siteContent.test.js src/components/download/ValidationEnvironment.test.jsx src/components/download/ArtifactIntegrity.test.jsx
```

再按既有本地流程完成官网构建与匿名回读。验证时确认 Windows 新制品和独立清单正确，另外九包及评论后台没有改动。本方案未应用到官网源码，也未执行发布或测试。