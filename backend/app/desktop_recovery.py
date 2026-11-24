"""Local-only graphical recovery. The server does not expose a reset endpoint."""

from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

from .recovery_cli import (
    is_os_administrator,
    personal_owner_scope,
    trusted_data_directory,
)


def run_desktop_recovery(*, host: bool = False) -> int:
    from tkinter import BooleanVar, StringVar, Tk, messagebox, simpledialog, ttk

    from . import setup_wizard as wizard

    class ResetDialog(simpledialog.Dialog):
        """One form, no recovery tokens or technical paths."""
        def body(self, master):
            self.title("党建智办 · 找回账号和密码")
            self.password = StringVar()
            self.confirmation = StringVar()
            self.visible = BooleanVar(value=False)
            self.error = StringVar(value="")
            master.columnconfigure(0, weight=1)
            ttk.Label(master, text="找回账号和密码", font=("TkDefaultFont", 16, "bold")).grid(row=0, column=0, sticky="w", pady=(8, 10))
            ttk.Label(master, text="已进入本机恢复助手。请设置新的登录密码。\n原账号、业务资料及权限不会被删除，旧登录会话将失效。", wraplength=360, justify="left").grid(row=1, column=0, sticky="w", pady=(0, 12))
            ttk.Label(master, text="新密码（8—128 个字符）").grid(row=2, column=0, sticky="w")
            first = ttk.Entry(master, textvariable=self.password, show="*", width=38)
            first.grid(row=3, column=0, sticky="ew", pady=(4, 10))
            ttk.Label(master, text="再次输入新密码").grid(row=4, column=0, sticky="w")
            second = ttk.Entry(master, textvariable=self.confirmation, show="*", width=38)
            second.grid(row=5, column=0, sticky="ew", pady=(4, 8))
            def toggle():
                mask = "" if self.visible.get() else "*"
                first.configure(show=mask)
                second.configure(show=mask)
            ttk.Checkbutton(master, text="显示密码", variable=self.visible, command=toggle).grid(row=6, column=0, sticky="w")
            ttk.Label(master, textvariable=self.error, foreground="#a32020", wraplength=360).grid(row=7, column=0, sticky="w", pady=(8, 4))
            ttk.Label(master, text="点击恢复后，软件将自动处理本机服务，无需手动操作。", wraplength=360).grid(row=8, column=0, sticky="w")
            return first

        def buttonbox(self):
            from tkinter import ttk

            box = ttk.Frame(self)
            ttk.Button(box, text="取消", command=self.cancel).pack(side="right", padx=6)
            ttk.Button(box, text="恢复账号和密码", command=self.ok).pack(side="right", padx=6)
            box.pack(fill="x", padx=8, pady=12)
            self.bind("<Return>", self.ok)
            self.bind("<Escape>", self.cancel)

        def validate(self):
            value = self.password.get()
            if not 8 <= len(value) <= 128:
                self.error.set("请输入 8—128 个字符的新密码。")
                return False
            if value != self.confirmation.get():
                self.error.set("两次密码不一致，请重新确认。")
                return False
            return True

        def apply(self):
            self.result = self.password.get()
            self.password.set("")
            self.confirmation.set("")

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
        password = ResetDialog(root).result
        if password is None:
            return 0
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
            creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
        )
        if result.returncode != 0:
            raise ValueError("恢复未完成。若有其他管理员，请联系其重置；否则请重新打开软件后重试。原账号和业务数据未被删除。")
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
