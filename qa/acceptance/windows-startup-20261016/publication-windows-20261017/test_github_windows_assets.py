"""Windows附件驱动的离线正负测试；不调用gh认证或网络。"""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import MagicMock, patch

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("windows_attachment_driver", HERE / "github_windows_assets.py")
g = importlib.util.module_from_spec(spec)
spec.loader.exec_module(g)


class WindowsAssetsTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory(prefix="github-assets-test-", dir=HERE)
        self.addCleanup(self.folder.cleanup)
        self.root = Path(self.folder.name)
        self.state = self.root / "state"
        self.expected = self.root / "expected.json"
        self.old = [{"id": i + 1, "name": f"old-{i}", "size": 100, "digest": "sha256:" + "a" * 64} for i in range(27)]
        names = [g.NAME, g.NAME + ".sha256", g.MANIFEST, g.GUIDE]
        self.assets = []
        for index, name in enumerate(names):
            path = self.root / name
            path.write_bytes(("synthetic-" + str(index)).encode())
            self.assets.append({"filename": name, "path": str(path), "bytes": path.stat().st_size, "sha256": g.digest(path)})
        self.payload = {"assets": self.assets, "old_assets": self.old, "package_sha256": g.SHA, "manifest_sha256": g.MANIFEST_SHA}
        g.exclusive(self.expected, self.payload)
        self.server = {"id": g.RELEASE_ID, "tag_name": g.TAG, "draft": False, "assets": copy.deepcopy(self.old)}

    def remote_asset(self, expected):
        return {"id": 100 + self.assets.index(expected), "name": expected["filename"], "size": expected["bytes"],
                "digest": "sha256:" + expected["sha256"], "state": "uploaded"}

    def test_prepare_is_local_and_refuses_changed_expected_payload(self):
        target = self.root / "new-expected.json"
        with patch.object(g, "EXPECTED", target), patch.object(g, "local_payload", return_value=self.payload), \
             patch.object(g.subprocess, "run", side_effect=AssertionError("no subprocess")), \
             patch.object(g.urllib.request, "build_opener", side_effect=AssertionError("no network")):
            self.assertFalse(g.prepare()["remote_called"])
            self.assertEqual(g.prepare()["asset_count"], 4)
            target.write_text("{}", encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "PREPARED_WINDOWS_ASSETS_CHANGED"): g.prepare()

    def test_domestic_requires_exact_current_candidate_and_real_full_readback(self):
        batch = self.root / "batch"; batch.mkdir()
        binding = {"filename": g.NAME, "bytes": g.SIZE, "package_sha256": g.SHA, "manifest_sha256": g.MANIFEST_SHA,
                   "formal_release_ready": True, "publication_authorized": True}
        result = {"filename": g.NAME, "bytes": g.SIZE, "sha256": g.SHA, "finalized": True, "public_readback_verified": True,
                  "url": g.BASE_URL + "/downloads/" + g.NAME}
        proof = {"binding": binding, "result": result, "other_nine_preserved": True, "old_windows_preserved": True,
                 "formal_release_ready": True, "completed_at": "synthetic"}
        (batch / "binding.json").write_text(json.dumps(binding), encoding="utf-8")
        with patch.object(g, "BATCH", batch), patch.object(g, "gh_api", side_effect=AssertionError("no API")):
            with self.assertRaisesRegex(RuntimeError, "CURRENT_WINDOWS_DOMESTIC_READBACK_REQUIRED"): g.domestic()
            path = batch / "windows-domestic-readback.json"
            for fault in (None, "sha", "manifest", "full-get", "old-asset"):
                changed = copy.deepcopy(proof)
                if fault == "sha": changed["result"]["sha256"] = "0" * 64
                if fault == "manifest": changed["binding"]["manifest_sha256"] = "0" * 64
                if fault == "full-get": changed["result"]["public_readback_verified"] = False
                if fault == "old-asset": changed["old_windows_preserved"] = False
                path.write_text(json.dumps(changed), encoding="utf-8")
                if fault:
                    with self.assertRaises(RuntimeError): g.domestic()
                else: self.assertEqual(g.domestic(), g.digest(path))

    def test_remote_pin_and_all_old_27_assets_are_enforced(self):
        for fault in (None, "tag", "release", "old-digest", "old-missing"):
            release = copy.deepcopy(self.server)
            tag = {"object": {"sha": g.TAG_SHA, "type": "commit"}}
            if fault == "tag": tag["object"]["sha"] = "0" * 40
            if fault == "release": release["id"] += 1
            if fault == "old-digest": release["assets"][0]["digest"] = "sha256:" + "0" * 64
            if fault == "old-missing": release["assets"].pop()
            with self.subTest(fault=fault), patch.object(g, "gh_api", side_effect=[release, tag]):
                if fault:
                    with self.assertRaises(RuntimeError): g.observe(self.payload)
                else: self.assertEqual(len(g.observe(self.payload)["assets"]), 27)

    def test_asset_conflicts_and_duplicates_are_never_clobbered(self):
        expected = self.assets[0]
        for fault in ("hash", "size", "state", "duplicate"):
            row = self.remote_asset(expected)
            if fault == "hash": row["digest"] = "sha256:" + "0" * 64
            if fault == "size": row["size"] += 1
            if fault == "state": row["state"] = "new"
            with self.assertRaises(RuntimeError):
                g.find_asset({"assets": [row, row] if fault == "duplicate" else [row]}, expected)
        row = self.remote_asset(expected); row["digest"] = None
        with patch.object(g, "public_hash", return_value={}) as public:
            self.assertIsNotNone(g.find_asset({"assets": [row]}, expected))
            public.assert_called_once_with(expected)

    def test_append_uploads_each_file_once_without_clobber_and_never_edits_release(self):
        commands = []
        def run(args, **kwargs):
            commands.append(args)
            expected = next(row for row in self.assets if row["path"] == args[-1])
            self.server["assets"].append(self.remote_asset(expected))
            return subprocess.CompletedProcess(args, 0, b"", b"")
        with patch.object(g, "checked", return_value=self.payload), patch.object(g, "domestic", return_value="b" * 64), \
             patch.object(g, "STATE", self.state), patch.object(g, "EXPECTED", self.expected), \
             patch.object(g, "observe", side_effect=lambda payload: copy.deepcopy(self.server)), patch.object(g.subprocess, "run", side_effect=run):
            with self.assertRaisesRegex(RuntimeError, "EXPLICIT_GITHUB_ATTACHMENT_UPLOAD_REQUIRED"): g.append()
            self.assertEqual(g.append(True)["status"], "four-assets-present-awaiting-anonymous-readback")
            self.assertEqual(len(commands), 4)
            g.append(True)
            self.assertEqual(len(commands), 4)
        for command in commands:
            self.assertEqual(command[:6], [str(g.GH), "release", "upload", g.TAG, "--repo", g.REPO])
            self.assertNotIn("--clobber", command)
        self.assertEqual(self.server["assets"][:27], self.old)

    def test_unknown_missing_upload_is_not_replayed_and_recovery_is_read_only(self):
        with patch.object(g, "checked", return_value=self.payload), patch.object(g, "domestic", return_value="b" * 64), \
             patch.object(g, "STATE", self.state), patch.object(g, "EXPECTED", self.expected), \
             patch.object(g, "observe", side_effect=lambda payload: copy.deepcopy(self.server)), patch.object(g.time, "sleep"), \
             patch.object(g.subprocess, "run", side_effect=subprocess.TimeoutExpired("gh", 1800)) as upload:
            for attempt in range(2):
                with self.assertRaisesRegex(RuntimeError, "GITHUB_UPLOAD_OUTCOME_UNKNOWN_READ_ONLY_RECOVERY_REQUIRED"): g.append(True)
            self.assertEqual(upload.call_count, 1)
            result = g.recover()
            self.assertEqual(result["status"], "partial")
            self.assertFalse(result["network_write_performed"])
            self.assertEqual(upload.call_count, 1)

    def test_readback_requires_complete_anonymous_get_of_all_four(self):
        self.server["assets"] += [self.remote_asset(row) for row in self.assets]
        def public(expected):
            return {"filename": expected["filename"], "bytes": expected["bytes"], "sha256": expected["sha256"], "public_readback_verified": True}
        with patch.object(g, "checked", return_value=self.payload), patch.object(g, "domestic", return_value="b" * 64), \
             patch.object(g, "STATE", self.state), patch.object(g, "EXPECTED", self.expected), \
             patch.object(g, "observe", side_effect=lambda payload: copy.deepcopy(self.server)), patch.object(g, "public_hash", side_effect=public) as full, \
             patch.object(g.subprocess, "run", side_effect=AssertionError("read only")):
            result = g.recover()
        proof = g.read(result["readback"])
        self.assertEqual(full.call_count, 4)
        self.assertTrue(proof["all_four_anonymous_full_get_verified"])
        self.assertEqual(len(proof["assets"]), 4)

    def test_anonymous_hash_rejects_truncation_and_does_not_send_auth(self):
        content = b"synthetic-public-file"
        expected = {"filename": g.NAME, "bytes": len(content), "sha256": hashlib.sha256(content).hexdigest()}
        for wrong in (False, True):
            response = MagicMock(); response.__enter__.return_value = response; response.status = 200
            response.read.side_effect = [content[:-1] if wrong else content, b""]
            opener = MagicMock(); opener.open.return_value = response
            with self.subTest(wrong=wrong), patch.object(g.urllib.request, "build_opener", return_value=opener):
                if wrong:
                    with self.assertRaisesRegex(RuntimeError, "ANONYMOUS_FULL_GET_SHA_MISMATCH"): g.public_hash(expected)
                else: self.assertTrue(g.public_hash(expected)["public_readback_verified"])
            request = opener.open.call_args.args[0]
            self.assertFalse(request.has_header("Authorization"))
            self.assertFalse(request.has_header("Cookie"))
            self.assertEqual(request.get_method(), "GET")


if __name__ == "__main__":
    unittest.main(verbosity=2)
