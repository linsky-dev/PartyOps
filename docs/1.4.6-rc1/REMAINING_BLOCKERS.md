# 1.4.6-rc.1 发布阻塞

| 阻塞 | 当前证据 | 解除条件 |
| --- | --- | --- |
| EdgeOne 官网工程 | `partyops-cn-overseas` 当前生产 `dpv7nyk55sit` 和旧部署均显示 ZIP 拉取，项目未关联 Git。制品只含静态站点及少量源文件，项目 Blob 和 KV 页面未发现源码归档。 | 在同一 EdgeOne 项目中定位可导出的原上传工程、构建脚本和锁文件。两套站点用同一份冻结清单做预览与回读。 |
| 特殊目标原生环境 | 免费标准 Runner 未提供最新十包清单对应的全部目标系统。 | 提供同系统和处理器架构的授权远程会话、安装权限、WPS 和图形会话。见[云端矩阵](CLOUD_CI_MATRIX.md)。 |
| Win7 第三方安全回移 | Windows 7 两种锁文件的上游扫描命中 AnyIO，x64 也命中 fsspec。Certifi 本轮升级到仍支持 Python 3.8 的 2026.10.19，单项复扫通过。Pillow 12.3.0 和其他带本地构建标识的回移轮子须核对补丁实际范围。 | 审计并验证兼容更新、两架构轮子 SHA-256、离线依赖和原生行为。 |
| Windows ARM64 完整运行时与安装包 | [运行 #44704652210](https://github.com/linsky-dev/PartyOps/actions/runs/44704652210) 已证明：原生 Windows ARM64 上成功用固定 OpenSSL 3.5.7 + cryptography 50.0.0 sdist 编译国密 wheel，并通过 SM3/SM4-GCM 向量及篡改拒绝，**密码库来源探测阻塞已解除**；当前仍无 ARM64 原生 PartyOps 发行目标。 | 将完整锁定依赖、国密轮子及实际业务程序集成进入另行审查的 ARM64 候选包，逐项验证安装、重启、设备入网、升级、数据与真实 WPS。当前锁文件静态核查还显示：`numpy==2.2.6`、`PyMuPDF==1.26.0`、`tokenizers==0.21.4` 未提供该锁定版本的 Windows ARM64 wheel（虽有 sdist，但未在原生 ARM64 验证），`onnxruntime==1.22.1` 在锁文件中既无 Windows ARM64 wheel 亦无 sdist。不得将密码学成功误标为完整应用支持。 候选 wheel 属 BUILD_ONLY、未经签名与最终验收，不得直接用于生产发布。 |
| 新版 LibreOffice 供应链 | Windows x64 已核验修复版 26.2.7.3 的官方 MSI、签名、哈希及样本转换。保留从旧 RC6 提取的 26.2.5.2 作隔离对照。当前 Linux、macOS 和旧版 Windows 的锁定输入仍需按公告筛查并更新。 | 使用纳入对应修复且兼容目标的已签名上游输入或可审计回移。按目标复验完整性、证书、许可证、最低系统和排版运行。 [上游安全公告](https://www.libreoffice.org/security/) |
| 安装器前置证据及运行时 | 源码与版本一致，既有全功能测试指纹已过期。当前检出还缺少原生排版复验及多架构兼容运行时输入。 | 完成依赖依据、原生格式化测试与测试指纹，按架构取得明确来源的 OCR、LibreOffice、模型及适用的兼容载荷。保留原包质量门禁。 |
| Windows ARM64 PDF 库候选兼容性 | `PyMuPDF==1.26.0` 的正式锁定版本仍无 win_arm64 wheel。新版 `pymupdf-lite==2.1` 有官方原生 wheel，但缺少完整 Layout 引擎，不能推断全功能等价。 | 先用隔离 ARM64 Runner 验证 PartyOps 当前 `fitz` 读取、渲染及错误拒绝接口。之后需真实中文 PDF/OCR、功能回归、许可证与供应链审查、完整安装升级验收。不得覆盖正式版本。 |\n| Windows ARM64 应用依赖闭包 | 国密轮子已在原生 ARM64 构建并通过 SM3/SM4-GCM，不能代表完整应用支持。冻结锁文件仍有 ONNX Runtime 1.22.1、NumPy 2.2.6、PyMuPDF 1.26.0、tokenizers 0.21.4 等原生轮子缺口。 | 本轮新增原生 Windows ARM64 只读锁文件与 wheel 标签审计，使用实际 ARM64 Python 及现有锁文件生成 BUILD_ONLY 证据；后续分别取得合法兼容包或经审查的原生构建、功能回归和安装升级验证。不得直接修改既有 x64 的锁定版本规避此项。 |
| ARM64 官方签名模型包完整导入 | [原生 #44862849895](https://github.com/linsky-dev/PartyOps/actions/runs/44862849895) 已对正式 RC2 BGE 包完成 SHA-256、外部 Ed25519 签名和载荷完整性校验，原始清单含 Windows ARM64 声明；这尚不是正式 PartyOps 安装、真实模型业务导入激活和重启验收。 | 新增原始 BGE 真实导入/激活/中文向量推理的独立临时 SQLite 原生脚本，先按实际 Actions 结果验收；后续仍需完整 API、安装包、重启、签名、WPS 和升级实测。 |
| 冻结、签名与生产批准 | 未冻结源码或制品，未通过完整安全和原生门禁，未请求最终发布批准。 | 完成全部必需检查、可重现制品、升级与回滚演练和双站预览。由用户批准后使用受保护环境发布。 |
