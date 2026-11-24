# 安全审查记录

核查日期为 2026-11-20。此记录随草稿 PR 更新。

- 登录密码继续使用 Argon2。恢复凭据采用独立表与 SM3 摘要。回复不缓存，登录和恢复请求走现有同源校验，Cookie 与 Bearer 混用显式拒绝。
- 设备入网恢复使用包内运行时支持的 SM4-GCM，新写入绑定设备和用途。Windows 开发运行时已验证已知答案，Win7、LoongArch 和其他发布包需原生复验。旧 Fernet 密文与密钥版本需继续覆盖。
- 独立 Reviewer 查出个人更新异常补偿、旧更新器、旧恢复码失效、Agent 就绪、轮换提交、缺失根 CA、生产入网证书请求和设备身份未绑定的共享权限问题。已提交回退防护、跨模式自动升级版本门槛、账号状态事务撤销、主机迁移就绪门闩、两阶段证书更换、CA 锚与缺失校验、CSR 校验和可信设备上下文。已打开的 SSE 在会话失效后结束。
- 独立 Reviewer 在 `d3d1d0c` 复核已关闭前次的两项高优先级工作区权限问题。AI 读取正文须同时满足接入范围、设备与用户下载权限及 AI 策略，关系、活动、搜索、引导和交接等入口按当前访问域检查。CSR 无效返回 422，主机 CA 不可用返回 503。`062bf15` 已把浏览、下载及 AI 白名单过滤前移到检索上限之前，通过独立源码复核和 107 项本地定向回归。
- 前端生产锁已将 `markdown-it` 更新为 15.0.3。2026-11-20 检索上游版本与 GHSA 公告后，通过 `pnpm audit --prod` 复验。此结果覆盖当前前端生产锁文件。 [上游发布记录](https://github.com/markdown-it/markdown-it/releases/tag/15.0.3)
- 跨认证模式的 RC6 自动升级不进入目录。旧程序仍安装在目标机时应按本版操作手册停写并覆盖安装，故障演练仍须使用真实旧安装包和目标包。
- 生产发布以最终独立审查、完整迁移演练和目标制品密码学测试通过为前提。国密结论仅描述本功能及已验证运行时。

- 2026-11-20 GitHub Actions [#44704652210](https://github.com/linsky-dev/PartyOps/actions/runs/44704652210) 证明 Windows ARM64 原生 OpenSSL 3.5.7 + cryptography 50.0.0 已完成源码哈希校验、SM3/SM4-GCM 标准向量与篡改拒绝。只认可该运行时范围；额外 ARM64 应用发行、安装与真实 WPS 仍按独立门禁。后续 wheel 必须留存实际编译 SHA-256 与 BUILD_ONLY 身份，不以 CI 成功当作可发布签名制品。
- 2026-11-21 模型启用路径安全加固：对已安装模型的激活强制清除尺寸/修改时间指纹缓存、重新读取全部文件 SHA-256；新增等体积篡改并恢复 mtime 的拒绝回归。保留状态页现有的轻量缓存与 WPS 实现不变。已在 [Windows 全量 #44866043106](https://github.com/linsky-dev/PartyOps/actions/runs/44866043106) 的 2364 项通过回归中验证，保留正式制品门禁。
- 2026-11-21 七条新 CI 工作流在 `f48bedc` 全部通过：[#44866043106](https://github.com/linsky-dev/PartyOps/actions/runs/44866043106) Windows 后端 2364 passed /16 skipped（95% 行和 92% 分支门槛通过），Linux 专项 207 passed /4 skipped；[#44866043200](https://github.com/linsky-dev/PartyOps/actions/runs/44866043200) 原生 Windows ARM64 完整验证了官方 RC2 BGE 原包签名、载荷、真正 SQLite 导入激活与中文推理。此前 Windows 随机票据与事件循环时间阈值两项误判复测已关闭，均没有修改业务排版代码；新增的 SQLite 重新开库/新 EmbeddingRuntime 测试需后续独立实证，官方 HTTP 上传和正式安装包仍未验证。
- 2026-11-21 [`457cbd5` Windows 全量 #44873341691](https://github.com/linsky-dev/PartyOps/actions/runs/44873341691) 与 [原生 #44873341720](https://github.com/linsky-dev/PartyOps/actions/runs/44873341720) 7/7 工作流及 8/8 原生子作业成功，Windows 2364 通过、16 跳过，Linux 207 通过、4 跳过；官方 RC2 BGE 模型的**隔离 SQLite 实际重开**成功。下一阶段只加强隔离验收脚本：独立 Python 解释器读取原官方模型及临时数据库，并仅篡改临时提取的 ONNX 一个字节且恢复 mtime，要求激活强制完整校验拒绝；不得篡改原签名包、官方信任根或生产数据，且不能把子进程解释成 PartyOps 主服务重启。该新增专项依原生 CI 实际结果再标记 PASS。
- 2026-11-21 [原生云端 #44877246001](https://github.com/linsky-dev/PartyOps/actions/runs/44877246001) 在 Windows ARM64 通过原版官方 BGE 签名包的全新 Python 进程重新加载、实际中文 ONNX 推理，并使用临时提取的官方 ONNX 模型等大小篡改 + 恢复 mtime 验证重新激活被拒绝（`MODEL_PACK_CORRUPT`）。原始官方发布 ZIP、公钥、业务库、WPS 均未更改；同 SHA [完整源码 #44877245979](https://github.com/linsky-dev/PartyOps/actions/runs/44877245979) 2365 passed、16 skipped，7/7 云端工作流通过。该结果仅属于 BUILD_ONLY 原生隔离安全测试。
- 2026-11-21 `3491741` 的 [正式签名 BGE FastAPI TestClient #44878080788](https://github.com/linsky-dev/PartyOps/actions/runs/44878080788) 首次完成管理员 HTTP 上传/激活/中文推理与重复上传拒绝；[#44878080751](https://github.com/linsky-dev/PartyOps/actions/runs/44878080751) 2366 passed、17 skipped，整轮 8/8 工作流通过。后续增加匿名/非管理员访问拒绝、模型错误能力拒绝、临时提取 ONNX 等大小篡改恢复 mtime 后通过**HTTP 激活端点**拒绝的回归（新增专项须 CI 实证）；限定 Windows x64 TestClient、独立临时数据，不将通过结果等同于真实发布包或公网 HTTP 服务验收。
- 2026-11-21 [正式签名 BGE HTTP #44886050440](https://github.com/linsky-dev/PartyOps/actions/runs/44886050440) 真实执行并通过两组测试：未登录/普通账号无权查看、上传、激活模型；管理员对模型不存在的能力激活被拒绝；使用原正式签名 RC2 包实际导入/推理后，对临时解包模型作等长并恢复 mtime 的篡改，再次 HTTP 激活明确拒绝为 `MODEL_PACK_CORRUPT`。同提交 Linux 功能回归 207 passed、4 skipped；静态 Ruff I001 导入顺序故障已在待推送的单行修改中修复，只有最新 CI PASS 才解除整体门禁。旧正式原包/外部公钥/生产库/WPS 未修改。
