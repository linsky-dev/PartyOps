import validationEnvironments from "../../qa/vm-lab/release-preparation/release-validation-environments-20261002.json" with { type: "json" };

// 每文件真实观察确认，不能当作服务端上传时间；来源未知不回填共同 HEAD。
const rc6Packages = validationEnvironments.packages.filter((row) => row.version === "1.4.5-rc.6");
const beijingConfirmation = (value) => new Intl.DateTimeFormat("sv-SE", {
  timeZone: "Asia/Shanghai", year: "numeric", month: "2-digit", day: "2-digit",
  hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
}).format(new Date(value)).replace(",", "") + "（北京时间，UTC+8）";
const rc6Files = Object.fromEntries(rc6Packages.map((row) => [row.file_key, row.download.domestic_url]));
const rc6Links = Object.fromEntries(rc6Packages.flatMap((row) => [
  [row.file_key, row.download.github_url], [row.file_key + "Official", row.download.github_url],
]));
const confirmedAtLabel = (value) => value
  ? beijingConfirmation(value)
  : "待公开回读确认";
const rc6Integrity = Object.fromEntries(rc6Packages.map((row) => [row.file_key, {
  filename: row.download.filename, sizeBytes: row.download.size_bytes,
  size: `${row.download.size_bytes.toLocaleString("en-US")} 字节（${(row.download.size_bytes / 1048576).toFixed(1)} MB）`,
  sha256: row.download.sha256, uploadedAt: confirmedAtLabel(row.download.confirmed_at),
  mirrorVerifiedAt: confirmedAtLabel(row.download.confirmed_at),
  timeMeaning: row.download.confirmed_at ? "upload-readback-confirmation" : "pending-public-readback",
  sourceCommit: row.download.source_commit, sourceFingerprint: row.download.source_fingerprint,
  ...(row.publication_state === "pending-public-readback" ? { publicationState: "pending" } : {}),
}]));

const buildAiInstallPrompt = (platformName, platformChecks) => `你是 PartyOps 安装与验收助手。当前电脑平台：${platformName}。

目标：帮助一名不熟悉电脑的用户，只使用 PartyOps 官网（https://partyops.cn）或 GitHub 官方 Release 中与系统、架构完全匹配的安装包，逐步完成安装或升级，并验证系统确实可用。

请严格遵守：
1. 一次只给一个小步骤，先解释这一步的目的，再等待用户回复结果或提供脱敏截图；不要一次倾倒整页命令。
2. 第一步先询问：系统版本、处理器架构、首次安装还是升级、个人/主机/协同机角色，以及安装包所在位置。不要猜测架构、地址、端口或文件名。
3. 先执行只读检查：${platformChecks}
4. 要求用户在 PartyOps 官网下载区复制该制品公开的文件名、文件大小和 SHA-256；在本机计算 SHA-256 并逐字比对。任何一项不一致都立即停止，不能建议绕过。
5. 不得关闭杀毒软件、防火墙、SmartScreen、Gatekeeper、SELinux 或系统安全策略；不得使用 --force、来源不明的 DLL/补丁/证书或第三方下载站。
6. 需要管理员权限、修改系统服务、覆盖安装或删除数据前，先说明影响并单独征得用户确认。升级前先指导用户在 PartyOps 内完成备份。
7. 安装后必须验证：应用能打开、页面版本与安装包一致、健康/运行诊断为就绪、数据库迁移成功、提醒设置可读取；主机/协同机还要验证公布地址不是 127.0.0.1、双方心跳和业务权限正常。
8. 如果失败，只收集诊断码和日志最后 120 行。提醒用户先遮盖用户名、内网地址、文件名和业务内容；绝不索要或上传密码、密钥、令牌、数据库、业务文件或完整日志。
9. 不替用户声称“安装成功”。只有上述核验全部通过，才输出一份清单：已通过项、未通过项、日志位置和安全的下一步。

现在先用通俗中文询问第 2 条所需信息，不要立即给安装命令。`;

