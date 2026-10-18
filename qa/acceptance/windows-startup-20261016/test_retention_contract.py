"""控制器正负契约测试；合成回执只验证门禁，不代表 Guest 通过。"""
import copy
import base64
import importlib.util
import json
import socket
import subprocess
import sys
import unittest
import uuid
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parents[2] / "qa/vm-lab"))
import retention_contract as c

spec = importlib.util.spec_from_file_location("tested_restart", HERE.parents[2] / "qa/vm-lab/scripts/reboot-windows.py")
restart = importlib.util.module_from_spec(spec)
spec.loader.exec_module(restart)


class Contracts(unittest.TestCase):
    def test_powershell_payload_and_build_wrapper_parse_without_execution(self):
        payload = next(v for v in c.probe.__code__.co_consts if isinstance(v, str) and "$ErrorActionPreference" in v)
        self.assertTrue(payload.startswith("$ErrorActionPreference"))
        for source in (payload, (HERE / "build-candidate.ps1").read_text(encoding="utf-8")):
            encoded = base64.b64encode(source.encode("utf-8")).decode("ascii")
            command = "$src=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "'));$errors=$null;[Management.Automation.Language.Parser]::ParseInput($src,[ref]$null,[ref]$errors)|Out-Null;if($errors.Count){$errors|ForEach-Object{$_.Message};exit 1}"
            parsed = subprocess.run(["powershell.exe", "-NoProfile", "-Command", command], capture_output=True, timeout=30)
            self.assertEqual(parsed.returncode, 0, parsed.stdout.decode(errors="replace") + parsed.stderr.decode(errors="replace"))

    def setUp(self):
        self.binding = {"package": {"sha256": "a" * 64}, "manifest_sha256": "b" * 64,
                        "executable_sha256": "c" * 64, "wizard_sha256": "d" * 64}
        self.actual = {**{k: v for k, v in self.binding.items() if k != "package"},
            "boot_id": "2026-10-17T04:00:00+00:00", "probe_boot_id": "new-boot",
            "processes": [{"pid": 42, "path": c.INSTALL + r"\PartyOps.exe", "created": "2026-10-17T04:01:00+00:00"}], "http_owners": [42]}
        self.vm = {"uuid": c.UUID, "disk": str(c.DISK), "qmp_port": 8606, "pid": 20}
        self.blocks = [{"inserted": {"ro": False, "file": str(c.DISK), "image": {"filename": str(c.DISK), "format": "qcow2"}}}]

    def qmp(self, port, command):
        return {"UUID": c.UUID} if command == "query-uuid" else self.blocks

    def test_guest_requires_actual_uuid_and_disk(self):
        lab = SimpleNamespace(state=lambda _: self.vm)
        self.assertEqual(c.bind_guest(lab, self.qmp), self.vm)
        for fault in ("registered-disk", "live-disk", "uuid", "additional-disk"):
            with self.subTest(fault=fault):
                vm, blocks = copy.deepcopy(self.vm), copy.deepcopy(self.blocks)
                if fault == "registered-disk": vm["disk"] = "D:/other.qcow2"
                if fault == "live-disk": blocks[0]["inserted"]["file"] = "D:/other.qcow2"
                if fault == "additional-disk": blocks += copy.deepcopy(blocks)
                def qmp(port, command):
                    return {"UUID": "wrong" if fault == "uuid" else c.UUID} if command == "query-uuid" else blocks
                with self.assertRaises(RuntimeError):
                    c.bind_guest(SimpleNamespace(state=lambda _: vm), qmp)

    def test_runtime_requires_candidate_http_process_and_fresh_start(self):
        c.runtime_matches(self.actual, self.binding)
        for fault in ("manifest_sha256", "executable_sha256", "wizard_sha256", "owner", "path", "old-process"):
            with self.subTest(fault=fault):
                actual = copy.deepcopy(self.actual)
                if fault.endswith("sha256"): actual[fault] = "e" * 64
                if fault == "owner": actual["http_owners"] = [99]
                if fault == "path": actual["processes"][0]["path"] = "C:/other/PartyOps.exe"
                if fault == "old-process": actual["processes"][0]["created"] = "2026-10-17T03:00:00+00:00"
                with self.assertRaises(RuntimeError): c.runtime_matches(actual, self.binding)

    def test_normal_reboot_requires_receipt_boot_change_and_owned_new_pid(self):
        before = {"guest_uuid": c.UUID, "disk": str(c.DISK.resolve()), "boot_id": "2026-10-17T03:00:00+00:00",
                  "probe_boot_id": "old-boot", "receipt_sha256": "before"}
        attempt = {"guest_uuid": c.UUID, "disk": str(c.DISK.resolve()), "candidate": self.binding,
                   "context": {"qa": "context"}, "boot_before_sha256": "before", "receipt_sha256": "attempt"}
        record = {"context": attempt["context"], "status": "passed", "actual": True, "qemu_pid_before": 10, "qemu_pid_after": 20,
            "identity_before": {"boot_id": "old-boot"}, "identity_after": {"boot_id": "new-boot"},
            "shutdown_event": {"event": "SHUTDOWN", "data": {"guest": True, "reason": "guest-shutdown"}}}
        receipt = {"candidate": self.binding, "attempt_sha256": "attempt", "boot_before_sha256": "before", "restart": record}
        c.validate_reboot(before, attempt, receipt, self.actual, self.binding, self.vm, restart.validate_restart)
        for fault in ("no-boot-change", "no-receipt-change", "forced-shutdown", "same-pid", "failed", "wrong-attempt", "different-candidate", "current-boot-mismatch"):
            with self.subTest(fault=fault):
                a, r = copy.deepcopy(self.actual), copy.deepcopy(receipt)
                if fault == "no-boot-change": a["boot_id"] = before["boot_id"]
                if fault == "no-receipt-change": r["restart"]["identity_after"]["boot_id"] = "old-boot"
                if fault == "forced-shutdown": r["restart"]["shutdown_event"]["data"]["guest"] = False
                if fault == "same-pid": r["restart"]["qemu_pid_before"] = 20
                if fault == "failed": r["restart"]["status"] = "failed"
                if fault == "wrong-attempt": r["attempt_sha256"] = "other"
                if fault == "different-candidate": r["candidate"] = {}
                if fault == "current-boot-mismatch": a["probe_boot_id"] = "other-boot"
                with self.assertRaises(RuntimeError):
                    c.validate_reboot(before, attempt, r, a, self.binding, self.vm, restart.validate_restart)

    def test_successful_install_requires_matching_exclusive_attempt(self):
        # 独立新目录，不调用 pytest，不轮换或删除历史临时目录。
        folder = HERE / "contract-test-runs" / uuid.uuid4().hex / "overlay-install"
        folder.mkdir(parents=True)
        c.exclusive(folder.parent / "retention-boot-before.json", {"synthetic": True})
        attempt = {"guest_uuid": c.UUID, "disk": str(c.DISK.resolve()), "candidate": self.binding,
                   "boot_before_sha256": c.digest(folder.parent / "retention-boot-before.json")}
        c.exclusive(folder / "attempt.json", attempt)
        completed = {"candidate": self.binding, "attempt_sha256": c.digest(folder / "attempt.json"), "status": "completed",
                     "identity": {"uuid": c.UUID}, "install": {"exit_code": 0, **{k: True for k in
                     ("app_exists", "launcher_exists", "formatter_exists", "uninstaller_exists", "install_log_exists")}}}
        with self.assertRaises(FileNotFoundError): c.completed_install(folder, self.binding)
        c.exclusive(folder / "completed.json", completed)
        c.completed_install(folder, self.binding)
        with self.assertRaises(FileExistsError): c.exclusive(folder / "attempt.json", attempt)
        for fault in ("status", "exit", "attempt", "candidate", "layout"):
            with self.subTest(fault=fault):
                value = copy.deepcopy(completed)
                if fault == "status": value["status"] = "failed"
                if fault == "exit": value["install"]["exit_code"] = 1
                if fault == "attempt": value["attempt_sha256"] = "other"
                if fault == "candidate": value["candidate"] = {}
                if fault == "layout": value["install"]["app_exists"] = False
                with patch.object(c, "read", side_effect=[attempt, value]):
                    with self.assertRaises(RuntimeError): c.completed_install(folder, self.binding)

    def test_official_package_manifest_mapping_is_required(self):
        from windows_build_payload import manifest_entries
        root = HERE / "contract-test-runs" / uuid.uuid4().hex
        artifacts = root / "artifacts"
        reports = root / "lab/reports/build"
        receipts = root / "lab/state/builds"
        for path in (artifacts, reports, receipts): path.mkdir(parents=True)
        package = artifacts / "PartyOps_1.4.5-rc.6_windows_amd64.exe"
        package.write_bytes(b"synthetic-installer-contract-only")
        log = reports / "build.log"
        log.write_text("synthetic successful build contract", encoding="utf-8")
        manifest = {"schema_version": 1, "product": "PartyOps", "version": "1.4.5-rc.6", "platform": "windows",
            "architecture": "amd64", "runtime_profile": "full", "files": [
            {"path": "PartyOps.exe", "size": 1, "sha256": "a" * 64},
            {"path": "PartyOpsWizard.exe", "size": 2, "sha256": "b" * 64}]}
        manifest_path = reports / "release-manifest.json"
        c.exclusive(manifest_path, manifest)
        pkg = {"id": "windows_amd64", "version": "1.4.5-rc.6", "path": str(package.resolve()), "sha256": c.digest(package)}
        payload = {"schema_version": 1, "capture": "controller-after-successful-build", "package_sha256": pkg["sha256"],
            "manifest": {"path": str(manifest_path.resolve()), "sha256": c.digest(manifest_path), "bytes": manifest_path.stat().st_size},
            "executables": {"PartyOps.exe": {"sha256": "a" * 64, "bytes": 1}, "PartyOpsWizard.exe": {"sha256": "b" * 64, "bytes": 2}},
            "manifest_file_count": len(manifest_entries(manifest, pkg))}
        record = {"package": pkg, "source_before": "source", "source_after": "source", "exit_code": 0,
            "output_created_during_build": True, "status": "completed", "errors": [],
            "log": {"path": str(log.resolve()), "sha256": c.digest(log)}, "windows_payload": payload}
        lab = SimpleNamespace(root=(root / "lab").resolve())
        with patch("lab.fingerprint", return_value="source"):
            with self.assertRaises(RuntimeError): c.candidate(lab, root, package, pkg["sha256"])
            c.exclusive(receipts / (pkg["sha256"] + ".json"), record)
            binding = c.candidate(lab, root, package, pkg["sha256"])
            self.assertEqual(binding["manifest_sha256"], c.digest(manifest_path))
            self.assertEqual(binding["executable_sha256"], "a" * 64)
            with self.assertRaises(RuntimeError): c.candidate(lab, root, package, "f" * 64)
            manifest_path.write_text("changed after receipt", encoding="utf-8")
            with self.assertRaises(RuntimeError): c.candidate(lab, root, package, pkg["sha256"])

    def test_shutdown_requires_real_normal_event_and_never_retries(self):
        for fault in (None, "event-before-ack", "forced", "eof", "timeout", "command-error"):
            with self.subTest(fault=fault):
                responses = [{"QMP": {}}, {"return": {}}, {"return": {}},
                    {"event": "SHUTDOWN", "data": {"guest": True, "reason": "guest-shutdown"}}]
                if fault == "forced": responses[-1]["data"]["reason"] = "host-qmp-quit"
                if fault == "eof": responses[-1] = None
                if fault == "command-error": responses[2] = {"error": {"class": "failed"}}
                if fault == "event-before-ack": responses[2], responses[3] = responses[3], responses[2]
                sent = []
                class Stream:
                    def readline(self):
                        if fault == "timeout" and len(responses) == 1: raise socket.timeout()
                        value = responses.pop(0)
                        return b"" if value is None else json.dumps(value).encode()
                    def write(self, value): sent.append(json.loads(value))
                    def flush(self): pass
                class Connection:
                    def __enter__(self): return self
                    def __exit__(self, *args): pass
                    def makefile(self, *args): return Stream()
                    def settimeout(self, value): pass
                with patch.object(c.socket, "create_connection", return_value=Connection()):
                    if fault and fault != "event-before-ack":
                        with self.assertRaises((RuntimeError, socket.timeout)): c.request_normal_shutdown(self.vm)
                    else:
                        self.assertEqual(c.request_normal_shutdown(self.vm)["event"], "SHUTDOWN")
                self.assertEqual(sum(v["execute"] == "system_powerdown" for v in sent), 1)

    def test_cold_start_rechecks_disk_and_does_not_start_after_shutdown_failure(self):
        import identity
        from unittest.mock import Mock
        original = {**self.vm, "acceleration": "whpx"}
        new = {**original, "pid": 21, "qmp_port": 9999}
        for fault in (None, "changed-disk", "shutdown-failed", "unchanged-boot"):
            with self.subTest(fault=fault):
                stopped = {**original, "pid": None}
                if fault == "changed-disk": stopped["disk"] = "D:/other.qcow2"
                lab = SimpleNamespace(state=lambda _: stopped, start=Mock())
                event = {"event": "SHUTDOWN", "data": {"guest": True, "reason": "guest-shutdown"}}
                with patch.object(c, "bind_guest", side_effect=[original, new]), \
                     patch.object(c, "request_normal_shutdown", side_effect=RuntimeError("failed") if fault == "shutdown-failed" else None, return_value=event) as shutdown, \
                     patch.object(identity, "probe", return_value={"boot_id": "old"}), \
                     patch.object(identity, "wait_stopped") as stopped_call, \
                     patch.object(identity, "wait_probe", return_value={"boot_id": "old" if fault == "unchanged-boot" else "new"}):
                    if fault:
                        with self.assertRaises(RuntimeError): c.reboot_once(lab, self.binding, self.qmp, restart.validate_restart)
                    else:
                        result = c.reboot_once(lab, self.binding, self.qmp, restart.validate_restart)
                        self.assertEqual(result["qemu_pid_after"], 21)
                        self.assertEqual(result["context"], c.reboot_context(self.binding))
                    shutdown.assert_called_once()
                    self.assertEqual(lab.start.call_count, 0 if fault in {"changed-disk", "shutdown-failed"} else 1)
                    if fault == "shutdown-failed": stopped_call.assert_not_called()


if __name__ == "__main__":
    unittest.main(verbosity=2)
