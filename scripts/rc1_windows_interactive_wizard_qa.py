"""Non-silent pre-install UI regression, only on disposable Windows hosted runner."""
from __future__ import annotations

import ctypes as c
from ctypes import wintypes as wt
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import uuid

u = c.WinDLL("user32", use_last_error=True)
HWND = wt.HWND
UINT = wt.UINT
LPARAM = c.c_ssize_t
WPARAM = c.c_size_t
CALLBACK = c.WINFUNCTYPE(wt.BOOL, HWND, LPARAM)
u.EnumWindows.argtypes = [CALLBACK, LPARAM]
u.EnumChildWindows.argtypes = [HWND, CALLBACK, LPARAM]
u.IsWindowVisible.argtypes = [HWND]
u.IsWindowEnabled.argtypes = [HWND]
u.GetWindow.argtypes = [HWND, UINT]
u.GetWindow.restype = HWND
u.GetClassNameW.argtypes = [HWND, wt.LPWSTR, c.c_int]
u.SendMessageTimeoutW.argtypes = [HWND, UINT, WPARAM, LPARAM, UINT, UINT, c.POINTER(WPARAM)]
u.SendMessageTimeoutW.restype = LPARAM
u.PostMessageW.argtypes = [HWND, UINT, WPARAM, LPARAM]
u.PostMessageW.restype = wt.BOOL
u.GetWindowThreadProcessId.argtypes = [HWND, c.POINTER(wt.DWORD)]
TITLE = "党建智办 PartyOps 1.4.6-rc.1 安装向导"


def windows(parent=None):
    handles = []
    @CALLBACK
    def append(h, _):
        if u.IsWindowVisible(h):
            handles.append(h)
        return True
    if parent is None:
        u.EnumWindows(append, 0)
    else:
        u.EnumChildWindows(parent, append, 0)
    return handles


def send(h, message, wp=0, lp=0, timeout=1000):
    out = WPARAM()
    ok = u.SendMessageTimeoutW(h, message, wp, lp, 2, timeout, c.byref(out))
    if not ok:
        raise TimeoutError(f"GUI HWND {h} did not respond to message {message}")
    return out.value


def caption(h):
    buf = c.create_unicode_buffer(1024)
    try:
        send(h, 0x000D, len(buf), c.addressof(buf), 300)
        return buf.value
    except TimeoutError:
        return ""


def classname(h):
    buf = c.create_unicode_buffer(256)
    u.GetClassNameW(h, buf, len(buf))
    return buf.value


def wait(predicate, seconds=15):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        result = predicate()
        if result:
            return result
        time.sleep(.15)
    raise TimeoutError("expected wizard state did not arrive")


def title_window():
    return next((h for h in windows() if caption(h) == TITLE), None)


def button(root, label):
    matches = [h for h in windows(root)
               if "button" in classname(h).lower()
               and label in caption(h).replace("&", "")
               and u.IsWindowEnabled(h)]
    assert len(matches) == 1, (label, [(classname(h),caption(h)) for h in windows(root)])
    return matches[0]


def click(h):
    # Nonblocking BM_CLICK; a hung window must fail the next bounded probe.
    if not u.PostMessageW(h, 0x00F5, 0, 0):
        raise OSError(c.get_last_error(), "cannot send button click")


def owned(root):
    return [h for h in windows() if h != root and u.GetWindow(h, 4) == root]


def logfile(path):
    if not path.exists():
        return ""
    data = path.read_bytes()
    if data.startswith((b"\xff\xfe", b"\xfe\xff")):
        return data.decode("utf-16", errors="replace")
    return data.decode("utf-8-sig", errors="replace")


def last_page(path):
    pages = re.findall(r"\[PARTYOPS_WIZARD_PAGE\] page=([a-z0-9]+)", logfile(path))
    return pages[-1] if pages else ""


def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda:f.read(1024*1024), b""):
            h.update(block)
    return h.hexdigest()


def main():
    assert os.environ.get("GITHUB_ACTIONS") == "true"
    assert os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted"
    assert c.windll.shell32.IsUserAnAdmin(), "ADMIN_REQUIRED"
    candidate, output = map(Path, sys.argv[1:3])
    source = sys.argv[3]
    output.mkdir(parents=True, exist_ok=True)
    exe, = candidate.glob("*_BUILD_ONLY_UNVERIFIED.exe")
    receipt = json.loads(exe.with_suffix(".candidate.json").read_text(encoding="utf-8-sig"))
    assert receipt["source_commit"] == source and receipt["sha256"] == sha(exe)
    assert receipt["release_ready"] is False and receipt["signed"] is False
    report = {"source_commit":source,"installer_sha256":receipt["sha256"],
              "status":"NOT_RUN","scope":"interactive_pre_install_only","navigation":[]}
    log = output/"interactive-setup.log"
    root = Path(os.environ["RUNNER_TEMP"])/("PartyOps-GUI-"+uuid.uuid4().hex)
    program = root/"程序 中文"
    assert not title_window(), "existing setup window"
    proc = subprocess.Popen([str(exe),f"/DIR={program}",f"/LOG={log}","/NORESTART"])
    hwnd = None
    try:
        hwnd = wait(title_window, 90)
        def page(name, seconds=15):
            wait(lambda:last_page(log)==name and u.IsWindowEnabled(hwnd), seconds)
            send(hwnd, 0)
        def step(label, destination):
            start = time.monotonic()
            click(button(hwnd,label))
            page(destination)
            elapsed = round(time.monotonic()-start, 3)
            report["navigation"].append({"button":label,"page":destination,"seconds":elapsed})
            assert elapsed <= 10, (label,destination,elapsed)
        page("welcome", 20)
        step("下一步","program")
        step("下一步","data")
        click(button(hwnd,"浏览"))
        dialog = wait(lambda:next(iter(owned(hwnd)),None), 10)
        send(dialog, 0)
        assert u.PostMessageW(dialog, 0x0010, 0, 0)
        wait(lambda:not owned(hwnd) and u.IsWindowEnabled(hwnd), 10)
        send(hwnd, 0)
        report["browse_open_close_passed"] = True
        step("上一步","program")
        step("下一步","data")
        step("下一步","ready")
        step("上一步","data")
        click(button(hwnd,"取消"))
        confirmation = wait(lambda:next(iter(owned(hwnd)),None),10)
        # Close confirmation by clicking the affirmative button, never Install.
        yes = [h for h in windows(confirmation)
               if "button" in classname(h).lower() and
               caption(h).replace("&","").strip() in ("是(Y)","是","Yes","Yes(Y)")]
        assert len(yes)==1, [(classname(h),caption(h)) for h in windows(confirmation)]
        click(yes[0])
        wait(lambda:not title_window(), 15)
        proc.wait(timeout=20)
        assert not program.exists(), "navigation unexpectedly wrote program files"
        text=logfile(log)
        assert "[PARTYOPS_PATH_VALIDATOR_EXTRACT_BEGIN]" not in text
        assert "[PARTYOPS_INSTALL_SECURITY_CHECK_BEGIN]" not in text
        report["status"]="PASS"
        print("INTERACTIVE_WIZARD_NAVIGATION_PASS",json.dumps(report,ensure_ascii=False))
    except Exception as exc:
        report["status"]="FAILED"
        report["error"]=repr(exc)
        raise
    finally:
        (output/"interactive-wizard-receipt.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
        if hwnd and u.IsWindowVisible(hwnd):
            u.PostMessageW(hwnd,0x0010,0,0)
        if proc.poll() is None:
            proc.terminate()

if __name__ == "__main__":
    main()
