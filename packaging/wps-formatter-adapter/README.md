# PartyOps WPS 原生适配器

此目录是 Linux 与 macOS 原生 WPS 适配的发布边界，不包含近似排版规则。
Windows 继续直接执行 `PartyOps.DocumentFormatter.AddIn` 的
`StandaloneBatchProcessor`；Linux 的正式处理路径则把同一份 AnyCPU 源码规则
封装进本机原生宿主，通过 WPS RPC C ABI 调用相同对象模型。它不会把规则改写
成 JavaScript，也不会维护第二套“近似排版”实现。

本目录的 WPS JSAPI 加载项只保留为开发诊断资源，不进入正式运行时：它可以
验证某些 WPS 版本的本机加载项协议，但不承担正式文档处理。正式金样和六类
功能测试只接受 `partyops-document-formatter-host` 的直接结果。

截至 2026-09-01，WPS 官方 RPC 二次开发接口面向 Linux，官方加载项文档也只
明确列出 Windows/Linux；macOS 不得把“已安装 WPS”视为“存在可静默自动化
WPS 的接口”。ARM64/x86_64 Mac 只有在目标机探针与完整金样均真实通过后才能
生成 PKG，当前构建门禁保持失败关闭。

探针也允许在 Windows 开发机运行，用于提前验证 WPS 加载项、静默调用和字符
缩进持久化协议；该结果不属于 Linux/macOS 发布证据，不能替代目标系统实测。

## 当前门禁

- 加载项只能由 `127.0.0.1` 临时服务提供，并校验每次运行生成的高强度令牌；
- WPS 必须以 `showToFront=false`、`mode=true` 运行，不允许弹出排版窗口；
- 探针在副本中写入“首行缩进 2 字符”，保存后同时检查 WPS 返回值和
  OOXML 的 `firstLineChars=200`，禁止用点值近似；
- 目标平台必须完成 3 个排版范围、6 项功能和 25 项能力的真实金样测试，
  才能生成 `runtime-evidence.json`；仅探针通过不代表排版宿主已经可发布。

协议实现依据金山官方开源示例 `zouyf/wps` 的提交
`5bf5c1b26245c4b3e07737b14984fd7810b4fcad`，许可见
`LICENSE-WPS-SDK.txt`。本目录不打包 WPS，本机须已安装支持加载项的 WPS。
