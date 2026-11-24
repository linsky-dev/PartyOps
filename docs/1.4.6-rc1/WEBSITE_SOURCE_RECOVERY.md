# PartyOps 1.4.6-rc.1：3D 官网历史源码恢复审查

## 发现与来源

2026-11-21 在用户 ChatGPT 文件库找到 PartyOps-3D-Website-Full-Source-v5.zip；交接日期 2026-10-28，原文件大小 91,727,068 字节。

原 ZIP SHA-256：4f01e37862530654f0173212ff42c222f721054970b8b3fc08c4d1dbcc819f11。

其根目录包含 partyops-immersive（Three.js/Vinext/Worker/D1 真实社区服务）和 partyops-demo-source（基于 PartyOps ba205288 的 Vue 只读演示），另有 START-HERE、AGENTS、SOURCE_INDEX 与 SOURCE_MANIFEST。

## 原始 ZIP 与代码验收

- ZIP 574 项、CRC 无异常，未见路径穿越及符号链接，清单登记 572/572 个实际工程文件 SHA-256 和字节长度全部匹配。
- 两套 package-lock.json 与 package.json 顶层依赖一致。
- 本地 Node.js 22.16.0 对外层 44 个 JS/ESM 文件完成语法检测：44/44 通过。
- 三维场景预加载 Node 测试：9/9 通过。
- 只读演示 Sites Worker 运行行为测试：前三项通过；第四项由于交接源码包本就未附带 dist/client/index.html 而未通过。完整依赖安装、编译和浏览器验收仍为 NOT_RUN。

## 不能越过的版本边界

- 历史三维官网站点修订 f8c3c1bcb20b8f307308b9d023d1deccbbbf606d；演示内屏 1.4.5-rc.6，不是新的 1.4.6-rc.1 业务系统。
- 目前 partyops.cn 仍展示 1.4.5-rc.6；EdgeOne 生产部署 dpv7nyk55sit 的最新源与历史 v5 尚未逐文件验证一致性。
- 外层站含真实 Worker/D1 社区表单与审核运营功能。不能把它作为无数据库的纯静态站；不能把内屏只读演示改成真实业务写入后端。
- 不能根据历史 ZIP 推断当前三维网站的完整可部署状态，也不能在软件新版本尚未验收时更新下载 URL 或捏造 SHA-256。

## 剩余条件

1. 在独立副本中安装两套工程已锁定的依赖，执行内屏 typecheck/test:demo/build:demo 与外层构建和全部单测，并验证 D1 迁移、受控社区审核、用户无权写业务数据。
2. 从现有 EdgeOne 项目导出当前生产源/实际构建资源，与历史 v5 完整对比，最小幅度迁移差异、保留最新物理场景和只读演示。
3. 在非生产预览完整验收 WebGL、Canvas、模型交互、首帧性能、两站内容、下载回读与回滚，再经用户批准发布。
4. PartyOps 十个安装包/十二原生系统、真实 WPS、签名及升级回滚仍是独立发布门禁。

此文只记录已核验历史源的事实；原 ZIP 保存在用户文件库，未复制到公开 GitHub 仓库。
