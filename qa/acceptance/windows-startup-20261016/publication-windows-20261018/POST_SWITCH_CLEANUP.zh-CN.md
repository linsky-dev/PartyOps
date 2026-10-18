# 本轮切换后撤旧

本地固定计划为 `post-switch-withdrawal-plan.json`；仅已准备计划和脚本，没有部署、删除或网络写入。必须先完成官网 production 回读与 GitHub 四附件匿名完整GET，才能生成可部署撤旧receiver或执行GitHub撤旧。官网凭证限定本目录 `production-website-readback.json`，并核当前完整网站与frozen inventory一致、生产result成功及intent inventorySHA、回读inventorySHA和resultSHA均与同批真实文件相符。

国内只撤 `/workspace/downloads/PartyOps_1.4.5-rc.6_windows_amd64.exe`（a7c044…，443690896B）和同目录 `PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe`（3c81…，449978846B）。新Windows e3e与其他九包完整核验后，精确核这两个旧文件的名字、根路径、普通文件身份、大小、magic及SHA；全部预检成功才逐个unlink。不存在只记already-absent，身份不符立即拒绝。部署后只剩最终十包路由，不改变其他文件或令牌。

```powershell
$py = 'C:/PartyOps-VM-Lab/windows-startup-repair-20261016/backend/.venv/Scripts/python.exe'
$pub = 'C:/PartyOps-VM-Lab/windows-startup-repair-20261016/qa/acceptance/windows-startup-20261016/publication-windows-20261018'
$siteProof = "$pub/production-website-readback.json"
$ghProof = "$pub/github-windows-assets-state/readback-<真实12位hex>.json"
& $py -X utf8 -B "$pub/prepare-post-switch-cleanup.py" prepare --website-proof $siteProof --github-readback $ghProof
```

`prepare`读取原麒麟 `prepare-post-switch-cleanup.py` 的静态撤旧片段，不执行旧脚本。独占生成 `batch/post-switch-receiver` 三个公开文件及准备回执。主控再将当前本轮已修正的 `batch/receiver/.upload-token` 原字节复制为第四文件，不读取/输出内容，不复制历史旧键。主控通过官方WorkBuddy单次部署这四文件；结果未知只读观察，不重发。

部署后，必须核health恰好十个最终包、大小/SHA/ready均匹配、withdrawal恰好两个旧文件及其精确字节/摘要，且结果为deleted或already-absent：

```powershell
& $py -X utf8 -B "$pub/prepare-post-switch-cleanup.py" observe --website-proof $siteProof --github-readback $ghProof --execute-read-only
```

该observe只GET并保存独立回执，不部署、不删除。

GitHub固定只撤 asset `606128919`（旧Windows exe）与 `606131252`（对应105B sidecar），名字、大小和digest必须与本轮实际 `github-current-before.json` 相等；不撤RC4、其他平台或历史签名清单。复用正常gh认证及原成功撤旧的逐文件intent/单次DELETE/随后读取确认方式，不读取凭据。新四附件身份必须与匿名回读回执中的asset_id一致，其他现有附件原值保留，tag不变。

```powershell
& $py -X utf8 -B "$pub/withdraw-github-old.py" plan
& $py -X utf8 -B "$pub/withdraw-github-old.py" withdraw --website-proof $siteProof --github-readback $ghProof --execute-network
```

任何删除结果未知都不重发；只执行：

```powershell
& $py -X utf8 -B "$pub/withdraw-github-old.py" recover --website-proof $siteProof --github-readback $ghProof --execute-read-only
```

本轮7项定向测试通过：原snapshot身份篡改拒绝、十包与两个撤旧health拒绝、无显式flag拒绝、生成JS的Node语法检查、真实prepare入口加载带连字符模块、同批生产inventory/intent/result关联及错误凭证路径拒绝。没有运行生成JS、DELETE、部署或真实网络观察；完整生产凭证未齐时保持阻断。
