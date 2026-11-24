# PartyOps 1.4.6-rc.1 · 唯一管理员忘记密码的本机应急恢复

## 为什么需要

原“管理员离线恢复码”与“本机维护授权”都需要事先保存的凭据，首次忘记密码时会陷入无法自助恢复的闭环。本轮还区分 Windows 团队主机与个人模式的数据所有权，防止个人账户因 SYSTEM 服务 ACL 检查而被错误锁死。
此改动只增加 `--account-recovery emergency` 控制台操作，保留旧的 `authorize`、`issue`、普通用户管理员签发和离线恢复码路径，不新增匿名 HTTP 改密接口，也不改动 WPS 排版实现。

## 使用边界

- 适用于**已经配置的主机或个人本机**，且数据库中**恰好只有一名有效系统管理员**（可以有普通成员）。
- **团队主机：**需要操作系统管理员/root 权限和交互式终端；Windows 团队服务数据库必须通过原有 SYSTEM 服务 ACL 校验，不能借个人模式放宽。**Windows 个人模式：**仅允许当前 Windows 登录账户的配置、目录与数据库所有者使用交互式终端；不要求该账户具有系统管理员权限，但必须逐项校验该账户 SID、私有 ACL、已保存个人模式及数据路径一致。协同机与匿名网页不能签发本机凭据。
- 必须指定已初始化、位于**本机固定磁盘**、经安装器权限校验的真实数据目录；拒绝符号链接/重解析点和过宽权限。
- 实际主机服务必须先停止；恢复工具获取独占实例锁，并且仅在已有 `recovery_credentials` 表时运行，不修改原有数据库结构。
- 必须在本机手动输入“本机应急恢复”确认；只有一名有效管理员时才签发 `admin_host` 30 分钟恢复凭据。
- 一次性恢复凭据只显示一次，仅在成功兑换时作废；新密码和用户名修改在权威主机的原事务内完成，旧 Cookie/Bearer 会话失效，并写入审计。
- 发现多位管理员、数据库未迁移、目录权限错误、主机仍运行、审计失败时**拒绝操作**。多位管理员请联系另一位有效管理员使用后台正式流程处理。

## 操作流程（先备份，再操作）

1. 在**真实主机**核实当前数据目录。Linux 系统服务常见位置为 `/var/lib/partyops`；个人模式默认通常为 `~/.local/share/partyops-personal`。Windows 团队主机的默认数据目录是 `C:\\ProgramData\\PartyOps-Data`，**不是** `C:\\ProgramData\\PartyOps`（后者用于安装控制标记）；真实值以 `C:\\ProgramData\\PartyOps\\install-data-dir.txt` 和已保存主机配置为准。Windows 个人模式默认在 `%LOCALAPPDATA%\\PartyOps-个人数据`，真实值必须读取当前账户的 `%LOCALAPPDATA%\\PartyOps\\personal.env` 中的 `PARTYOPS_DATA_DIR`，并与 `mode.json` 的个人模式绑定一致。**任何模式都不可指向新建空目录。**
2. 完成业务数据库与附件备份，确认恢复后能够校验原始数据。停止 PartyOps 主机服务，防止双进程同时操作一个数据库。不要先卸载旧版，也不要删除旧账号、旧数据库或初始化系统。
3. 在提升权限的本机交互终端运行（将数据目录与程序安装路径替换成真实值）：

   UOS / 麒麟 / Linux：

   ```bash
   sudo /opt/partyops/partyops --account-recovery emergency --data-dir /var/lib/partyops
   ```

   Windows **团队主机**：先读取 `C:\\ProgramData\\PartyOps\\install-data-dir.txt` 或现有 `partyops.env` 确定真实业务目录。在“以管理员身份运行”的 PowerShell 中进入 PartyOps 的实际安装目录，执行（下面路径仅为安装器默认示例）：

   ```powershell
   & ".\\PartyOps.exe" --account-recovery emergency --data-dir "C:\\ProgramData\\PartyOps-Data"
   ```

   Windows **个人模式**：使用当前登录的 Windows 账户（即拥有原个人配置与业务数据库的账户），无需管理员提权。先从 `%LOCALAPPDATA%\\PartyOps\\personal.env` 读取 `PARTYOPS_DATA_DIR`，确认其所属目录和 `mode.json` 均与当前账户匹配，再在 PartyOps 实际安装目录运行：

   ```powershell
   & ".\\PartyOps.exe" --account-recovery emergency --data-dir "$env:LOCALAPPDATA\\PartyOps-个人数据"
   ```

   上述个人目录是**默认示例**；自定义数据目录必须以原配置中的 `PARTYOPS_DATA_DIR` 为准。如果换了 Windows 账户或管理员身份代替个人账户启动，所有者检查将拒绝恢复；不能复制数据库或放宽 ACL 规避。

   macOS：在本机具备授权的提升权限终端，指定完整包内主机可执行文件和实际数据目录：

   ```bash
   sudo "/Applications/PartyOps.app/Contents/MacOS/partyops" --account-recovery emergency --data-dir "/实际主机数据目录"
   ```

4. 仔细阅读风险提示，在本机输入 `本机应急恢复`。如果业务数据权限或实例锁不合格，先排查问题；**不要通过拷贝数据库到不受保护的新目录来绕开门禁**。
5. 安全地暂存控制台单次显示的恢复凭据，重启 PartyOps 原主机服务。在 30 分钟内打开登录页“恢复账号”，填入恢复凭据和**新密码**并提交；成功后使用原用户名或新用户名与新密码登录。
6. 在“账号设置”中重新签发新的管理员离线恢复码并离线保管；本机确认旧浏览器 Cookie、其他设备 Bearer 会话失效；核验原账号 ID、业务附件、成员归属和审计。

## 验收与非目标

- 单管理员无既有恢复码可恢复；旧密码不必输入。Windows 团队主机仍需管理员提权与 SYSTEM 服务 DACL；Windows 个人模式则必须由原数据归属的同一 Windows 用户、凭私有 ACL 使用。
- 多管理员、无有效管理员、团队主机非 OS 管理员、非 TTY、个人模式非原 Windows 目录所有者、个人配置不匹配或 ACL 过宽、目录重解析/非固定磁盘、原主机实例未退出、未完成数据库迁移、取消确认、审计异常均拒绝签发。
- 发行包只在能证明包含新 `backend/app/recovery_cli.py` 及原 `packaging/uos/entrypoint.py` 入口时才算源码集成，**不能把 CI 通过当作 Windows/UOS 真实安装升级验收**。
- 不修改 WPS 原生二进制和排版代码，不新增通过 HTTP 远程匿名找回管理员密码的通路。
