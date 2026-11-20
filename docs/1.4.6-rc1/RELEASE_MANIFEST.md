# 1.4.6-rc.1 候选制品记录

源码通过[草稿 PR #16](https://github.com/linsky-dev/PartyOps/pull/16)集成，当前未冻结。发行制品、签名、公证、附件 SBOM 和正式下载地址为 `NOT_RUN`，以[验收报告](ACCEPTANCE_REPORT.md)为准。

冻结时按 Windows x64、Win7 x86/x64、DEB amd64/arm64/loong64、RPM x86_64/aarch64、macOS Intel/Apple Silicon 十个包登记源提交、依赖锁文件、制品名、大小、SHA-256、构建和原生证据链接、签名状态及下载回读。仅登记存在且验签通过的升级包。RC6 安装包及其历史补丁一律按原始版本保留。

PR 阶段新增只读源码/SBOM 预检（`cloud-pr-source-preflight.yml`）。该流程归档测试使用的 PR 合并提交并生成 Python 和前端锁定依赖的 CycloneDX 清单、SHA-256 和 `BUILD_ONLY` 来源证据；并非正式冻结、签名、安装包或发布许可，不能替代主分支的人工确认门禁。

RC1 候选证据区分**安装包**与**原生验收目标**：10 个制品目标复用已有发行分类，12 个系统环境分别保留独立安装、功能、签名、升级和 WPS 证据。Windows 10/11 x64 使用同一现代 Windows 安装包，但必须两套原生验收；UOS ARM64 和麒麟 ARM64 同理复用对应 DEB。新增 `scripts/rc1_release_targets.py` 维护精确映射，`cloud-release-gate.yml` 对缺少任何一项、重复目标、错误包关联或缺少可校验附件的结果一律阻断；候选清单默认 `NOT_RUN`，不可将 `BUILD_ONLY` 自动提升为 PASS。
