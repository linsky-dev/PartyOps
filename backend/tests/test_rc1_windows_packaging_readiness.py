"""The Windows packaging preflight reports truth, never fabricates installers."""

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))
spec = importlib.util.spec_from_file_location(
    "rc1_windows_packaging_readiness", SCRIPTS / "rc1_windows_packaging_readiness.py"
)
assert spec and spec.loader
preflight = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preflight)


class WindowsPackagingReadinessTests(unittest.TestCase):
    def test_clean_checkout_never_claims_wps_or_installer(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "backend").mkdir()
            (root / "backend/uv.lock").write_text(
                '[[package]]\nname = "pymupdf"\nversion = "1.26.0"\n', encoding="utf-8"
            )
            report = preflight.audit(root, "a" * 40)
            self.assertEqual(report["status"], "BLOCKED")
            self.assertFalse(report["installer_generated"])
            self.assertFalse(report["signed"])
            self.assertFalse(report["release_ready"])
            self.assertEqual(report["modern_x64"]["production_pymupdf"]["state"], "BLOCKED")
            self.assertIn("SECURE_PYMUPDF_PRODUCTION_LOCK_REQUIRED", report["blockers"])
            self.assertIn("WINDOWS_X64_REAL_WPS_GOLDEN_ACCEPTANCE_REQUIRED", report["blockers"])
            self.assertEqual(set(report["legacy"]), {"windows7-amd64", "windows7-x86"})

    def test_present_inputs_are_never_reported_verified(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "placeholder.exe").write_bytes(b"not a real installer")
            items = preflight.inspect_inputs(
                root, {"present": "placeholder.exe", "absent": "missing.exe"}
            )
            self.assertEqual(items["present"]["state"], "PRESENT_UNVERIFIED")
            self.assertEqual(items["absent"]["state"], "MISSING")

    def test_official_two_part_version_clears_only_the_version_floor(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "backend").mkdir()
            lock = root / "backend/uv.lock"
            lock.write_text('[[package]]\nname = "pymupdf"\nversion = "2.1"\n', encoding="utf-8")
            report = preflight.current_security_lock(root)
            self.assertEqual(report["state"], "VERSION_FLOOR_ONLY")
            self.assertEqual(report["version"], "2.1")
            self.assertNotEqual(report["state"], "PASS")

    def test_zero_native_inputs_cannot_be_promoted_by_version_change(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "backend").mkdir()
            (root / "backend/uv.lock").write_text(
                '[[package]]\nname = "pymupdf"\nversion = "2.1"\n', encoding="utf-8"
            )
            report = preflight.audit(root, "b" * 40)
            self.assertFalse(report["release_ready"])
            self.assertIn("WINDOWS_X64_REAL_WPS_GOLDEN_ACCEPTANCE_REQUIRED", report["blockers"])
            self.assertEqual(report["modern_x64"]["inputs"]["office_executable"]["state"], "MISSING")


if __name__ == "__main__":
    unittest.main()
