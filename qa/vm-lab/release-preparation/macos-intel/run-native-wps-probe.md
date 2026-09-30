# GitHub 原生 macOS WPS JSAPI 探针

状态：仅新增本地 QA 包装脚本；尚未在 macOS runner 执行，也未确认 WPS 安装包或 GUI 环境的实际结果。此脚本只适用于一次性、原生架构 GitHub macOS runner，不是 PartyOps 产品安装或排版通过证明。

从仓库根目录调用：

```bash
bash qa/vm-lab/release-preparation/macos-intel/run-native-wps-probe.sh
```

脚本要求 `RUNNER_TEMP` 指向现有可写目录。它读取 `uname -m`，只接受 `x86_64` 或 `arm64`，并据此选择锁定的 WPS 12.1.29166 官方 DMG。Intel 下载 URL 为 `https://package.mac.wpscdn.cn/mac_wps_pkg/12.1.29166/WPS_Office_12.1.29166(29166)_x64.dmg`，SHA-256 为 `79d121a5269ce2b2b0422fbf0d61c1e21b382c1b46b41be24ce3e4b8021b8d46`；Apple Silicon URL 为 `https://package.mac.wpscdn.cn/mac_wps_pkg/12.1.29166/WPS_Office_12.1.29166(29166)_arm64.dmg`，SHA-256 为 `f493b07f48e4911d5836d51afd8b4254e2b12aa16d2d0816b1474c3fa0785c31`。来源记录为 Homebrew 官方 `wpsoffice-cn` cask；URL 与哈希按任务输入锁定，脚本运行时仍会对下载文件做严格 SHA-256 比较。

脚本先要求当前进程用户与 `/dev/console` GUI 用户一致，且 `launchctl print gui/<uid>` 成功；同时优先使用 PATH 中 `python3.11`（例如 `setup-python` 安装的版本），回退到 `python3` 或 `/usr/bin/python3`，最低要求 Python 3.9。它拒绝覆盖常见系统/用户 Applications 路径中的已有 WPS、正在运行的 WPS 和 Spotlight 查到的 WPS Bundle ID。之后以只读方式挂载 DMG，检查代码签名 Team ID `YK4WKE5WAM` 和主可执行文件架构，只在 `/Applications/wpsoffice.app` 不存在时用 `sudo -n ditto` 复制。它通过正常 `open -a` 请求启动 WPS；不会点击欢迎页、接受许可、登录账号、改 TCC/系统安全配置或模拟 GUI 坐标。若首次运行需要人工界面操作或 runner 没有 GUI 登录会话，探针会失败并保留失败诊断。

relay 检查要求 `http://127.0.0.1:58890/version` 返回 HTTP 200 和非空响应。启动等待只对 connection-refused 轮询最多 20 次、间隔 1 秒；429 固定等待 20 秒后最多重试一次，5xx/超时等待 2 秒后最多重试一次，其余 curl/HTTP 错误立即失败且保留最近响应头、响应体和尝试码。下载请求不自动重试。随后调用仓库已有 `scripts/probe-wps-native-bridge.py`，参数固定为 macOS 与实际 runner 架构，并传 `--no-start-relay`，避免探针自行反复触发 URI 启动。探针调用本身不自动重放，因为重复 JSAPI 操作并不安全；406 等失败只保存响应摘要与请求路径，不代表可重试或通过。探针的通过范围只有静默 JSAPI 首行缩进、返回令牌/可见性契约及保存后 OOXML 的 `firstLineChars=200`；它不代表正式六项功能、对象代理或金样验收通过。

每次运行在 `$RUNNER_TEMP/partyops-wps-native-probe.*` 留下独立目录。运行目录可能包含完整第三方安装器 DMG；它不属于可发布或可上传的诊断产物。GitHub 工作流仅复制顶层明确列出的诊断文件到独立 artifact 目录，排除 `gui-launchctl.txt`、`artifact-manifest.txt`、`*.dmg`、挂载目录、`.app` 和应用程序；脚本本身不上传产物。成功证据包含签名/架构记录、relay 响应、探针 stdout/stderr、输出 DOCX、evidence JSON 与 status JSON。失败目录还记录失败阶段/码、WPS 相关进程列表；若系统允许截屏则保存 `failure-screen.png`。HTTP 406 时既有探针会在 `probe.stderr.log` 记录 `WPS_PROBE_INVOKE_FAILED`、HTTP 状态和受限响应摘要，以及已收到的加载项服务请求路径；保留该日志和 relay-version 文件作为失败证据，不把 `/version` 成功计作 JSAPI 成功。若需要把诊断文件留出 runner 生命周期，仅能按此白名单收集到已配置的内部证据存储；不得把日志或文档上传到外部服务。

当前步骤已接入 rc.6 工作流的独立双架构 `PROBE-WPS-145-RC6` 作业，不自动解决 GUI 欢迎/许可界面，也不声称已安装或运行通过。旧 Mac VM runbook 仍在 `wps-jsapi-probe-runbook-20260927.md`；官方 relay 406 注意事项见 `official-relay-update-20260930.md`。
