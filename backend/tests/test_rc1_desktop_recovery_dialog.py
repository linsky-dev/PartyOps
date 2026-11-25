"""Exercise recovery dialog control flow without requiring an interactive desktop.

Real Tk/frozen package acceptance is separate. No business DB is touched here.
"""
from __future__ import annotations

import json
import sys
from types import SimpleNamespace

import pytest

from app import desktop_recovery, setup_wizard


class Variable:
    def __init__(self, value=""):
        self.value = value

    def get(self):
        return self.value

    def set(self, value):
        self.value = value


class Widget:
    def __init__(self, *args, **kwargs):
        self.kwargs = kwargs

    def grid(self, **kwargs):
        pass

    def pack(self, **kwargs):
        pass

    def columnconfigure(self, *args, **kwargs):
        pass

    def configure(self, **kwargs):
        self.kwargs.update(kwargs)

    def withdraw(self):
        pass

    def destroy(self):
        pass


@pytest.fixture
def gui(monkeypatch, tmp_path):
    events = []
    state = SimpleNamespace(mode="personal", owner=True, admin=False, cancel=False,
                            consent=True, code=0, stopped=True, restart_failure=False,
                            discovered=None, submit_failure=False, dialogs=0)
    root = tmp_path / "config"
    root.mkdir()
    data = tmp_path / "中文 数据"
    data.mkdir()
    state.root, state.data = root, data
    mode_file = root / "mode.json"
    mode_file.write_text('{"mode":"personal"}', encoding="utf-8")

    class Dialog:
        def __init__(self, parent):
            state.dialogs += 1
            self.result = None
            self.body(Widget())
            self.buttonbox()
            self.password.set("short")
            assert not self.validate()
            self.password.set("Synthetic-Password123!")
            self.confirmation.set("different")
            assert not self.validate()
            self.confirmation.set(self.password.get())
            assert self.validate()
            if not state.cancel:
                self.apply()
                assert not self.password.get() and not self.confirmation.get()

        def title(self, *args):
            pass

        def bind(self, *args):
            pass

        def cancel(self):
            pass

        def ok(self):
            pass

    def checkbutton(*args, **kwargs):
        variable = kwargs["variable"]
        variable.set(True)
        kwargs["command"]()
        variable.set(False)
        kwargs["command"]()
        return Widget()

    def message(kind):
        def record(title, text, **kwargs):
            events.append((kind, title, text))
            return state.consent
        return record

    fake = SimpleNamespace(
        Tk=Widget, StringVar=Variable, BooleanVar=Variable,
        simpledialog=SimpleNamespace(Dialog=Dialog),
        ttk=SimpleNamespace(Label=Widget, Entry=Widget, Frame=Widget,
                            Button=Widget, Checkbutton=checkbutton),
        messagebox=SimpleNamespace(showinfo=message("info"), showerror=message("error"),
                                   showwarning=message("warning"), askyesno=message("ask")),
    )
    monkeypatch.setitem(sys.modules, "tkinter", fake)
    monkeypatch.setattr(setup_wizard, "config_root", lambda: root)
    monkeypatch.setattr(setup_wizard, "load_host_environment", lambda *a, **k: {
        "PARTYOPS_MODE": state.mode, "PARTYOPS_DATA_DIR": str(data), "PARTYOPS_PORT": "18775",
    })
    monkeypatch.setattr(desktop_recovery, "trusted_data_directory", lambda p: p)
    monkeypatch.setattr(desktop_recovery, "personal_owner_scope", lambda p: state.owner)
    monkeypatch.setattr(desktop_recovery, "is_os_administrator", lambda: state.admin)
    monkeypatch.setattr(setup_wizard, "_executable", lambda name: tmp_path / "PartyOps.exe")
    monkeypatch.setattr(setup_wizard, "_discover_running_windows_personal", lambda *a: state.discovered)
    monkeypatch.setattr(setup_wizard, "_write_personal_process_marker", lambda *a: events.append(("marker", *a)))
    monkeypatch.setattr(setup_wizard, "_personal_process_is_owned", lambda *a: False)
    monkeypatch.setattr(setup_wizard, "_personal_health_version", lambda *a: "1.4.6-rc.1")
    monkeypatch.setattr(setup_wizard, "_recover_legacy_personal_process_marker", lambda *a: events.append(("legacy-marker", *a)))
    monkeypatch.setattr(setup_wizard, "_stop_personal_process_for_data_migration", lambda *a: events.append(("stop", *a)) or state.stopped)
    monkeypatch.setattr(setup_wizard, "assert_windows_service_data_path_security", lambda *a, **k: events.append(("acl",)))
    monkeypatch.setattr(setup_wizard, "_acquire_windows_data_migration_lock", lambda: "lock")
    monkeypatch.setattr(setup_wizard, "_stop_windows_service_for_data_migration", lambda: {"PartyOps": True})
    monkeypatch.setattr(setup_wizard, "_restore_windows_services_after_data_migration", lambda s: events.append(("restore", s)))
    monkeypatch.setattr(setup_wizard, "_release_windows_data_migration_lock", lambda lock: events.append(("unlock", lock)))
    monkeypatch.setattr(setup_wizard, "run_elevated", lambda *a: events.append(("elevate", *a)) or SimpleNamespace(returncode=state.code))

    def restart(config):
        events.append(("restart", config))
        if state.restart_failure:
            raise OSError("simulated service failure")
        return "http://127.0.0.1:18775"

    monkeypatch.setattr(setup_wizard, "launch_personal", restart)

    def submit(args, **kwargs):
        events.append(("submit", args))
        assert "Synthetic-Password123!" not in " ".join(args)
        assert json.loads(kwargs["input"]) == {
            "password": "Synthetic-Password123!", "confirmation": "本机应急恢复",
        }
        assert kwargs["capture_output"] and kwargs["timeout"] == 120
        if state.submit_failure:
            raise OSError("private failure")
        return SimpleNamespace(returncode=state.code, stdout='{"username":"admin"}')

    monkeypatch.setattr(desktop_recovery.subprocess, "run", submit)

    def mode(value):
        mode_file.write_text(json.dumps({"mode": value}), encoding="utf-8")

    state.set_mode = mode
    return state, events


