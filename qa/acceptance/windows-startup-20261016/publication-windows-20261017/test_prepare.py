"""本地发布准备契约；不上传、不连接 Guest，不产生通过标记。"""
import copy
import importlib.util
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("windows_publication_prepare", Path(__file__).with_name("prepare.py"))
p = importlib.util.module_from_spec(spec)
spec.loader.exec_module(p)


class PreparationContracts(unittest.TestCase):
    def setUp(self):
        names = [p.NAME] + ["retained-" + str(i) for i in range(9)]
        self.manifest = {"assets": [{"filename": n, "size": i + 1, "sha256": str(i) * 64, "signature": "preserve-" + n} for i, n in enumerate(names)],
            "acceptance": {"packages": [{"id": "windows_amd64" if i == 0 else "other-" + str(i), "filename": n, "scope": "historical"} for i, n in enumerate(names)]}}
        self.metadata = {"assets": [{"file_name": a["filename"], "bytes": a["size"], "sha256": a["sha256"]} for a in self.manifest["assets"]]}
        self.candidate = {"filename": p.NAME, "bytes": p.SIZE, "sha256": p.SHA}

    def test_only_windows_changes_and_other_nine_signatures_stay_equal(self):
        original = copy.deepcopy(self.manifest)
        plan = p.windows_only_plan(self.manifest, self.metadata, self.candidate)
        self.assertEqual(self.manifest, original)
        self.assertEqual(plan["retained_assets"], original["assets"][1:])
        self.assertEqual(plan["retained_acceptance"], original["acceptance"]["packages"][1:])
        self.assertFalse(plan["network_write_allowed"])
        self.assertFalse(plan["formal_release_ready"])
        self.assertFalse(plan["old_windows_withdrawal_allowed"])

    def test_mismatch_duplicate_old_package_and_incomplete_baseline_block(self):
        for fault in ("receiver-mismatch", "duplicate", "old-package", "missing-package"):
            with self.subTest(fault=fault):
                manifest, meta, candidate = copy.deepcopy(self.manifest), copy.deepcopy(self.metadata), copy.deepcopy(self.candidate)
                if fault == "receiver-mismatch": meta["assets"][1]["sha256"] = "f" * 64
                if fault == "duplicate": manifest["assets"][1]["filename"] = p.NAME
                if fault == "old-package": candidate["sha256"] = manifest["assets"][0]["sha256"]
                if fault == "missing-package": manifest["assets"].pop()
                with self.assertRaises(RuntimeError): p.windows_only_plan(manifest, meta, candidate)

    def test_actual_preflight_has_no_network_or_auth_subprocess(self):
        with patch("socket.create_connection", side_effect=AssertionError("no network")), \
             patch("urllib.request.urlopen", side_effect=AssertionError("no network")), \
             patch("subprocess.run", side_effect=AssertionError("no authentication command")):
            result = p.preflight()
        self.assertEqual(result["package"]["sha256"], p.SHA)
        self.assertEqual(result["published_baseline"]["sha256"], p.BASELINE_SHA)
        self.assertEqual(len(result["replacement_plan"]["retained_assets"]), 9)
        self.assertFalse(result["credentials_read"])
        self.assertFalse(result["network_write_allowed"])
        self.assertEqual(result["publication_status"], "blocked-pending-host-uac-and-wps-acceptance")


if __name__ == "__main__":
    unittest.main(verbosity=2)
