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

## Windows ARM64 真 BGE 模型权重专项（独立候选，待实证）

新增 `cloud-arm64-real-bge-candidate.yml` 仅在原生 Windows ARM64 上运行，使用固定提交 `onnx-community/bge-small-zh-v1.5-ONNX@18e2da99700c49ed48c0a0e3683da39348fbbb36` 的 FP32 ONNX 模型及外置权重，配合固定上游 `BAAI/bge-small-zh-v1.5@7999e1d3359715c523056ef9478215996d62a620` 的 tokenizer.json。下载过程核对预置 SHA-256 与大小上限，核验不通过立即拒绝，不把权重或任何私钥上传到 GitHub 审查附件。运行现有 `EmbeddingRuntime` 源码提取类，验证中文真实 512 维向量、CLS 归一化、相同/不同文本相似度、查询前缀和卸载。

注意：ONNX Community 是**第三方导出**，并非已有 RC6 正式签名的 94,965,776 字节 `.partyops-modelpack`，也不声称与历史 `3f24c066…` ONNX 逐字节一致。通过只意味着原生 ARM64 上这组可溯源真实权重与隔离候选运行时可协同工作；正式模型包签名、PartyOps 导入启用、整包安装、资源指标、WPS 和许可证/再分发审查仍是独立门禁。来源：https://huggingface.co/onnx-community/bge-small-zh-v1.5-ONNX/commit/18e2da99700c49ed48c0a0e3683da39348fbbb36 。
