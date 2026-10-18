# PartyOps 文档索引

这里集中列出 PartyOps 的当前操作文档、发布证据和历史升级记录。首次安装或试用请从“当前版本”开始；带旧版本号的文件仅用于迁移、审计和故障追溯，不能替代当前候选说明。

## 当前公开候选 1.4.5-rc.6

- [v1.4.5-rc.6 预发布说明](release-notes-v1.4.5-rc.6.md)
- [v1.4.5-rc.6 支持矩阵](support-matrix-1.4.5-rc.6.json)
- [v1.4.5-rc.6 本地验收记录](acceptance-1.4.5-rc.6.md)
- [v1.4.5-rc.6 模型验收记录](model-validation-1.4.5-rc.6.md)
- [Windows x64 启动修复候选安装说明](windows-installation-20261018.zh-CN.md)
- RC.6 当前提供十个安装包；本批更新 Windows x64 启动修复候选与麒麟 ARM64 完整候选；其他平台包与既有验收范围保持原记录；Windows x64 候选在 Windows 10 专业版 22H2（10.0.19045）/ x86_64 / QEMU / WPS 12.1.0.28488 环境下，覆盖/重启恢复、主机配置、恢复个人及原数据保留，以及六项排版、五字体、三页金样和保存重开验收通过。安装版真实 UAC 超时未实测；各包验证环境与范围见发布说明，未宣称完整生命周期全部通过。
- [麒麟 ARM64 签名发布清单](release-manifest-kylin-arm64-20261016.json)、Windows x64 独立签名清单 `release-manifest-windows-startup-20261018.json` 与[逐包实际验证环境](release-validation-environments-1.4.5-rc.6.json)分别对应各自候选；Windows 验收更新日期为 2026-10-18，旧日期只代表对应历史代码和制品。

## 通用操作文档与历史 1.4.4 记录

- [使用说明](user-guide.md)
- [安装、升级与回滚](upgrade-1.4.4.md)
- [v1.4.4 发布说明](release-notes-v1.4.4.md)
- [v1.4.4 支持矩阵](support-matrix-1.4.4.json)
- [v1.4.3-rc.9 发布说明（历史）](release-notes-v1.4.3-rc.9.md)
- [v1.4.3-rc.8 发布说明（历史）](release-notes-v1.4.3-rc.8.md)
- [v1.4.3-rc.7 发布说明（历史）](release-notes-v1.4.3-rc.7.md)
- [v1.4.3-rc.5 发布说明](release-notes-v1.4.3-rc.5.md)
- [v1.4.3-rc.4 历史发布说明](release-notes-v1.4.3-rc.4.md)
- [v1.4.3-rc.3 发布说明（历史）](release-notes-v1.4.3-rc.3.md)
- [v1.4.3-rc.2 发布说明（历史）](release-notes-v1.4.3-rc.2.md)
- [v1.4.3-rc.1 历史发布说明](release-notes-v1.4.3-rc.1.md)
- [安装检查清单](installation-checklist.md)
- [部署与独立搭建](deployment.md)
- [2026 党员发展规则说明](party-development-rules-2026.md)
- [1.4.3 发布就绪判定](release-readiness-1.4.3.md)
- [1.4.3 候选验收记录](acceptance-1.4.3.md)
- [1.4.3 制品清单](artifact-manifest-1.4.3.md)

## 运维与通用说明

- [备份与恢复](backup-restore.md)
- [长期运行手册](operations-runbook.md)

## 1.4.2 历史候选

- [安装、升级与回滚](upgrade-1.4.2.md)
- [v1.4.2-rc.1 发布说明](release-notes-v1.4.2-rc.1.md)

## 发布门禁与证据

- [国内下载与官网发布工作流](domestic-release-workflow.md)
- [历史下载包上传与 EdgeOne 部署记录](cloud-studio-edgeone-deployment.md)
- [1.4.2 发布就绪判定](release-readiness-1.4.2.md)
- [1.4.2 候选验收记录](acceptance-1.4.2.md)
- [1.4.2 制品清单](artifact-manifest-1.4.2.md)
- [需求追踪矩阵](requirements-matrix.md)
- [交付记录](delivery-record.md)
- [安全加固记录](security-hardening-2026-08-04.md)
- [第三方组件与许可证](../THIRD_PARTY_NOTICES.md)
- [安全报告策略](../SECURITY.md)

## 架构与设计

- [1.3.0 架构说明](architecture-1.3.0.md)
- [1.2.0 架构说明](architecture-1.2.0.md)
- [产品设计规范](../DESIGN.md)
- [界面验收记录](../design-qa.md)

## 历史版本

- [完整更新记录](../CHANGELOG.md)
- `upgrade-*.md` 与 `党建智办-*-更新说明.txt`：旧版本升级路径和中文发布说明。
- `release-readiness-*.md`、`acceptance-*.md` 与 `artifact-manifest-*.md`：历次候选发布的门禁与制品证据。
- `*-hotfix-1.1.3.md`：1.1.3 现场修复的历史记录。

历史文档中的包名、命令和测试结论只适用于对应版本。下载和安装前始终回到[项目主页](../README.md)核对最新 Release、SHA-256、签名状态和已知限制。
