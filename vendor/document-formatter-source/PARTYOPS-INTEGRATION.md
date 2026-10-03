# PartyOps 公文排版源码快照

最后同步与逐文件核对日期：2026-09-01（北京时间）

此目录是 `E:\paiban\PartyOps.DocumentFormatter.Source` 的可构建源码快照，作为 PartyOps
内嵌公文排版模块的唯一业务规则来源。快照包含业务核心、Windows 宿主工程、测试、锁定依赖、
图片资源和第三方声明；不包含 `bin`、`obj`、`artifacts`、开发证书或私钥。

当前功能/构建快照共 897 个文件；文本换行归一化后的聚合 SHA-256 为
`bcd57410083aa5624a85c5131b6c14fb40c5164e85fef1d41f2582a8e19b9641`。构建前门禁会重新
计算该指纹，任一业务文件被 PartyOps 侧修改都会立即停止构建。

集成约束：

- Windows 后台宿主必须直接引用 `src/PartyOps.DocumentFormatter.AddIn`，并通过
  `StandaloneBatchProcessor` 调用 `FeatureTaskExecutor`；不得复制或重写排版规则。
- PartyOps 只替换界面与任务桥，不改变功能识别顺序、默认配置、命名规则和输出语义。
- Windows 使用本机 WPS（优先）或 Word（回退）；宿主为无控制台、无独立产品窗口进程。
- 国产 Linux 与 macOS 的平台桥只允许适配 WPS 宿主接口。平台输出未通过同一金样契约前，
  对应安装包不得发布。
- `backend/tests/fixtures/document-formatter-source` 保存用户确认正确的输入与输出金样；
  发布门禁同时校验 OOXML 结构与逐页像素哈希。

更新此快照时必须先运行源项目回归测试，再运行 PartyOps 的完整测试门禁。不得直接修改快照内
的排版业务实现来迁就 PartyOps；确需修复时应先在规格源中完成并验证，再同步整个快照。
