# GitHub 原生 macOS WPS JSAPI 探针

状态：双原生 run 36675682233 已完成官方 WPS 12.1.29166 签名安装、欢迎页关闭及协议 URI 请求；两架构均只在 `relay-version` 的 `http://127.0.0.1:58890/version` 收到 connection refused。此前 run 36674097319 的欢迎页关闭结果和 run 36672407746 的禁用按钮失败回执仍保留。此次单一 HTTP 端点的失败不足以判断 relay 监听状态：官方缓存 `official-wps-source-20260927/wps_sdk.js` 的 L77/L476 按 `location.protocol` 选择 HTTP 58890 或 HTTPS 58891，且 Mac 不走自动 URI 路径。run 36675682233 已验证协议 URI 请求被接受；下述新增失败诊断尚未在 macOS runner 运行。此脚本只适用于一次性、原生架构 GitHub macOS runner，不是 PartyOps 产品安装或排版通过证明。

欢迎页关闭后，脚本读取已验签官方 WPS 主应用及实际 `wpscloudsvr` 子应用的 `Contents/Info.plist`，核 `CFBundleURLTypes` 是否声明 `ksoWPSCloudSvr`。若两者均合法声明，确定性优先子应用；仅主应用声明时回退主应用。缺失、同一候选重复、越出安装包或所选应用签名不符即失败；证据只记录协议名、所选位置及包内相对路径。通过时显式指定该应用，使用系统 `open -g -a` 对固定 `ksoWPSCloudSvr://start=RelayHttpServer` URI 发送一次启动请求，不依赖其他应用对同名协议的默认处理。`open` 接受请求只记录为请求成功，不能算 relay 已就绪；后续仍按严格 relay 轮询和既有探针验收。

从仓库根目录调用：

```bash
export RUNNER_TEMP="$(mktemp -d)"
/usr/bin/osacompile -o "$RUNNER_TEMP/wps-welcome.scpt" qa/vm-lab/release-preparation/macos-intel/handle-wps-welcome.applescript
/usr/bin/xcrun swiftc -O qa/vm-lab/release-preparation/macos-intel/click-wps-consent-checkbox.swift -framework AppKit -framework ApplicationServices -o "$RUNNER_TEMP/wps-consent-click"
bash qa/vm-lab/release-preparation/macos-intel/run-native-wps-probe.sh
```

脚本要求 `RUNNER_TEMP` 指向现有可写目录。它读取 `uname -m`，只接受 `x86_64` 或 `arm64`，并据此选择锁定的 WPS 12.1.29166 官方 DMG。Intel 下载 URL 为 `https://package.mac.wpscdn.cn/mac_wps_pkg/12.1.29166/WPS_Office_12.1.29166(29166)_x64.dmg`，SHA-256 为 `79d121a5269ce2b2b0422fbf0d61c1e21b382c1b46b41be24ce3e4b8021b8d46`；Apple Silicon URL 为 `https://package.mac.wpscdn.cn/mac_wps_pkg/12.1.29166/WPS_Office_12.1.29166(29166)_arm64.dmg`，SHA-256 为 `f493b07f48e4911d5836d51afd8b4254e2b12aa16d2d0816b1474c3fa0785c31`。来源记录为 Homebrew 官方 `wpsoffice-cn` cask；URL 与哈希按任务输入锁定。工作流以架构和固定 SHA-256 为键缓存唯一官方 DMG；缓存命中仍重算 SHA，坏缓存直接拒绝。缓存缺失时每次下载写独立 `.part` 文件并记录 HTTP 状态、curl 退出码和字节数；429 等 20 秒重试一次，5xx 或 curl 28 超时等 2 秒重试一次，其他错误立即停止。仅 HTTP 200、curl 成功且 SHA-256 精确匹配的完整输入会进入缓存并成为可挂载 DMG；即使后续探针失败，也只保存这个已验证输入，失败部分文件和用户数据均不入缓存。

