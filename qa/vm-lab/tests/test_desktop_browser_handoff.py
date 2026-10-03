"""在真实 Bash 中验证桌面入口不会等待或终止长驻浏览器。"""
import os
import signal
import subprocess
import tempfile
import unittest
from pathlib import Path


@unittest.skipUnless(os.name == "posix", "需要 Linux Bash；另在原版 Guest 执行")
class DesktopBrowserHandoffTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        override = os.environ.get("PARTYOPS_DESKTOP_TEST_SCRIPT")
        script = Path(override) if override else Path(__file__).resolve().parents[3] / "packaging/uos/desktop-launcher.sh"
        source = script.read_text(encoding="utf-8")
        start = source.index("launch_browser_detached()") if "launch_browser_detached()" in source else source.index("open_browser_url()")
        cls.functions = source[start:source.index("open_local_tool_url()", start)]

    def run_opener(self, opener, fallback, expected, *, browser_survives=False):
        with tempfile.TemporaryDirectory(prefix="partyops-browser-handoff-") as directory:
            root = Path(directory)
            for name, body in (("xdg-open", opener), ("gio", fallback)):
                path = root / name
                path.write_text("#!/bin/bash\n" + body + "\n", encoding="utf-8")
                path.chmod(0o700)
            env = dict(os.environ, PATH=str(root) + os.pathsep + os.environ["PATH"], CASE_DIR=str(root))
            script = self.functions + '\nLAUNCH_LOG="$CASE_DIR/launch.log"\nexec 9>"$CASE_DIR/launch.lock"\nflock -n 9\nif open_browser_url http://127.0.0.1:18775; then exit 0; else exit 1; fi\n'
            process = subprocess.Popen(["/bin/bash", "-c", script], env=env,
                                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                       start_new_session=True)
            try:
                self.assertEqual(process.wait(timeout=4), expected)
                if browser_survives:
                    import fcntl

                    pid = int((root / "browser.pid").read_text())
                    os.kill(pid, 0)
                    with (root / "launch.lock").open("a") as lock:
                        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                    self.assertFalse((root / "fallback.called").exists())
                elif expected == 0:
                    self.assertTrue((root / "fallback.called").exists())
            finally:
                # 只结束本测试显式创建的独立进程组，不触碰真实桌面浏览器。
                try:
                    os.killpg(process.pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
                process.wait(timeout=3)

    def test_long_lived_browser_survives_launcher_return(self):
        self.run_opener('echo $$ > "$CASE_DIR/browser.pid"; sleep 30',
                        'touch "$CASE_DIR/fallback.called"; exit 7', 0, browser_survives=True)

    def test_immediate_failure_uses_fallback(self):
        self.run_opener("exit 7", 'touch "$CASE_DIR/fallback.called"; exit 0', 0)

    def test_both_openers_failing_is_not_success(self):
        self.run_opener("exit 7", "exit 8", 1)


if __name__ == "__main__":
    unittest.main()
