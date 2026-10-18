"""新候选发布准备的离线契约测试；不执行Guest、gh或网络。"""
import copy
import io
import json
from pathlib import Path
import tempfile
import unittest
import sys
from types import SimpleNamespace
from unittest.mock import patch

import frozen_domain as f
import release_driver as d


class FrozenDomainTests(unittest.TestCase):
    def test_only_compilation_filename_is_normalized(self):
        source = "def nested(value=3):\n    return lambda: value + 7\n"
        a, b = compile(source, "old.py", "exec"), compile(source, "new.py", "exec")
        self.assertEqual(f.code_digest(a), f.code_digest(b))
        for changed in (source.replace("+ 7", "+ 8"), source.replace("value=3", "value=4"),
                        source.replace("+ 7", "* 7"), "\n" + source):
            self.assertNotEqual(f.code_digest(a), f.code_digest(compile(changed, "old.py", "exec")))

    def test_research_snapshot_does_not_offer_an_equivalence_approval(self):
        self.assertFalse(hasattr(f, "compare"))
        status = f.read(d.HERE / "historical-wps-domain-research-status.json")
        self.assertEqual(status["status"], "not-approved-for-evidence-reuse")



class PreparationTests(unittest.TestCase):
    def test_failed_host_recovery_requires_explicit_independent_round(self):
        from unittest.mock import MagicMock
        with tempfile.TemporaryDirectory(dir=d.HERE) as directory, patch.object(d, "QA", Path(directory)):
            root = Path(directory)
            self.assertEqual(d.host_round({}, object()), ("", [], []))
            (root / "host-journal-recovery-verified.json").write_text('{}', encoding="utf-8")
            recovered = SimpleNamespace(PREFIX="after-journal-", recovery=MagicMock(return_value={"launch_source": "host-journal-resume-launch"}))
            with patch.object(d, "load", return_value=recovered):
                prefix, names, diagnostics = d.host_round({"package": "new"}, object())
                self.assertEqual(prefix, "after-journal-")
                self.assertIn("first-host-approved-existing-root-submit.result.json", names)
                self.assertIn("host-journal-resume-launch.attempt.json", names)
                self.assertIn("host-journal-resume-launch.result.json", names)
                self.assertIn("host-journal-reopen.attempt.json", names)
                self.assertIn("host-journal-reopen-failed-script.ps1", diagnostics)
                recovered.recovery.side_effect = RuntimeError("RECOVERY_NOT_CONFIRMED")
                with self.assertRaisesRegex(RuntimeError, "RECOVERY_NOT_CONFIRMED"):
                    d.host_round({}, object())

    def test_qa_helper_bindings_restore_even_after_failure(self):
        old_path = sys.path[:]
        original_modules = {name: sys.modules.get(name) for name in ("controller", "reboot_evidence")}
        sentinel_controller, sentinel_reboot = object(), object()
        try:
            sys.modules["controller"], sys.modules["reboot_evidence"] = sentinel_controller, sentinel_reboot
            with self.assertRaisesRegex(RuntimeError, "synthetic-failure"):
                with d.qa_helpers() as (c, reboot):
                    self.assertIs(sys.modules["controller"], c)
                    self.assertIs(sys.modules["reboot_evidence"], reboot)
                    self.assertIs(reboot.c, c)
                    raise RuntimeError("synthetic-failure")
            self.assertEqual(sys.path, old_path)
            self.assertIs(sys.modules["controller"], sentinel_controller)
            self.assertIs(sys.modules["reboot_evidence"], sentinel_reboot)
        finally:
            for name, value in original_modules.items():
                if value is None: sys.modules.pop(name, None)
                else: sys.modules[name] = value

    def test_reboot_selection_preserves_standard_and_rejects_ambiguity(self):
        with tempfile.TemporaryDirectory(dir=d.HERE) as folder, patch.object(d, "QA", Path(folder)):
            root = Path(folder)
            with self.assertRaisesRegex(RuntimeError, "EXACTLY_ONE"):
                d.reboot_files()
            (root / "normal-reboot-completed.json").write_text('{}', encoding="utf-8")
            self.assertEqual(d.reboot_files(), ["normal-reboot-completed.json"])
            (root / "reboot-recovery-completed.json").write_text('{}', encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "EXACTLY_ONE"):
                d.reboot_files()

    def test_recovery_event_basename_is_checked_before_read(self):
        with tempfile.TemporaryDirectory(dir=d.HERE) as folder, patch.object(d, "QA", Path(folder)):
            root = Path(folder)
            receipt = root / "reboot-recovery-completed.json"
            for name in ("../private.json", str(root / "outside.json"), "reboot-recovery-events-xyz.json", None):
                receipt.write_text(json.dumps({"events_file": name}), encoding="utf-8")
                with self.subTest(name=name), self.assertRaisesRegex(RuntimeError, "BASENAME_REQUIRED"):
                    d.reboot_files()
            events = "reboot-recovery-events-123456abcdef.json"
            receipt.write_text(json.dumps({"events_file": events}), encoding="utf-8")
            (root / events).write_text('{}', encoding="utf-8")
            self.assertEqual(d.reboot_files(), ["reboot-recovery-completed.json", "recover-start.attempt.json", "recover-start.result.json", events])
            self.assertFalse((root / "normal-reboot-completed.json").exists())

    def test_recovery_branch_calls_shared_retention_validator(self):
        binding, report, state = {"package": "new"}, {"runtime": "real"}, {"uuid": "same"}
        rows = {"reboot-recovery-completed.json": {}, "retention-after-reboot.json": report}
        from unittest.mock import MagicMock
        reboot = SimpleNamespace(validate_retention=MagicMock())
        validator = SimpleNamespace(validate_restart=object())
        contract = SimpleNamespace(DISK=Path("D:/PartyOps-VM-Lab/vms/win10-x64/system.qcow2"))
        with patch.object(d, "load", return_value=validator), patch.object(d, "read", return_value=state):
            d.reboot_chain(rows, binding, {}, contract, reboot)
        reboot.validate_retention.assert_called_once_with(binding, report, state, validator.validate_restart, d.QA)

    def test_standard_reboot_keeps_original_gate(self):
        from unittest.mock import MagicMock
        reboot = SimpleNamespace(validate_retention=MagicMock())
        validator = SimpleNamespace(validate_restart=MagicMock())
        boot = {"boot_id": "before", "probe_boot_id": "before-probe"}
        restart = {"status": "passed", "actual": True, "qemu_pid_after": 9000,
                   "identity_before": {"boot_id": "before-probe"}, "identity_after": {"boot_id": "after-probe"}}
        rows = {"normal-reboot-completed.json": {"attempt_sha256": "hash", "boot_before_sha256": "hash", "restart": restart},
                "normal-reboot-attempt.json": {"context": "context", "overlay_sha256": "hash"},
                "retention-after-reboot.json": {"runtime": {"boot_id": "after", "probe_boot_id": "after-probe"}}}
        with patch.object(d, "digest", return_value="hash"), patch.object(d, "load", return_value=validator):
            d.reboot_chain(rows, {}, boot, None, reboot)
            validator.validate_restart.assert_called_once_with(restart, "context", {"pid": 9000})
            reboot.validate_retention.assert_not_called()
            for field, bad in (("actual", False), ("status", "failed")):
                altered = copy.deepcopy(rows)
                altered["normal-reboot-completed.json"]["restart"][field] = bad
                with self.subTest(field=field), self.assertRaisesRegex(RuntimeError, "ACTUAL_NEW_NORMAL_REBOOT_REQUIRED"):
                    d.reboot_chain(altered, {}, boot, None, reboot)

    def test_actual_controller_phase_links_and_independent_tamper_cases(self):
        import retention_contract as contract
        binding = {"package": {"sha256": "a" * 64}}
        sha = {"retention-boot-before.json": "b" * 64, "overlay-install/completed.json": "c" * 64,
               "first-host-prepare.attempt.json": "d" * 64}
        rows = {"retention-boot-before.json": {"boot_id": "os-before", "probe_boot_id": "probe-before"},
                "normal-reboot-attempt.json": {"context": contract.reboot_context(binding), "guest_uuid": contract.UUID,
                    "disk": str(contract.DISK.resolve()), "boot_before_sha256": sha["retention-boot-before.json"]},
                "retention-after-overlay.json": {"install_completed_sha256": sha["overlay-install/completed.json"],
                    "runtime": {"boot_id": "os-before", "probe_boot_id": "probe-before"}},
                "retention-after-reboot.json": {"install_completed_sha256": sha["overlay-install/completed.json"]},
                "first-host-prepare.result.json": {"status": "prepared", "attempt_sha256": sha["first-host-prepare.attempt.json"]}}
        d.phase_links(rows, binding, contract, sha)
        mutations = [
            ("normal-reboot-attempt.json", ("context",), {"candidate": binding}, "NEW_REBOOT_ATTEMPT_BINDING_REQUIRED"),
            ("normal-reboot-attempt.json", ("guest_uuid",), "different-guest", "NEW_REBOOT_ATTEMPT_BINDING_REQUIRED"),
            ("normal-reboot-attempt.json", ("disk",), "different-disk", "NEW_REBOOT_ATTEMPT_BINDING_REQUIRED"),
            ("normal-reboot-attempt.json", ("boot_before_sha256",), "0" * 64, "NEW_REBOOT_ATTEMPT_BINDING_REQUIRED"),
            ("retention-after-overlay.json", ("install_completed_sha256",), "0" * 64, "NEW_RETENTION_INSTALL_CHAIN_REQUIRED"),
            ("retention-after-reboot.json", ("install_completed_sha256",), "0" * 64, "NEW_RETENTION_INSTALL_CHAIN_REQUIRED"),
            ("retention-after-overlay.json", ("runtime", "boot_id"), "other-os-boot", "NEW_OVERLAY_BOOT_CHAIN_REQUIRED"),
            ("retention-after-overlay.json", ("runtime", "probe_boot_id"), "other-probe-boot", "NEW_OVERLAY_BOOT_CHAIN_REQUIRED"),
            ("first-host-prepare.result.json", ("status",), "failed", "NEW_HOST_PREPARE_CHAIN_REQUIRED"),
            ("first-host-prepare.result.json", ("attempt_sha256",), "0" * 64, "NEW_HOST_PREPARE_CHAIN_REQUIRED")]
        for filename, keys, value, code in mutations:
            changed = copy.deepcopy(rows); cursor = changed[filename]
            for key in keys[:-1]: cursor = cursor[key]
            cursor[keys[-1]] = value
            with self.subTest(filename=filename, keys=keys), self.assertRaisesRegex(RuntimeError, code):
                d.phase_links(changed, binding, contract, sha)

    def test_unknown_and_held_candidate_block_before_build_or_credentials(self):
        for sha, code in ((None, "EXPLICIT_ACTUAL_SHA_REQUIRED"), (f.OLD_SHA, "HELD_PREVIOUS_CANDIDATE_REJECTED")):
            with patch.object(d.domain, "official_payload") as payload:
                with self.assertRaisesRegex((RuntimeError, TypeError), code if sha else ".*"):
                    d.candidate(Path("missing.exe"), sha, Path("missing.json"), Path("gate.json"))
                payload.assert_not_called()

    def test_current_hold_and_previous_hold_remain_effective(self):
        with tempfile.TemporaryDirectory(dir=d.HERE) as folder:
            root = Path(folder)
            with patch.object(d, "HERE", root), patch.object(d, "read", return_value={"blocked_package_sha256": "a" * 64}):
                with self.assertRaisesRegex(RuntimeError, "HELD_CANDIDATE_REJECTED"): d.blocked_hold("a" * 64)
                (root / "PUBLICATION_HOLD.json").write_text("{}", encoding="utf-8")
                with self.assertRaisesRegex(RuntimeError, "CURRENT_BATCH_HELD"): d.blocked_hold("b" * 64)

    def test_missing_inputs_are_explicitly_blocked_without_writes(self):
        output = io.StringIO()
        with patch.object(d, "prepare") as prepare, patch("sys.stdout", output):
            self.assertEqual(d.main(["prepare"]), 2)
        prepare.assert_not_called()
        self.assertEqual(json.loads(output.getvalue())["status"], "blocked")

    def test_composition_preserves_all_nine_signatures_and_acceptance(self):
        assets = [{"filename": "PartyOps_1.4.5-rc.6_windows_amd64.exe", "signature": "old"}] + [
            {"filename": "platform-" + str(i), "signature": {"value": str(i)}} for i in range(9)]
        acceptance = [{"id": "windows_amd64"}] + [{"id": str(i), "historical": True} for i in range(9)]
        original = {"assets": assets, "acceptance": {"packages": acceptance}, "signature": "global-old"}
        checked = {"candidate": {"package": {"sha256": "a" * 64}}, "package": {"bytes": 123}, "source_fingerprint": "b" * 64,
                   "build": {"sha256": "c" * 64}, "gate": {"sha256": "d" * 64}, "guest_receipts": [],
                   "wps_evidence_sha256": "e" * 64, "evidence_sha256": "f" * 64}
        result = d.compose(original, checked, "synthetic")
        self.assertEqual(result["assets"][1:], assets[1:])
        self.assertEqual(result["acceptance"]["packages"][1:], acceptance[1:])
        self.assertEqual(original["signature"], "global-old")
        scope = result["acceptance"]["packages"][0]["targets"][0]
        self.assertFalse(scope["installed_uac_timeout_live_tested"])
        self.assertTrue(scope["new_package_wps_live_tested"])

    def test_preparation_never_overwrites_prior_attempt(self):
        with tempfile.TemporaryDirectory(dir=d.HERE) as folder:
            root = Path(folder); (root / "candidate-inputs.json").write_text("historical", encoding="utf-8")
            args = type("Args", (), {"package": "x", "sha256": "a", "build_receipt": "y", "gate": "z", "wps_summary": "s"})()
            with patch.object(d, "HERE", root), patch.object(d, "candidate", return_value={"candidate": {}}), \
                 patch.object(d, "guest_evidence", return_value=[]), patch.object(d, "wps_evidence", return_value={}), \
                 patch.object(d, "baseline", return_value={}):
                with self.assertRaisesRegex(RuntimeError, "PREPARATION_EXISTS_VERIFY_ONLY"): d.prepare(args)
            self.assertEqual((root / "candidate-inputs.json").read_text(), "historical")
            self.assertEqual(len(list(root.iterdir())), 1)

    def test_wps_requires_exact_new_candidate_and_no_historical_resume(self):
        with tempfile.TemporaryDirectory(dir=d.HERE.parent, prefix="win10-formatter-") as folder:
            path = Path(folder) / "summary.json"
            binding = {"package": {"sha256": "a" * 64}}
            for value in ({"candidate": {"package": {"sha256": f.OLD_SHA}}},
                          {"candidate": binding, "resume_sources": {"run": "old"}}):
                path.write_text(json.dumps(value), encoding="utf-8")
                with patch.object(d, "load") as load:
                    with self.assertRaisesRegex(RuntimeError, "NEW_CANDIDATE_FULL_WPS_RUN_REQUIRED"):
                        d.wps_evidence(binding, path)
                    load.assert_not_called()

    def test_new_receiver_refuses_held_or_unknown_candidate(self):
        receiver = d.load("test_new_receiver", d.HERE / "windows_receiver_factory.py")
        for binding in ({}, {"filename": d.NAME, "receiver": d.PROFILE, "formal_release_ready": True,
                              "publication_authorized": True, "package_sha256": f.OLD_SHA}):
            with self.assertRaisesRegex(RuntimeError, "NEW_APPROVED_RECEIVER_BINDING_REQUIRED"):
                receiver.build_receiver([], "a" * 64, binding)


if __name__ == "__main__":
    unittest.main()
