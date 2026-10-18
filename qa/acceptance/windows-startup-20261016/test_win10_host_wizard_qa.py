"""主机向导控制器离线契约；不执行 Guest 功能。"""
import copy
import importlib.util
import json
import subprocess
import unittest
from pathlib import Path
from unittest.mock import MagicMock

spec = importlib.util.spec_from_file_location("win10_host_wizard_qa", Path(__file__).with_name("win10-host-wizard-qa.py"))
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)


class HostWizardTests(unittest.TestCase):
    def setUp(self):
        self.binding = {"manifest_sha256": "a" * 64, "wizard_sha256": "b" * 64}
        self.since = "2026-10-17T08:00:00+00:00"
        self.snapshot = {"username": "partyopsuser", "administrator": False, "session_id": 1,
            "processes": [{"name": "PartyOpsWizard.exe", "pid": 12, "installed_path_matches": True,
                "owner_is_original_user": True, "session_id": 1, "created": "2026-10-17T08:00:01Z"}],
            "listeners": [{"pid": 12, "port": 49211, "owned_by_wizard": True, "loopback": True}]}

    def test_wizard_identity_and_listener_must_match(self):
        adapter.validate_wizard(self.snapshot, self.since)
        for key, value in (("installed_path_matches", False), ("owner_is_original_user", False),
                           ("session_id", 2), ("created", "2026-10-17T07:59:59Z")):
            changed = copy.deepcopy(self.snapshot); changed["processes"][0][key] = value
            with self.assertRaises(RuntimeError): adapter.validate_wizard(changed, self.since)
        for key, value in (("pid", 99), ("owned_by_wizard", False), ("loopback", False)):
            changed = copy.deepcopy(self.snapshot); changed["listeners"][0][key] = value
            with self.assertRaises(RuntimeError): adapter.validate_wizard(changed, self.since)
        with self.assertRaises(RuntimeError): adapter.validate_wizard({**self.snapshot, "administrator": True}, self.since)

    def test_launch_only_ordinary_interactive_wizard(self):
        script = adapter.launch_script(self.binding, self.since)
        self.assertIn("--reconfigure --initial-role host", script)
        self.assertIn(".LogonType=3", script)
        self.assertIn(".RunLevel=0", script)
        self.assertIn("if($pending -or $journal)", script)
        self.assertIn("EXISTING_WIZARD_INSPECT_WITHOUT_RELAUNCH", script)
        for forbidden in ("--privileged-", "--data-dir", "Stop-Process", ".Kill(", "-Verb RunAs", "Start-Service"):
            self.assertNotIn(forbidden, script)

    def test_inspection_excludes_private_sources_and_raw_payloads(self):
        script = adapter.inspect_script(self.binding, self.since)
        for forbidden in ("wizard.url", "personal.env", "partyops.env", "$value.detail", "$value.message", "$value.transaction_id"):
            self.assertNotIn(forbidden, script)
        self.assertIn("uac_acceptance='not-verified'", script)
        self.assertIn("public_outcomes_since_launch", script)

    def test_transport_command_is_short_and_script_is_utf8_exclusive(self):
        client, execute = MagicMock(), MagicMock(return_value='{"launch_requested":true}')
        script = adapter.launch_script(self.binding, self.since)
        adapter.transfer(client, execute, script, "host-wizard-012345abcdef")
        fs = client.open_sftp.return_value.__enter__.return_value
        self.assertEqual(fs.file.call_args.args[1], "wx")
        fs.file.return_value.__enter__.return_value.write.assert_called_once_with(script.encode("utf-8"))
        self.assertLess(len(execute.call_args.args[1]), 300)
        with self.assertRaises(RuntimeError): adapter.transfer(client, execute, script, "../unowned")

    def test_powershell_parser_only(self):
        parser = "$s=[Console]::In.ReadToEnd();$e=$null;$t=$null;[void][Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){$e|ForEach-Object {$_.Message};exit 1}"
        for script in (adapter.launch_script(self.binding, self.since), adapter.inspect_script(self.binding, self.since)):
            result = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", parser],
                                    input=script, encoding="utf-8", capture_output=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__": unittest.main()