@pytest.mark.parametrize("scenario,expected", [
    ("success", 0), ("cancel", 0), ("denied", 2), ("client", 2),
    ("mismatch", 2), ("child-error", 2), ("pipe-error", 2),
    ("restart-error", 0), ("not-running", 0), ("discovered", 0),
])
def test_personal_dialog_transaction(gui, scenario, expected):
    state, events = gui
    if scenario == "cancel":
        state.cancel = True
    elif scenario == "denied":
        state.owner = False
    elif scenario == "client":
        state.set_mode("client")
    elif scenario == "mismatch":
        state.mode = "host"
    elif scenario == "child-error":
        state.code = 2
    elif scenario == "pipe-error":
        state.submit_failure = True
    elif scenario == "restart-error":
        state.restart_failure = True
    elif scenario == "not-running":
        state.stopped = False
    elif scenario == "discovered":
        state.discovered = (42, 18778, state.data / "PartyOps.exe", "1.4.5-rc.6")
    assert desktop_recovery.run_desktop_recovery() == expected
    kinds = [e[0] for e in events]
    assert "unlock" in kinds
    if scenario in {"cancel", "denied", "client", "mismatch"}:
        assert "submit" not in kinds and "stop" not in kinds
    else:
        assert "submit" in kinds and "stop" in kinds
        assert ("restart" in kinds) == state.stopped
    if scenario in {"success", "restart-error", "not-running", "discovered"}:
        assert any(e[0] == "info" and "admin" in e[2] for e in events)
    if scenario == "restart-error":
        assert "warning" in kinds
    if scenario == "discovered":
        assert any(e[0] == "stop" and e[2] == 18778 for e in events)


@pytest.mark.parametrize("scenario,expected", [
    ("cancel-elevation", 0), ("elevate", 0), ("failed-elevation", 1),
    ("already-admin", 0), ("explicit-admin", 0), ("explicit-denied", 2),
])
def test_team_dialog_os_authorization(gui, monkeypatch, scenario, expected):
    state, events = gui
    # Simulate only the module-local OS facade, not global os.name/Path.
    monkeypatch.setattr(desktop_recovery, "os", SimpleNamespace(
        name="nt", getenv=lambda *args: str(state.root), environ={},
    ))
    monkeypatch.setattr(desktop_recovery.subprocess, "CREATE_NO_WINDOW", 0x08000000, raising=False)
    state.set_mode("host")
    state.mode = "host"
    state.admin = scenario in {"already-admin", "explicit-admin"}
    state.consent = scenario != "cancel-elevation"
    state.code = 2 if scenario == "failed-elevation" else 0
    assert desktop_recovery.run_desktop_recovery(host=scenario.startswith("explicit")) == expected
    kinds = [e[0] for e in events]
    if state.admin:
        assert "acl" in kinds and "submit" in kinds and "restore" in kinds
        assert ("unlock", "lock") in events
    else:
        assert "submit" not in kinds
        assert ("elevate" in kinds) == (scenario in {"elevate", "failed-elevation"})