脚本先要求当前进程用户与 `/dev/console` GUI 用户一致，且 `launchctl print gui/<uid>` 成功；同时优先使用 PATH 中 `python3.11`（例如 `setup-python` 安装的版本），回退到 `python3` 或 `/usr/bin/python3`，最低要求 Python 3.9。它拒绝覆盖常见系统/用户 Applications 路径中的已有 WPS、正在运行的 WPS 和 Spotlight 查到的 WPS Bundle ID。之后以只读方式挂载 DMG，检查代码签名 Team ID `YK4WKE5WAM` 和主可执行文件架构，只在 `/Applications/wpsoffice.app` 不存在时用 `sudo -n ditto` 复制。它通过正常 `open -a` 请求启动 WPS；先截图并用 AppleScript System Events 导出控件树，仅在同一窗口唯一识别许可文案、未勾选框与 `Start Now` 按钮时，使用随 runner 编译的 CoreGraphics 辅助程序再次核 WPS Bundle ID、唯一欢迎窗口、前台、AX bounds 与输入权限，向勾选框实时中心发送一次真实鼠标 down/up；不通过 AX 改值。随后必须实际读回勾选值为 1 且 `Start Now` 的 AX enabled 为 true，才继续按原路径按按钮；若按钮仍禁用直接失败，绝不点击。脚本不会登录账号、改 TCC/系统安全配置。若已启用按钮的 AXPress 后同一欢迎窗口仍可见，脚本仅一次读取该按钮实时位置与尺寸，校验正尺寸、中心仍在同一窗口、进程仍在前台，再由 System Events 对实时中心执行一次 `click at`；不是从截图猜坐标。权限不足、控件未知或转场未完成均失败并保留前后控件树、截图、按钮 bounds 与固定阶段码。工作流在下载前先用 `osacompile` 静态编译辅助脚本。

relay 检查要求 `http://127.0.0.1:58890/version` 返回 HTTP 200 和非空响应。启动等待只对 connection-refused 轮询最多 20 次、间隔 1 秒；429 固定等待 20 秒后最多重试一次，5xx/超时等待 2 秒后最多重试一次，其余 curl/HTTP 错误立即失败且保留最近响应头、响应体和尝试码。若 `relay-start` 已完成后在 `relay-version` 或 `probe` 失败，脚本另行记录两个固定端口的监听进程，以及通过已验签安装包内实际可执行文件路径核验的官方 `wpsoffice` 与 `wpscloudsvr` PID 的监听 socket；`wpscloudsvr` 路径来自子应用 `Info.plist` 的 `CFBundleExecutable`。并对固定 IPv4/IPv6 loopback 的 HTTP 58890、HTTPS 58891 各发送一次 `POST {}`。诊断 curl 每次最多 3 秒、绕过代理、使用默认 TLS 证书验证，不带 `-k` 且不重试；headers、body、curl stderr 和独立退出码/HTTP 状态均写入各自诊断文件。诊断只作失败排查，不计为通过、不改变原 relay/probe 验收、`status.json` 或脚本退出码，也不替换固定的 HTTP 58890 实际 relay 检查。下载仅按前述有限规则重试。随后调用仓库已有 `scripts/probe-wps-native-bridge.py`，参数固定为 macOS 与实际 runner 架构，并传 `--no-start-relay`，避免探针自行反复触发 URI 启动。探针调用本身不自动重放，因为重复 JSAPI 操作并不安全；406 等失败只保存响应摘要与请求路径，不代表可重试或通过。探针的通过范围只有静默 JSAPI 首行缩进、返回令牌/可见性契约及保存后 OOXML 的 `firstLineChars=200`；它不代表正式六项功能、对象代理或金样验收通过。

每次运行在 `$RUNNER_TEMP/partyops-wps-native-probe.*` 留下独立目录。运行目录可能包含完整第三方安装器 DMG；它不属于可发布或可上传的诊断产物。GitHub 工作流仅复制顶层明确列出的诊断文件到独立 artifact 目录，排除 `gui-launchctl.txt`、`artifact-manifest.txt`、`*.dmg`、挂载目录、`.app` 和应用程序；脚本本身不上传产物。成功证据包含签名/架构记录、relay 响应、探针 stdout/stderr、输出 DOCX、evidence JSON 与 status JSON。失败目录还记录失败阶段/码、WPS 相关进程列表；若系统允许截屏则保存 `failure-screen.png`。HTTP 406 时既有探针会在 `probe.stderr.log` 记录 `WPS_PROBE_INVOKE_FAILED`、HTTP 状态和受限响应摘要，以及已收到的加载项服务请求路径；保留该日志和 relay-version 文件作为失败证据，不把 `/version` 成功计作 JSAPI 成功。若需要把诊断文件留出 runner 生命周期，仅能按此白名单收集到已配置的内部证据存储；不得把日志或文档上传到外部服务。

当前步骤已接入 rc.6 工作流的独立双架构 `PROBE-WPS-145-RC6` 作业；协议启动处理只针对 run 36674097319 已取证的 relay 拒绝连接，不声称新版脚本已运行或排版通过。旧 Mac VM runbook 仍在 `wps-jsapi-probe-runbook-20260927.md`；官方 relay 406 注意事项见 `official-relay-update-20260930.md`。
