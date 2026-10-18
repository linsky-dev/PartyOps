import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { externalLinkProps, siteContent } from "./siteContent.js";

describe("官网集中内容配置", () => {
  it("RC6限域预发布区分Apple Silicon15与Intel11", () => {
    const serialized = JSON.stringify(siteContent);
    const indexHtml = readFileSync(path.resolve("index.html"), "utf8");

    expect(siteContent.product.version).toBe("1.4.5-rc.6");
    expect(siteContent.product.releaseTag).toBe("v1.4.5-rc.6");
    expect(serialized).not.toContain("1.4.2");
    expect(siteContent.links.release).toContain("/v1.4.5-rc.6");
    expect(siteContent.links.windows).toContain("PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe");
    expect(siteContent.links.windows7Amd64).toContain("windows7_amd64.exe");
    expect(siteContent.links.windows7X86).toContain("windows7_x86.exe");
    expect(siteContent.links.linuxDebArm64).toMatch(/linux_arm64.*\.deb$/);
    expect(siteContent.links.linuxRpmAmd64).toContain("x86_64.rpm");
    expect(siteContent.links.macosX86_64).toContain("PartyOps_1.4.5-rc.6_macos_x86_64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg");
    expect(siteContent.links.macosArm64).toContain("PartyOps_1.4.5-rc.6_macos_arm64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg");
    expect(siteContent.product.incidentNotice).toContain("Apple Silicon 支持 macOS 15 及以上");
    expect(siteContent.product.incidentNotice).toContain("WPS 排版未实测");
    expect(siteContent.product.manifestPath).toBe("/releases/v1.4.5-rc.6/release-manifest-windows-startup-20261017.json");
    expect(siteContent.beginnerChooser.matches["macos:arm64"].sentence).toContain("macOS 15 及以上");
    expect(siteContent.beginnerChooser.matches["macos:x86_64"].sentence).toContain("macOS 11 及以上");
    expect(siteContent.installPreflight[0]).not.toContain("Apple Silicon 保留 RC4");
    expect(JSON.stringify(siteContent.aiInstallAssistant)).not.toContain("Apple Silicon 仍使用 1.4.5-rc.4 历史包");
    expect(indexHtml).toContain("<title>PartyOps 1.4.5-rc.6｜本地优先的党建协同工作台</title>");
    expect(indexHtml).not.toContain("PartyOps 1.4.4｜");
  });

  it("保留固定锚点、37 张 rc.3 完整功能实景与六项亮点", () => {
    expect(siteContent.navigation.map(({ href }) => href)).toEqual([
      "#highlights",
      "#screenshots",
      "#download",
      "#install",
      "/models",
      "/changelog",
      "#community",
    ]);
    expect(siteContent.highlights).toHaveLength(6);
    expect(siteContent.screenshots).toHaveLength(37);
    expect(siteContent.screenshots[0].title).toBe("今日工作台");
    expect(siteContent.galleryCategories.map(({ id }) => id)).toEqual([
      "all",
      "today",
      "work",
      "party",
      "materials",
      "collaboration",
      "management",
    ]);
    expect(
      Object.fromEntries(
        siteContent.galleryCategories.slice(1).map(({ id }) => [
          id,
          siteContent.screenshots.filter(({ category }) => category === id).length,
        ]),
      ),
    ).toEqual({
      today: 2,
      work: 9,
      party: 5,
      materials: 7,
      collaboration: 4,
      management: 10,
    });
    expect(siteContent.screenshots.every(({ alt }) => alt.includes("真实界面"))).toBe(true);
    expect(siteContent.screenshots.every(({ src, alt }) => {
      const isRc4Refresh = src.includes("partyops-update-diagnostics-1.4.5-rc.4");
      return isRc4Refresh ? alt.includes("1.4.5-rc.4") : src.includes("1.4.5-rc.3") && alt.includes("1.4.5-rc.3");
    })).toBe(true);
    expect(siteContent.screenshots.every(({ thumbnailSrc }) => thumbnailSrc.endsWith("-thumb.webp"))).toBe(true);
    expect(
      siteContent.screenshots.every(({ src, thumbnailSrc }) =>
        [src, thumbnailSrc].every((file) =>
          existsSync(path.resolve("public", file.replace(/^\//, ""))),
        ),
      ),
    ).toBe(true);
  });

  it("下载标题正下方固定展示涉密与敏感文件警告", () => {
    expect(siteContent.product.confidentialityWarning).toBe(
      "安全提醒：不建议在涉密、敏感电脑上安装 PartyOps，也不得使用 PartyOps 处理涉密文件。",
    );
    const source = readFileSync(
      path.resolve("src/components/download/DownloadSection.jsx"),
      "utf8",
    );
    expect(source.indexOf("confidentiality-warning")).toBeGreaterThan(
      source.indexOf("download-title"),
    );
    expect(source.indexOf("confidentiality-warning")).toBeLessThan(
      source.indexOf("candidate-warning"),
    );
    expect(source).not.toContain("<details className=\"confidentiality-warning\"");
  });

  it("显示安装包上传时间并提供独立更新日志页", () => {
    expect(siteContent.artifactIntegrity.windows.uploadedAt).toBeTruthy();
    expect(siteContent.artifactIntegrity.linuxDebArm64.uploadedAt).toBeTruthy();
    expect(siteContent.links.changelog).toBe("/changelog");
    expect(siteContent.links.models).toBe("/models");
    expect(siteContent.links.resume).toBe("/resume/");
    // 追加专项修复日志后，主版本记录仍按版本精确核对，不依赖数组首项。
    const mainRelease = siteContent.changelog.find((entry) => entry.version === "v1.4.5-rc.6");
    expect(mainRelease).toBeDefined();
    expect(mainRelease.releasedAt).toMatch(
      /^2026-10-03 \d{2}:\d{2}:\d{2}（北京时间，UTC\+8）$/,
    );
    expect(siteContent.artifactIntegrity.windows.uploadedAt).toBe("待公开回读确认");
    expect(mainRelease.releasedAt).not.toContain("__RC9_");
    expect(JSON.stringify(siteContent.changelog)).not.toContain("待线上发布核验");
    const changelog = JSON.stringify(siteContent.changelog);
    for (const internalDetail of [
      "CloudStudio",
      "GitHub",
      "Gitee",
      "cryptography",
      "wheelhouse",
      "离线闭包",
      "国内直链",
      "挂载",
      "ClamAV",
    ]) {
      expect(changelog).not.toContain(internalDetail);
    }
    expect(mainRelease.changes.length).toBeGreaterThanOrEqual(5);
  });

  it("挂载自包含的个人简历二级页面", () => {
    const resume = readFileSync(
      path.resolve(process.cwd(), "public/resume/index.html"),
      "utf8",
    );

    expect(resume).toContain("<title>潘理 · 个人简历</title>");
    expect(resume).toContain('lang="zh-CN"');
    expect(resume).not.toMatch(/<script[^>]+src=/i);
    expect(resume).not.toMatch(/<link[^>]+href=["']https?:/i);
  });

  it("删除官网微信社群入口并将导航改为开源与支持", () => {
    expect(siteContent.navigation.at(-1)).toEqual({ label: "开源与支持", href: "#community" });
    expect(siteContent.community).not.toHaveProperty("wechatGroup");
    expect(JSON.stringify(siteContent)).not.toContain("微信社群");
  });

  it("赞助区只保留爱发电单一订阅入口，且公开榜不展示金额", () => {
    const sponsors = siteContent.community.sponsors;
    const support = siteContent.community.support;

    expect(siteContent.community.supportNotice).toContain("永久免费");
    expect(siteContent.community.supportNotice).toContain("与所在单位无关");
    expect(siteContent.community.supportNotice).toContain("GPLv3/AGPLv3");
    expect(siteContent.community.supportNotice).toContain("禁止冒充官方、违法倒卖");

    expect(sponsors.list).toEqual([
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
    ]);
    expect(support.provider).toBe("爱发电");
    expect(support.plan).toContain("PartyOps");
    expect(support).not.toHaveProperty("paymentMethods");
    expect(JSON.stringify(support)).not.toMatch(/微信支付|支付宝|收款码/);
    expect(JSON.stringify(sponsors)).not.toMatch(/amount|金额|¥|￥/i);
  });

  it("保留十个候选安装包与已公开龙芯包", () => {
    const windows = siteContent.downloads.filter(({ platform }) => platform === "Windows");
    const linux = siteContent.downloads.filter(({ platform }) => platform.startsWith("Linux"));
    const macos = siteContent.downloads.filter(({ platform }) => platform === "macOS");

    expect(windows).toHaveLength(3);
    expect(windows.every(({ action }) => action.includes("安装器"))).toBe(true);
    expect(windows[0].description).toContain("无需再下载校验包");
    expect(windows[0].secondary.map(({ label }) => label)).toContain("Windows 验证清单");
    expect(linux).toHaveLength(5);
    expect(linux.every(({ action }) => action.startsWith("下载"))).toBe(true);
    const commands = siteContent.installGuides.uos[1].commands;
    expect(commands).toHaveLength(4);
    expect(commands.map(({ command }) => command).join("\n")).toContain("apt install");
    expect(commands.map(({ command }) => command).join("\n")).toContain("dnf install");
    expect(siteContent.installGuides.uos[1].body).toContain("安装脚本会立即检查");
    expect(JSON.stringify(siteContent.installGuides.windows)).toContain("KB2533623");
    expect(macos).toHaveLength(2);
    expect(macos.every(({ supportLevel }) => supportLevel === "preview")).toBe(true);
    expect(macos.every(({ action }) => action.startsWith("下载"))).toBe(true);
    expect(siteContent.installGuides.macos).toHaveLength(3);
    expect(JSON.stringify(siteContent.installGuides.macos)).toContain("exit_code=255");
    expect(siteContent.downloads).toHaveLength(10);
    const loong = siteContent.downloads.find(({ href }) => href === "linuxDebLoong64");
    expect(loong).toMatchObject({ releaseTag: "v1.4.5-rc.6", supportLevel: "preview" });
    expect(siteContent.downloadFiles.linuxDebLoong64).toContain("/downloads/PartyOps_1.4.5-rc.6_linux_loong64.deb");
    expect(siteContent.artifactIntegrity.linuxDebLoong64.sha256).toBe("bec5993a12a9a66800fa039045f45329ecc486934cd1d1106c10d5e2ecf78017");
    expect(siteContent.beginnerChooser.matches["windows:x86"].fileKey).toBe("windows7X86");
    expect(siteContent.artifactIntegrity.windows7X86.sha256).toBe("b3b77f2a5a319906c641ca693df31b93c3fbcb56e28eaae7a52bdfa94913bf88");
    expect(JSON.stringify(siteContent.downloadMirrors)).not.toMatch(/ghfast|ghproxy/i);
    expect(siteContent.links.windowsManifest).toMatch(/\/v1\.4\.5-rc\.6\/release-manifest\.json$/);
    expect(siteContent.links.windowsStartupManifest).toMatch(/release-manifest-windows-startup-20261017\.json$/);
    expect(siteContent.downloadFiles.windowsStartupManifest).toBe("/releases/v1.4.5-rc.6/release-manifest-windows-startup-20261017.json");
    expect(siteContent.downloads.find(({ href }) => href === "windows").secondary).toContainEqual({
      label: "Windows 验证清单", href: "windowsStartupManifest",
    });
    expect(siteContent.artifactIntegrity.windows).toMatchObject({
      filename: "PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe",
      sizeBytes: 449978846,
      sha256: "3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27",
      publicationState: "pending",
      uploadedAt: "待公开回读确认",
      mirrorVerifiedAt: "待公开回读确认",
    });
    expect(siteContent.downloads.find(({ href }) => href === "windows7Amd64").secondary)
      .toContainEqual({ label: "发布验证清单", href: "windowsManifest" });
  });

  it("十个RC6包使用各自真实国内线路并保留Intel原元数据", () => {
    const packageKeys = [
      "windows",
      "windows7Amd64",
      "windows7X86",
      "linuxDebAmd64",
      "linuxDebArm64",
      "linuxDebLoong64",
      "linuxRpmAmd64",
      "linuxRpmArm64",
      "macosArm64",
      "macosX86_64",
    ];

    packageKeys.forEach((key) => {
      expect(siteContent.downloadFiles[key]).toMatch(/^https:\/\/[^/]+\/downloads\//);
      expect(siteContent.links[key]).toMatch(/^https:\/\/github\.com\/linsky-dev\/PartyOps\/releases\/download\//);
      if (key === "windows") {
        expect(siteContent.artifactIntegrity[key].mirrorVerifiedAt).toBe("待公开回读确认");
      } else {
        expect(siteContent.artifactIntegrity[key].mirrorVerifiedAt).toContain("北京时间");
      }
    });
    expect(siteContent.validationEnvironments.packages.filter((row) => row.version === "1.4.5-rc.6")).toHaveLength(10);
    const windowsRecord = siteContent.validationEnvironments.packages.find(
      (row) => row.package_id === "windows_amd64",
    );
    expect(windowsRecord).toMatchObject({
      publication_state: "pending-public-readback",
      scope_summary: expect.stringContaining("WPS 12.1.0.28488 六项排版、五字体、三页金样及保存重开通过"),
      full_lifecycle_verified: false,
      download: {
        filename: "PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe",
        size_bytes: 449978846,
        sha256: "3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27",
        confirmed_at: null,
      },
    });
    expect(siteContent.downloads.find(({ href }) => href === "macosX86_64").releaseTag).toBe("v1.4.5-rc.6");
    expect(siteContent.downloads.find(({ href }) => href === "macosArm64").releaseTag).toBe("v1.4.5-rc.6");
    expect(siteContent.links.macosX86_64).toContain("PartyOps_1.4.5-rc.6_macos_x86_64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg");
    expect(siteContent.downloadFiles.macosX86_64).toBe("https://partyops-rc6-win7-x86.app.workbuddy.host/downloads/PartyOps_1.4.5-rc.6_macos_x86_64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg");
    const nativeDownloads = siteContent.downloads.filter(({ platform, supportLevel }) =>
      supportLevel !== "unavailable" && (platform.startsWith("Linux") || platform === "macOS"),
    );
    expect(
      nativeDownloads.every(({ secondary }) =>
        secondary.some(({ label, href }) => label === "GitHub 备用线路" && href.endsWith("Official")),
      ),
    ).toBe(true);
  });

  it("向新手说明安装前检查、诊断码和 UOS 安装验证合一", () => {
    const faqs = siteContent.installFaqGroups.flatMap(({ items }) => items);
    const serialized = JSON.stringify(faqs);

    expect(siteContent.installPreflight).toHaveLength(4);
    expect(siteContent.installFaqGroups).toHaveLength(3);
    expect(faqs.length).toBeGreaterThanOrEqual(12);
    expect(serialized).toContain("SERVICE_MISSING");
    expect(serialized).toContain("WinError 10061");
    expect(serialized).toContain("127.0.0.1:18775");
    expect(serialized).toContain("[PORT_IN_USE] 个人模式端口 18775");
    expect(serialized).toContain("可执行路径、命令行标记和运行身份三项证据");
    expect(serialized).not.toContain("Stop-Process");
    expect(serialized).toContain("自动选择空闲的本机端口");
    expect(serialized).toContain("不要伪造进程标记");
    expect(serialized).toContain("LEGACY SERVICE CONFLICT");
    expect(serialized).toContain("PROTOCOL_REGISTRY_DENIED");
    expect(serialized).toContain("[CHILD_EXITED] 个人进程启动后提前退出");
    expect(serialized).toContain("PACKAGE_RUNTIME_STARTUP_SELFTEST_FAILED");
    expect(serialized).toContain("C:\\\\ProgramData\\\\PartyOps-Data\\\\launcher.log");
    expect(serialized).toContain("Apple 无法验证 PartyOps PKG 是否包含恶意软件");
    expect(serialized).toContain("exit_code=255");
    expect(serialized).toContain("launch-stderr.log");
    expect(serialized).toContain("LAUNCH_LOCK_TIMEOUT");
    expect(serialized).toContain("RUNTIME_VERSION_MISMATCH");
    expect(serialized).toContain("一事一档误操作后可以回退吗");
    expect(serialized).toContain("新建文件夹");
    expect(serialized).toContain("导出 Word");
    expect(serialized).toContain("程序升级尚未完成：页面与服务版本不同");
    expect(serialized).toContain("Ctrl+F5");
    expect(serialized).toContain("cryptography==50.0.0");
    expect(serialized).toContain("cryptography==50.0.0");
    expect(serialized).toContain("程序安装目录也能改吗");
    expect(serialized).toContain("1.4.5-rc.4 使用本机受控的正式 Ed25519 生产密钥");
    expect(serialized).toContain("rc.1 及更早版本无法验证新信任根");
    expect(serialized).toContain("升级到 rc.2 后，系统内更新继续校验签名");
    expect(serialized).not.toContain("可直接在系统内完成");
    expect(serialized).toContain("UPGRADE_SERVICE_STOP_FAILED");
    expect(serialized).toContain("个人使用");
    expect(serialized).toContain("无需管理员授权");
    expect(serialized).toContain("WinError 786");
    expect(serialized).toContain("ADMIN_POLICY_BLOCKED");
    expect(serialized).toContain("彻底卸载");
    expect(serialized).toContain("Ed25519");
    expect(serialized).toContain("失败会回滚");
    expect(serialized).toContain("/var/log/partyops-package-selftest.log");
    expect(serialized).toContain("sudo dpkg --configure -a");
    expect(serialized).toContain("_apt 无法访问");
    expect(serialized).not.toContain("程序留在 Program Files");
  });

  it("所有新窗口外链使用安全属性", () => {
    expect(externalLinkProps).toEqual({
      target: "_blank",
      rel: "noopener noreferrer",
    });
  });
});
