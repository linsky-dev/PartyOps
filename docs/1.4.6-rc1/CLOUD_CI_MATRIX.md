# 云端构建与验证矩阵

以 2026-11-19 实际运行的 [六平台探测](https://github.com/linsky-dev/PartyOps/actions/runs/44439294911) 为基线。GitHub 的免费标签范围见[标准托管 Runner 文档](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)。

| Runner | 环境探测 | 本轮发行验证 |
| --- | --- | --- |
| `windows-2025` | PASS | Windows Server 工具链探测。Windows 10/11 客户端 x64 原生安装另行验收。 |
| `windows-11-arm` | PASS | Windows ARM64 基础运行探测。当前十包清单中没有 ARM64 原生安装包。 |
| `ubuntu-24.04` | PASS | amd64 依赖、构建和软件包冒烟。UOS x64 和 openEuler x64 专项另行验收。 |
| `ubuntu-24.04-arm` | PASS | ARM64 依赖、构建和软件包冒烟。麒麟 ARM64、UOS ARM64 和 openEuler ARM64 专项另行验收。 |
| `macos-15-intel` | PASS | 当前候选 PKG 和真实 WPS 待专项验证，低版本 macOS 按承诺范围补测。 |
| `macos-15` | PASS | 当前候选 PKG 和真实 WPS 待专项验证。 |

Windows 7 SP1 x86/x64、Deepin 25.2.0 LoongArch64、麒麟与 UOS 桌面、openEuler 24.03 LTS-SP2，以及实际声明的 Windows 客户端和旧版 macOS 需对应系统、架构、安装权限和原生图形会话。真实 WPS 场景使用已核实的安装来源、对应版本和合成文档。交叉构建记录 `BUILD_ONLY`，模拟运行记录 `EMULATED`。RC6 的[逐包验证环境](https://github.com/linsky-dev/PartyOps/blob/main/docs/release-validation-environments-1.4.5-rc.6.json)用于确定基线和目标系统。
