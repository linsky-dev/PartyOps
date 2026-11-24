# PartyOps RC1 Windows x64 同源码安装与升级验收（2026-11-23）

> 状态：**托管原生 Windows Server x64 安装链路 PASS；正式生产发行 NOT_READY。** 本文件只记录可复核的证据，不把云端服务器误称为 Windows 10/11 用户图形桌面，也不把测试证书当作正式签名。

## 冻结证据身份

- 代码提交：`204f62b2e3c7a0a194e3367fb11606772e0de680`（分支 `feature/1.4.6-rc1-cloud-rebuild`）。
- 未签名候选 EXE SHA-256：`1e70e677adbc0de1f338d0daaa0d845d04bde90447f286c31209355154cb59eb`。
- 原始真实构建、安装、回滚、断盘测试：[GitHub Actions 45126787399](https://github.com/linsky-dev/PartyOps/actions/runs/45126787399)，所有四项安装作业 PASS。该运行产生的各证据附件均以同一源码 SHA 标识。
- 正式候选状态：`BUILD_ONLY_UNVERIFIED_NATIVE_WPS`；`signed=false`，`release_ready=false`。

## 逐项验收

| 项目 | 结果 | 证据边界 |
| --- | --- | --- |
| 完整 PyInstaller + Inno Windows x64 EXE | PASS | 官方依赖和原始脚本，内部未签名测试制品 |
| 全新安装、启动、卸载 | PASS | Windows Server 2025 托管环境，安装进程退出 0、安装主程序及清单 SHA 匹配，`PartyOpsLauncher.exe --background` 退出 0，卸载退出 0 |
| 断开历史 E: 程序及数据盘、残留旧记录 | PASS | 无 `/DIR` 覆盖参数，安装到 `C:\Program Files\PartyOps`，新数据位置 `C:\ProgramData\PartyOps-Data`；旧数据未恢复、未删除，均需显式缺盘确认 |
| RC6 → RC1 原位覆盖升级 | PASS | 真实 RC6 安装包，后续 RC1 安装不指定 `/DIR`；程序哈希更新 |
| RC1 → RC6 程序文件回滚 | PASS | 回滚后 RC6 EXE 与原始 SHA 完全相同；原数据目录标记、合成 SQLite、附件及文件标记未被改动 |
| 生产数据库结构降级 | NOT_RUN | 合成 SQLite 不是完整 PartyOps 真实迁移后的数据库，不能允许生产库直接用 RC6 打开 |
| Windows 10/11 桌面真实 WPS 自动排版 | NOT_RUN | 源码和宿主契约检查不代表图形会话真实 WPS 金样验收 |
| 正式可信 Authenticode 签名 | BLOCKED | 没有经发布者批准的可信生产证书及签名权限 |

## 实测耗时与清单

同一原生 Windows Server Runner 记录的当前版数据（单位秒）：

| 指标 | 数值 |
| --- | ---: |
| 安装含安全检查与自检 | 103.98 |
| 安装后独立启动器预检 | 0.69 |
| 卸载 | 6.54 |
| 安装后 `release-manifest.json` 大小（字节） | 4,216,553 |

历史安装实测约 144.52 秒；相比新版本减少约 40.54 秒（约 28%）。该比较来自不同托管运行批次，磁盘和杀毒环境不同，**不是严格受控的性能基准**。继续分析解包前后安全验证耗时，不应通过关闭清单哈希或 ACL 自检换速度。

## 仍未满足的正式发布条件

1. 在实际 Windows 10/11 x64 图形桌面，以正式 WPS 和已有回归金样执行文档自动排版及完整业务流程。
2. 使用合成的 **真实 PartyOps RC6 数据库结构**，升级到 RC1 并覆盖迁移失败/升级后新写入后的安全恢复场景；保全 WAL/SHM、密钥、附件、认证及审计。
3. 获取经用户明确批准的生产 Authenticode 签名证书和可信时间戳；只对冻结的候选包签名并再次验收 SHA/签名。
4. 扩展正式支持矩阵及双官网预览后，经发布审批才合并、发布 GitHub Release 或修改 EdgeOne。

当前工作不可更改已通过的 WPS 排版业务代码，不可自动删除真实数据，不可因 UI 无法操作而放宽安全检查；此前 RC6 Release 和下载入口保持原样。
