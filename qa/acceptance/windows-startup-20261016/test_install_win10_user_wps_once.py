"""普通用户WPS一次安装工具离线契约；不运行安装器或连接Guest。"""
import copy
import importlib.util
import subprocess
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
spec = importlib.util.spec_from_file_location("ordinary_wps_once", HERE / "install-win10-user-wps-once.py")
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)


class OrdinaryWpsTests(unittest.TestCase):
    def setUp(self):
        self.request = adapter.request()
        self.proof = {"guest_uuid": adapter.contract.UUID, "username": "partyopsuser", "standard_account": True,
            "session_id": 1, "ordinary_wps_registered": False, "installer_processes": [],
            "source": {"sha256": adapter.INSTALLER_SHA, "bytes": adapter.INSTALLER_SIZE, "signature_status": "Valid",
                "signer_is_kingsoft": True, "requested_execution_level": "asInvoker", "ui_access": "false", "manifest_sha256": "a" * 64}}

    def test_exact_source_and_original_user_precheck(self):
        adapter.validate_precheck(self.proof)
        for fault in ("uuid", "user", "admin", "no-desktop", "registered", "helper", "hash", "signature", "manifest", "uiAccess"):
            with self.subTest(fault=fault):
                value = copy.deepcopy(self.proof)
                if fault == "uuid": value["guest_uuid"] = "other"
                if fault == "user": value["username"] = "partyopsqa"
                if fault == "admin": value["standard_account"] = False
                if fault == "no-desktop": value["session_id"] = 0
                if fault == "registered": value["ordinary_wps_registered"] = True
                if fault == "helper": value["installer_processes"] = [{"pid": 42, "owner": "partyopsqa"}]
                if fault == "hash": value["source"]["sha256"] = "b" * 64
                if fault == "signature": value["source"]["signature_status"] = "NotSigned"
                if fault == "manifest": value["source"]["requested_execution_level"] = "requireAdministrator"
                if fault == "uiAccess": value["source"]["ui_access"] = "true"
                with self.assertRaises(RuntimeError): adapter.validate_precheck(value)

    def test_original_inner_silent_interactive_lua_no_bypass(self):
        launch = adapter.launch_script(self.request)
        user = adapter.user_script(self.request)
        self.assertEqual(self.request["installer"], adapter.INSTALLER)
        self.assertTrue(self.request["transport"].startswith(r"C:\Users\partyopsuser\AppData\Local\PartyOpsQa" + "\\"))
        for required in (".LogonType=3", ".RunLevel=0", "RegisterTaskDefinition($request.task,$definition,2", "'install.ps1'"):
            self.assertIn(required, launch)
        for required in ("$identity.User.Value -ne $sid", "$session -ne $desktop[0]", "-ArgumentList '/S'", "-WindowStyle Hidden",
                         "$process.WaitForExit()", "$process.ExitCode", "'started.json'", "'process.json'", "'result.json'",
                         "own_user_profile_scope", "wps_acceptance_passed=$false", "[IO.FileMode]::CreateNew"):
            self.assertIn(required, user)
        for forbidden in ("RunAsInvoker", "-Verb RunAs", "Set-Acl", "icacls", "Stop-Process", "Remove-Item", "uninst.exe", "personal.env", "partyops.env"):
            self.assertNotIn(forbidden, user + launch)

    def test_real_manifest_resource_proof_is_readonly(self):
        script = adapter.source_proof_script()
        for required in ("LoadLibraryEx($source.FullName,[IntPtr]::Zero,2)", "[IntPtr]1,[IntPtr]24", "requestedExecutionLevel",
                         "asInvoker", "uiAccess", "Get-AuthenticodeSignature", "Get-FileHash", "manifest_sha256"):
            self.assertIn(required, script)
        for forbidden in ("Start-Process", "WriteAllBytes", "Copy-Item", "Set-Content", "Set-Acl"):
            self.assertNotIn(forbidden, script)
        precheck = adapter.precheck_script(self.request)
        self.assertIn("installer_processes=$installers", precheck)
        self.assertIn("ordinary_wps_registered=", precheck)

    def test_start_requires_explicit_flag_and_existing_attempt_blocks_before_guest(self):
        with patch.object(sys, "argv", ["install-win10-user-wps-once.py", "start"]), patch.object(adapter, "load") as loader:
            with self.assertRaises(RuntimeError): adapter.main()
            loader.assert_not_called()
        with patch.object(sys, "argv", ["install-win10-user-wps-once.py", "start", "--execute-start"]), \
             patch.object(Path, "exists", return_value=True), patch.object(adapter, "load") as loader:
            with self.assertRaisesRegex(RuntimeError, "ALREADY_ATTEMPTED"): adapter.main()
            loader.assert_not_called()

    def test_powershell_parser_without_execution(self):
        parser = "$s=[Console]::In.ReadToEnd();$e=$null;$t=$null;[void][Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){$e|ForEach-Object {$_.Message};exit 1}"
        for script in (adapter.precheck_script(self.request), adapter.user_script(self.request),
                       adapter.launch_script(self.request), adapter.status_script(self.request)):
            result = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", parser],
                                    input=script, encoding="utf-8", capture_output=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