export const siteContent = {
  arm64DiagnosticRepair: {
    published: false, // 已由完整麒麟候选包接替，历史清单保留。
    scope: "用于安装后因旧数据升级而启动失败的 ARM64 电脑。尚待麒麟原机复验。",
    warning: "先备份，再覆盖安装，不要先卸载或删库",
    afterInstall: "随后检查原账号、事项、日历和附件，并再次启动确认。",
    domesticUrl: "https://partyops-rc6-win7-x86.app.workbuddy.host/downloads/PartyOps_1.4.5-rc.6_linux_arm64_sameversion-kylin9000c-diagnostic-20261010.deb",
    githubUrl: "https://github.com/linsky-dev/PartyOps/releases/download/v1.4.5-rc.6/PartyOps_1.4.5-rc.6_linux_arm64_sameversion-kylin9000c-diagnostic-20261010.deb",
    guidePath: "/releases/v1.4.5-rc.6/diagnostic-installation-20261010.zh-CN.md",
    manifestPath: "/releases/v1.4.5-rc.6/release-manifest-arm64-startup-diagnostic-20261010.json",
    confirmedAt: "2026-10-10 13:57:37（北京时间，UTC+8；完整回读确认）",
    sizeBytes: 352209994,
    sha256: "c4ba88eb4f654599cbefd1a4f6eafde55865cb0035ae66c55ff82887cf4ac3dd",
    sourceCommit: "a8aaf44984c1d9ce85981bd4756e71372c6b8cb8",
  },
  validationEnvironments,
  product: {
    name: "党建智办",
    englishName: "PartyOps",
    version: "1.4.5-rc.6",
    releaseTag: "v1.4.5-rc.6",
    manifestPath: "/releases/v1.4.5-rc.6/release-manifest-windows-startup-20261017.json",
    releaseLabel: "限域验收候选版 · 按系统与处理器选择",
    publicationState: "published",
    sourceCommit: null,
    incidentNotice:
      "1.4.5-rc.6 为限域验收候选版：各包仅在列明环境和范围内验收，不代表完整生命周期或所有系统均已验证。Apple Silicon 支持 macOS 15 及以上，安装与自检通过，WPS 排版未实测；Intel Mac 保持 macOS 11 及以上，排版分页需人工核对正文、字体和页码。Mac 候选未使用 Developer ID、未公证，AI 需另行配置模型。升级前请备份。",
    tagline: "本地优先的党建协同工作台",
    description:
      "把事项办理、真实文件、重要档案、迎检材料与工作留痕，收进一套可在单位局域网内落地的协同闭环。",
    keywords: ["本地优先", "局域协同", "数据自主", "开源可审计"],
    confidentialityWarning:
      "安全提醒：不建议在涉密、敏感电脑上安装 PartyOps，也不得使用 PartyOps 处理涉密文件。",
    downloadGuidance:
      "请按电脑系统选择安装包，升级前先备份。M 系列需 macOS 15 或更新版本；Intel Mac 仍支持 macOS 11 及以上，排版后请再检查。",
  },
  links: {
    ...rc6Links,
    github: "https://github.com/linsky-dev/PartyOps",
    gitee: "https://gitee.com/lintianbei/PartyOps",
    release:
      "https://github.com/linsky-dev/PartyOps/releases/tag/v1.4.5-rc.6",
    changelog: "/changelog",
    models: "/models",
    resume: "/resume/",
    windowsSha:
      "https://github.com/linsky-dev/PartyOps/releases/download/v1.4.5-rc.6/PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe.sha256",
    windowsManifest:
      "https://github.com/linsky-dev/PartyOps/releases/download/v1.4.5-rc.6/release-manifest.json",
    windowsStartupManifest:
      "https://github.com/linsky-dev/PartyOps/releases/download/v1.4.5-rc.6/release-manifest-windows-startup-20261017.json",
    macosArm64:
      "https://github.com/linsky-dev/PartyOps/releases/download/v1.4.5-rc.6/PartyOps_1.4.5-rc.6_macos_arm64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg",
    macosX86_64:
      "https://github.com/linsky-dev/PartyOps/releases/download/v1.4.5-rc.6/PartyOps_1.4.5-rc.6_macos_x86_64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg",
    macosArm64Official:
      "https://github.com/linsky-dev/PartyOps/releases/download/v1.4.5-rc.6/PartyOps_1.4.5-rc.6_macos_arm64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg",
    macosX86_64Official:
      "https://github.com/linsky-dev/PartyOps/releases/download/v1.4.5-rc.6/PartyOps_1.4.5-rc.6_macos_x86_64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg",
    issues: "https://github.com/linsky-dev/PartyOps/issues",
    discussions:
      "https://github.com/linsky-dev/PartyOps/discussions",
    contributing:
      "https://github.com/linsky-dev/PartyOps/blob/main/CONTRIBUTING.md",
    docs: "https://github.com/linsky-dev/PartyOps/tree/main/docs",
    security:
      "https://github.com/linsky-dev/PartyOps/blob/main/SECURITY.md",
    license:
      "https://github.com/linsky-dev/PartyOps/blob/main/LICENSE",
    thirdParty:
      "https://github.com/linsky-dev/PartyOps/blob/main/THIRD_PARTY_NOTICES.md",
  },
  /* 国内链接和确认时间只取真实发布记录；ARM 新候选不沿用 RC4 回读时间。 */
  downloadFiles: {
    ...rc6Files,
    windowsSha: "https://github.com/linsky-dev/PartyOps/releases/download/v1.4.5-rc.6/PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe.sha256",
    windowsManifest: "/releases/v1.4.5-rc.6/release-manifest.json",
    windowsStartupManifest: "/releases/v1.4.5-rc.6/release-manifest-windows-startup-20261017.json",
    macosIntelManifest: "/releases/v1.4.5-rc.6/release-manifest.json",
    macosArm64Manifest: "/releases/v1.4.5-rc.6/release-manifest-macos-arm64-20261009.json",
    kylinArm64Manifest: "/releases/v1.4.5-rc.6/release-manifest-kylin-arm64-20261016.json",
    macosArm64: rc6Files.macosArm64,
  },
  downloadMirrors: [
    {
      id: "local",
      name: "国内主下载线路",
      note: "Cloud Studio /downloads/ 国内线路；仅启用已完整回下载校验的制品",
      prefix: "",
    },
    {
      id: "official",
      name: "官方源 · GitHub",
      note: "海外直连，速度取决于网络",
      prefix: "",
    },
  ],
  artifactIntegrity: {
    ...rc6Integrity,
    macosArm64: rc6Integrity.macosArm64 || { filename: "PartyOps_1.4.5-rc.6_macos_arm64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg", sizeBytes: 588743660, size: "588,743,660 字节（561.5 MB）", sha256: "bcc370a22545c31ad789324d1d153a0d6faa30ec61fc0632e4b004f82c0ff736", uploadedAt: "待发布回读确认", mirrorVerifiedAt: "待发布回读确认", sourceCommit: "9de8b721225cf6913727614540d431e1c48811e3", sourceFingerprint: "c48933169424f5e9dc81b06eec7ba92c94dc563cc5ffeea62470ab5906fedea0", publicationState: "pending" },
  },
  navigation: [
    { label: "亮点", href: "#highlights" },
    { label: "界面预览", href: "#screenshots" },
    { label: "下载", href: "#download" },
    { label: "安装指南", href: "#install" },
    { label: "本地模型", href: "/models" },
    { label: "更新日志", href: "/changelog" },
    { label: "开源与支持", href: "#community" },
  ],
  changelog: [
    {
      version: "v1.4.5-rc.6 · 麒麟 ARM64 完整候选包",
      releasedAt: "2026-10-16 13:10:48（北京时间，UTC+8）",
      timeLabel: "公开回读确认",
      status: "限域验收候选版",
      summary: "修复麒麟 ARM64 启动与排版组件问题，提供完整安装包",
      changes: [
        "修复旧数据升级、启动配置检查与排版运行库依赖问题；完整包接替旧 ARM64 主包和诊断修复包。",
        "验证环境：银河麒麟桌面 V10 SP1 2503 ARM64，QEMU 虚拟机，WPS 12.8.2.1119.AK.preload.sw。覆盖安装、重启和原账号、事项、日历、附件保留通过；六项排版、字体及三页金样通过。",
        "新包与已安装验收版本的程序和安装脚本一致，仅更新验收记录及校验清单；新包解包后的程序与排版宿主自检通过。麒麟 9000C 实机及其他系统版本仍需复验。",
        "先备份，再覆盖安装，不要先卸载或删库。安装后核对原账号、事项、日历和附件，并再次启动确认。",
      ],
    },
    {
      "version": "v1.4.5-rc.6 · ARM64 启动诊断修复包",
      "releasedAt": "2026-10-10 13:57:37（北京时间，UTC+8；完整回读确认）",
      "timeLabel": "公开回读确认",
      "status": "独立诊断候选",
      "summary": "修复特定旧数据升级造成的 ARM64 启动失败",
      "changes": [
        "修复旧数据库缺少迁移标记但已含后续表时重复建表导致的启动退出；不按处理器型号写死。",
        "在麒麟/UOS/deepin ARM64 推荐卡片下补充修复入口，原十包下载、默认选包及其验证范围不变。",
        "先备份，再覆盖安装，不要先卸载或删库；安装后核对原账号、事项、日历和附件，并再次启动确认。",
        "验证：70项源码回归、QEMU真实成品自检、新旧库升级至0026、重复启动与回滚独立审计通过。原额外跨SQLite版本文件头断言失败记录保留，独立审计确认恢复库与产品备份快照一致、业务数据和附件完整。",
        "范围：麒麟原机、systemd目标安装、真实桌面与本次WPS重跑未验证；正式全功能门禁仍为STALE，动态门禁77，不声明完整功能通过。",
        "公开原始验收产物，DEB内容未改；包内“未公开发布”为构建时标记。详见独立诊断清单与覆盖安装说明。"
      ],
      "limitations": []
    },
    {
      version: "v1.4.5-rc.6",
      releasedAt: beijingConfirmation(validationEnvironments.publication_evidence.github_published_at),
      timeLabel: "预发布确认",
      status: "限域验收候选版 · 已预发布",
      summary: "功能更新与问题修复",
      changes: [
        "新增本机智能编排与步骤确认，支持模型规划、权限校验和规则回退。",
        "Windows x64 启动修复包：修复个人模式启动诊断与已保存数据目录处理；在 Windows 10 Pro 22H2（19045）x64/QEMU 验证覆盖安装、重启及原账号、事项、日历、附件保留。WPS 12.1.0.28488 六项排版、五字体、三页金样及保存重开通过；此范围不代表完整生命周期或其他 Windows 环境均通过。",
        "修复 Windows 启动诊断误读历史日志的问题，准确区分本次启动的权限、依赖和运行错误。",
        "修复个人模式配置读取和数据目录处理，保留已保存的个人数据目录。",
        "修复同一电脑不同账号的公文排版实例互相干扰，分别绑定排版端口和访问票据。",
        "缩短公文排版临时路径，保留原始中文文件名，并提示超长路径问题。",
        "完善 Windows 系统及当前用户字体读取，支持识别仅为当前用户安装的字体，并在排版时重新检查。",
        "修复国产 Linux 用户配置解析及桌面启动，配置异常时提供明确诊断和修复向导，保留原业务数据。",
        "新增龙芯 LoongArch64 DEB 安装包，并完善 Windows 7 与 Windows 10 的 32 位安装包适配。",
        "新增 Intel x86_64 Mac RC6 安装包；有限验收范围内的排版与批量格式功能可用，分页结果需人工核对。",
        "新增 Apple Silicon ARM64 RC6 候选，支持 macOS 15 及以上；在 macOS 15.7.9 ARM 原生托管主机完成安装、包架构校验和四项命令行自检。WPS 六类排版功能与 AI 业务使用流程未实测。",
      ],
      limitations: [],
    },
    {
      version: "v1.4.5-rc.5",
      internalOnly: true,
      summary: "内部测试版本，未公开发布。",
      changes: [],
      limitations: [],
    },
    {
      version: "v1.4.5-rc.4",
      releasedAt: "2026-08-26 15:51:05（北京时间，UTC+8）",
      status: "预发布",
      summary: "修复 rc.3 从 0023 原位升级时在迁移前崩溃的问题，并对九个平台重新构建、启动验证和完整公网回读。",
      changes: [
        "升级前备份改为不依赖当前 ORM 的 SQLite 在线快照；迁移成功后才登记 0024 的备份和升级记录。",
        "备份、迁移、迁移后结构校验、管理员不变量、SQLite/FTS5 与健康接口纳入同一启动事务；失败恢复已验证快照。",
        "启动错误重新分类：备份失败使用 UPGRADE_BACKUP_FAILED，结构迁移失败使用 DATABASE_SCHEMA_FAILED，SQLite 驱动、版本或 FTS5 故障才使用 SQLITE_RUNTIME_FAILED。",
        "Windows 覆盖安装可识别并受控接管 rc.1、rc.2 与已撤回 rc.3 的 PartyOps 服务残留；未知同名服务仍会安全拒绝。",
        "Win7 x64 移除调用 Win8+ API 的不兼容 llama.cpp 运行时，保留核心业务、协同、OCR 与语义重排，避免伪兼容。",
        "九个平台安装包与更新包全部重建，Cloud Studio 19 个冻结制品均完成公网全量回读和 SHA-256 校验。",
      ],
      limitations: [
        "Windows 与 macOS 仍无商业签名；macOS 为 ad-hoc 签名且未公证，首次放行前必须核对官网 SHA-256",
        "Linux ARM64 通过 QEMU 动态门禁，但尚无对应商业系统真机桌面交互证据，支持级别仍为 preview",
        "Needle 2 签名包目前仅验证 Windows AMD64，其他平台不得导入该包",
      ],
    },
    {
      version: "v1.4.5-rc.3",
      releasedAt: "2026-08-25 23:30:00（北京时间，UTC+8）",
      status: "已撤回",
      summary: "该候选版因 0023 原位升级在迁移前备份阶段失败而撤回；数据库不是损坏，官网和在线更新已临时恢复 rc.2。",
      changes: [
        "公文规范排版改为页面内完成选择、诊断、确认、排版、复检和导出；文件始终留在当前电脑，本机服务使用短时设备票据并在 15 分钟内清理临时副本。",
        "新增通用台账导入：支持主流电子表格的表头识别、字段映射、全量校验、重复处理、批次审计和冲突保护撤销，可用于发展党员、人事调动、年度考核和重要档案。",
        "发展党员人员台账新增真实进度和全流程时间轴；实际完成、逾期、即将到期、未来计划和人工调整分色呈现，上游变化只重算尚未发生的节点。",
        "新增 Needle 2 离线意图助手签名包；只输出结构化操作预览，写操作仍需权限校验和人工确认，低置信度自动回退规则引擎。",
        "完成业务生命周期审查，发展党员台账等记录支持影响分析、受控作废与恢复；提醒、导入、导出、协同权限和错误恢复均纳入回归测试。",
        "Windows 安装目录权限探针和个人运行权限诊断改为精确错误码；文档数据目录可选择当前用户有权限的本机固定磁盘，不再强制使用系统盘。",
        "官网保留全部稳定功能实景并扩展至 37 张当前版本真实截图，新手安装区提供可复制给 DeepSeek、豆包、Kimi、WorkBuddy 等桌面 AI 的安全验收提示词。",
      ],
      limitations: [
        "Windows 与 macOS 安装包暂无商业签名；首次运行前必须从本页官方线路下载并核对 SHA-256",
        "Needle 2 当前仅提供 Windows AMD64 签名包；其他平台不会以伪通用包冒充支持",
        "Linux ARM64 已通过 QEMU 动态门禁，但 UOS、麒麟和各原生桌面环境仍以支持矩阵中的真机验证状态为准",
        "macOS 双架构由原生 Darwin 托管机完成构建和启动自检，安装包为 ad-hoc 签名、未公证，且尚无用户设备交互验收",
      ],
    },
    {
      version: "v1.4.5-rc.2",
      releasedAt: "2026-08-24 21:26:47（北京时间，UTC+8）",
      status: "候选版已发布",
      summary: "新增离线公文规范排版，集中修复身份重配、协同地址、文件打开、跨平台启动、官方更新与发展党员预测，并提供正式签名本地模型。",
      changes: [
        "新增“公文规范排版”：DOCX 依据 GB/T 9704-2012 在本机离线诊断、排版、复检和导出；DOC/WPS 只调用本机办公套件转换，文件内容不进入服务器、数据库或 AI。",
        "运行身份重配改为可轮询、可回滚的本机事务；严格区分回环浏览、监听、自动探测和协同公布地址，禁止把 127.0.0.1 误发给协同机。",
        "原始文件中心修复 PDF 同源预览和一次性持久化打开授权；WPS 等不支持结构化预览的格式直接切换本机打开或下载，失败原因分别显示。",
        "发展党员快速测算直接生成全部后续第一轮参考计划，实际日期、法定边界、参考日期和人工调整分开保存并可追溯重算。",
        "麒麟安装必需阶段限制在 30 秒内；Windows 启动前置探针细分依赖、配置、端口和权限；macOS 双架构在 Python 前创建启动日志并完成原生 LaunchServices 自检。",
        "安全卸载明确区分仅删程序与删除本机数据；数据预检失败自动退回保留数据，避免误删；具备业务权限的协同用户可共同新建、编辑和提交党务工作。",
        "官方更新清单使用正式 Ed25519 信任根，错误细分为 DNS、TLS、代理、超时、HTTP、清单、签名、哈希和平台不匹配；rc.1 及更早版本须先用完整安装器升级信任根。",
        "官网提供 BGE Small、Qwen2.5 0.5B、Qwen3 0.6B 与 DeepSeek R1 Distill 1.5B 四个正式签名直导包，并以实测内存、速度、优缺点帮助新手选型。",
        "帮助与上手和各新增页面的“本页帮助”已同步；官网界面实景替换为 8 张 rc.2 脱敏真实运行截图。",
      ],
      limitations: [
        "Windows 安装器尚无商业代码签名，首次运行前请核对 SHA-256",
        "Windows 7 与国产 Linux 尚未完成对应用户真机交互验收，ARM64 Linux 已通过 QEMU 动态门禁",
        "macOS 双架构已通过原生安装与 LaunchServices 启动门禁；安装包为 ad-hoc 签名、未公证，且尚无用户设备交互验收",
        "模型不是核心功能的必装项；只有来源、许可证、签名和资源门禁完整的模型包才允许直接导入",
      ],
    },
    {
      version: "v1.4.4",
      releasedAt: "2026-08-21 23:03:48（北京时间，UTC+8）",
      status: "已发布",
      summary: "集中修复跨版本协同、跨平台启动、文件打开与通知一致性，并补齐会议筹备、结构化文档和发展党员全周期能力。",
      changes: [
        "协同凭据升级为 v2 设备凭据；旧绑定可迁移，401/403 停止无限重试并进入一次性重新授权，页面启动不再被灾备同步拖死。",
        "Windows、麒麟/UOS 与 macOS 增加统一启动状态和前置探针；macOS 两种架构均在原生构建环境完成安装与启动自检。",
        "文件打开授权改为哈希化、一次性、可持久化凭据；PDF 使用同源内联预览，WPS 等不支持格式明确切换到本机程序或下载。",
        "用户支持影响分析、责任移交、软删除、恢复与角色修改；禁止删除当前用户和最后一名管理员，并撤销被归档用户的会话与设备令牌。",
        "新增党委会等党建会议模板、固定筹备步骤、负责人和进度、结构化在线文档、修订历史、DOCX 导出及年度统计。",
        "新增发展党员档案、法规最早/截止时间、参考计划与人工调整分离的一键生成、分级提醒和 Word/Excel 统计导出。",
        "任务改期后立即收敛未读通知，撤销旧时限提醒并更新新时限；已读历史继续保留审计。",
        "主机对外地址可以验证后事务化修改；HTTP 大模型 API 允许由管理员明确启用，界面持续提示传输风险。",
      ],
      limitations: [
        "新增平台制品已通过构建和静态门禁；真机验证状态以支持矩阵为准",
        "Windows 安装器未使用商业代码签名",
        "macOS 安装包未使用 Developer ID、未公证，首次安装需按 FAQ 在系统安全界面放行",
        "Windows ARM64、Linux i686/ARMv7/LoongArch64/RISC-V64 尚未完成原生依赖闭包与真机验证，因此不提供伪兼容包",
      ],
    },
    {
      version: "v1.4.3-rc.9",
      releasedAt: "2026-08-20 19:25:07（北京时间，UTC+8）",
      status: "已发布",
      summary: "从安装所有权、进程身份和数据可恢复性出发，集中修复跨平台启动、安装回滚与三项业务闭环问题。",
      changes: [
        "UOS 启动器迁移旧绑定配置、回收失效启动锁并保留子进程退出证据，避免安装后有图标却长期等不到配置页面。",
        "个人模式遇到 18775 被未知程序占用时不再误杀进程；只有可执行路径、命令行标记和运行身份均匹配才接管，否则自动选择安全空闲端口。",
        "Windows 主机服务升级采用三项所有权证明；同名未知服务保持不变并显示 LEGACY_SERVICE_CONFLICT，禁止按名称粗暴删除。",
        "Linux 封包边界统一 desktop 文件为 UTF-8/LF，安装后逐项验证入口、权限、架构与健康端点，修复 PACKAGE_DESKTOP_ENTRY_INVALID 和 dpkg 半配置。",
        "页面、服务和安装载荷使用同一版本指纹；旧进程或旧页面不会再冒充当前运行时，RPM 回滚种子也保持在 rc.9 代际。",
        "Windows 协议注册改用当前用户范围且不覆盖第三方归属；Win7 改按安全 DLL API 的真实能力识别更新，不再只看 KB 编号。",
        "三个 Windows 安装器新增最终主程序启动门禁：在写入服务和协议前真实拉起个人模式，验证 RSA/Fernet、SQLite/FTS5、健康接口和前端入口；子进程提前退出则整单回滚。",
        "Win11 加入静态资源与健康端点启动看门狗，持续白屏会落入可复制诊断；UOS 连续双击复用同一事务并自动回收失效锁。",
        "一事一档新增带权限、并发检查和审计记录的回退；文档管理中心恢复新建文件夹操作；发展党员节点结果恢复标准 DOCX 导出。",
      ],
      limitations: [
        "未签名测试候选",
        "Windows 7、麒麟、UOS、deepin、openEuler 尚未完成对应用户真机交互验收",
        "macOS 原生包使用 ad-hoc 签名，尚无 Developer ID、公证和用户真机交互反馈",
        "Win7 已停止系统级安全维护，只建议在受控局域网使用",
      ],
    },
    {
      version: "v1.4.3-rc.8",
      releasedAt: "2026-08-19 21:51:07（北京时间，UTC+8）",
      status: "已由 rc.9 取代",
      summary: "集中修复国产 Linux 安装后双击无响应、启动日志缺失、共享运行时拦截与跨版本残留进程问题。",
      changes: [
        "麒麟、UOS、deepin 与 openEuler 的主程序、协同端、配置向导和更新器统一复用固定运行时，不再把 libgcc_s.so.1 等共享库解包为临时可执行文件。",
        "国产 Linux 桌面入口在启动动作前创建私有日志；用户配置目录异常时自动使用稳定后备目录，并生成非空 startup-diagnostic.txt。",
        "个人或主机进程退出、PID 文件缺失、系统服务停止、端口未就绪或健康检查超时时立即显示稳定中文诊断码，不再机械等待后只报笼统超时。",
        "连续双击桌面图标会复用同一个配置工具，启动事务按当前用户串行化，避免多个向导覆盖页面地址或残留后台进程。",
        "桌面入口会核对健康端点的实际版本；端口上残留旧进程时直接提示 RUNTIME_VERSION_MISMATCH，不会打开新旧版本混合页面。",
        "Windows 无控制台桌面入口增加顶层中文诊断与紧急日志，未预料异常不再表现为黑窗口一闪而过。",
        "Linux 安装后事务会核对载荷版本和健康端点实际版本；版本不一致时安装配置直接失败并保留可读诊断。",
        "银河麒麟桌面 V10 SP1 2107—2503 统一按处理器架构选包；飞腾 D2000/FT-2000、麒麟 9000C/9006C/990、鲲鹏等 aarch64 电脑共用 ARM64 DEB，不再让新手按 CPU 型号猜包。",
        "新增 macOS 11 及以上双架构桌面包：Apple 芯片使用 ARM64，Intel 芯片使用 x86_64；首次配置、个人、主机与协同模式沿用同一套本地优先流程。",
        "修复 macOS 默认磁盘不区分文件名大小写时桌面入口与核心进程名称碰撞、安装后无法启动的问题。",
        "macOS 安装包会先逐项校验并签署 OCR、本地模型等嵌套运行时，再处理桌面主入口和完整应用，避免全新运行时造成安装后无法打开。",
      ],
      limitations: [
        "未签名候选版",
        "Windows 7、麒麟、UOS、deepin、openEuler 未执行对应真机运行验收",
        "macOS 包已在对应架构的原生构建环境完成安装和自检，但尚无开发者证书、公证及用户真机运行反馈",
        "macOS 本版只提供首次安装 PKG，尚未发布系统内更新包",
        "Win7 已停止系统级安全维护，只建议在受控局域网使用",
      ],
    },
    {
      version: "v1.4.3-rc.7",
      releasedAt: "2026-08-19 16:12:18（北京时间，UTC+8）",
      status: "已由 rc.8 取代",
      summary: "集中修复首次配置连接拒绝、跨版本旧进程、Win7 首页错误、国产 Linux 安装后无法启动与模式切换回滚。",
      changes: [
        "个人模式创建管理员前会重新确认同版本进程健康；连接结果不明确时不自动重发账号、密码和一次性令牌，而是显示中文原因并让用户明确重试。",
        "个人模式覆盖升级会停止遗留的局域网主机服务并恢复为手动启动，同时保持专业更新服务自动运行，避免端口争用、页面与服务版本错配和意外开放局域网端口。",
        "浏览器入口加入运行版本指纹，旧页面仅自动强制刷新一次；持续不一致时给出关闭旧标签页、重新打开和复制诊断的中文步骤。",
        "Windows 桌面入口不再弹出黑色控制台；配置损坏、进程提前退出、策略阻断或浏览器关联失败时显示中文诊断码与日志位置。",
        "旧 PartyOps 主机服务仍登记但服务管理程序已损坏时，安装器核验服务归属后通过 Windows 服务控制器原位修复，不再要求手工卸载。",
        "主机重试忽略上次启动遗留的失败状态；模式切换只有在旧模式服务真正恢复后才删除恢复快照。",
        "Win7 专用 Python 3.8 运行时修复首页 500，并按安全 DLL API 的真实可用性识别系统补丁，不再只依赖旧 KB 登记。",
        "国产 Linux 修复安装成功后双击无反应、PID 复用误判、_apt 下载目录权限和图形软件中心在 95% 处失败却缺少诊断的问题。",
        "事项负责人、创建人和管理员现在可以删除误建事项；删除与编辑使用同一并发锁，避免误删其他电脑刚完成的更新。",
      ],
      limitations: [
        "未签名候选版",
        "Windows 7、麒麟、UOS、deepin、openEuler 未执行对应真机运行验收",
        "Win7 已停止系统级安全维护，只建议在受控局域网使用",
      ],
    },
    {
      version: "v1.4.3-rc.6",
      releasedAt: "2026-08-18 23:00:37（北京时间，UTC+8）",
      status: "已由 rc.7 取代",
      summary: "修复 Windows PowerShell 5.1 安装失败、Win7 误装、冻结向导缺包，以及全平台桌面入口静默失败。",
      changes: [
        "安装目录校验脚本改用 Windows PowerShell 5.1 可确定识别的 UTF-8 BOM，解决中文注释被按 GBK/ANSI 解码后出现逗号、括号和大括号伪语法错误。",
        "安装器把校验脚本标准输出、错误输出和退出码完整写入安装日志；即使诊断文件尚未生成，也不再只显示笼统的 INSTALL_DIR_CHECK_FAILED。",
        "Windows 10/11 继续支持本机固定 D/E 盘、中文和空格自定义目录；覆盖安装会保留原数据目录并恢复主机与更新服务。",
        "Windows 10/11 通用包在 Win7 上会直接给出中文选包提示；Win7 必须使用 windows7_amd64 或 windows7_x86 专用 Python 3.8 Legacy 包。",
        "Win7 专用版不再依赖 api-ms-win-core-path-*；如系统提示缺少 DLL，请勿从第三方网站单独下载系统文件，应改用对应位数的 Win7 专用安装器。",
        "修复麒麟 ARM64 安全中心反复拦截 libgcc_s.so.1：共享库与静态资源统一为不可执行，只有受控入口保留执行权限；服务连续失败三次后停止重试并保留中文日志。",
        "修复麒麟/UOS 安装成功后双击无反应：首次配置、个人、主机与协同模式都等待真实页面或健康端点就绪再打开浏览器，浏览器关联失败会显示中文提示和日志位置。",
        "Windows 个人、主机与协同入口同样等待页面准备完成；默认浏览器关联损坏或协同进程超时时显示中文弹窗和可复制地址。",
        "修复部分 Windows 电脑安装后配置向导立即退出、没有中文提示的问题。",
        "修复 Bash 4.2 首次无参数启动退出、个人模式重登读取错误配置、个人配置被误改为 HTTPS 及协同页面无法交回桌面进程；四个 Linux 包增加桌面入口安装后自检与成品动态启动门禁。",
        "官网新增两步新手下载助手：先选系统，再按飞腾/鲲鹏或海光/兆芯等处理器品牌精确选包；架构不明与龙芯/RISC-V 不再出现猜测性下载按钮。",
      ],
      limitations: [
        "未签名候选版",
        "Windows 7、麒麟、UOS、deepin、openEuler 未执行对应真机运行验收",
        "Win7 已停止系统级安全维护，只建议在受控局域网使用",
      ],
    },
    {
      version: "v1.4.3-rc.5",
      releasedAt: "2026-08-18 12:12:34（北京时间，UTC+8）",
      status: "已由 rc.6 取代",
      summary: "彻底修复 Windows 自定义程序目录误拦截，并重新冻结七平台安装包与专业应用内更新链。",
      changes: [
        "Windows 10/11 自定义程序目录支持本机固定 D/E 盘、中文与空格；不再因为父目录的日常文件 ACL 误报 INSTALL_DIR_PARENT_ACL_UNSAFE。",
        "安装器创建目标后收敛 ACL 为 SYSTEM/Administrators 完全控制、普通用户只读执行，同时继续拒绝网络盘、移动盘、系统目录、重解析点、磁盘根及外部非空目录。",
        "在当前 Win11 完成真实自定义路径安装、覆盖升级、保留数据卸载、重装、双服务自动启动与健康检查闭环。",
        "七个平台均提供 Ed25519 签名更新包和 format v3 更新目录，按系统、包格式与 CPU 架构精确选包并在失败时回滚。",
      ],
      limitations: [
        "未签名候选版",
        "Windows 7、麒麟、UOS、deepin、openEuler 未执行对应真机运行验收",
        "Win7 已停止系统级安全维护，只建议在受控局域网使用",
      ],
    },
    {
      version: "v1.4.3-rc.4",
      releasedAt: "2026-08-17 18:50:48（北京时间，UTC+8）",
      status: "已由 rc.5 取代",
      summary: "修复 Windows 自定义程序目录误判，完成 Win7 x64/x86 Legacy 安装器与七平台专业更新链。",
      changes: [
        "Windows 10/11 自定义程序目录现在允许受管理员保护的本机 D/E 盘、中文和空格路径，同时继续拒绝真正可被普通用户替换服务文件的目录。",
        "新增 Windows 7 SP1 x64/x86 中文品牌安装器，独立 Python 3.8 Legacy 运行时、app-local UCRT、OCR 与 PE 导入门禁完整通过。",
        "Win7 x64 提供完整主机、协同、OCR、语义重排与本地 LLM；x86 保留核心主机、协同、档案、备份和 OCR，明确关闭智能重排与本地 LLM。",
        "七个平台均提供 Ed25519 签名应用内更新包，按系统、包格式和架构精确选包，自动备份、健康检查并在失败时回滚。",
      ],
      limitations: [
        "未签名候选版",
        "Windows 7、麒麟、UOS、deepin、openEuler 未执行对应真机运行验收",
        "Win7 已停止系统级安全维护，只建议在受控局域网使用",
      ],
    },
    {
      version: "v1.4.3-rc.3",
      releasedAt: "2026-08-17 08:15（北京时间，UTC+8）",
      status: "已由 rc.5 取代",
      summary: "新增专业系统内升级、个人模式与国产 Linux 原生包，并关闭安装器、服务和依赖闭包反馈。",
      changes: [
        "Windows 安装器修复协议注册表拒绝和半安装状态，保留自定义程序目录与数据目录。",
        "新增无需管理员权限的个人使用模式与可选彻底卸载。",
        "麒麟、UOS、deepin 提供双架构 DEB；openEuler 提供双架构 RPM。",
        "更新目录升级到 format v3，按系统、包格式和架构选包并校验 Ed25519。",
      ],
      limitations: ["未签名候选版", "未提供 Win7 安装器"],
    },
    {
      version: "v1.4.3-rc.2",
      releasedAt: "2026-08-13 17:32（北京时间，UTC+8）",
      status: "已由 rc.3 取代",
      summary: "集中解决自定义数据目录、主机服务诊断、空白页与单文件下载流程。",
      changes: [
        "修复 UAC 提权后自定义数据目录丢失，支持中文、空格和非 C 盘固定磁盘路径。",
        "主机服务按阶段报告注册、子进程、端口与健康状态；未就绪时不再打开空白页面。",
        "本机健康检查和首次管理员创建固定访问 127.0.0.1，消除服务未监听时的 WinError 10061。",
        "Windows 普通用户只下载一个 EXE，安装器校验内部载荷；SHA-256 直接展示在官网。",
      ],
      limitations: ["未签名候选版", "Windows 10 与 UOS 未完成原生真机验收"],
    },
    {
      version: "v1.4.3-rc.1",
      releasedAt: "2026-08-13 前",
      status: "已由 rc.2 取代",
      summary: "首次提供 1.4.3 候选安装流程，保留为历史版本追溯。",
      changes: ["增加个人备忘、发展党员时间计算与文件协同增强。"],
      limitations: ["该历史版本已被 rc.4 替代，请只使用官网当前 1.4.5-rc.4 版本"],
    },
  ],
  proofPoints: ["完全本地运行", "不上传云端", "局域网协作", "数据不出内网"],
  community: {
    supportNotice:
      "本系统遵循开源协议，永久免费提供下载使用。党建智办由个人利用工作之余独立开发、维护与升级，与所在单位无关。欢迎体验和学习借鉴；商业使用与再分发必须严格遵守 GPLv3/AGPLv3 的源码提供和同许可证义务，禁止冒充官方、违法倒卖或以虚假承诺误导用户。因个人开发资金有限，现开放完全自愿的支持通道，作为长期免费开发的一点回馈。",
    sponsors: {
      eyebrow: "每一份支持都有名字",
      title: "感谢支持者",
      description:
        "如果 PartyOps 的体验帮助到了你，欢迎自愿赞助，支持后续开发、国产系统适配与持续更新。",
      list: [
        {
          name: "🧚今天一定会按时下班！",
          note: "首位支持者 · 2026.08",
        },
        {
          name: "子木",
          note: "支持者 · 2026.08",
        },
        {
          name: "葵了花了鹿",
          note: "支持者",
        },
      ],
      futureNote: "更多支持者的名字将继续在此出现",
    },
    support: {
      action: "支持后续开发",
      title: "感谢你的认可",
      description: "赞助完全自愿，不影响功能使用；官网只保留爱发电订阅通道。",
      provider: "爱发电",
      plan: "PartyOps 持续开发支持计划",
    },
    /* icon 为组件内 iconMap 的字符串键；hrefKey 对应 links 的键名 */
    actions: [
      {
        icon: "github",
        title: "开源透明",
        body: "查看源码、版本证据和公开路线。",
        label: "查看 GitHub 仓库",
        hrefKey: "github",
      },
      {
        icon: "issue",
        title: "报告问题",
        body: "带上版本、系统、复现步骤和脱敏日志。",
        label: "提交 Issue",
        hrefKey: "issues",
      },
      {
        icon: "discussion",
        title: "提出建议",
        body: "分享真实工作场景、卡点和希望改进的细节。",
        label: "参与 Discussions",
        hrefKey: "discussions",
      },
      {
        icon: "contribute",
        title: "参与共建",
        body: "贡献代码、文档、测试或真机验证证据。",
        label: "阅读贡献指南",
        hrefKey: "contributing",
      },
    ],
  },
  /* 新手下载助手：两步（系统 → 处理器）定位唯一兼容安装包；
   * checkText 为「我不知道，教我查看」分支的指引文案。 */
  beginnerChooser: {
    systems: [
      {
        id: "windows",
        label: "Windows 10 / 11",
        note: "大多数普通台式机、笔记本",
      },
      {
        id: "windows7",
        label: "Windows 7",
        note: "桌面左下角是圆形开始按钮",
      },
      {
        id: "linux-deb",
        label: "麒麟 / UOS / deepin",
        note: "国产桌面系统，下载 DEB",
      },
      {
        id: "linux-rpm",
        label: "openEuler",
        note: "欧拉系统，下载 RPM",
      },
      {
        id: "macos",
        label: "macOS · Apple 15+ / Intel 11+",
        note: "Apple 芯片需 macOS 15 及以上，Intel 仍为 11 及以上",
      },
    ],
    processors: {
      windows: [
        { id: "amd64", label: "Intel / AMD / 海光（绝大多数电脑）" },
        { id: "x86", label: "Windows 10 · 32 位操作系统" },
        { id: "unsupported", label: "骁龙 / ARM 电脑（本批未测）" },
        { id: "unknown", label: "我不知道，教我查看" },
      ],
      windows7: [
        { id: "amd64", label: "64 位操作系统" },
        { id: "x86", label: "32 位操作系统" },
        { id: "unknown", label: "我不知道，教我查看" },
      ],
      "linux-deb": [
        { id: "arm64", label: "飞腾 D2000 / 麒麟 9000C / 鲲鹏 / ARM64" },
        { id: "amd64", label: "海光 / 兆芯 / Intel / AMD" },
        { id: "loong64", label: "龙芯 / LoongArch64" },
        { id: "unsupported", label: "RISC-V 或其他处理器" },
        { id: "unknown", label: "我不知道，教我查看" },
      ],
      "linux-rpm": [
        { id: "arm64", label: "飞腾 D2000 / 麒麟 9000C / 鲲鹏 / ARM64" },
        { id: "amd64", label: "海光 / 兆芯 / Intel / AMD" },
        { id: "unsupported", label: "龙芯 / LoongArch / RISC-V" },
        { id: "unknown", label: "我不知道，教我查看" },
      ],
      macos: [
        { id: "arm64", label: "Apple M1 / M2 / M3 / M4 等 Apple 芯片" },
        { id: "x86_64", label: "Intel 处理器" },
        { id: "unknown", label: "我不知道，教我查看" },
      ],
    },
    matches: {
      "windows:amd64": {
        fileKey: "windows",
        sentence: "Windows 10/11 + Intel、AMD 或海光电脑",
      },
      "windows:x86": {
        fileKey: "windows7X86",
        sentence: "Windows 10 + 32 位操作系统（共用 b3 专用包）",
      },
      "windows7:amd64": {
        fileKey: "windows7Amd64",
        sentence: "Windows 7 + 64 位操作系统",
      },
      "windows7:x86": {
        fileKey: "windows7X86",
        sentence: "Windows 7 + 32 位操作系统",
      },
      "linux-deb:arm64": {
        fileKey: "linuxDebArm64",
        sentence: "麒麟、UOS 或 deepin + 飞腾 D2000、麒麟 9000C、鲲鹏或其他 ARM64 电脑",
      },
      "linux-deb:amd64": {
        fileKey: "linuxDebAmd64",
        sentence: "麒麟、UOS 或 deepin + 海光、兆芯、Intel 或 AMD 电脑",
      },
      "linux-deb:loong64": {
        fileKey: "linuxDebLoong64",
        sentence: "龙芯 LoongArch64 + Deepin 25.2.0（其他发行版未验收）",
      },
      "linux-rpm:arm64": {
        fileKey: "linuxRpmArm64",
        sentence: "openEuler + 飞腾 D2000、麒麟 9000C、鲲鹏或其他 ARM64 电脑",
      },
      "linux-rpm:amd64": {
        fileKey: "linuxRpmAmd64",
        sentence: "openEuler + 海光、兆芯、Intel 或 AMD 电脑",
      },
      "macos:arm64": {
        fileKey: "macosArm64",
        sentence: "macOS 15 及以上 + Apple M 系列芯片",
      },
      "macos:x86_64": {
        fileKey: "macosX86_64",
        sentence: "macOS 11 及以上 + Intel 处理器",
      },
    },
    checkText: {
      macos: "点击左上角苹果菜单 → 关于本机：显示 Apple M1、M2、M3、M4 等就选 Apple 芯片；明确显示 Intel 才选 Intel。终端运行 uname -m 时，arm64 对应 Apple 芯片，x86_64 对应 Intel。",
      windows7: "打开“控制面板 → 系统”，查看“系统类型”：写着 64 位就选 64 位，写着 32 位就选 32 位。",
      windows: "打开“设置 → 系统 → 系统信息”，查看“系统类型”：x64 选 Intel / AMD / 海光；Windows 10 的 32 位系统选 x86 专用包。ARM 本批未测，不要下载 x86/x64 包替代。",
      linux: "打开“系统设置 → 关于本机”查看处理器名称；最准确的方法是在终端输入 uname -m：x86_64 选海光/兆芯/Intel/AMD，aarch64 选飞腾 D2000、麒麟 9000C、鲲鹏或其他 ARM64，loongarch64 对应龙芯。还须核对发行版和 DEB/RPM 包类型；银河麒麟 V10 SP1 的 2107/2203/2303/2403/2503 使用同一 ARM64 选包规则。",
    },
  },
  highlights: [
    {
      icon: "week",
      title: "今日工作与可靠提醒",
      body: "把事项、日历、材料、党务节点和逾期风险放在第一屏；改期后提醒立即收敛，旧通知不会继续误导。",
    },
    {
      icon: "collaboration",
      title: "通用台账安全导入",
      body: "发展党员、调动、考核和重要档案均可识别表头、预览映射、全量校验后导入；不确定字段必须人工确认并支持撤销。",
    },
    {
      icon: "archive",
      title: "党务真实进度闭环",
      body: "三会一课、中心组、党建会议与发展党员档案共同协作；真实进度、法规边界和未来计划在同一时间轴可追溯呈现。",
    },
    {
      icon: "memo",
      title: "内嵌公文规范排版",
      body: "无需另开助手窗口，在页面内完成诊断、确认、排版、复检与导出；文档只在当前电脑临时处理，不进入主机或 AI。",
    },
    {
      icon: "files",
      title: "文件、档案与协同",
      body: "真实文件保持原位，PDF 同源预览、Office 本机打开；具备权限的协同人员也能新建业务、编辑文档和归档材料。",
    },
    {
      icon: "control",
      title: "可恢复的安全治理",
      body: "删除先看影响、再停用或归档，保留审计并可恢复；安装、启动、更新、备份、模型签名和协同权限都有明确门禁。",
    },
  ],
  galleryCategories: [
    { id: "all", label: "全部" },
    { id: "today", label: "今日" },
    { id: "work", label: "工作" },
    { id: "party", label: "党务" },
    { id: "materials", label: "资料" },
    { id: "collaboration", label: "协同" },
    { id: "management", label: "管理" },
  ],
  screenshots: [
    {
      src: "/screenshots/partyops-today-1.4.5-rc.3.png",
      category: "today",
      title: "今日工作台",
      caption: "事项、党务提醒、风险与周期汇总在第一屏集中呈现",
      alt: "PartyOps 1.4.5-rc.3 今日工作台真实界面",
    },
    {
      src: "/screenshots/partyops-memos-1.4.5-rc.3.png",
      category: "today",
      title: "备忘录",
      caption: "随手记录待办线索，再按需转成正式工作事项",
      alt: "PartyOps 1.4.5-rc.3 备忘录真实界面",
    },
    {
      src: "/screenshots/partyops-tasks-1.4.5-rc.3.png",
      category: "work",
      title: "事项与清单",
      caption: "主办、协办、审核与日期提醒沿同一责任链推进",
      alt: "PartyOps 1.4.5-rc.3 事项与清单真实界面",
    },
    {
      src: "/screenshots/partyops-my-work-1.4.5-rc.3.png",
      category: "work",
      title: "我的工作",
      caption: "只看分配给自己的任务、协作请求与待审核工作",
      alt: "PartyOps 1.4.5-rc.3 我的工作真实界面",
    },
    {
      src: "/screenshots/partyops-notifications-1.4.5-rc.3.png",
      category: "work",
      title: "通知中心",
      caption: "事项改期后通知即时联动，已失效提醒自动退出活动列表",
      alt: "PartyOps 1.4.5-rc.3 通知中心真实界面",
    },
    {
      src: "/screenshots/partyops-calendar-1.4.5-rc.3.png",
      category: "work",
      title: "工作日历",
      caption: "按日、周查看工作安排与节点，提前识别时间冲突",
      alt: "PartyOps 1.4.5-rc.3 工作日历真实界面",
    },
    {
      src: "/screenshots/partyops-inbox-1.4.5-rc.3.png",
      category: "work",
      title: "快速收件箱",
      caption: "零散信息先快速归集，再转入事项、档案或知识库",
      alt: "PartyOps 1.4.5-rc.3 快速收件箱真实界面",
    },
    {
      src: "/screenshots/partyops-reports-1.4.5-rc.3.png",
      category: "work",
      title: "周期汇总",
      caption: "从真实完成记录生成周报、月报和年度工作依据",
      alt: "PartyOps 1.4.5-rc.3 周期汇总真实界面",
    },
    {
      src: "/screenshots/partyops-journal-1.4.5-rc.3.png",
      category: "work",
      title: "工作日志",
      caption: "按时间保留办理过程、状态变化与责任人记录",
      alt: "PartyOps 1.4.5-rc.3 工作日志真实界面",
    },
    {
      src: "/screenshots/partyops-topics-1.4.5-rc.3.png",
      category: "work",
      title: "专题工作空间",
      caption: "将跨周期事项、材料和参与人归集为独立专题",
      alt: "PartyOps 1.4.5-rc.3 专题工作空间真实界面",
    },
    {
      src: "/screenshots/partyops-party-life-1.4.5-rc.3.png",
      category: "party",
      title: "三会一课",
      caption: "会议计划、召开、材料和归档形成可追溯闭环",
      alt: "PartyOps 1.4.5-rc.3 三会一课真实界面",
    },
    {
      src: "/screenshots/partyops-study-center-1.4.5-rc.3.png",
      category: "party",
      title: "中心组学习",
      caption: "年度学习安排、专题资料与成果统一管理",
      alt: "PartyOps 1.4.5-rc.3 中心组学习真实界面",
    },
    {
      src: "/screenshots/partyops-party-development-1.4.5-rc.3.png",
      category: "party",
      title: "发展党员快速测算",
      caption: "录入申请日期与真实进度，先生成可解释、可调整的完整参考计划",
      alt: "PartyOps 1.4.5-rc.3 发展党员快速测算真实界面",
    },
    {
      src: "/screenshots/partyops-development-timeline-1.4.5-rc.3.png",
      category: "party",
      title: "发展党员全流程时间轴",
      caption: "19 个详细节点分清实际事实、法定边界、逾期风险和未来参考计划",
      alt: "PartyOps 1.4.5-rc.3 发展党员全流程时间轴真实界面",
    },
    {
      src: "/screenshots/partyops-ledger-import-1.4.5-rc.3.png",
      category: "work",
      title: "通用台账导入",
      caption: "六步完成工作表识别、字段映射、全量校验、重复处理与可撤销归档",
      alt: "PartyOps 1.4.5-rc.3 通用台账导入真实界面",
    },
    {
      src: "/screenshots/partyops-business-meetings-1.4.5-rc.3.png",
      category: "party",
      title: "其他党建会议",
      caption: "会议、议题、资金与筹备步骤形成年度统计闭环",
      alt: "PartyOps 1.4.5-rc.3 其他党建会议真实界面",
    },
    {
      src: "/screenshots/partyops-file-center-1.4.5-rc.3.png",
      category: "materials",
      title: "原始文件中心",
      caption: "原文件保持原位，预览、本机打开与操作目录清晰可见",
      alt: "PartyOps 1.4.5-rc.3 原始文件中心真实界面",
    },
    {
      src: "/screenshots/partyops-official-format-1.4.5-rc.3.png",
      category: "materials",
      title: "公文规范排版",
      caption: "本机一次性处理，按 GB/T 9704-2012 诊断、排版与复核",
      alt: "PartyOps 1.4.5-rc.3 公文规范排版真实界面",
    },
    {
      src: "/screenshots/partyops-archives-1.4.5-rc.3.png",
      category: "materials",
      title: "重要档案",
      caption: "以责任、分类和保管状态组织关键业务档案",
      alt: "PartyOps 1.4.5-rc.3 重要档案真实界面",
    },
    {
      src: "/screenshots/partyops-inspection-1.4.5-rc.3.png",
      category: "materials",
      title: "迎检与归档",
      caption: "按检查主题核对材料完整性并生成归档清单",
      alt: "PartyOps 1.4.5-rc.3 迎检与归档真实界面",
    },
    {
      src: "/screenshots/partyops-knowledge-1.4.5-rc.3.png",
      category: "materials",
      title: "知识与联系人",
      caption: "沉淀办理规范、易错点、交接说明和常用联系人",
      alt: "PartyOps 1.4.5-rc.3 知识与联系人真实界面",
    },
    {
      src: "/screenshots/partyops-document-comparisons-1.4.5-rc.3.png",
      category: "materials",
      title: "文档比较与查重",
      caption: "在本地比对版本差异，减少重复材料和误覆盖",
      alt: "PartyOps 1.4.5-rc.3 文档比较与查重真实界面",
    },
    {
      src: "/screenshots/partyops-business-documents-1.4.5-rc.3.png",
      category: "materials",
      title: "在线业务文档",
      caption: "议程、通知和纪要支持协同编辑、修订历史与导出",
      alt: "PartyOps 1.4.5-rc.3 在线业务文档真实界面",
    },
    {
      src: "/screenshots/partyops-collaboration-1.4.5-rc.3.png",
      category: "collaboration",
      title: "设备协同",
      caption: "设备状态、版本、凭据与安全操作集中管理",
      alt: "PartyOps 1.4.5-rc.3 设备协同真实界面",
    },
    {
      src: "/screenshots/partyops-fleet-inbox-1.4.5-rc.3.png",
      category: "collaboration",
      title: "文件接收箱",
      caption: "协同机接收主机授权文件并保留来源与处理状态",
      alt: "PartyOps 1.4.5-rc.3 文件接收箱真实界面",
    },
    {
      src: "/screenshots/partyops-fleet-transfers-1.4.5-rc.3.png",
      category: "collaboration",
      title: "传输任务",
      caption: "查看跨设备传输进度、失败原因与安全重试入口",
      alt: "PartyOps 1.4.5-rc.3 传输任务真实界面",
    },
    {
      src: "/screenshots/partyops-fleet-grants-1.4.5-rc.3.png",
      category: "collaboration",
      title: "设备授权权限边界",
      caption: "无相应管理能力时明确拒绝访问，不隐藏权限原因",
      alt: "PartyOps 1.4.5-rc.3 设备授权权限边界真实界面",
    },
    {
      src: "/screenshots/partyops-templates-1.4.5-rc.3.png",
      category: "management",
      title: "周期与模板",
      caption: "复用周期事项、会议流程和固定材料要求",
      alt: "PartyOps 1.4.5-rc.3 周期与模板真实界面",
    },
    {
      src: "/screenshots/partyops-development-settings-1.4.5-rc.3.png",
      category: "management",
      title: "党员发展补充材料",
      caption: "集中维护单位规则、参考间隔与所需补充材料",
      alt: "PartyOps 1.4.5-rc.3 党员发展补充材料真实界面",
    },
    {
      src: "/screenshots/partyops-automation-1.4.5-rc.3.png",
      category: "management",
      title: "自动归档规则",
      caption: "按状态与业务类型执行可审计的归档规则",
      alt: "PartyOps 1.4.5-rc.3 自动归档规则真实界面",
    },
    {
      src: "/screenshots/partyops-report-designer-1.4.5-rc.3.png",
      category: "management",
      title: "报告模板",
      caption: "为周期汇总和年度统计维护统一输出结构",
      alt: "PartyOps 1.4.5-rc.3 报告模板真实界面",
    },
    {
      src: "/screenshots/partyops-ai-approvals-1.4.5-rc.3.png",
      category: "management",
      title: "AI 草稿审批",
      caption: "AI 只生成待审核草稿，最终采用由人员明确确认",
      alt: "PartyOps 1.4.5-rc.3 AI 草稿审批真实界面",
    },
    {
      src: "/screenshots/partyops-update-diagnostics-1.4.5-rc.4.png",
      category: "management",
      title: "系统更新",
      caption: "签名、校验、备份、升级与失败回滚在系统内完成",
      alt: "PartyOps 1.4.5-rc.4 系统更新真实界面",
    },
    {
      src: "/screenshots/partyops-backups-1.4.5-rc.3.png",
      category: "management",
      title: "备份恢复",
      caption: "备份数据库、配置、证书与协同状态并受控恢复",
      alt: "PartyOps 1.4.5-rc.3 备份恢复真实界面",
    },
    {
      src: "/screenshots/partyops-diagnostics-1.4.5-rc.3.png",
      category: "management",
      title: "运行诊断",
      caption: "集中核验服务、端口、证书、数据库与本机助手状态",
      alt: "PartyOps 1.4.5-rc.3 运行诊断真实界面",
    },
    {
      src: "/screenshots/partyops-help-1.4.5-rc.3.png",
      category: "management",
      title: "帮助与上手",
      caption: "首次配置、协同、文件、公文、更新与模型说明统一检索",
      alt: "PartyOps 1.4.5-rc.3 帮助与上手真实界面",
    },
    {
      src: "/screenshots/partyops-local-models-1.4.5-rc.3.png",
      category: "management",
      title: "本地模型与 Needle 2",
      caption: "签名模型包按平台导入，意图助手只生成预览，低置信和敏感操作自动回退",
      alt: "PartyOps 1.4.5-rc.3 本地模型与 Needle 2 真实界面",
    },
  ].map((screenshot) => ({
    ...screenshot,
    thumbnailSrc: screenshot.src.replace(/\.png$/, "-thumb.webp"),
  })),
  downloads: [
    {
      platform: "Windows",
      title: "Windows 10/11 x64",
      supportLevel: "preview",
      description: "适合绝大多数 Intel、AMD、海光台式机和笔记本。骁龙/ARM 电脑不要下载；单个 EXE 已包含完整载荷，无需再下载校验包。",
      action: "下载 Windows 安装器（推荐）",
      href: "windows",
      integrity: "windows",
      secondary: [
        { label: "Windows 验证清单", href: "windowsStartupManifest" },
      ],
    },
    {
      platform: "Windows",
      title: "Windows 7 SP1 x64",
      supportLevel: "preview",
      description: "只适合“控制面板 → 系统”显示 64 位的 Win7 SP1；32 位电脑请选下一项，仅建议受控局域网使用。",
      action: "下载 Win7 x64 安装器",
      href: "windows7Amd64",
      integrity: "windows7Amd64",
      secondary: [{ label: "发布验证清单", href: "windowsManifest" }],
    },
    {
      platform: "Windows",
      title: "Windows 7 SP1 / Windows 10 · x86",
      supportLevel: "preview",
      description: "Win7 SP1 与 Win10 32 位共用此 b3 专用包；按实际普通业务、OCR 与排版范围验收。Win10 本地模型 409 未通过，Windows ARM 未测，仅建议受控局域网使用。",
      action: "下载 Win7 / Win10 x86 安装器",
      href: "windows7X86",
      integrity: "windows7X86",
      secondary: [{ label: "发布验证清单", href: "windowsManifest" }],
    },
    {
      platform: "Linux DEB",
      title: "麒麟 / UOS / deepin · AMD64",
      supportLevel: "preview",
      description: "海光、兆芯、Intel、AMD 电脑选这个；终端 uname -m 显示 x86_64。不要把名称中的 AMD64 误解为只能用于 AMD 品牌。",
      action: "下载 AMD64 DEB",
      href: "linuxDebAmd64",
      integrity: "linuxDebAmd64",
      secondary: [
        { label: "GitHub 备用线路", href: "linuxDebAmd64Official" },
        { label: "发布验证清单", href: "windowsManifest" },
      ],
    },
    {
      platform: "Linux DEB",
      title: "麒麟 / UOS / deepin · ARM64",
      supportLevel: "preview",
      description: "ARM64 完整候选包，适用于 uname -m 显示 aarch64 的电脑。已在银河麒麟 V10 SP1 2503 虚拟机验证；其他版本、UOS/deepin 与实机仍需复验。",
      installNotice: "先备份，再覆盖安装，不要先卸载或删库。安装后核对原账号、事项、日历和附件，并再次启动确认。",
      action: "下载 ARM64 DEB",
      href: "linuxDebArm64",
      integrity: "linuxDebArm64",
      secondary: [
        { label: "GitHub 备用线路", href: "linuxDebArm64Official" },
        { label: "发布验证清单", href: "kylinArm64Manifest" },
      ],
    },
    {
      platform: "Linux DEB",
      title: "龙芯 LoongArch64 · DEB",
      releaseTag: "v1.4.5-rc.6",
      supportLevel: "preview",
      description: "实际验收范围为 Deepin 25.2.0 LoongArch64。其他龙芯发行版未验收，AMD64、ARM64 包不能替代；请先核对系统版本。",
      action: "下载龙芯 DEB",
      href: "linuxDebLoong64",
      integrity: "linuxDebLoong64",
      secondary: [{ label: "GitHub 备用线路", href: "linuxDebLoong64Official" }, { label: "发布验证清单", href: "windowsManifest" }],
    },
    {
      platform: "Linux RPM",
      title: "openEuler · x86_64",
      supportLevel: "preview",
      description: "openEuler 的海光、兆芯、Intel、AMD 电脑选这个；终端 uname -m 显示 x86_64。",
      action: "下载 x86_64 RPM",
      href: "linuxRpmAmd64",
      integrity: "linuxRpmAmd64",
      secondary: [
        { label: "GitHub 备用线路", href: "linuxRpmAmd64Official" },
        { label: "发布验证清单", href: "windowsManifest" },
      ],
    },
    {
      platform: "Linux RPM",
      title: "openEuler · aarch64",
      supportLevel: "preview",
      description: "openEuler 的飞腾、鲲鹏电脑选这个；终端 uname -m 显示 aarch64，安装阶段还会严格复核。",
      action: "下载 aarch64 RPM",
      href: "linuxRpmArm64",
      integrity: "linuxRpmArm64",
      secondary: [
        { label: "GitHub 备用线路", href: "linuxRpmArm64Official" },
        { label: "发布验证清单", href: "windowsManifest" },
      ],
    },
    {
      platform: "macOS",
      title: "macOS 15+ · Apple 芯片",
      releaseTag: "v1.4.5-rc.6",
      supportLevel: "preview",
      description: "验证：macOS 15.7.9 · ARM 原生托管主机；安装与自检通过，WPS 排版未实测。仅支持 macOS 15 及以上，未使用 Developer ID、未公证；AI 业务使用流程尚未验收。",
      action: "下载 Apple 芯片 PKG",
      href: "macosArm64",
      integrity: "macosArm64",
      secondary: [
        { label: "GitHub 备用线路", href: "macosArm64Official" },
        { label: "发布验证清单", href: "macosArm64Manifest" },
      ],
    },
    {
      platform: "macOS",
      title: "macOS 11+ · Intel 芯片",
      releaseTag: "v1.4.5-rc.6",
      supportLevel: "preview",
      description: "Intel x86_64 RC6 有限验收包；排版分页前端与 WPS 统计有差异，需人工核对正文、字体和页码。未使用 Developer ID、未公证；AI 需另行配置模型。",
      action: "下载 Intel 芯片 PKG",
      href: "macosX86_64",
      integrity: "macosX86_64",
      secondary: [
        { label: "GitHub 备用线路", href: "macosX86_64Official" },
        { label: "发布验证清单", href: "macosIntelManifest" },
      ],
    },
  ],
  installPreflight: [
    "Windows 10/11 的 64 位系统选 x64；Windows 10 的 32 位系统选共用 b3 x86 包，ARM 本批未测。Windows 7 必须确认 SP1、KB2533623、Universal CRT 和 32/64 位；国产 Linux 请确认发行版、DEB/RPM 与架构；macOS 请确认 Apple 芯片或 Intel：Apple Silicon 需 macOS 15 及以上，Intel 仍支持 macOS 11 及以上，两者均为有限验收的 RC6 候选。",
    "主机数据目录必须在本机固定磁盘，至少保留 2 GB 可用空间；附件或模型较多时应预留更多空间。",
    "升级前先完成一次备份并退出正在运行的 PartyOps；安装和升级默认保留原数据目录。",
    "候选版尚未签名。只有下载自本页且 SHA-256 完全一致时，才继续处理系统安全提示。",
  ],
  aiInstallAssistant: {
    tools: ["DeepSeek", "豆包", "Kimi", "WorkBuddy", "其他支持文字/截图问答的 AI"],
    platformLabels: {
      windows: "Windows 10/11 与 Windows 7",
      uos: "麒麟 / UOS / deepin / openEuler",
      macos: "macOS",
    },
    promptByPlatform: {
      windows: buildAiInstallPrompt(
        "Windows",
        "打开“设置/控制面板 → 系统”确认 Windows 版本和 32/64 位；使用 PowerShell 的 Get-FileHash 或命令提示符的 certutil 计算安装包 SHA-256",
      ),
      uos: buildAiInstallPrompt(
        "国产 Linux",
        "运行 cat /etc/os-release 与 uname -m，确认 DEB/RPM 和 x86_64/aarch64；使用 sha256sum 计算安装包 SHA-256",
      ),
      macos: buildAiInstallPrompt(
        "macOS",
        "运行 sw_vers 与 uname -m，确认系统版本和 Apple 芯片/Intel；使用 shasum -a 256 计算 PKG 的 SHA-256",
      ),
    },
  },
  installGuides: {
    windows: [
      {
        title: "下载并核对一个 EXE",
        body: "点击 Windows 主按钮只下载一个 EXE，不需要再找校验包。下载完成后先看文件名和大小；需要更严格核验时，用下面任一命令计算 SHA-256，并与下载卡片上的 64 位值逐字对比。",
        tips: [
          "Windows 10/11 x64 文件名应包含 1.4.5-rc.6 和 windows_amd64；Win7 64位为windows7_amd64，Win7/Win10 32位共用windows7_x86。",
          "哈希不一致、文件大小明显不符或浏览器提示下载未完成时，请删除后重新下载。",
        ],
        /*
         * Get-FileHash 仅存在于 PowerShell；
         * certutil 是 CMD 内置命令，两者都能校验，按用户实际终端选择。
         */
        commands: [
          {
            name: "PowerShell",
            command:
              "Get-FileHash .\\PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe -Algorithm SHA256",
          },
          {
            name: "命令提示符",
            command:
              "certutil -hashfile PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe SHA256",
          },
        ],
      },
      {
        title: "Win7 先检查补丁和系统位数",
        body: "只在受控局域网使用。打开“控制面板 → 系统”，确认 Windows 7 SP1 和系统类型；64 位下载 amd64，32 位下载 x86。安装器会在释放文件前检查 KB2533623 与 Universal CRT，缺少时用中文页面停止并给出修复步骤。",
        tips: [
          "Win7 已停止系统级安全维护，PartyOps 的制品门禁不能恢复操作系统或旧浏览器的安全性。",
          "x86 版本不含语义重排和本地 LLM，这是明确能力边界，不是安装失败后的静默降级。",
        ],
      },
      {
        title: "中文向导中分别选择两个目录",
        body: "双击安装器并在 UAC 中确认。若 SmartScreen 提示未知发布者，先确认哈希一致，再点“更多信息 → 仍要运行”。PartyOps 中文向导会先询问程序安装目录，再询问业务数据目录；两者彼此独立，都不会锁死在 C 盘。",
        tips: [
          "程序目录可选 E:\\PartyOps\\PartyOps 等本机固定磁盘路径；数据目录可选 D:\\党建资料\\PartyOps 数据，不要把两者选成同一个文件夹。",
          "数据库、附件、备份、证书、模型、缓存和日志跟随数据目录；中文、空格和非 C 盘路径都支持，升级会分别沿用原程序目录和数据目录。",
        ],
      },
      {
        title: "选择角色并等到就绪",
        body: "只给自己使用时选择“个人使用（新手推荐）”，日常启动无需管理员授权且不会开放局域网；多台电脑协同时，第一台稳定电脑选“配置为主机”，其余电脑选“加入已有主机”。只有健康检查通过后才创建首位管理员并打开页面。",
        tips: [
          "个人模式默认使用 18775，只监听 127.0.0.1；以后需要协同时可在向导中明确切换为主机并完成一次 UAC 授权。",
          "正常冷启动目标为 90 秒内；诊断最多等待 180 秒，明确错误会立即停止并显示诊断码。",
          "协同机请从主机端复制完整地址和一次性入网码，不要凭记忆手输 IP、端口或 https。",
        ],
      },
    ],
    uos: [
      {
        title: "确认包格式与 CPU 架构",
        body: "麒麟、UOS、deepin 使用 DEB，openEuler 使用 RPM；每台电脑只下载一个与 CPU 架构匹配的文件。飞腾 D2000/FT-2000、麒麟 9000C/9006C/990、鲲鹏选择 ARM64，海光 C86、兆芯、Intel、AMD 选择 AMD64。",
        tips: [
          "终端运行 uname -m；x86_64 对应 AMD64，aarch64 对应 ARM64。",
          "银河麒麟 V10 SP1 2107/2203/2303/2403/2503 使用同一选包规则；系统设置里只写处理器品牌时，仍以 uname -m 的结果为准。",
        ],
        command: "uname -m && cat /etc/os-release",
      },
      {
        title: "用系统包管理器安装一个文件",
        body: "DEB 先用下面的命令把安装包复制到系统临时目录，再交给 apt；这样即使“下载”目录不允许 _apt 账号访问，也不会出现脱离沙盒警告。RPM 直接用 dnf 安装。安装脚本会立即检查架构、文件清单、桌面入口、前端资源、SQLite/FTS5、中文 OCR、本地智能、更新服务和健康端点，任一步失败都会显示稳定诊断码并保持主机服务停止。",
        tips: [
          "桌面软件中心可以双击安装；出现问题时优先用上面的终端命令，以便看到中文诊断码。",
          "不要使用 --force-architecture，也不要关闭 SELinux、防火墙或系统安全策略。",
        ],
        commands: [
          { name: "DEB · AMD64", command: "sudo install -m 0644 ./PartyOps_1.4.5-rc.6_linux_amd64.deb /var/tmp/partyops.deb && sudo apt install /var/tmp/partyops.deb" },
          { name: "DEB · ARM64", command: "sudo install -m 0644 ./PartyOps_1.4.5-rc.6_linux_arm64_kylin-full-20261016.deb /var/tmp/partyops.deb && sudo apt install /var/tmp/partyops.deb" },
          { name: "RPM · x86_64", command: "sudo dnf install ./PartyOps-1.4.5-0.rc.3.1.x86_64.rpm" },
          { name: "RPM · aarch64", command: "sudo dnf install ./PartyOps-1.4.5-0.rc.3.1.aarch64.rpm" },
        ],
      },
      {
        title: "从应用菜单打开并等待页面",
        body: "安装完成后，用日常账号从应用菜单双击“党建智办”。首次配置可能需要十几秒，启动器会等页面真正就绪后再打开浏览器；不要在 root 桌面运行。个人、主机和协同三种模式都使用同一个入口。",
        tips: [
          "浏览器关联失败时会出现中文提示，可按提示复制本机地址到浏览器。",
          "仍未打开时查看 ~/.config/partyops/desktop-launch.log；不要反复双击叠加后台进程。",
        ],
        command: "tail -n 120 ~/.config/partyops/desktop-launch.log",
      },
      {
        title: "看见验证通过再首次组网",
        body: "脚本会在安装后继续核对版本、架构、核心程序、应用菜单、桌面入口和系统内更新服务；终端明确显示“安装完成”才算成功。随后从应用菜单启动党建智办，选择主机或协同机角色并完成配对。",
        tips: [
          "失败时不要删除原业务数据，也不要使用 --force-architecture；先查看日志最后 120 行。",
          "1.4.5-rc.6 的 DEB/RPM 按卡片中实际 UOS/openEuler 虚拟机范围验收，未声明所有商业发行版或完整生命周期通过。",
        ],
      },
    ],
    macos: [
      {
        title: "先确认芯片并核对一个 PKG",
        body: "点击左上角苹果菜单 → 关于本机：显示 Apple M 系列就下载 ARM64，明确显示 Intel 就下载 x86_64。下载后运行 shasum -a 256，并与卡片中的 SHA-256 逐字核对。",
        tips: [
          "Apple M 系列使用 PartyOps_1.4.5-rc.6_macos_arm64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg，需 macOS 15 及以上；Intel 使用 RC6 x86_64 包，仍支持 macOS 11 及以上。",
          "两类 Mac RC6 候选均未使用 Developer ID、未公证；Apple 芯片包的 WPS 排版尚未实测。仅在文件来源与 SHA-256 完全匹配后继续安装。",
        ],
        command: "uname -m && sw_vers && shasum -a 256 PartyOps_1.4.5-rc.6_macos_x86_64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg",
      },
      {
        title: "通过系统安全界面完成首次安装",
        body: "双击 PKG 安装。若 macOS 阻止未公证软件，先关闭提示，再打开“系统设置 → 隐私与安全”，只对已核对哈希的 PartyOps 选择“仍要打开”；不要关闭 Gatekeeper，也不要执行全局放行命令。",
        tips: [
          "安装完成后从“应用程序”打开党建智办，等待配置页面就绪；首次加载本地运行时可能比普通应用稍慢。",
          "选择个人、主机或协同模式后，后续可在设置中重新启动配置向导。",
        ],
      },
      {
        title: "打不开时先取三份中文诊断日志",
        body: "原生入口会在冻结桌面程序启动前写日志。若没有打开浏览器，请依次查看 launch-probe.log、launcher.log 和 launch-stderr.log；日志会记录入口阶段、桌面子进程 PID、架构、退出码和标准错误。",
        tips: [
          "历史 RC4 已针对旧日志中桌面子进程 exit_code=255 的继承环境问题增加原生双架构回归门禁；本次 Intel RC6 安装包仅有有限范围验收。",
          "反馈前请隐藏用户名、内网地址和业务内容，但保留版本、芯片、macOS 版本和日志末尾。",
        ],
        command: "tail -n 120 ~/Library/Logs/PartyOps/launch-probe.log ~/Library/Logs/PartyOps/launcher.log ~/Library/Logs/PartyOps/launch-stderr.log",
      },
    ],
  },
  /* 新手常见问题按任务分组，先给可执行动作，再解释原因。 */
  installFaqGroups: [
    {
      title: "下载与安装",
      items: [
        {
          question: "我不懂 AMD64、ARM64、x86_64 和 aarch64，应该选哪个？",
          answer:
            "最简单的方法是使用下载区最上方的“新手下载助手”。飞腾 D2000/FT-2000、麒麟 9000C/9006C/990、鲲鹏通常选 ARM64；海光、兆芯、Intel、AMD 通常选 AMD64。最准确的方法是在国产系统终端输入 uname -m：x86_64 对应 AMD64，aarch64 对应 ARM64。银河麒麟 V10 SP1 2107—2503 不需要按小版本另下一个包。AMD64 不是“只给 AMD 电脑”，ARM64 也不能用于龙芯 LoongArch。仍不确定时不要猜，也不要强制架构安装。",
        },
        {
          question: "现在到底要下载几个文件？",
          answer:
            "每台电脑只下载一个文件：Windows 10/11 x64 选对应 EXE，Win7 按 32/64 位选一个 Legacy EXE；麒麟、UOS、deepin 按架构选一个 DEB；openEuler 按架构选一个 RPM；Mac 在“关于本机”显示 Apple M 系列时选 ARM64 PKG，显示 Intel 时选 x86_64 PKG。SHA-256 直接显示在卡片中，外部 .sha256 只供自动化使用。",
        },
        {
          question: "Windows 提示“未知发布者”或被 SmartScreen 拦住怎么办？",
          answer:
            "1.4.5-rc.4 是尚未签名的候选版。先确认文件来自本页主下载线路或 GitHub 官方 Release，并核对 SHA-256 完全一致；只有两项都满足时，才在 SmartScreen 中点“更多信息 → 仍要运行”。哈希不一致时不要运行，也不要把整个数据盘加入杀毒白名单。",
        },
        {
          question: "Mac 提示“Apple 无法验证 PartyOps PKG 是否包含恶意软件”怎么办？",
          answer:
            "Apple Silicon 与 Intel 均使用 RC6 候选 PKG，未使用 Developer ID、未公证，Gatekeeper 可能提示无法验证；Apple Silicon 需 macOS 15 及以上，Intel 仍支持 macOS 11 及以上。先确认文件名与芯片匹配并核对卡片 SHA-256，再按系统安全界面允许打开，不要全局关闭 Gatekeeper 或执行不明放行命令。AI 功能需另行配置模型。若未打开页面或日志显示 exit_code=255，请读取 ~/Library/Logs/PartyOps 下的 launch-probe.log、launcher.log 和 launch-stderr.log。",
        },
        {
          question: "数据可以放到 D 盘、中文目录或带空格的目录吗？",
          answer:
            "可以，例如 D:\\党建资料\\PartyOps 数据。必须选择本机固定磁盘且至少剩余 2 GB；Windows、Program Files、网络共享、映射盘、U 盘和无写权限目录会被拒绝。全部业务数据跟随所选数据目录。",
        },
        {
          question: "程序安装目录也能改吗，还是只能装到 C 盘？",
          answer:
            "程序目录也能改。安装向导的“程序安装目录”页可以选择 E:\\PartyOps\\PartyOps 等本机路径；下一页再单独选择业务数据目录。升级会分别记住两者，不会把自定义程序路径改回 Program Files，也不要把程序和数据放进同一个文件夹。",
        },
        {
          question: "从 rc.1 或旧版本升级会丢数据吗？",
          answer:
            "安装器默认读取并保留原程序目录和数据目录，覆盖文件前会等待主机服务和更新服务完全停止。主动迁移时会复制并核验数据库与文件，再原子切换；失败会回滚到旧配置且不删除旧数据。升级前仍建议先备份并记下两个目录。",
        },
        {
          question: "升级时提示旧文件无法替换或显示英文错误框怎么办？",
          answer:
            "新版中文安装器会在释放文件前自动停止两个 PartyOps 服务；停服失败会显示 UPGRADE_SERVICE_STOP_FAILED，不应再出现英文 DeleteFile failed。先关闭仍打开的 PartyOps 窗口后重试，不要点“跳过此文件”。若旧安装已经损坏，可先卸载旧程序但保留业务数据目录，再重新安装。",
        },
      ],
    },
    {
      title: "首次配置与主机服务",
      items: [
        {
          question: "个人模式、主机模式和协同机模式怎么选？",
          answer:
            "只在自己电脑使用就选“个人使用”，无需管理员授权，也不开放局域网；保存团队数据库、附件和证书的第一台稳定电脑选“配置为主机”，这一步需要一次 UAC；其他办公电脑选“加入已有主机”。同一单位通常只需要一台主机。",
        },
        {
          question: "为什么以前提示主机配置需要管理员授权？",
          answer:
            "主机模式需要注册系统服务、保护团队数据目录并配置最小防火墙规则，因此必须由 Windows 明确授权；这不是日常启动要求。新版把“个人使用”单独列为默认推荐项，只在本机运行，全程不申请主机权限。系统不会再把个人使用误当成权限不足的主机降级。",
        },
        {
          question: "出现“[WinError 786] 管理员用策略规则限制了访问”怎么办？",
          answer:
            "这表示单位电脑的 SRP、AppLocker 或 WDAC 组织策略阻止了 PartyOps 管理员助手，不是文件损坏，反复重装也不会解除。只在本机使用时请返回选择“个人使用”，无需主机权限；需要主机或系统内升级时，请复制诊断并让单位电脑管理员允许安装目录中的 PartyOpsWizard.exe、PartyOpsService.exe 和 PartyOpsUpdater.exe，再在原数据目录重试。PartyOps 不会关闭或绕过单位安全策略。诊断码为 ADMIN_POLICY_BLOCKED。",
        },
        {
          question: "提示“PartyOps 主机服务未能启动”怎么办？",
          answer:
            "先留在配置向导，不要改成普通用户进程，也不要反复打开网址。点“复制诊断”或“打开日志”，按诊断码处理：SERVICE_MISSING 需要修复安装，SERVICE_STOPPED 可重试启动，CHILD_EXITED 查看退出阶段，PORT_IN_USE 处理 18765 端口占用，DATA_DIR_DENIED 更换或授权数据目录。",
        },
        {
          question: "个人模式提示“[RUNTIME_PERMISSION_DENIED] 启动探针发现明确故障”怎么办？",
          answer:
            "1.4.5-rc.4 会在启动子进程前逐项检查当前桌面账号能否读取并执行 PartyOps、读取个人模式配置、在所选数据目录完成原子写入和追加诊断日志。先复制摘要确认故障阶段：程序或配置不可读时，用同一安装包执行“修复安装”；个人数据目录不可写时，回到配置向导选择当前账号可写的本机固定磁盘目录。原数据不会被自动删除。不要关闭单位安全策略、不要给 Everyone 完全控制，也不要从未知网站下载 DLL 或权限修复工具。Windows 安装器会在完成前切回原桌面账号执行相同权限探针，失败则回滚程序。",
        },
        {
          question: "提示“90 秒内未能就绪（https://局域网IP:18765）”怎么办？",
          answer:
            "1.4.5-rc.4 会先在本机 127.0.0.1 做健康检查，局域网 IP 只用于协同访问。正常冷启动目标是 90 秒内完成，慢迁移最多诊断 180 秒；端口、权限、TLS 或子进程错误会立即给出原因。未显示“健康检查通过”前不要手动打开该网址。",
        },
        {
          question: "创建管理员时报 WinError 10061 怎么办？",
          answer:
            "10061 表示本机进程当时还没有接受连接。若选择“个人使用”，它不会安装 Windows 主机服务：请先关闭旧配置向导，从桌面重新打开 PartyOps，等待个人模式页面就绪后再继续；也可访问 http://127.0.0.1:18775 核对。不要去启动 PartyOpsHost，也不要删除数据。若选择“主机模式”，返回向导点“重试”，确认“端口监听”和“健康检查”都通过后再创建管理员。仍失败时请打开所选数据目录中的日志并反馈 CHILD_EXITED、TLS_INIT_FAILED 或 HEALTH_TIMEOUT 诊断。",
        },
        {
          question: "提示“[PORT_IN_USE] 个人模式端口 18775 已被身份不明的程序占用”怎么办？",
          answer:
            "这是安全保护，不是安装包损坏。1.4.5-rc.4 先用可执行路径、命令行标记和运行身份三项证据核对占用者：能证明是当前 PartyOps 的旧进程才会安全接管；无法证明时绝不结束未知程序，而是自动选择空闲的本机端口并同步保存配置。关闭旧页面后从桌面重新打开即可，业务数据和账号不会移动。若单位策略固定要求 18775，请复制启动诊断交给管理员查明占用者，不要手工结束 System、PID 4 或路径不明的进程，也不要伪造进程标记。",
        },
        {
          question: "安装提示“[LEGACY SERVICE CONFLICT] 同名 PartyOps 主机服务无法证明归属”怎么办？",
          answer:
            "这是 rc.1、rc.2 或已撤回 rc.3 卸载后仍留在 Windows SCM 中的服务记录，不是业务数据库损坏；仅重启电脑不会删除 SCM 记录。1.4.5-rc.4 会在旧 EXE 仍存在时核对官方冻结 SHA-256；若旧 EXE 已被卸载器删除，则只在服务已停止、目标文件不存在、服务名/显示名/说明、LocalSystem 身份、服务类型、启动类型和错误策略全部吻合时受控接管。仅名称相同、仍在运行、路径另有文件或元数据不完整的服务仍会返回 LEGACY_SERVICE_CONFLICT，防止误删其他软件。请直接用 rc.4 覆盖安装并保留数据；若 rc.4 仍报错，请复制安装日志，不要自行执行 sc delete。",
        },
        {
          question: "Windows 安装提示“[PROTOCOL_REGISTRY_DENIED] 无法写入协议注册表”怎么办？",
          answer:
            "1.4.5-rc.4 将 partyops-client 与 partyops-file 协议写入当前用户的 HKCU\\Software\\Classes，不再要求修改整机 HKLM，也不会覆盖已属于其他程序的同名协议。若单位策略仍阻止当前用户注册协议，安装会完整回滚，不留下半安装项；请把安装日志交给管理员放行当前用户协议注册，或先使用系统内的普通网页入口，不要关闭 UAC 或修改组织安全策略。",
        },
        {
          question: "Windows 7 提示未检测到 KB2533623，但系统已经更新怎么办？",
          answer:
            "1.4.5-rc.4 不再只查补丁编号，而会直接探测安全 DLL 搜索 API 是否真实可用；系统已通过汇总更新获得同等能力时不会误拦截。若仍未通过，请确认 Windows 7 SP1 与 Universal CRT 完整，重启后再运行对应 32/64 位安装包。能力探测失败时不要复制来历不明的 DLL，也不要跳过门禁；请让管理员安装微软官方适用更新或迁移到受支持系统。",
        },
        {
          question: "Windows 7 双击后提示“[CHILD_EXITED] 个人进程启动后提前退出”怎么办？",
          answer:
            "rc.8 的这个外层提示只能证明主进程提前结束，无法单独判断是系统补丁、原生依赖、数据目录还是安全策略；没有 C:\\ProgramData\\PartyOps-Data\\launcher.log 时不能据此伪造唯一根因。1.4.5-rc.4 的 32/64 位专用包已回移 Win7 原生依赖，并在安装完成前真实启动最终 PartyOps.exe，依次验证 RSA/Fernet、SQLite/FTS5、版本健康接口和前端首页；任一失败会显示 PACKAGE_RUNTIME_STARTUP_SELFTEST_FAILED 并回滚，不再留下“安装成功但首次双击才退出”的半成品。请先确认下载的是与系统位数一致的 windows7_amd64 或 windows7_x86 包并核对 SHA-256，再覆盖安装；不要删除业务数据。若 1.4.5-rc.4 仍提示 CHILD_EXITED，请复制 C:\\ProgramData\\PartyOps-Data\\launcher.log 的末尾内容、Windows 7 版本与系统位数反馈，项目会在后续版本按日志精准修复。",
        },
        {
          question: "提示“程序升级尚未完成：页面与服务版本不同”怎么办？",
          answer:
            "这通常是浏览器仍保留升级前的旧标签页，或旧 PartyOps 进程没有完全退出，业务数据没有丢失。1.4.5-rc.4 会给新页面加入运行版本指纹，并在个人模式升级时停止遗留主机服务。请先关闭所有 PartyOps 页面，再从桌面图标重新打开；仍不一致时按 Ctrl+F5，或用无痕窗口打开启动器给出的 127.0.0.1 地址。不要删除数据目录；无痕窗口仍不一致时复制页面提示、健康检查结果和启动日志反馈。",
        },
        {
          question: "打开后是空白页，应该刷新还是重装？",
          answer:
            "先关闭空白页并从 PartyOps 启动器重新进入，不要连续刷新或重复创建管理员。1.4.5-rc.4 的启动看门狗会核对静态资源、页面版本和健康端点；Win11 上出现短暂跳转后空白时会自动刷新一次，持续失败则显示稳定诊断码而不是永久白屏。若仍空白，请复制诊断并打开日志反馈，不要删除数据，也不要上传真实业务资料。",
        },
      ],
    },
    {
      title: "UOS、更新与功能使用",
      items: [
        {
          question: "UOS 安装时出现“_apt 无法访问”并以 dpkg error code 1 结束怎么办？",
          answer:
            "“_apt 无法访问下载目录”本身只是 apt 的沙盒提示，不是 dpkg 失败根因。请使用安装指南中的 sudo install 命令先把当前架构的 DEB 复制为 /var/tmp/partyops.deb，再运行 sudo apt install /var/tmp/partyops.deb。1.4.5-rc.4 会先把三个 desktop 文件规范成 UTF-8/LF，再用 desktop-file-validate 逐项检查；PACKAGE_DESKTOP_ENTRY_INVALID 会指出具体文件。修复首个原因后优先运行 sudo dpkg --configure partyops，仅在确认没有其他半配置软件包时才运行 sudo dpkg --configure -a。请保留 /var/log/partyops-package-selftest.log 与 /var/log/partyops-package-install.log，不要使用 --force、不要关闭系统安全中心，也不要删除 /var/lib/partyops。",
        },
        {
          question: "UOS 桌面有图标，但提示 LAUNCH_LOCK_TIMEOUT 或 180 秒未显示页面怎么办？",
          answer:
            "1.4.5-rc.4 的启动事务锁会记录进程、启动时间和安装版本；所属进程不存在、身份不一致或超过有效期时会自动回收，连续双击则复用同一个配置向导，不再永久占锁。请只保留一次启动，等待诊断窗口；若仍提示超时，复制 ~/.config/partyops/startup-diagnostic.txt 和 launcher.log 后重新启动桌面入口。不要删除数据目录或盲目清空整个配置目录。",
        },
        {
          question: "提示 RUNTIME_VERSION_MISMATCH，端口上不是当前版本怎么办？",
          answer:
            "1.4.5-rc.4 会核对健康端点报告的版本、进程路径和运行身份，不会把旧服务页面当作新版本打开。请关闭所有 PartyOps 页面，从桌面入口重新启动；受控旧进程会按所有权证据安全切换，无法证明归属的进程不会被结束，并会改用安全端口。仍失败时提交 startup-diagnostic.txt，不要手工删除未知服务或进程。",
        },
        {
          question: "国产 Linux 的安装和验证真的合在一起了吗？",
          answer:
            "是。每台电脑只下载一个 DEB 或 RPM；包管理器配置阶段立即核对架构、文件清单、前端资源、SQLite/FTS5、中文 OCR、本地语义、LLM、更新服务与回环健康端点。任一步失败都会让安装配置返回非零，主机服务保持停止，不会显示假成功。",
        },
        {
          question: "UOS 仍提示 cryptography 重复或 arm64 闭包不完整怎么办？",
          answer:
            "1.4.5-rc.4 发布包在生成前已按规范化包名检查依赖集合；amd64 和 arm64 各只允许一个 cryptography==50.0.0，语义、OCR 与 LLM 运行时不完整时不会生成制品。若安装时仍看到该历史提示，请核对文件名、大小和 SHA-256，删除旧解压目录或旧缓存后只保留本页当前架构的一个原生包。",
        },
        {
          question: "麒麟 ARM64 一直提示安全中心阻止 libgcc_s.so.1 启动怎么办？",
          answer:
            "这是旧包把共享库错误标记为可执行文件后触发的安全策略，不需要关闭安全中心，也不要把 libgcc_s.so.1 加入白名单。请先核对本页 ARM64 DEB 的最新上传时间和 SHA-256，再通过系统包管理器覆盖安装。1.4.5-rc.4 冻结包已把共享库和静态资源统一设为不可执行，仅保留受控程序入口；服务连续失败三次会停止重试并保留日志，不再无限弹窗。若安装最新包后仍出现，请执行 systemctl status partyops --no-pager 和 journalctl -u partyops -n 120 --no-pager 后反馈，但不要上传业务数据。",
        },
        {
          question: "协同电脑必须和主机在同一网段吗？",
          answer:
            "不强制同一网段，但两台电脑必须能通过办公局域网互相访问，且防火墙允许主机的 18765 端口。最省事的方式是放在同一网段；从主机“设备协同 → 新增协同电脑”原样复制完整地址，并使用 10 分钟内有效的一次性入网码。",
        },
        {
          question: "卸载 PartyOps 会删除业务数据吗？",
          answer:
            "卸载器会先让你选择：“仅删除程序”会保留数据，方便以后重装；“彻底卸载”会在二次说明后删除经 PartyOps 所有权标记的数据库、附件、备份、证书、模型、缓存、日志及本机配置。安全预检不通过时会停止，绝不凭一个可篡改路径递归删除。彻底卸载不可恢复，请先验证备份。",
        },
        {
          question: "以后升级还要重新到官网下载安装包吗？",
          answer:
            "1.4.5-rc.4 使用本机受控的正式 Ed25519 生产密钥并轮换了客户端内置信任根，没有使用临时测试密钥。rc.1 及更早版本无法验证新信任根，必须先从本页下载与当前系统和架构匹配的完整安装包覆盖升级；安装器会保留原数据目录。升级到 rc.2 后，系统内更新继续校验签名、版本、平台、架构、长度和 SHA-256，并在安装前备份、安装后健康检查、失败时回滚。",
        },
        {
          question: "主机和不同系统的协同电脑会不会下错更新包？",
          answer:
            "不会。主机只通知协同电脑目标版本，不会把自己的 Windows 或 Linux 下载地址塞给其他电脑。每台协同电脑都会使用安装目录中的受保护公钥重新验证官网更新目录，再按自己的系统、安装包格式和 CPU 架构独立选包；目录签名、下载长度、SHA-256 和包内签名任一不一致都会停止，旧版本和业务数据保持不变。",
        },
        {
          question: "一事一档误操作后可以回退吗？",
          answer:
            "可以。1.4.5-rc.4 在一事一档中保留带权限和并发检查的回退，并增加多文件上传、逐文件重试与 30 天可恢复删除。操作会写入新的审计记录，不会篡改历史轨迹；并发期间内容已被其他电脑更新时会拒绝覆盖，刷新后重新核对即可。",
        },
        {
          question: "文档管理中心为什么看不到“新建文件夹”按钮？",
          answer:
            "1.4.5-rc.4 按当前目录权限显示“新建文件夹”。请先选中有写权限的目录；只读目录不会伪装操作成功。创建后目录会立即刷新，名称冲突或权限变化会显示明确中文原因，不再静默无响应。",
        },
        {
          question: "发展党员时间节点计算完成后如何导出 Word？",
          answer:
            "在发展党员模块打开参考计划或实际时间轴后点击“导出 Word”。1.4.5-rc.4 会把实际日期、法定边界和参考日期分栏写入标准 DOCX，并附带“系统推算不替代组织研究审批”说明；若没有节点结果或权限不足，系统会明确拒绝，不会生成空白文档。",
        },
      ],
    },
  ],
};

export const externalLinkProps = {
  target: "_blank",
  rel: "noopener noreferrer",
};
