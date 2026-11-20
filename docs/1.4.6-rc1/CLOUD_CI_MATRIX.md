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
