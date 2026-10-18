# 本轮本地精确清理计划

入口 `prepare-local-cleanup.ps1` 兼容 Windows PowerShell 5.1，默认只生成dryrun；本次未执行任何删除。只覆盖健康C工作区的11项可再生缓存、旧3c81包四个明确硬链接及其精确匹配的独立sidecar，保留最终e3e完整包和校验、历史JSON/验证记录、WPS金样、源码、业务数据、备份和VM。不访问E盘、pytest或`.test-temp`目录。

已实际生成 `local-cleanup-dryrun-f3059600af0e.json`：19个精确路径、18个当前存在，逻辑总和7,840,314,709B（四个旧硬链接重复计数，不能视为实际可释放量）；3个独立旧sidecar的内容均匹配旧SHA与文件名，历史目录无sidecar。最终两处e3e包和sidecar核验通过，全目标与后代无reparse，未发现活跃构建。前后C盘可用空间均29,591,564,288B，未删除。首次较慢的无Execute扫描已仅结束自身本地dryrun进程；优化后上述dryrun正常完成，无清理attempt产生。

```powershell
powershell.exe -NoProfile -NonInteractive -File C:/PartyOps-VM-Lab/windows-startup-repair-20261016/qa/acceptance/windows-startup-20261016/publication-windows-20261018/prepare-local-cleanup.ps1
```

生产尚未发布时dryrun不声称凭证就绪。生产官网及GitHub四附件真实回读完成后，主控可以先传明确回读文件再dryrun；显式`-Execute`才执行同一计划：

```powershell
$cleanup = 'C:/PartyOps-VM-Lab/windows-startup-repair-20261016/qa/acceptance/windows-startup-20261016/publication-windows-20261018/prepare-local-cleanup.ps1'
$githubProof = 'C:/PartyOps-VM-Lab/windows-startup-repair-20261016/qa/acceptance/windows-startup-20261016/publication-windows-20261018/github-windows-assets-state/readback-<真实12位hex>.json'
powershell.exe -NoProfile -NonInteractive -File $cleanup -GithubReadback $githubProof
# 仅主控在发布阶段完成后显式执行；本次没有运行此行。
powershell.exe -NoProfile -NonInteractive -File $cleanup -GithubReadback $githubProof -Execute
```

执行前复用只读`post_switch_common.proofs`绑定实际production部署、当前冻结inventory、最终manifest/guide及GitHub四附件全量回读；核最终包SHA/大小和sidecar、无活跃构建、绝对路径/父链/所有后代无reparse，并核旧包与旧sidecar身份。只使用当前PowerShell的`Remove-Item -LiteralPath`删除，不经cmd或另一shell拼接路径。每项删除前再次检查，单次intent、逐项结果、前后空间及最终包保留结果保存在本pub。任何失败立即停止，既有attempt/result阻断重发，不能删回执绕过。

本地PS5.1语法及真实函数拒绝测试通过：跨盘、归一化逃逸、受拦测试目录、缺发布凭证、活跃编译器均被拒绝；未执行删除函数。
