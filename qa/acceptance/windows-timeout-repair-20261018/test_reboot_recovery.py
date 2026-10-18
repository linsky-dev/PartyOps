"""本次磁盘门槛中断恢复的离线测试，不操作 VM。"""
import importlib.util
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import MagicMock, patch

import reboot_evidence as e
import copy

spec = importlib.util.spec_from_file_location("reboot_recovery", Path(__file__).with_name("reboot-recovery.py"))
r = importlib.util.module_from_spec(spec)
spec.loader.exec_module(r)


class RecoveryTests(unittest.TestCase):
    def test_stopped_guest_requires_dead_observed_pid(self):
        import psutil
        state = {"target": "win10-x64", "uuid": r.contract.UUID, "disk": str(r.contract.DISK),
                 "status": "stopped", "pid": None, "installation_media_detached": True, "seed": None}
        lab = MagicMock()
        lab.live.return_value = lab.process_active.return_value = False
        with patch.object(psutil, "pid_exists", return_value=False), patch.object(psutil, "process_iter", return_value=[]):
            r.stopped(lab, state, 2900)
            with self.assertRaisesRegex(RuntimeError, "PID_2900_REQUIRED"):
                r.stopped(lab, state, 999)
            with self.assertRaisesRegex(RuntimeError, "STOPPED_VM_REQUIRED"):
                r.stopped(lab, {**state, "pid": 2900}, 2900)
        with patch.object(psutil, "pid_exists", return_value=True):
            with self.assertRaisesRegex(RuntimeError, "PID_STILL_EXISTS"):
                r.stopped(lab, state, 2900)

    def test_start_once_then_repeat_blocked(self):
        with TemporaryDirectory(dir=r.OUTPUT) as folder, patch.object(r, "OUTPUT", Path(folder)):
            binding = {"package": {"sha256": "new"}}
            state = {"acceleration": "whpx"}
            lab = MagicMock()
            lab.state.return_value = state
            lab.start.return_value = {"uuid": r.contract.UUID, "disk": str(r.contract.DISK), "pid": 8000}
            with patch.object(r.c, "environment", return_value=(object(), lab, {})), \
                 patch.object(r.contract, "candidate", return_value=binding), \
                 patch.object(r.evidence, "inputs", return_value=({}, {}, {})), \
                 patch.object(r.contract, "digest", return_value="hash"), patch.object(r, "stopped"):
                args = ["recover-start", "--package", "not-opened.exe", "--sha256", "new", "--old-qemu-pid", "2900", "--execute"]
                r.main(args)
                lab.start.assert_called_once_with("win10-x64", "whpx", False)
                self.assertFalse((Path(folder) / "normal-reboot-completed.json").exists())
                with self.assertRaisesRegex(RuntimeError, "READ_ONLY_INSPECT_REQUIRED"):
                    r.main(args)
                lab.start.assert_called_once()
                lab.stop.assert_not_called()

    def test_start_failure_keeps_attempt_and_blocks_retry(self):
        with TemporaryDirectory(dir=r.OUTPUT) as folder, patch.object(r, "OUTPUT", Path(folder)):
            lab = MagicMock()
            lab.state.return_value = {"acceleration": "whpx"}
            lab.start.side_effect = RuntimeError("DISK_HEADROOM")
            with patch.object(r.c, "environment", return_value=(object(), lab, {})), \
                 patch.object(r.contract, "candidate", return_value={}), patch.object(r.evidence, "inputs", return_value=({}, {}, {})), \
                 patch.object(r.contract, "digest", return_value="hash"), patch.object(r, "stopped"):
                args = ["recover-start", "--package", "unused", "--sha256", "new", "--old-qemu-pid", "2900", "--execute"]
                with self.assertRaisesRegex(RuntimeError, "DISK_HEADROOM"):
                    r.main(args)
                self.assertTrue((Path(folder) / "recover-start.attempt.json").exists())
                self.assertFalse((Path(folder) / "recover-start.result.json").exists())
                with self.assertRaisesRegex(RuntimeError, "READ_ONLY_INSPECT_REQUIRED"):
                    r.main(args)
                lab.start.assert_called_once()

    def test_execute_is_required(self):
        with self.assertRaisesRegex(RuntimeError, "EXPLICIT_RECOVERY_EXECUTE_REQUIRED"):
            r.main(["recover-start", "--package", "unused", "--sha256", "new"])


class EventEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.boot = {"boot_id": "2026-10-18T04:00:00Z"}
        self.overlay = {"runtime": {"processes": [{"created": "2026-10-18T09:36:00Z"}]}}
        self.actual = {"guest_uuid": e.contract.UUID, "purpose": "disposable-qa", "boot_id": "2026-10-18T10:00:00Z"}
        self.events = [
            {"log": "System", "provider": "Microsoft-Windows-Kernel-General", "id": 13, "record_id": 100, "utc": "2026-10-18T09:37:00Z"},
            {"log": "System", "provider": "EventLog", "id": 6006, "record_id": 101, "utc": "2026-10-18T09:38:00Z"},
            {"log": "System", "provider": "Microsoft-Windows-Kernel-General", "id": 12, "record_id": 102, "utc": "2026-10-18T10:00:00Z"},
            {"log": "System", "provider": "EventLog", "id": 6005, "record_id": 103, "utc": "2026-10-18T10:00:02Z"}]

    def test_real_system_event_shape_without_user32_is_accepted(self):
        result = e.validate_events(self.events, self.boot, self.overlay, self.actual)
        self.assertFalse(result["qmp_shutdown_event_persisted"])
        self.assertFalse(result["user32_1074_observed"])

    def test_abnormal_41_and_6008_are_rejected(self):
        for identifier, provider in ((41, "Microsoft-Windows-Kernel-Power"), (6008, "EventLog")):
            with self.subTest(identifier=identifier), self.assertRaisesRegex(RuntimeError, "ABNORMAL_SHUTDOWN"):
                e.validate_events(self.events + [{"log": "System", "provider": provider, "id": identifier,
                    "record_id": 104, "utc": "2026-10-18T10:00:01Z"}], self.boot, self.overlay, self.actual)

    def test_missing_shutdown_pair_or_wrong_boot_rejected(self):
        for events in (self.events[1:], self.events[:1] + self.events[2:]):
            with self.assertRaises(RuntimeError):
                e.validate_events(events, self.boot, self.overlay, self.actual)
        with self.assertRaisesRegex(RuntimeError, "BOOT_CHANGE"):
            e.validate_events(self.events, self.boot, self.overlay, {**self.actual, "boot_id": self.boot["boot_id"]})

    def test_event_source_record_order_and_repeated_boot_rejected(self):
        for change in ({"provider": "unrelated"}, {"record_id": 100}, {"log": "Application"},
                       {"utc": "2026-10-18T09:35:00Z"}, {"utc": "2026-10-18T10:10:00Z"}):
            events = copy.deepcopy(self.events)
            events[2].update(change)
            with self.subTest(change=change), self.assertRaises(RuntimeError):
                e.validate_events(events, self.boot, self.overlay, self.actual)
        with self.assertRaisesRegex(RuntimeError, "SINGLE_KERNEL_START"):
            e.validate_events(self.events + [{**self.events[2], "record_id": 104}], self.boot, self.overlay, self.actual)

    def test_evidence_script_omits_sensitive_event_content(self):
        script = r.event_script("2026-10-18T09:36:00Z")
        self.assertIn("Get-WinEvent", script)
        self.assertNotIn("$_.Message", script)
        self.assertNotIn("$_.Properties", script)
        self.assertNotIn("system_powerdown", script)

    def test_actual_delayed_kernel_records_follow_utc_not_record_id(self):
        # 来自保留的 reboot-recovery-events-82c20dedd363.json；只列公开系统事件元数据。
        rows = [(2433, "Microsoft-Windows-Kernel-General", 12, "2026-10-18T04:00:05.8017188Z"),
                (2450, "Microsoft-Windows-Kernel-Power", 41, "2026-10-18T04:00:15.8525026Z"),
                (2468, "EventLog", 6008, "2026-10-18T04:00:35.8899992Z"),
                (2470, "EventLog", 6005, "2026-10-18T04:00:35.8899992Z"),
                (2488, "User32", 1074, "2026-10-18T09:37:26.6574690Z"),
                (2490, "EventLog", 6006, "2026-10-18T09:37:27.5804044Z"),
                (2499, "EventLog", 6005, "2026-10-18T09:43:55.5791037Z"),
                (2502, "Microsoft-Windows-Kernel-General", 13, "2026-10-18T09:37:31.2291748Z"),
                (2503, "Microsoft-Windows-Kernel-General", 12, "2026-10-18T09:43:26.8211984Z")]
        events = [{"record_id": record, "provider": provider, "id": identifier, "utc": utc, "log": "System"}
                  for record, provider, identifier, utc in rows]
        actual = {**self.actual, "boot_id": "2026-10-18T09:43:26.5000000Z"}
        result = e.validate_events(events, self.boot, self.overlay, actual)
        self.assertTrue(result["user32_1074_observed"])
        self.assertFalse(result["abnormal_41_6008_observed"])
        # 历史04:00异常被UTC作用域排除；本次启动异常仍必须阻断。
        with self.assertRaisesRegex(RuntimeError, "ABNORMAL_SHUTDOWN"):
            e.validate_events(events + [{"record_id": 2504, "provider": "EventLog", "id": 6008,
                "utc": "2026-10-18T09:43:56Z", "log": "System"}], self.boot, self.overlay, actual)
        altered = copy.deepcopy(events)
        altered[6]["utc"] = "2026-10-18T09:43:26.6Z"
        with self.assertRaisesRegex(RuntimeError, "ORDERED_SYSTEM_LOG"):
            e.validate_events(altered, self.boot, self.overlay, actual)


