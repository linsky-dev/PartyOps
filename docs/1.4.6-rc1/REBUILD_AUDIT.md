# 1.4.6-rc.1 源码恢复审计

核对日期为 2026-11-19（UTC+8）。

## 恢复基线

- 仓库为 [linsky-dev/PartyOps](https://github.com/linsky-dev/PartyOps)，默认分支为 `main`。
- 恢复起点是 `ba205288982da4e7ddf490d7abdccfbff2d6c3af`，较 `v1.4.5-rc.6` 的 `a8aaf44984c1d9ce85981bd4756e71372c6b8cb8` 多 15 个提交，包含补充原生验收、Windows 启动修复和后续发布记录。开发分支为 `feature/1.4.6-rc1-cloud-rebuild`。
- 数据库基线为 Alembic `0026`。已有后端 pytest、前端 Vitest 与 Playwright、安装版自检、原生业务验收及 macOS 手动构建。历史测试记录仅适用于其中注明的源码和制品。
- 使用健康 D 盘独立检出和 GitHub 托管构建。旧工作树及未提交内容无可核实的公开记录。

## 已有能力

Argon2 密码、数据库会话与管理员重置密码已存在。设备短期本地密文使用 Fernet。时间轴已保留实际事实和追加式纠正、作废历史，已有计划和法规日期计算。通知已有接收者与对象标识及简单跳转。普通恢复凭据、离线管理员恢复、本人改名、入网 SM4-GCM 和全部通知的统一定位是本次重建范围。

## 文件与历史数据

| 位置 | 审计结论与处置 |
| --- | --- |
| `scripts/`、`packaging/` | 沿用当前构建及发布入口。云端入口从独立检出运行。 |
| `qa/vm-lab/` | 使用当前检出和显式兼容构建输入。历史 VM 诊断、遗留实验配置与清理脚本保留作审计，在本轮禁止调用涉及故障盘或本地大型虚拟机的入口。 |
| `qa/acceptance/windows-startup-20261016/baseline/` | 保留历史 Windows 启动修复基线及验收证据。迁移至内容寻址存储前须做完整性、引用和可复现性验收。 |
| `vendor/document-formatter-source/` | 保留固定来源及指纹。旧路径仅作为历史定位信息，当前调用者提供实际工具链路径。 |
| 历史清单与文档 | 历史原路径、制品 SHA-256 和验证结果原样归档。 |

当前公开候选为 [v1.4.5-rc.6](https://github.com/linsky-dev/PartyOps/releases/tag/v1.4.5-rc.6)，包含 Windows 通用 x64、Win7 x64/x86、DEB amd64/arm64/loong64、RPM x86_64/aarch64、macOS Intel/Apple Silicon 共十包，适用范围以逐包验收证据为准。Windows 通用和麒麟 ARM64 分别使用 20261018 与 20261016 完整修复包。
