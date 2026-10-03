# 产品功能对等矩阵

最后验证日期：2026-08-29（Asia/Hong_Kong）

## 验收口径

本矩阵把用户描述的产品能力固化为 25 条可执行源码契约。`ProductCapabilityCatalog` 会在构建回归中验证每条能力所属功能、唯一编号、说明和真实责任类型，防止后续迭代把入口保留成空壳。无 Office 路径由 `FeatureParityRegressionTests` 执行；需要 Word/WPS COM、Ribbon 或真实文档渲染的路径仍须完成宿主验收。

| 功能 | 能力契约 | 当前自动化证据 | 宿主验收状态 |
| --- | --- | --- | --- |
| 一键排版（5） | `format.element-recognition`、`format.execution-scopes`、`format.templates`、`format.page-layout`、`format.images-and-tables` | 要素责任类型、全文/选区/汇编范围、9 套用户模板上限、标题/正文/页码/附件/图片/表格默认参数通过 | 独立版统一执行器已接入；登录后的真实版面、网格、页码、图片和表格样本待验收 |
| 一键替换（5） | `replace.text-and-regex`、`replace.wildcard`、`replace.format`、`replace.saved-plans`、`replace.batch-rules` | 普通文本和正则实算通过；通配符、格式模式归一化、模式冲突、多方案和批量流水线契约通过 | Word/WPS Find 通配符、字体/段落实改待验收 |
| 一键套红（4） | `redheader.document-types`、`redheader.top-marks`、`redheader.agency-and-number`、`redheader.red-line-and-imprint` | 下行文/上行文/便函默认模板、份号补零、密级/期限/紧急程度、签发人、红线和版记参数通过 | 真实模板套用、分页和打印版式待验收 |
| 一键命名（3） | `rename.content-analysis`、`rename.composable-rules`、`rename.online` | 标题/文号/副标题/日期/自定义文字组合、非法字符清理、轮替词和在线模式规则通过 | 打开状态下的真实文件重命名、冲突与权限路径待验收 |
| 一键转换（4） | `convert.document-formats`、`convert.image-modes`、`convert.page-selection`、`convert.output-policy` | DOCX/PDF/TXT/图片枚举、分页图片/长图、全部/范围/离散页、PNG/JPG、72–600 DPI、保存和同名策略通过 | Word 导出、实际分页渲染、长图和文件冲突待验收 |
| PDF 转 Word（4） | `pdf-to-word.local-reading`、`pdf-to-word.layout-reconstruction`、`pdf-to-word.tables`、`pdf-to-word.verification` | 生成一页文本型 PDF，经真实本地引擎转为含有效主文档的 DOCX；页面、文本量、扫描件判定和包结构通过 | 复杂表格、扫描件和大文档基准待验收；“千页 3 秒、段落 1:1”尚无语料证据 |

## 自动化结果

- `RecognitionRegressionTests`：26 项断言，0 失败。
- `SignatureLayoutHostSmoke`：11 项断言，0 失败。
- `FeatureParityRegressionTests`：128 项断言，0 失败，其中包含独立桌面版生产契约。
- 每种最新 Release 构建合计：165 项断言，0 失败。
- `Release|x86`、`Release|x64` 均为 0 警告、0 错误。
- 覆盖率：行 6.89%（3,484/50,550），分支 4.92%（1,240/25,197）；未达到 90% 目标。

## 当前宿主证据

- `Get-OfficeHostInventory.ps1` 未发现真实 `WINWORD.EXE`。
- `Word.Application` COM 服务端指向 WPS，属于宿主接管，不是 Microsoft Word 安装证据。
- WPS 12.1.0.28043 位于 D 盘，独立版通过 `Word.Application` COM 成功启动隐藏宿主。
- 真实自检完成只读打开源 DOCX、另存新 DOCX、关闭宿主、Open XML 完整性校验；源文件 SHA-256 前后相同，自检后可见 WPS 窗口为 0。
- WPS 加载项兼容性不再是独立版前提；Microsoft Word VSTO 加载仍因没有 `WINWORD.EXE` 而未验证。

因此，独立版的源码能力、构建、离线回归、真实 WPS 文档引擎链路、源文件保护和便携包已闭环。登录后的内容级五大功能 E2E 仍需用户账号和代表性公文样本，不能用宿主自检代替。
