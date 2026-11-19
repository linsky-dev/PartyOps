# 1.4.6-rc.1 验收报告

| 范围 | 状态 | 证据 |
| --- | --- | --- |
| 工程恢复阶段本地回归 | PASS | `37f4efc` 在 Windows x64 主机运行两份实验室测试，83 项通过。PowerShell、Bash 语法及 Git 差异检查通过。 |
| 六种标准托管 Runner | PASS | [探测运行](https://github.com/linsky-dev/PartyOps/actions/runs/44439294911) 覆盖源检出、系统和架构、磁盘余量、Python、Node.js、Git。 |
| 首轮完整 PR CI | FAIL | [运行 44469509485](https://github.com/linsky-dev/PartyOps/actions/runs/44469509485) 记录 Windows 注册表模块导致 Linux 测试收集失败，前端 Blob 跨运行环境断言失败，以及 Pillow 12.3.0 告警。 |
| Pillow 升级后的现代运行时依赖审计 | PASS | Windows x64 开发环境执行 `pip-audit -r backend/requirements.txt`，无已知漏洞。Win7 回移轮子仍需单独审计。 |
| 十包构建与逐包真实安装 | NOT_RUN | 尚无本次冻结制品。 |
| 特殊目标原生安装、升级及真实 WPS | BLOCKED | 需要对应目标环境和合法可用的 WPS 输入。 |
| 双官网预览及下载回读 | BLOCKED | EdgeOne 控制台可见内容缺少可重建的原始工程。 |

验收证据以各次运行记录的执行系统、架构、命令、依赖和 SHA 为准。安装验收需追加包 SHA-256、日期和未覆盖范围。
