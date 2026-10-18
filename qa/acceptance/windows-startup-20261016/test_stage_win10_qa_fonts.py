"""字体控制器离线契约；不连接Guest、不执行字体任务、不写宿主字体。"""
import copy
import importlib.util
import subprocess
import sys
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
spec = importlib.util.spec_from_file_location("win10_qa_fonts", HERE / "stage-win10-qa-fonts.py")
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)


class FontTests(unittest.TestCase):
    def setUp(self):
        self.fonts = [{"filename": name, "registry_name": registry, "sha256": str(i + 1) * 64, "bytes": 100}
                      for i, (name, registry) in enumerate(adapter.FONTS)]
        self.value = adapter.request(self.fonts, "012345abcdef")
        self.snapshot = {"guest_uuid": adapter.contract.UUID, "username": "partyopsuser", "standard_account": True,
                         "session_id": 1, "fonts": [{"filename": f["filename"], "sha256": f["sha256"], "registry_matches": True} for f in self.fonts]}

    def test_fixed_three_names_and_transport(self):
        self.assertEqual(len(self.value["fonts"]), 3)
        self.assertEqual(self.value["username"], "partyopsuser")
        with self.assertRaises(RuntimeError): adapter.request(self.fonts + [self.fonts[0]], "012345abcdef")
        with self.assertRaises(RuntimeError): adapter.request(self.fonts, "../other")

    def test_matching_fonts_are_reused_missing_registration_is_pending(self):
        self.assertEqual(adapter.pending_fonts(self.snapshot, self.fonts), [])
        missing = copy.deepcopy(self.snapshot)
        missing["fonts"][0]["sha256"] = None
        self.assertEqual(adapter.pending_fonts(missing, self.fonts), [self.fonts[0]])
        registration = copy.deepcopy(self.snapshot)
        registration["fonts"][1]["registry_matches"] = False
        self.assertEqual(adapter.pending_fonts(registration, self.fonts), [self.fonts[1]])
        for fault in ("hash", "uuid", "username", "session", "administrator", "duplicate"):
            with self.subTest(fault=fault):
                changed = copy.deepcopy(self.snapshot)
                if fault == "hash": changed["fonts"][0]["sha256"] = "f" * 64
                if fault == "uuid": changed["guest_uuid"] = "other"
                if fault == "username": changed["username"] = "partyopsqa"
                if fault == "session": changed["session_id"] = 0
                if fault == "administrator": changed["standard_account"] = False
                if fault == "duplicate": changed["fonts"][1] = changed["fonts"][0]
                with self.assertRaises(RuntimeError): adapter.pending_fonts(changed, self.fonts)

    def test_ordinary_interactive_user_hkcu_and_no_overwrite(self):
        user = adapter.user_script(self.value)
        launch = adapter.launch_script(self.value)
        self.assertIn("$identity.User.Value -ne $sid", user)
        self.assertIn("$session -ne $desktop[0]", user)
        self.assertIn("IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)", user)
        self.assertIn("Registry]::CurrentUser.CreateSubKey", user)
        self.assertIn("AddFontResourceEx($dest,0", user)
        self.assertIn("0x001d", user)
        self.assertIn("$dest,$false)", user)
        self.assertLess(user.index("FONT_DESTINATION_CONFLICT"), user.index("[IO.Directory]::CreateDirectory"))
        self.assertLess(user.index("AddFontResourceEx($dest,0"), user.index("$key.SetValue("))
        self.assertIn(".LogonType=3", launch)
        self.assertIn(".RunLevel=0", launch)
        self.assertIn("RegisterTaskDefinition($request.task,$definition,2", launch)
        for forbidden in ("HKLM:", "LocalMachine.Create", "-Verb RunAs", "Remove-Item", "Stop-Process", "Start-Service", "Program Files"):
            self.assertNotIn(forbidden, user + launch)

    def test_utf8_short_command_transport(self):
        client, execute = MagicMock(), MagicMock(return_value='{"fonts":[]}')
        script = adapter.inspect_script(self.value)
        adapter.execute_script(client, execute, script)
        fs = client.open_sftp.return_value.__enter__.return_value
        self.assertEqual(fs.file.call_args.args[1], "wx")
        fs.file.return_value.__enter__.return_value.write.assert_called_once_with(script.encode("utf-8"))
        self.assertLess(len(execute.call_args.args[1]), 300)

    def test_stage_requires_explicit_flag_before_any_connection(self):
        with patch.object(sys, "argv", ["stage-win10-qa-fonts.py", "stage"]), patch.object(adapter, "local_fonts") as local:
            with self.assertRaises(RuntimeError): adapter.main()
            local.assert_not_called()

    def test_powershell_parser_only(self):
        parser = "$s=[Console]::In.ReadToEnd();$e=$null;$t=$null;[void][Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){$e|ForEach-Object {$_.Message};exit 1}"
        for script in (adapter.inspect_script(self.value), adapter.user_script(self.value), adapter.launch_script(self.value)):
            result = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", parser],
                                    input=script, encoding="utf-8", capture_output=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
