"""运行态切换的离线契约测试；不连接 Guest、不创建测试目录。"""
import copy
import importlib.util
import subprocess
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("runtime_transition", Path(__file__).with_name("runtime-transition.py"))
transition = importlib.util.module_from_spec(spec)
spec.loader.exec_module(transition)


class RuntimeTransitionTests(unittest.TestCase):
    def setUp(self):
        self.desktop = {"uuid": transition.contract.UUID, "username": "partyopsuser", "administrator": False,
                        "desktop": [{"owner": "partyopsuser", "session_id": 1}]}
        self.process = {"name": "PartyOps.exe", "pid": 12, "path": transition.contract.INSTALL + r"\PartyOps.exe",
                        "created": "2026-10-17T04:00:00.0000000Z", "owner": "partyopsuser", "domain": "QA",
                        "local_owner": True, "session_id": 1}
        self.actual = {"manifest_sha256": transition.OLD_MANIFEST, "executable_sha256": "exe", "wizard_sha256": "wizard",
                       "boot_id": "2026-10-17T03:00:00Z", "processes": [self.process], "http_owners": [12]}
        self.inventory = {"products": [self.process], "installer_count": 0}

    def test_exact_old_process(self):
        self.assertEqual(transition.old_target(self.actual, self.inventory, self.desktop), self.process)

    def test_fail_closed_identity_matrix(self):
        changes = [("owner", "Administrator"), ("local_owner", False), ("session_id", 0),
                   ("path", r"C:\Other\PartyOps.exe"), ("created", "")]
        for key, value in changes:
            with self.subTest(key=key):
                inventory = copy.deepcopy(self.inventory)
                inventory["products"][0][key] = value
                with self.assertRaises(RuntimeError):
                    transition.old_target(self.actual, inventory, self.desktop)
        for key, value in [("products", self.inventory["products"] * 2), ("installer_count", 1)]:
            inventory = {**self.inventory, key: value}
            with self.assertRaises(RuntimeError):
                transition.old_target(self.actual, inventory, self.desktop)
        for key, value in [("http_owners", [99]), ("manifest_sha256", "other")]:
            with self.assertRaises(RuntimeError):
                transition.old_target({**self.actual, key: value}, self.inventory, self.desktop)
        inventory = copy.deepcopy(self.inventory)
        inventory["products"][0]["created"] = "2026-10-17T04:01:00.0000000Z"
        with self.assertRaises(RuntimeError):
            transition.old_target(self.actual, inventory, self.desktop)

    def test_admin_or_missing_desktop_rejected(self):
        for update in ({"administrator": True}, {"desktop": []}, {"username": "other"}):
            with self.assertRaises(RuntimeError):
                transition.standard_desktop({**self.desktop, **update})

    def test_wmi_uuid_case_preserves_exact_identity(self):
        transition.standard_desktop({**self.desktop, "uuid": transition.contract.UUID.upper()})
        for value in (None, "", "047cf265-72a2-4d51-9cf1-b1f5ae960cb2"):
            with self.assertRaises(RuntimeError):
                transition.standard_desktop({**self.desktop, "uuid": value})

    def test_cim_microsecond_precision_accepts_same_instant_only(self):
        # 只执行纯时间比较函数，不获取或操作本机/Guest 进程。
        script = transition.START_TIME_MATCH + r"""
$cim='2026-10-17T04:00:00.5055330Z'
foreach($same in @('2026-10-17T04:00:00.5055330Z','2026-10-17T04:00:00.5055334Z','2026-10-17T04:00:00.5055339Z','2026-10-17T12:00:00.5055334+08:00')) {
  if(-not (Test-CimCreationInstant ([DateTimeOffset]::Parse($same).UtcDateTime) $cim)) {throw 'SAME_MICROSECOND_REJECTED'}
}
foreach($different in @('2026-10-17T04:00:00.5055340Z','2026-10-17T04:00:00.5055329Z','2026-10-17T04:01:00.5055334Z','2026-10-18T04:00:00.5055334Z')) {
  if(Test-CimCreationInstant ([DateTimeOffset]::Parse($different).UtcDateTime) $cim) {throw 'DIFFERENT_CREATION_ACCEPTED'}
}
"""
        completed = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", script],
                                   encoding="utf-8", capture_output=True)
        self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)
        self.assertNotIn("SAME_MICROSECOND_REJECTED", completed.stderr)
        self.assertNotIn("DIFFERENT_CREATION_ACCEPTED", completed.stderr)

    def test_generated_powershell_parses_and_termination_is_explicit(self):
        script = transition.controlled_stop_script(self.process, False)
        self.assertIn('"allow_termination": false', script)
        self.assertIn("if(-not $request.allow_termination)", script)
        self.assertLess(script.index("CloseMainWindow()"), script.index("$handle.Kill()"))
        self.assertNotIn("Stop-Process", script)
        self.assertIn("Test-CimCreationInstant $handle.StartTime $t.created", script)
        self.assertIn("'name','pid','path','created','owner','domain','session_id','local_owner'", script)
        # 只调用本机 PowerShell 解析器，不执行脚本内容。
        parser = "$s=[Console]::In.ReadToEnd();$e=$null;$t=$null;[void][Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){$e|ForEach-Object {$_.Message};exit 1}"
        for text in (transition.INVENTORY, script, transition.controlled_stop_script(self.process, True)):
            completed = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", parser],
                                       input=text, encoding="utf-8", capture_output=True)
            self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)


if __name__ == "__main__":
    unittest.main()
