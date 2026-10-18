"""Windows独立发布的离线契约；不读凭据、不创建令牌、不发网络请求。"""
import copy
import importlib.util
import subprocess
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from contextlib import ExitStack
from unittest.mock import patch, MagicMock

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
spec = importlib.util.spec_from_file_location("windows_publication_driver", HERE / "publication.py")
w = importlib.util.module_from_spec(spec); spec.loader.exec_module(w)


class PublicationContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls): cls.baseline, cls.retained = w.inputs()

    def formatter_resume_fixture(self):
        # 纯内存合成证据，不创建通过回执、发布索引或签名。
        path = HERE.parent / "win10-formatter-ffffffffffff/summary.json"
        sources = {"run": "win10-formatter-aaaaaaaaaaaa", "summary_sha256": "1" * 64, "supplements": []}
        old_proofs = [{"feature": "format" if i < 2 else "convert", "host_display_name": "WPS",
                       "input_sha256": f"{i:064x}", "request_sha256": f"{i + 10:064x}", "response_sha256": f"{i + 20:064x}"} for i in range(5)]
        native_font = {"feature": "convert", "host_display_name": "WPS", "input_sha256": "e" * 64,
                       "request_sha256": "f" * 64, "response_sha256": "a" * 64,
                       "outputs": [{"sha256": "d" * 64, "bytes": 10, "extension": ".pdf"}]}
        rows = [{"font": name, "synthetic_marker_drawn": True, "visible_trace_count": 1, "actual_pdf_fonts": [name]}
                for name in ("宋体", "黑体", "仿宋_GB2312", "楷体_GB2312")]
        rows.append({"font": "方正小标宋简体", "synthetic_marker_drawn": True,
                     "actual_font_source": "pinned-ttf-independent-glyph-raster-match", "reference_os2_fs_type": 2})
        inherited = [{"id": "feature-native-source-host", "result": {"status": "passed"}, "proofs": {}},
                     {"id": "six-feature-http-output-contracts", "status": "passed", "cases": 9, "image_decode": "passed"},
                     {"id": "format-and-pdf-golden", "status": "passed", "host_proofs": old_proofs,
                      "visual_comparisons": [{"passed": True}, {"passed": True}]}]
        value = {"scope": "original-win10-x64-installed-http-formatter", "status": "passed", "formatter_acceptance_passed": True,
                 "executable_checks_status": "passed", "font_render_use": "passed", "six_feature_native_host_proof": "passed",
                 "completed_at": "synthetic", "candidate": {"package": {"sha256": w.receiver.SHA}, "manifest_sha256": "b" * 64},
                 "native_host_sha256": "c" * 64, "resume_sources": copy.deepcopy(sources),
                 "checks": [{"id": "installed-wps-x64-font-files", "result": {}}, *copy.deepcopy(inherited),
                            {"id": "five-font-visible-pdf-glyphs", "status": "passed", "fonts": copy.deepcopy(rows),
                             "input_sha256": "e" * 64, "pdf_sha256": "d" * 64, "native_host_proofs": [copy.deepcopy(native_font)]}]}
        features = {"cases": [{"status": "passed", "job": {"state": "completed"},
                               "outputs": [{"filename": f"feature-{i}.docx", "sha256": "d" * 64}]} for i in range(9)]}
        job = {"id": "2" * 32, "state": "completed", "feature": "convert", "host_proofs": [copy.deepcopy(native_font)],
               "outputs": [{"file": "five-font-witness.pdf", "sha256": "d" * 64}]}
        formatter = SimpleNamespace(ROOT=w.preparation.ROOT, FONT_WITNESSES=[(r["font"],) for r in rows],
                    validate_wps=MagicMock(), verify_native_cases=MagicMock(return_value={"status": "passed"}),
                    font_render_proof=MagicMock(return_value=rows), load=MagicMock(return_value=SimpleNamespace(oracle=object())),
                    resume_font_evidence=MagicMock(return_value=(inherited, sources)))
        payload = HERE.parent / "synthetic-payload.json"
        def read(source):
            if Path(source) == w.preparation.BUILD: return {"windows_payload": {"manifest": {"path": str(payload)}}}
            if Path(source) == payload: return {"files": [{"path": "formatter-host/PartyOps.DocumentFormatter.Host.exe", "sha256": "c" * 64}]}
            if Path(source).name == "installed-features-evidence.json": return features
            if Path(source).name == "job-" + job["id"] + ".json": return job
            raise AssertionError("unexpected synthetic read")
        def digest(source):
            if Path(source) == payload: return "b" * 64
            return "e" * 64 if Path(source).name == "five-font-witness.docx" else "d" * 64
        return path, value, formatter, read, digest, job

    def test_resume_formatter_independently_rechecks_sources_raster_and_native_downloads(self):
        for fault in (None, "source-digest", "copied-flags", "forged-raster", "native-hash", "fake-old-job", "source-recheck-fails"):
            path, value, formatter, read, digest, job = self.formatter_resume_fixture()
            job_paths = [path.parent / ("job-" + job["id"] + ".json")]
            if fault == "source-digest": value["resume_sources"]["summary_sha256"] = "0" * 64
            if fault == "copied-flags": value["checks"][1]["result"] = {"status": "passed", "forged": True}
            if fault == "forged-raster": value["checks"][-1]["fonts"][-1]["visible_trace_count"] = 1
            if fault == "native-hash": job["host_proofs"][0]["outputs"][0]["sha256"] = "0" * 64
            if fault == "fake-old-job": job_paths *= 3
            if fault == "source-recheck-fails": formatter.resume_font_evidence.side_effect = RuntimeError("FONT_RESUME_GOLDEN_DOWNLOAD_CHANGED")
            with self.subTest(fault=fault), ExitStack() as stack:
                stack.enter_context(patch.object(w, "qa_driver", return_value=formatter))
                stack.enter_context(patch.object(w.p, "read", side_effect=read))
                stack.enter_context(patch.object(w.p, "digest", side_effect=digest))
                stack.enter_context(patch.object(Path, "resolve", lambda self, **kwargs: self))
                stack.enter_context(patch.object(Path, "stat", return_value=SimpleNamespace(st_size=10)))
                stack.enter_context(patch.object(Path, "glob", return_value=job_paths))
                if fault:
                    with self.assertRaises(RuntimeError): w.formatter_evidence(path, value)
                else:
                    result = w.formatter_evidence(path, value)
                    self.assertEqual(result["resume_sources"], value["resume_sources"])
                    formatter.resume_font_evidence.assert_called_once()
                    formatter.font_render_proof.assert_called_once_with(path.parent / "five-font-witness.pdf")
                    self.assertNotIn("visible_trace_count", value["checks"][-1]["fonts"][-1])

    def test_full_formatter_still_requires_all_three_bound_local_jobs(self):
        for missing in (False, True):
            path, value, formatter, original_read, digest, font_job = self.formatter_resume_fixture()
            value.pop("resume_sources")
            golden = value["checks"][3]["host_proofs"]
            files = ["formatted-1.docx", "formatted-2.docx", "render-0.pdf", "render-1.pdf", "render-2.pdf"]
            for name, proof in zip(files, golden):
                proof["outputs"] = [{"sha256": "d" * 64, "bytes": 10, "extension": Path(name).suffix}]
            jobs = {"job-" + font_job["id"] + ".json": font_job}
            for index, (names, proofs, feature) in enumerate(((files[:2], golden[:2], "format"), (files[2:], golden[2:], "convert"))):
                identifier = f"{index + 3:032x}"
                jobs["job-" + identifier + ".json"] = {"id": identifier, "state": "completed", "feature": feature,
                    "host_proofs": proofs, "outputs": [{"file": name, "sha256": "d" * 64} for name in names]}
            job_paths = [path.parent / name for name in jobs]
            if missing: job_paths.pop()
            with self.subTest(missing=missing), ExitStack() as stack:
                stack.enter_context(patch.object(w, "qa_driver", return_value=formatter))
                stack.enter_context(patch.object(w.p, "read", side_effect=lambda p: jobs[Path(p).name] if Path(p).name in jobs else original_read(p)))
                stack.enter_context(patch.object(w.p, "digest", side_effect=digest))
                stack.enter_context(patch.object(Path, "resolve", lambda self, **kwargs: self))
                stack.enter_context(patch.object(Path, "stat", return_value=SimpleNamespace(st_size=10)))
                stack.enter_context(patch.object(Path, "glob", return_value=job_paths))
                if missing:
                    with self.assertRaisesRegex(RuntimeError, "GOLDEN_DOWNLOADED_JOB_EVIDENCE_REQUIRED"):
                        w.formatter_evidence(path, value)
                else:
                    w.formatter_evidence(path, value)
                    formatter.resume_font_evidence.assert_not_called()

    def test_blocked_draft_preserves_other_nine_asset_signatures_and_acceptance(self):
        original = copy.deepcopy(self.baseline)
        value = w.compose(self.baseline, generated_at="2026-10-17T00:00:00Z")
        w.validate_manifest(value, self.baseline, None)
        self.assertEqual(self.baseline, original)
        self.assertFalse(value["publication_authorized"])
        self.assertFalse(value["formal_release_ready"])
        self.assertEqual(value["formal_gate"], "FULL_FUNCTION_GATE_PASSED")
        self.assertEqual(value["formal_gate_evidence"]["status"], "passed")
        self.assertEqual(value["publication_authorization"], "existing-user-authorization-after-real-acceptance")
        self.assertEqual(len(value["assets"]), 10)
        self.assertEqual(len([a for a in value["assets"] if a["filename"] == w.receiver.NAME]), 1)
        self.assertNotIn("signature", value)
        for mutation in ("other-asset", "other-acceptance", "formal-gate", "authorization", "candidate-sha"):
            changed = copy.deepcopy(value)
            if mutation == "other-asset": next(a for a in changed["assets"] if a["filename"] != w.receiver.NAME)["sha256"] = "a" * 64
            if mutation == "other-acceptance": next(a for a in changed["acceptance"]["packages"] if a["id"] != "windows_amd64")["full_lifecycle_verified"] = True
            if mutation == "formal-gate": changed["formal_release_ready"] = True
            if mutation == "authorization": changed["publication_authorized"] = True
            if mutation == "candidate-sha": next(a for a in changed["assets"] if a["filename"] == w.receiver.NAME)["sha256"] = "a" * 64
            with self.assertRaises(RuntimeError): w.validate_manifest(changed, self.baseline, None)

    def test_receiver_has_independent_profile_and_only_new_name_can_receive_posts(self):
        script = w.receiver.build_receiver(self.retained, "b" * 64)
        self.assertIn(w.receiver.PROFILE, script)
        self.assertIn(w.preparation.BASELINE_SHA, script)
        self.assertIn(".partyops-windows-startup-20261017-", script)
        self.assertIn("file.file_name !== candidate.file_name", script)
        self.assertIn("names.slice(0,10)", script)
        self.assertIn('meta.manifest_sha256 !== "' + "b" * 64 + '"', script)
        self.assertNotIn("kylin-full-replace-v1", script)
        self.assertNotIn(".partyops-kylin-full-20261016-", script)
        # Node只解析源码，不启动服务器或访问网络。
        parsed = subprocess.run(["node", "--check", "-"], input=script, encoding="utf-8", capture_output=True)
        self.assertEqual(parsed.returncode, 0, parsed.stderr)
        for rows, sha in ((self.retained[:-1], "b" * 64), (self.retained, "unsafe")):
            with self.assertRaises(RuntimeError): w.receiver.build_receiver(rows, sha)

    def test_blocked_paths_stop_before_auth_client_token_or_network(self):
        with patch.object(w.p, "release_client", side_effect=AssertionError("auth client must not load")) as client, \
             patch.object(w.secrets, "token_hex", side_effect=AssertionError("no token creation")), \
             patch("socket.create_connection", side_effect=AssertionError("no network")):
            with self.assertRaisesRegex(RuntimeError, "BLOCKED_PENDING_REAL_HOST_WPS_ACCEPTANCE"): w.upload(None, True)
            with self.assertRaisesRegex(RuntimeError, "BLOCKED_PENDING_REAL_HOST_WPS_ACCEPTANCE"): w.read_only_recover(True, None)
            with self.assertRaisesRegex(RuntimeError, "BLOCKED_PENDING_REAL_HOST_WPS_ACCEPTANCE"): w.prepare_bundle(Path("not-read.json"), None)
            with self.assertRaises(RuntimeError): w.upload(None, False)
            client.assert_not_called()

    def test_health_requires_every_retained_file_and_new_tuple(self):
        binding = {"manifest_sha256": "b" * 64}
        health = {"ok": True, "receiver": w.receiver.PROFILE, "manifest_sha256": "b" * 64,
            "files": {row["file_name"]: {"bytes": row["bytes"], "sha256": row["sha256"], "file_ready": True, "reused": True}
                      for row in self.retained}}
        health["files"][w.receiver.NAME] = {"bytes": w.receiver.SIZE, "sha256": w.receiver.SHA, "file_ready": False, "reused": False}
        self.assertFalse(w.file_health(health, binding, self.retained)["file_ready"])
        for key, value in (("reused", False), ("file_ready", False), ("sha256", "a" * 64)):
            changed = copy.deepcopy(health); changed["files"][self.retained[0]["file_name"]][key] = value
            with self.assertRaises(RuntimeError): w.file_health(changed, binding, self.retained)
        changed = copy.deepcopy(health); changed["files"][w.receiver.NAME]["bytes"] += 1
        with self.assertRaises(RuntimeError): w.file_health(changed, binding, self.retained)

    def test_local_draft_inputs_make_no_network_or_auth_calls(self):
        with patch("urllib.request.urlopen", side_effect=AssertionError("no network")), \
             patch("subprocess.run", side_effect=AssertionError("no auth subprocess")):
            baseline, retained = w.inputs()
            value = w.compose(baseline)
            script = w.receiver.build_receiver(retained, "b" * 64)
        self.assertEqual(len(retained), 10)
        self.assertFalse(value["publication_authorized"])
        self.assertIn(w.receiver.NAME, script)

    def test_current_formal_gate_is_bound_to_build_and_new_package(self):
        gate = w.formal_gate()
        self.assertEqual(gate["tested_at"], "2026-10-17T12:38:32+08:00")
        self.assertEqual(gate["package_sha256"], w.receiver.SHA)
        original = w.p.read
        for fault in ("status", "source", "package"):
            def changed(path):
                value = copy.deepcopy(original(path))
                if Path(path) == w.GATE:
                    if fault == "status": value["status"] = "failed"
                    if fault == "source": value["source_fingerprints"]["package"]["sha256"] = "a" * 64
                if Path(path) == w.preparation.BUILD and fault == "package": value["package"]["sha256"] = "a" * 64
                return value
            with patch.object(w.p, "read", side_effect=changed):
                with self.assertRaisesRegex(RuntimeError, "CURRENT_WINDOWS_FORMAL_GATE_BUILD_BINDING_REQUIRED"): w.formal_gate()

    def test_same_generic_passed_receipt_cannot_substitute_host_and_wps(self):
        # 仅拒绝路径测试，不构造最终成功证据，不写证据索引或签名。
        source = HERE.parent / "first-host-approved-existing-root-submit.result.json"
        generic = {"status": "passed", "candidate": {"package": {"sha256": w.receiver.SHA}}}
        row = {"path": str(source), "sha256": "a" * 64}
        index = {key: copy.deepcopy(row) for key in ("host_acceptance", "personal_restore", "retained_business", "wps_acceptance")}
        with patch.object(w.p, "read", return_value=index), patch.object(w.p, "release_client", side_effect=AssertionError("no client")):
            with self.assertRaisesRegex(RuntimeError, "ACCEPTANCE_SOURCES_MUST_BE_DISTINCT"):
                w.acceptance_evidence(HERE / "test_publication.py")
        with self.assertRaisesRegex(RuntimeError, "FINAL_WPS_CONTROLLER_ACCEPTANCE_REQUIRED"):
            w.formatter_evidence(HERE.parent / "win10-formatter-0123456789ab/summary.json", generic)
        sources = [source, HERE.parent / "overlay-install/completed.json", HERE.parent / "retention-baseline.json",
                   HERE.parent / "restore-personal-preflight-inspect.json"]
        for name, path in zip(index, sources): index[name]["path"] = str(path)
        with patch.object(w.p, "read", return_value=index), patch.object(w, "local_evidence", side_effect=lambda row, *_: (Path(row["path"]), generic)):
            with self.assertRaisesRegex(RuntimeError, "REAL_TERMINAL_HOST_ACCEPTANCE_REQUIRED"):
                w.acceptance_evidence(HERE / "test_publication.py")

    def test_external_windows_acceptance_cannot_override_derived_scope(self):
        for external in ({"platform": "windows/arm64", "full_lifecycle_verified": True},
                         {"platform": "windows/amd64", "full_lifecycle_verified": False,
                          "source_fingerprint": "forged", "targets": [{"target": "win11-x64", "full_lifecycle_verified": True}]}):
            with self.subTest(external=external), patch.object(w.p, "read", return_value={"windows_acceptance": external}), \
                 patch.object(w.p, "release_client", side_effect=AssertionError("no client")):
                with self.assertRaisesRegex(RuntimeError, "EXTERNAL_WINDOWS_ACCEPTANCE_OVERRIDE_REJECTED"):
                    w.acceptance_evidence(HERE / "test_publication.py")


if __name__ == "__main__": unittest.main(verbosity=2)
