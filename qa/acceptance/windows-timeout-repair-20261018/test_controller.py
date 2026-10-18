"""离线正负测试：不连接 Guest、不安装、不重启。"""
import importlib.util
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
from unittest.mock import MagicMock, patch
from contextlib import ExitStack

spec = importlib.util.spec_from_file_location("timeout_controller", Path(__file__).with_name("controller.py"))
c = importlib.util.module_from_spec(spec)
spec.loader.exec_module(c)


class ControllerTests(unittest.TestCase):
    def test_original_baseline_and_backup_read_only(self):
        value = c.preserved_inputs()
        self.assertEqual(len(value["backup_sha256"]), 64)
        self.assertFalse((c.OUTPUT / "retention-baseline.json").exists())

    def test_previous_real_build_binding(self):
        value = c.previous_binding()
        self.assertEqual(value["package"]["sha256"], c.PREVIOUS_SHA)
        self.assertNotEqual(value["manifest_sha256"], c.contract.read(c.OLD / "retention-baseline.json")["installed_manifest_sha256"])

    def test_previous_tampered_receipt_rejected(self):
        with patch.object(c.contract, "digest", return_value="bad"):
            with self.assertRaisesRegex(RuntimeError, "PREVIOUS_OFFICIAL_RECEIPT_CHANGED"):
                c.previous_binding()

    def test_identity_requires_all_three_files(self):
        binding = {"manifest_sha256": "manifest", "executable_sha256": "exe", "wizard_sha256": "wizard"}
        c.identity_matches(binding, binding)
        for field in binding:
            with self.subTest(field=field):
                with self.assertRaisesRegex(RuntimeError, "INSTALLED_IDENTITY_MISMATCH"):
                    c.identity_matches({**binding, field: "different"}, binding)

    def test_exclusive_attempt_blocks_unknown_result(self):
        # 临时文件只在当前新目录中创建；测试结束正常清理。
        with TemporaryDirectory(dir=c.OUTPUT) as folder:
            path = Path(folder) / "attempt.json"
            c.no_attempt(path)
            c.contract.exclusive(path, {"status": "unknown"})
            with self.assertRaisesRegex(RuntimeError, "READ_ONLY_INSPECT_REQUIRED"):
                c.no_attempt(path)
            with self.assertRaises(FileExistsError):
                c.contract.exclusive(path, {})

    def test_boot_links_previous_and_original_baseline(self):
        inputs = {"baseline_sha256": "b", "backup_sha256": "k"}
        previous = {"manifest_sha256": "previous"}
        boot = {"guest_uuid": c.contract.UUID, "disk": str(c.contract.DISK.resolve()),
                "baseline_sha256": "b", "inputs": inputs, "previous": previous}
        with patch.object(c, "preserved_inputs", return_value=inputs), patch.object(c, "previous_binding", return_value=previous):
            with patch.object(c.contract, "read", return_value=boot):
                self.assertEqual(c.boot_inputs(), (inputs, boot))
            for field in ("guest_uuid", "disk", "baseline_sha256", "inputs", "previous"):
                with self.subTest(field=field), patch.object(c.contract, "read", return_value={**boot, field: "wrong"}):
                    with self.assertRaisesRegex(RuntimeError, "BOUND_CURRENT_START_REQUIRED"):
                        c.boot_inputs()

    def test_preflight_never_binds_guest(self):
        binding = {"package": {"sha256": "new"}}
        with patch.object(c, "preserved_inputs", return_value={}), patch.object(c, "previous_binding", return_value={}), \
             patch.object(c, "environment", return_value=(object(), object(), {})), \
             patch.object(c.contract, "candidate", return_value=binding), \
             patch.object(c.contract, "bind_guest", side_effect=AssertionError("Guest forbidden")):
            c.main(["preflight", "--package", "not-opened.exe", "--sha256", "new"])

    def test_reboot_requires_explicit_flag_before_environment(self):
        with patch.object(c, "environment", side_effect=AssertionError("environment forbidden")):
            with self.assertRaisesRegex(RuntimeError, "EXPLICIT_NORMAL_REBOOT_REQUIRED"):
                c.main(["normal-reboot", "--package", "not-opened.exe", "--sha256", "new"])

    def test_original_old_candidate_is_rejected(self):
        with patch.object(c, "preserved_inputs", return_value={}), patch.object(c, "previous_binding", return_value={}), \
             patch.object(c, "environment", return_value=(object(), object(), {})), \
             patch.object(c.contract, "candidate", return_value={"package": {"sha256": c.PREVIOUS_SHA}}):
            with self.assertRaisesRegex(RuntimeError, "NEW_CANDIDATE_REQUIRED"):
                c.main(["preflight", "--package", "not-opened.exe", "--sha256", c.PREVIOUS_SHA])

    def test_duplicate_mutations_never_connect(self):
        for action, filename, extra in (("install", "overlay-install/attempt.json", []),
                ("normal-reboot", "normal-reboot-attempt.json", ["--execute-normal-reboot"])):
            with self.subTest(action=action), TemporaryDirectory(dir=c.OUTPUT) as folder, ExitStack() as stack:
                output = Path(folder)
                path = output / filename
                path.parent.mkdir(exist_ok=True)
                path.write_text('{"status":"unknown"}', encoding="utf-8")
                stack.enter_context(patch.object(c, "OUTPUT", output))
                stack.enter_context(patch.object(c, "preserved_inputs", return_value={}))
                stack.enter_context(patch.object(c, "previous_binding", return_value={}))
                stack.enter_context(patch.object(c, "environment", return_value=(SimpleNamespace(qmp=None), object(), {})))
                stack.enter_context(patch.object(c.contract, "candidate", return_value={"package": {"sha256": "new"}}))
                stack.enter_context(patch.object(c.contract, "bind_guest", return_value={}))
                # module 为 object，没有 windows.connect；若越过防重试门就必然失败。
                with self.assertRaisesRegex(RuntimeError, "READ_ONLY_INSPECT_REQUIRED"):
                    c.main([action, "--package", "not-opened.exe", "--sha256", "new", *extra])

    def test_runtime_action_requires_same_new_candidate(self):
        for action in ("stop-current", "launch"):
            with self.subTest(action=action), ExitStack() as stack:
                stack.enter_context(patch.object(c, "preserved_inputs", return_value={}))
                stack.enter_context(patch.object(c, "previous_binding", return_value={}))
                stack.enter_context(patch.object(c, "environment", return_value=(SimpleNamespace(qmp=None), object(), {})))
                stack.enter_context(patch.object(c.contract, "candidate", return_value={"package": {"sha256": "new"}}))
                stack.enter_context(patch.object(c.contract, "bind_guest", return_value={}))
                stack.enter_context(patch.object(c, "boot_inputs", return_value=({}, {"candidate": "other"})))
                with self.assertRaisesRegex(RuntimeError, "START_BOUND_TO_DIFFERENT_CANDIDATE"):
                    c.main([action, "--package", "not-opened.exe", "--sha256", "new"])

    def test_install_invoked_once_and_second_call_blocked(self):
        import identity
        with TemporaryDirectory(dir=c.OUTPUT) as folder, ExitStack() as stack:
            output = Path(folder)
            (output / "retention-boot-before.json").write_text('{}', encoding="utf-8")
            previous = {"manifest_sha256": "m", "executable_sha256": "e", "wizard_sha256": "w"}
            binding = {"package": {"sha256": "new"}}
            boot = {"candidate": binding, "boot_id": "boot"}
            client = MagicMock()
            module = SimpleNamespace(qmp=None, windows=SimpleNamespace(connect=MagicMock(return_value=client),
                execute=MagicMock(return_value='{"products":[],"installer_count":0}')))
            install = MagicMock(return_value=({"uuid": c.contract.UUID}, {"exit_code": 0}))
            loader = lambda name, path: (SimpleNamespace(INVENTORY="read-only inventory")
                if name == "timeout_inventory" else SimpleNamespace(installer=SimpleNamespace(install_modern=install)))
            stack.enter_context(patch.object(c, "OUTPUT", output))
            stack.enter_context(patch.object(c, "preserved_inputs", return_value={}))
            stack.enter_context(patch.object(c, "previous_binding", return_value=previous))
            stack.enter_context(patch.object(c, "boot_inputs", return_value=({}, boot)))
            stack.enter_context(patch.object(c, "environment", return_value=(module, object(), {})))
            stack.enter_context(patch.object(c, "load", side_effect=loader))
            stack.enter_context(patch.object(c.contract, "candidate", return_value=binding))
            stack.enter_context(patch.object(c.contract, "bind_guest", return_value={}))
            stack.enter_context(patch.object(c.contract, "probe", return_value={**previous, "boot_id": "boot", "processes": [], "http_owners": []}))
            stack.enter_context(patch.object(c.contract, "digest", return_value="new"))
            stack.enter_context(patch.object(c.contract, "completed_install", return_value={}))
            stack.enter_context(patch.object(identity, "probe", return_value={"boot_id": "probe"}))
            args = ["install", "--package", "not-opened.exe", "--sha256", "new"]
            c.main(args)
            install.assert_called_once()
            self.assertTrue((output / "overlay-install/completed.json").exists())
            with self.assertRaisesRegex(RuntimeError, "READ_ONLY_INSPECT_REQUIRED"):
                c.main(args)
            install.assert_called_once()

    def test_reboot_invoked_once_and_second_call_blocked(self):
        import identity
        with TemporaryDirectory(dir=c.OUTPUT) as folder, ExitStack() as stack:
            output = Path(folder)
            binding = {"package": {"sha256": "new"}}
            c.contract.exclusive(output / "retention-after-overlay.json", {"status": "passed", "candidate": binding,
                "install_completed_sha256": "digest"})
            (output / "overlay-install").mkdir()
            c.contract.exclusive(output / "overlay-install/attempt.json", {"time_utc": "start"})
            reboot = MagicMock(return_value={"status": "passed"})
            client = MagicMock()
            module = SimpleNamespace(qmp=None, windows=SimpleNamespace(connect=MagicMock(return_value=client), execute=None),
                restart=SimpleNamespace(validate_restart=None))
            stack.enter_context(patch.object(c, "OUTPUT", output))
            stack.enter_context(patch.object(c, "preserved_inputs", return_value={}))
            stack.enter_context(patch.object(c, "previous_binding", return_value={}))
            stack.enter_context(patch.object(c, "boot_inputs", return_value=({}, {"candidate": binding, "boot_id": "boot", "probe_boot_id": "probe"})))
            stack.enter_context(patch.object(c, "environment", return_value=(module, object(), {})))
            stack.enter_context(patch.object(c.contract, "candidate", return_value=binding))
            stack.enter_context(patch.object(c.contract, "bind_guest", return_value={}))
            stack.enter_context(patch.object(c.contract, "probe", return_value={"boot_id": "boot"}))
            stack.enter_context(patch.object(c.contract, "runtime_matches", return_value={}))
            stack.enter_context(patch.object(c.contract, "digest", return_value="digest"))
            stack.enter_context(patch.object(c.contract, "completed_install", return_value={}))
            stack.enter_context(patch.object(c.contract, "reboot_once", reboot))
            stack.enter_context(patch.object(identity, "probe", return_value={"boot_id": "probe"}))
            args = ["normal-reboot", "--package", "not-opened.exe", "--sha256", "new", "--execute-normal-reboot"]
            c.main(args)
            reboot.assert_called_once()
            self.assertTrue((output / "normal-reboot-completed.json").exists())
            with self.assertRaisesRegex(RuntimeError, "READ_ONLY_INSPECT_REQUIRED"):
                c.main(args)
            reboot.assert_called_once()


if __name__ == "__main__":
    unittest.main()
