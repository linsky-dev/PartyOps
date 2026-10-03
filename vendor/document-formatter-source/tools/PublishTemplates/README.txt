partyops公文排版助手 — 本地 VSTO 发布包

1. 只读诊断（检查真实 Word、WPS COM 接管以及包/Word 位数是否匹配）：
   powershell -ExecutionPolicy Bypass -File .\Install-Local.ps1 -Action Diagnose

2. 使用自签名开发证书的本机安装（推荐精确绑定清单与公钥）：
   powershell -ExecutionPolicy Bypass -File .\Install-Local.ps1 -Action Install -TrustManifest -LaunchWord

3. 使用受信任正式代码签名证书的安装：
   powershell -ExecutionPolicy Bypass -File .\Install-Local.ps1 -Action Install -LaunchWord

4. WPS 兼容安装（必须选择与 WPS 位数一致的包，并先保存所有文档）：
   powershell -ExecutionPolicy Bypass -File .\Install-Local.ps1 -Action Install -HostTarget Wps -TrustManifest -LaunchWps

5. 卸载当前用户加载项注册：
   powershell -ExecutionPolicy Bypass -File .\Install-Local.ps1 -Action Uninstall

注意：开发证书只用于恢复工程的本机验收；对外发布必须使用真实代码签名证书重新执行 tools\Publish-Vsto.ps1。
`-TrustManifest` 使用 Microsoft VSTO 用户 Inclusion 机制，只信任当前清单 URL 与签名公钥；`-TrustPublisher` 会写当前用户受信任根/发布者并可能触发 Windows 安全确认，一般不用于开发包。
诊断只把磁盘上真实存在的 WINWORD.EXE 视为 Microsoft Word；Word.Application 若指向 WPS 不会被误判为 Word。WPS 兼容安装必须显式传入 -HostTarget Wps。该目标会保留 Word 的 VSTO 注册，同时安装 `PartyOps.DocumentFormatter.WpsShim.dll` 这个传统 `IDTExtensibility2` COM 入口，并移除本包先前写入的 WPS VSTO 白名单，避免 WPS 再次进入不兼容的 VSTO Loader 路径。WPS 原生入口与 Word VSTO 入口共用同一套排版、替换、套红、命名和转换业务代码。

单独检查 WPS 原生 COM 注册：
   powershell -ExecutionPolicy Bypass -File .\Register-WpsComAddIn.ps1 -Action Diagnose

卸载时 `Install-Local.ps1 -Action Uninstall` 会同时移除 WPS 原生 COM 注册和当前包拥有的 Word/VSTO 注册；不会删除发布文件、证书或用户配置。