class ReceiptChainTests(unittest.TestCase):
    setUp = EventEvidenceTests.setUp

    def test_complete_recovery_chain_and_tamper_rejection(self):
        with TemporaryDirectory(dir=r.OUTPUT) as directory, patch.object(e.c, "preserved_inputs", return_value={"baseline_sha256": "baseline", "backup_sha256": "backup"}), \
             patch.object(e.contract, "completed_install", return_value={}):
            root = Path(directory)
            binding = {"manifest_sha256": "m", "executable_sha256": "e", "wizard_sha256": "w"}
            raw_inputs = e.c.preserved_inputs()
            boot = {**self.boot, "guest_uuid": e.contract.UUID, "disk": str(e.contract.DISK.resolve()), "candidate": binding,
                    "inputs": raw_inputs, "baseline_sha256": "baseline", "probe_boot_id": "old-probe"}
            (root / "overlay-install").mkdir()
            e.contract.exclusive(root / "overlay-install/completed.json", {})
            overlay = {**self.overlay, "candidate": binding, "inputs": raw_inputs, "status": "passed",
                "runtime": {**self.overlay["runtime"], "boot_id": boot["boot_id"], "probe_boot_id": "old-probe"},
                "install_completed_sha256": e.contract.digest(root / "overlay-install/completed.json")}
            e.contract.exclusive(root / "retention-boot-before.json", boot)
            e.contract.exclusive(root / "retention-after-overlay.json", overlay)
            normal = {"guest_uuid": e.contract.UUID, "disk": str(e.contract.DISK.resolve()), "candidate": binding,
                "context": e.contract.reboot_context(binding), "operation": "normal-shutdown-start-once",
                "boot_before_sha256": e.contract.digest(root / "retention-boot-before.json"),
                "overlay_sha256": e.contract.digest(root / "retention-after-overlay.json")}
            e.contract.exclusive(root / "normal-reboot-attempt.json", normal)
            start = {"candidate": binding, "guest_uuid": e.contract.UUID, "disk": str(e.contract.DISK.resolve()),
                "normal_attempt_sha256": e.contract.digest(root / "normal-reboot-attempt.json"),
                "overlay_sha256": normal["overlay_sha256"], "boot_before_sha256": normal["boot_before_sha256"],
                "operation": "resume-interrupted-normal-reboot-start-only-once", "old_qemu_pid": 2900,
                "old_pid_absent": True, "no_bound_qemu_process": True, "qmp_unreachable": True}
            e.contract.exclusive(root / "recover-start.attempt.json", start)
            e.contract.exclusive(root / "recover-start.result.json", {"candidate": binding, "status": "started", "start_count": 1,
                "attempt_sha256": e.contract.digest(root / "recover-start.attempt.json"), "new_qemu_pid": 8000})
            actual = {**self.actual, **binding, "probe_boot_id": "new-probe", "processes": [{"pid": 7,
                "path": e.contract.INSTALL + r"\PartyOps.exe", "created": "2026-10-18T10:00:01Z"}], "http_owners": [7]}
            captured = {"events": self.events, "candidate": binding, "runtime": actual, "source": "actual-Windows-System-event-log"}
            e.contract.exclusive(root / "reboot-recovery-events-test.json", captured)
            receipt = {"candidate": binding, "guest_uuid": e.contract.UUID, "disk": str(e.contract.DISK.resolve()),
                "normal_attempt_sha256": e.contract.digest(root / "normal-reboot-attempt.json"), "status": "passed",
                "kind": "windows-system-event-recovery", "qmp_shutdown_event_persisted": False,
                "recover_start_attempt_sha256": e.contract.digest(root / "recover-start.attempt.json"),
                "recover_start_result_sha256": e.contract.digest(root / "recover-start.result.json"),
                "actual_boot_id": actual["boot_id"], "probe_boot_id": actual["probe_boot_id"],
                "events_file": "reboot-recovery-events-test.json", "events_sha256": e.contract.digest(root / "reboot-recovery-events-test.json"),
                "event_validation": e.validate_events(self.events, boot, overlay, actual)}
            e.contract.exclusive(root / "reboot-recovery-completed.json", receipt)
            state = {"uuid": e.contract.UUID, "disk": str(e.contract.DISK.resolve()), "pid": 8000}
            link = e.validate_recovery(binding, actual, state, root)
            self.assertEqual(link["kind"], "windows-system-event-recovery")
            for field, value in (("candidate", "old"), ("qmp_shutdown_event_persisted", True),
                    ("normal_attempt_sha256", "wrong"), ("recover_start_result_sha256", "wrong"),
                    ("events_sha256", "wrong"), ("actual_boot_id", boot["boot_id"])):
                with self.subTest(field=field), patch.object(e.contract, "read", wraps=e.contract.read) as reader:
                    original_read = reader._mock_wraps
                    reader.side_effect = lambda path, f=field, v=value: {**receipt, f: v} if Path(path).name == "reboot-recovery-completed.json" else original_read(path)
                    with self.assertRaises(RuntimeError):
                        e.validate_recovery(binding, actual, state, root)
            for change in ({"candidate": "old"}, {"source": "source-test"},
                    {"runtime": {**actual, "guest_uuid": "other"}}, {"runtime": {**actual, "purpose": "other"}},
                    {"runtime": {**actual, "boot_id": boot["boot_id"]}}, {"runtime": {**actual, "probe_boot_id": "old"}},
                    *({"runtime": {**actual, key: "other"}} for key in binding)):
                with self.subTest(capture=change), patch.object(e.contract, "read", wraps=e.contract.read) as reader:
                    original_read = reader._mock_wraps
                    reader.side_effect = lambda path, update=change: {**captured, **update} if Path(path).name == "reboot-recovery-events-test.json" else original_read(path)
                    with self.assertRaises(RuntimeError):
                        e.validate_recovery(binding, actual, state, root)
            # 采集PID与后续实际PID不同仍可通过，各自的身份/监听归属都必须有效。
            sampled = {**actual, "processes": [{**actual["processes"][0], "pid": 8}], "http_owners": [8]}
            with patch.object(e.contract, "read", wraps=e.contract.read) as reader:
                original_read = reader._mock_wraps
                reader.side_effect = lambda path: {**captured, "runtime": sampled} if Path(path).name == "reboot-recovery-events-test.json" else original_read(path)
                e.validate_recovery(binding, actual, state, root)

    def test_retention_report_must_link_selected_reboot_receipt(self):
        binding = {"manifest_sha256": "new"}
        report = {"status": "passed", "candidate": binding, "guest_uuid": e.contract.UUID,
            "disk": str(e.contract.DISK.resolve()), "inputs": {"baseline_sha256": "b"},
            "runtime": {"boot_id": "new"}, "install_completed_sha256": "install", "reboot_evidence": {"kind": "recovery", "sha256": "receipt"}}
        with patch.object(e.c, "preserved_inputs", return_value=report["inputs"]), \
             patch.object(e.contract, "digest", return_value="install"), \
             patch.object(e, "validate", return_value=report["reboot_evidence"]):
            self.assertEqual(e.validate_retention(binding, report, {}, None), report["reboot_evidence"])
            with self.assertRaisesRegex(RuntimeError, "LINK_CHANGED"):
                e.validate_retention(binding, {**report, "reboot_evidence": {"sha256": "forged"}}, {}, None)


if __name__ == "__main__":
    unittest.main()
