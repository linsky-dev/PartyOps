# 云端构建与验证矩阵

以 2026-11-19 实际运行的 [六平台探测](https://github.com/linsky-dev/PartyOps/actions/runs/44439294911) 为基线。GitHub 的免费标签范围见[标准托管 Runner 文档](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)。

| Runner | 环境探测 | 本轮发行验证 |
| --- | --- | --- |
| `windows-2025` | PASS | Windows Server 工具链探测。Windows 10/11 客户端 x64 原生安装另行验收。 |
| `windows-11-arm` | PASS（原生国密运行时） | [2026-11-20 运行 #44704652210](https://github.com/linsky-dev/PartyOps/actions/runs/44704652210) 从固定源码在原生 ARM64 上编译 OpenSSL 3.5.7 和 cryptography 50.0.0；SM3、RFC 8998 SM4-GCM 与篡改拒绝均 PASS。官方 ARM64 wheel 仍缺，完整 PartyOps 原生安装包、WPS/升级尚未验收。 |
| `ubuntu-24.04` | PASS | amd64 依赖、构建和软件包冒烟。UOS x64 和 openEuler x64 专项另行验收。 |
| `ubuntu-24.04-arm` | PASS | ARM64 依赖、构建和软件包冒烟。麒麟 ARM64、UOS ARM64 和 openEuler ARM64 专项另行验收。 |
| `macos-15-intel` | PASS | 当前候选 PKG 和真实 WPS 待专项验证，低版本 macOS 按承诺范围补测。 |
| `macos-15` | PASS | 当前候选 PKG 和真实 WPS 待专项验证。 |

Windows 7 SP1 x86/x64、Deepin 25.2.0 LoongArch64、麒麟与 UOS 桌面、openEuler 24.03 LTS-SP2，以及实际声明的 Windows 客户端和旧版 macOS 需对应系统、架构、安装权限和原生图形会话。真实 WPS 场景使用已核实的安装来源、对应版本和合成文档。交叉构建记录 `BUILD_ONLY`，模拟运行记录 `EMULATED`。RC6 的[逐包验证环境](https://github.com/linsky-dev/PartyOps/blob/main/docs/release-validation-environments-1.4.5-rc.6.json)用于确定基线和目标系统。

## Windows ARM64 AI 推理依赖隔离研究

完整应用仍受锁定 `onnxruntime==1.22.1` 等原生 wheel 缺失影响。由于上游后续版本提供 ARM64 wheel，本轮增加一个 **仅在原生 Windows ARM64 Runner 执行的隔离候选探测**，使用 `onnxruntime==1.24.2`、`numpy==2.3.5`、`onnx==1.23.2`、`tokenizers==0.23.3` 执行合成 ONNX Identity CPU 推理、Tokenizer JSON 读取/批量编码及 3 输入嵌入接口模拟。结果单独归为 `BUILD_ONLY`，并与现有 PartyOps 模型、生产锁文件、真实 WPS 和十包验收明确隔离。即使合成模型运行成功，也必须进一步评审 BGE/本地模型算子兼容、应用加载路径、性能、打包、升级和原生验收，才可以考虑 ARM64 特有依赖方案。来源：https://pypi.org/project/onnxruntime/1.24.2/ 、https://pypi.org/project/numpy/2.3.5/ 、https://pypi.org/project/onnx/1.23.2/ 。

## Windows ARM64 PDF 运行时隔离验证

目前 PartyOps 正式锁定 `PyMuPDF==1.26.0`，没有对应的 Windows ARM64 原生 wheel。上游 `pymupdf-lite==2.1` 已提供 `win_arm64` wheel，但官方声明它缺少完整 PyMuPDF Layout 引擎。新建只读 GitHub ARM64 独立作业只用合成文件检验当前 PartyOps 调用到的 `fitz.open`、`get_text("text")`、`get_text("text", sort=True)`、`Matrix`、`get_pixmap`、`Pixmap`、加密文档 `needs_pass` 及无效内容拒绝。不得因这个实验就修改已验收的 WPS 排版、`backend/uv.lock`，也不得把布局分析、真实档案/中文 OCR、安装/签名/升级或供应链许可证核查标记为通过。

候选来源：PyMuPDF Lite 2.1 的官方 PyPI 页 https://pypi.org/project/pymupdf-lite/2.1/ 及官方精简版说明 https://github.com/pymupdf/PyMuPDF/blob/main/README-lite.md 。状态以本轮原生 Actions 日志为准。

- **原生 PDF API 首轮实证：** [GitHub Actions #44740939940](https://github.com/linsky-dev/PartyOps/actions/runs/44740939940) 在 Windows ARM64 通过 PyMuPDF Lite 2.1 的十项合成接口测试，包含旧 `fitz` API 兼容导入、PDF 读取、提取、渲染和异常拒绝；[临时 BUILD_ONLY 证据](https://github.com/linsky-dev/PartyOps/actions/runs/44740939940/artifacts/14631305434)。`fitz` 已提示将来弃用，仅属适配风险记录，不修改已有排版代码。
- **业务模块专项（已通过原生云端合成测试）：** 新增隔离验证 `app.intake._extract_pdf` 的真实 PartyOps 源码路径，使用纯合成 PDF 测试正常识别、加密拒绝、超过 500 页上限的拒绝；不接触真实文件、OCR 引擎、WPS 或完整安装包，结果必须单独留证。

- **原始 EmbeddingRuntime 业务代码的合成复验（已通过原生云端测试）：** 在隔离 Windows ARM64 Runner 上直接通过 AST 提取 GitHub 源码中的原始 `EmbeddingRuntime` 类，依赖项在测试命名空间中注入，而不修改 `backend/app/local_ai.py`。使用合成 WordLevel tokenizer 和只有 Cast/Unsqueeze/Concat 的微型 ONNX 图，运行原 `encode()` 路径，覆盖 CLS/Mean 池化、padding、查询前缀、模型切换、维度和不支持池化拒绝、卸载。只可认定源方法逻辑与隔离候选运行时的兼容性，不代表全应用依赖、正式 BGE 模型包或生产 AI 已验收。

## Windows ARM64 真 BGE 模型权重专项（独立候选，已实证 BUILD_ONLY）

新增 `cloud-arm64-real-bge-candidate.yml` 仅在原生 Windows ARM64 上运行，使用固定提交 `onnx-community/bge-small-zh-v1.5-ONNX@18e2da99700c49ed48c0a0e3683da39348fbbb36` 的 FP32 ONNX 模型及外置权重，配合固定上游 `BAAI/bge-small-zh-v1.5@7999e1d3359715c523056ef9478215996d62a620` 的 tokenizer.json。下载过程核对预置 SHA-256 与大小上限，核验不通过立即拒绝，不把权重或任何私钥上传到 GitHub 审查附件。运行现有 `EmbeddingRuntime` 源码提取类，验证中文真实 512 维向量、CLS 归一化、相同/不同文本相似度、查询前缀和卸载。

注意：ONNX Community 是**第三方导出**，并非已有 RC6 正式签名的 94,965,776 字节 `.partyops-modelpack`，也不声称与历史 `3f24c066…` ONNX 逐字节一致。通过只意味着原生 ARM64 上这组可溯源真实权重与隔离候选运行时可协同工作；正式模型包签名、PartyOps 导入启用、整包安装、资源指标、WPS 和许可证/再分发审查仍是独立门禁。来源：https://huggingface.co/onnx-community/bge-small-zh-v1.5-ONNX/commit/18e2da99700c49ed48c0a0e3683da39348fbbb36 。

- 固定 tokenizer SHA 纠正记录：2026-11-20 原生 ARM64 从 `BAAI/bge-small-zh-v1.5@7999e1d…` 下载到 439,125 字节，SHA-256 为 `48cea5d44424912a6fd1ea647bf4fe50b55ab8b1e5879c3275f80e339e8fae26`。历史 RC6 验收表中 `258135af…` 为历史封装输入，不能作为当前 BAAI 上游 tokenizer 的文件 SHA；首轮检测因此阻断且未执行真实模型推理。已在 [原生 ARM64 真 BGE 候选 #44878080740](https://github.com/linsky-dev/PartyOps/actions/runs/44878080740) 实际复验通过；此项属于第三方导出权重专项，不能代替正式 RC2 签名包。

## ARM64 签名模型包 → 原始向量化连续测试

增量测试 `scripts/probe_arm64_signed_embedding_chain.py` 使用原始 PartyOps 构建器、原始模型包校验函数和原始 `EmbeddingRuntime` 类。仅在原生 Windows ARM64 的固定 `cryptography==50.0.0` 源构建验证后，安装实验专用 `numpy==2.3.5`、`onnxruntime==1.24.2`、`onnx==1.23.2`、`tokenizers==0.23.3` 官方原生 wheel，生成可执行但极小的合成 ONNX/分词器，使用一次性 Ed25519 密钥打包。验签和 SHA-256 成功后才展开模型，原 `encode()` 验证批量向量、查询前缀和归一化；载荷篡改必须在执行前阻断。

测试钥匙、原始 ZIP 和所有模型文件在临时目录内创建及销毁，GitHub 仅留签名包的 SHA-256、源码 SHA、候选依赖版本、脱敏结果。此测试没有正式公钥、RC6 模型包、真实用户数据或应用导入/数据库激活，不代表模型包可公开发布。WPS 核心、生产锁文件和现有 `backend/app` 均不改动。

## Windows ARM64 模型包真实安装与激活源码专项（原生云端已实证）

基于已 PASS 的签名包 → ONNX 链路，增加 `scripts/probe_arm64_modelpack_db_lifecycle.py`：原生 ARM64 安装与当前锁一致的 SQLAlchemy、Pydantic、Pydantic Settings，使用生产 `app.model_packs` 及 `app.models` 原始模块（不是复制实现），在**独立内存 SQLite、一次性目录、一次性测试密钥**中执行真实 `install_model_pack`、`verify_installed_pack`、`activate_model_pack`、`active_model_pack`、`EmbeddingRuntime.encode`。验证模型包签名、受控目录存储、重复导入拒绝、激活记录提交、已安装模型推理和存储文件损坏拒绝；全部测试材料在 Runner 退出前清理，仅保留无私钥的 BUILD_ONLY 证据。

该专项不启动 HTTP API 或既有正式安装程序，也不验证正式 RC6 签名权重、真实 WPS、全部 ARM64 依赖或数据库升级路径。验收状态以实际 Actions 运行结果为准。

## 正式签名 BGE 历史发布包来源审查（原生云端已实证）

前期阻塞报告将 RC6 正式 BGE 包误记为原始文件不可获得。现已通过 GitHub Releases API 核实，历史 RC6 验收使用的 `PartyOps_BGE_Small_ZH_1.5.0.partyops-modelpack` 原文件仍存在于 [v1.4.5-rc.2 Release](https://github.com/linsky-dev/PartyOps/releases/tag/v1.4.5-rc.2)，大小 **94,965,776 字节**。对照仓库历史 SHA-256 `57df3669e368497ad6d428dab309fb7665229a57edfe3a4fe1f2afd6a0f1a155`，新建 `scripts/probe_arm64_official_bge_signature.py` 将在原生 ARM64 Runner 下载文件，先完成长度及完整 SHA-256 校验，再使用 `packaging/uos/update-public-key.txt` 作为外部信任根调用未修改的 `app.model_packs._validate_manifest`，逐文件核验签名和摘要，检查原始 `architecture`/`architectures`、`platforms` 声明。所有下载文件仅保存在一次性目录，不重签、不修改模型、不把原包上传为 Actions 制品。

该专项若确实通过，只能标记为**正式已发布包的来源和签名/载荷真实验证**。如原包仅声明 AMD64，ARM64 的正式导入激活继续保留 `BLOCKED`；即使原清单声明 ARM64，此专项也没有完成生产 HTTP 上传、数据库激活、真实 BGE 推理和完整原生安装。生产发布仍需独立签名、WPS 与升级门禁。
## 2026-11-21 官方模型独立原生闭环与新增持久化专项

- **已实证 PASS（BUILD_ONLY）：** [Windows ARM64 原生 #44866043200](https://github.com/linsky-dev/PartyOps/actions/runs/44866043200) 将原版 RC2 已发布 BGE 签名包用正式外部信任根与真实 PartyOps 模型包管理代码在隔离 SQLite 中导入、激活、重复导入拒绝，调用原始 EmbeddingRuntime 验证中文实际向量、查询向量与卸载。标志 `PARTYOPS_ARM64_OFFICIAL_SIGNED_BGE_IMPORT_ACTIVATION_INFERENCE_PASS`；此前仅验证官方包声明、未实际导入的限制在此单个独立脚本范围内已解除。
- **SQLite 全新引擎重开 PASS（BUILD_ONLY）：** [#44873341720](https://github.com/linsky-dev/PartyOps/actions/runs/44873341720) 通过临时 SQLite 文件持久化、旧引擎与模型运行实例关闭、独立新 Engine/Session/EmbeddingRuntime 重开激活记录及实际中文向量结果核对。
- **继续阻断：** 完整 Windows ARM64 PartyOps 依赖/安装/升级/回滚、授权真实 WPS、多种国产系统同构原生环境、服务端完整 API 与签名生产分发，均不能由上述 BUILD_ONLY 自检替代。
- **全新 Python 进程重载与真 ONNX 篡改拒绝 PASS（BUILD_ONLY）：** [#44877246001](https://github.com/linsky-dev/PartyOps/actions/runs/44877246001) 在原生 Windows ARM64 实际重启新的 Python 解释器、读取同一临时 SQLite 模型状态并复做真实 BGE 向量，后仅篡改临时提取 ONNX 的一个字节且恢复 mtime，原始激活函数重新哈希并拒绝 `MODEL_PACK_CORRUPT`；完整 PartyOps 进程及正式安装器未重启。
- **正式签名历史 BGE 管理 HTTP 路径 PASS（Windows x64 TestClient / BUILD_ONLY）：** [#44878080788](https://github.com/linsky-dev/PartyOps/actions/runs/44878080788) 用原已发布包、官方外部公钥和冻结 Windows x64 依赖，通过真实 FastAPI TestClient 路由完成管理员上传、激活、实际中文推理及重复上传拒绝；后续 [#44886050440](https://github.com/linsky-dev/PartyOps/actions/runs/44886050440) 实证匿名/普通用户权限拒绝、非法能力拒绝、临时提取 ONNX 同长度/mtime 篡改后重新通过 HTTP 激活拒绝。后者关联的 Linux 源码测试 207 通过，Ruff I001 单行导入顺序问题已定位、修复待复验。
- **未解除的发行门禁：** 本文件全部源代码/隔离模型证据都不代表 Windows ARM64 生产级锁定依赖完整运行、真实 Windows 10/11 的安装与升级、真 WPS 排版、十包和十二原生目标安装验收或两套网站发布。
