"""Local-only graphical recovery. The server does not expose a reset endpoint."""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

from .recovery_cli import is_os_administrator, personal_owner_scope, trusted_data_directory


def run_desktop_recovery(*, host: bool = False) -> int:
    from tkinter import Tk, messagebox, simpledialog

    from . import setup_wizard as wizard

    root = Tk()
    root.withdraw()
    stopped = False
    states: dict[str, bool] = {}
    lock_path = None
    config: Path | None = None
    try:
        mode_path = wizard.config_root() / "mode.json"
        if not host:
            mode = json.loads(mode_path.read_text(encoding="utf-8")).get("mode")
            if mode == "host" and os.name == "nt":
                if not is_os_administrator():
                    if messagebox.askyesno("党建智办", "团队主机恢复需要 Windows 管理员授权，是否继续？", parent=root):
                        result = wizard.run_elevated(str(sys.executable), ["--recover-account", "--recovery-host"])
                        return int(result.returncode != 0)
                    return 0
                host = True
            elif mode != "personal":
                raise ValueError("请在保存业务数据的个人电脑或团队主机操作。")
        if host:
            if os.name != "nt" or not is_os_administrator():
                raise ValueError("团队主机需要本机管理员授权；当前平台请使用已有主机恢复流程。")
            config = Path(os.getenv("PROGRAMDATA", "C:/ProgramData")) / "PartyOps" / "partyops.env"
        else:
            config = wizard.config_root() / "personal.env"
        saved = wizard.load_host_environment(config, inherit_environment=False)
        expected_mode = "host" if host else "personal"
        if saved.get("PARTYOPS_MODE") != expected_mode:
            raise ValueError("本机配置身份不匹配，恢复已停止。")
        data = trusted_data_directory(Path(saved["PARTYOPS_DATA_DIR"]))
        if host:
            wizard.assert_windows_service_data_path_security(data, verify_target=True)
        elif not personal_owner_scope(data):
            raise ValueError("当前操作系统账号不是已配置个人数据的安全所有者，恢复已停止。")
        if not messagebox.askyesno("恢复账号", "仅用于唯一管理员忘记账号或密码。\n将短暂停止本机服务，保留原账号与业务数据，并使旧登录会话失效。\n确认继续？", parent=root):
            return 0
        password = simpledialog.askstring("设置新密码", "请输入新密码（至少 8 个字符）：", show="*", parent=root)
        if password is None:
            return 0
        confirm = simpledialog.askstring("确认新密码", "请再次输入新密码：", show="*", parent=root)
        if confirm != password or not 8 <= len(password) <= 128:
            raise ValueError("两次密码不一致，或密码长度不符合要求。")
        executable = wizard._executable("partyops")
        if host:
            lock_path = wizard._acquire_windows_data_migration_lock()
            states = wizard._stop_windows_service_for_data_migration()
        else:
            port = int(saved["PARTYOPS_PORT"])
            discovered = wizard._discover_running_windows_personal(data, port)
            if discovered is not None:
                pid, port, binary, _ = discovered
                wizard._write_personal_process_marker(data, pid, binary)
            stopped = wizard._stop_personal_process_for_data_migration(data, port)
        env = os.environ.copy()
        env.update({"PYTHONUTF8": "1", "PYINSTALLER_RESET_ENVIRONMENT": "1"})
        result = subprocess.run(
            [str(executable), "--account-recovery", "desktop-reset", "--data-dir", str(data)],
            input=json.dumps({"password": password, "confirmation": "本机应急恢复"}),
            text=True, encoding="utf-8", capture_output=True, timeout=120, env=env,
            **({"creationflags": subprocess.CREATE_NO_WINDOW} if os.name == "nt" else {}),
        )
        if result.returncode != 0:
            raise ValueError("恢复未完成。请确认本机只有一位有效管理员、数据权限正确且原服务已经退出。")
        username = json.loads(result.stdout)["username"]
        messagebox.showinfo("恢复成功", f"您的登录账号：{username}\n请使用刚设置的新密码登录。", parent=root)
        return 0
    except Exception as exc:
        messagebox.showerror("账号恢复未完成", str(exc) if isinstance(exc, ValueError) else "请确认原电脑已正确安装并完成配置。", parent=root)
        return 2
    finally:
        try:
            if states:
                wizard._restore_windows_services_after_data_migration(states)
            if stopped and config is not None:
                wizard.launch_personal(config)
        except Exception:
            messagebox.showwarning("需要重新启动", "请重新打开党建智办。恢复操作的结果不受启动失败影响。", parent=root)
        finally:
            wizard._release_windows_data_migration_lock(lock_path)
            root.destroy()
