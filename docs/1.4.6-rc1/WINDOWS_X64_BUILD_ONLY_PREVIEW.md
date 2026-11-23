# PartyOps 1.4.6-rc.1 Windows x64 内部候选安装包（BUILD_ONLY）

## 目标和边界

优先生成真实 Inno Setup Windows 10/11 x64 单文件安装候选，供下一步专用测试机试装。上一已发布版本的 WPS 排版已有实机金样与十项功能场景通过记录，可作为历史参考；原有排版业务代码继续保留。

这些历史证据不能直接视作新版编译产物的实机通过。因此本候选明确标记 BUILD_ONLY_UNVERIFIED_NATIVE_WPS；未签名，不属于正式更新，不得推送官网下载或生产自动升级。

## PDF 安全与原生产依赖

Windows x64 候选使用 PyMuPDF 2.1，已有 Windows x64 全后端隔离回归。使用现有 uv 工具生成单独的版本锁，严格核对旧锁差异、三个依赖声明、构建解释器实际安装版本。候选内第三方声明更新为 2.1，原仓库其他平台所用 PyMuPDF 1.26.0 生产锁不改动。

仅有内部安装候选不代表已经完成 AGPLv3/商业许可证、再分发和全部平台安全验收；严禁将旧版 1.26.0 悄悄打入新 Windows x64 候选。

## 原始构建器与候选构建器的区别

保留原 packaging/windows/build-windows.ps1 为唯一真正的 PyInstaller、运行时来源核验与 Inno Setup 构建链。默认无 -BuildOnlyPreview 时，继续要求安全的正式 PDF 生产锁和当前源码的完整功能通过凭据，不改变任何正式门禁。

仅在明确传入 -BuildOnlyPreview 和 -PdfCandidateRoot（artifacts 下的独立候选目录）时，允许 Windows x64 候选绕过当前版 WPS 通过和全功能发布凭据。它仍必须锁定 PyMuPDF 2.1、保证原依赖锁除 PDF 外不变、核验历史 WPS x64 证据；随包写入 historical-wps-reference.json 并明确 current_version_native_wps_tested=false，不得生成伪造的新实机 PASS。

内部候选 EXE 文件名含 BUILD_ONLY_UNVERIFIED，候选 JSON 明确 release_ready=false、signed=false、full_function_gate_passed=false，不与正式安装包混淆。

## 下阶段专用 Windows 真机测试

在无业务数据的 Windows 10/11 x64 QA 虚拟机内实测安装、启动、WPS 全功能、PDF/本地 AI、原 RC6 覆盖、重启、数据留存和回滚。全部通过且完成许可证、签名、其他平台和双官网发布审批后，才能改走默认正式构建门禁发布。当前内部候选不能进入 GitHub Release 或网站下载页。
