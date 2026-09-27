# Windows 构建说明

本文件保留为旧入口。当前唯一规范构建、发布、安装和验收说明见 `BUILD_AND_RUN_WINDOWS.md`。

最短验证命令：

```powershell
.\build.ps1 -Configuration Debug -Platform AnyCPU
```

该命令已经包含结构门禁、完整解决方案重建和三套回归程序（最新 Release 配置共 165 项断言），不再需要按项目手工决定构建顺序。
