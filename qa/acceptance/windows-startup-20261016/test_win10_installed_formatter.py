"""Win10 排版薄适配的离线契约验证，不连接或改变 Guest。"""
import copy
import ast
import io
import importlib.util
import json
import subprocess
import unittest
import uuid
from collections import namedtuple
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import MagicMock, patch

spec = importlib.util.spec_from_file_location("win10_installed_formatter", Path(__file__).with_name("win10-installed-formatter.py"))
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)


class FormatterAdapterTests(unittest.TestCase):
    def test_installation_evidence_is_explicit_and_keeps_output_root(self):
        self.assertEqual(adapter.installation_evidence(SimpleNamespace()), adapter.OUTPUT.resolve())
        current = adapter.OUTPUT.parent / "windows-timeout-repair-20261018"
        self.assertEqual(adapter.installation_evidence(SimpleNamespace(installation_evidence=current)), current.resolve())
        with self.assertRaisesRegex(RuntimeError, "INSTALLATION_EVIDENCE_DIRECTORY_NOT_ALLOWED"):
            adapter.installation_evidence(SimpleNamespace(installation_evidence=adapter.OUTPUT.parent))

    def font_run(self):
        folder = adapter.OUTPUT / "win10-formatter-aac5c77979a7"
        if not (folder / "five-font-witness.pdf").is_file() or not adapter.FZX_FONT.is_file():
            self.skipTest("本机固定QA字体和合成验收制品未提供")
        return folder

    def test_restricted_font_requires_independent_visible_raster_proof(self):
        import fitz
        folder = self.font_run()
        with fitz.open(folder / "five-font-witness.pdf") as pdf:
            traces = [span for page in pdf for span in page.get_texttrace()]
        with self.assertRaisesRegex(RuntimeError, "PDF_ACTUAL_FONT_GLYPHS_NOT_PROVEN"):
            adapter.font_trace_proof(traces)  # 原五字体断言仍拒绝用图片冒充texttrace。
        result = adapter.font_render_proof(folder / "five-font-witness.pdf")
        self.assertEqual(len(result), 5)
        self.assertEqual(result[-1]["reference_sha256"], adapter.FZX_FONT_SHA256)
        self.assertEqual(result[-1]["reference_os2_fs_type"], 2)
        self.assertEqual(result[-1]["pdf_behavior"], "restricted-embedding-font-rasterized")
        adapter.require_raster_glyph_match(result[-1]["metrics"])
        adapter.require_raster_glyph_match(result[-1]["visible_page_metrics"])
        with patch.object(adapter, "FZX_FONT_SHA256", "0" * 64), self.assertRaisesRegex(RuntimeError, "FZX_REFERENCE_FONT_CHANGED"):
            adapter.font_render_proof(folder / "five-font-witness.pdf")
        page = MagicMock()
        page.get_texttrace.return_value = traces
        page.get_images.return_value = [(29, 0, 1999, 459)]
        fake_pdf = MagicMock()
        fake_pdf.__enter__.return_value = fake_pdf
        fake_pdf.__iter__.side_effect = lambda: iter([page])
        fake_pdf.__len__.return_value = 1
        fake_pdf.__getitem__.return_value = page
        with patch.object(fitz, "open", return_value=fake_pdf), self.assertRaisesRegex(RuntimeError, "FZX_VISIBLE_SMASK_REQUIRED"):
            adapter.font_render_proof(folder / "five-font-witness.pdf")

    def test_resume_control_flow_imports_fitz_before_common_font_rendering(self):
        folder = self.font_run()
        tree = ast.parse(Path(adapter.__file__).read_text(encoding="utf-8"))
        exercise = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == "exercise")
        execution = next(node for node in exercise.body if isinstance(node, ast.Try)).body
        start = next(index for index, node in enumerate(execution) if isinstance(node, ast.If)
                     and ast.unparse(node.test) == "resume_data is not None")
        end = next(index for index, node in enumerate(execution) if isinstance(node, ast.Assign)
                   and ast.unparse(node.value) == "'final-runtime-verification'")
        # 执行原函数真实续验分支及公共字体区段，省去Guest连接前置，不复制实现。
        self.assertIsInstance(execution[start - 1], ast.Import)
        self.assertEqual(execution[start - 1].names[0].name, "fitz")
        statements = execution[start - 1:end]
        namespace = dict(adapter.__dict__)
        namespace.pop("fitz", None)
        shared = SimpleNamespace(upload=lambda *args: ("a" * 32, "b" * 64),
                                 job=lambda *args: ([folder / "five-font-witness.pdf"], [{"outputs": []}]))
        namespace.update(resume_data=([], {"run": folder.name}), record={"checks": []}, output=folder,
                         run=object(), base="synthetic", prefix="synthetic", headers={}, temp="synthetic", shared=shared,
                         font_witness=lambda path: None)
        exec(compile(ast.fix_missing_locations(ast.Module(body=statements, type_ignores=[])), adapter.__file__, "exec"), namespace)
        self.assertEqual(namespace["record"]["checks"][-1]["id"], "five-font-visible-pdf-glyphs")
        self.assertEqual(len(namespace["record"]["checks"][-1]["fonts"]), 5)
        # 完整路径中的真实图片解码循环也必须在同一公共import之后执行。
        image_loop = next(node for node in execution[start].orelse if isinstance(node, ast.For)
                          and isinstance(node.target, ast.Name) and node.target.id == "image")
        branch = ast.If(test=execution[start].test, body=execution[start].body, orelse=[image_loop])
        full_namespace = dict(adapter.__dict__)
        full_namespace.pop("fitz", None)
        full_namespace.update(resume_data=None, output=folder)
        exec(compile(ast.fix_missing_locations(ast.Module(body=[execution[start - 1], branch], type_ignores=[])),
                     adapter.__file__, "exec"), full_namespace)
        self.assertIn("pixmap", full_namespace)

    def test_raster_font_rejects_wrong_font_text_transparency_and_threshold_failures(self):
        import fitz
        from PIL import Image, ImageDraw, ImageFont
        folder = self.font_run()
        with fitz.open(folder / "five-font-witness.pdf") as pdf:
            mask_xref = pdf[0].get_images()[0][1]
            pixmap = fitz.Pixmap(pdf, mask_xref)
            mask = Image.frombytes("L", (pixmap.width, pixmap.height), pixmap.samples)
        for name, text in (("方正小标宋简.TTF", "字体验收甲"), ("仿宋_GB2312.ttf", "字体验收戊"), ("楷体_GB2312.ttf", "字体验收戊")):
            with self.subTest(name=name, text=text):
                reference = Image.new("L", (2500, 1000))
                font = ImageFont.truetype(str(adapter.FZX_FONT.parent / name), 400)
                ImageDraw.Draw(reference).text((50, 50), text, font=font, fill=255)
                with self.assertRaisesRegex(RuntimeError, "FZX_RASTER_GLYPHS_NOT_PROVEN"):
                    adapter.require_raster_glyph_match(adapter.raster_glyph_metrics(mask, reference))
        with self.assertRaisesRegex(RuntimeError, "FONT_RASTER_INK_REQUIRED"):
            adapter.raster_glyph_metrics(Image.new("L", mask.size), mask)
        for metric in ({"iou": .939, "mae": 0, "per_glyph_iou": [1] * 5},
                       {"iou": 1, "mae": .026, "per_glyph_iou": [1] * 5},
                       {"iou": 1, "mae": 0, "per_glyph_iou": [1, 1, .899, 1, 1]}):
            with self.assertRaisesRegex(RuntimeError, "FZX_RASTER_GLYPHS_NOT_PROVEN"):
                adapter.require_raster_glyph_match(metric)

    def test_font_resume_rechecks_original_evidence_and_rejects_changes(self):
        folder = self.font_run()
        original = adapter.contract.read(folder / "summary.json")
        initial_digest = adapter.contract.digest(folder / "summary.json")
        host_sha = next(iter(original["checks"][1]["proofs"].values()))["host_sha256"]
        shared = adapter.load(adapter.ROOT / "qa/vm-lab/scripts/verify-win10-x86-installed-wps.py", "test_resume_oracle")
        checks, sources = adapter.resume_font_evidence(folder, original["candidate"], host_sha, shared.oracle)
        self.assertEqual(len(checks), 3)
        self.assertEqual(sources["summary_sha256"], initial_digest)
        self.assertEqual(len(sources["supplements"]), 2)
        self.assertEqual(adapter.contract.digest(folder / "summary.json"), initial_digest)
        real_digest = adapter.contract.digest
        for name in ("features-manifest.json", "replace-0.docx", "formatted-1.docx", "job-d6e044109b624d01a15acae3287272f1.json"):
            with self.subTest(name=name), patch.object(adapter.contract, "digest", side_effect=lambda p, name=name: "0" * 64 if Path(p).name == name else real_digest(p)):
                with self.assertRaises(RuntimeError):
                    adapter.resume_font_evidence(folder, original["candidate"], host_sha, shared.oracle)
        with self.assertRaisesRegex(RuntimeError, "FONT_RESUME_SOURCE_BINDING_INVALID"):
            adapter.resume_font_evidence(folder, {**original["candidate"], "manifest_sha256": "0" * 64}, host_sha, shared.oracle)

    def test_http_failure_reports_status_route_and_only_safe_problem_code(self):
        for status, code in ((401, "LOGIN_FAILED"), (429, "LOGIN_THROTTLED")):
            body = json.dumps({"error": {"code": code, "detail": "secret password C:/private"}, "token": "secret"}).encode()
            error = adapter.urllib.error.HTTPError("http://private/?token=secret", status, "password-secret", {}, io.BytesIO(body))
            row = adapter.http_failure(error, "/api/v1/auth/login?token=secret", "POST")
            self.assertEqual(row, {"route": "/api/v1/auth/login", "method": "POST", "exception_type": "HTTPError",
                                   "http_status": status, "error_code": code})
            self.assertNotIn("secret", json.dumps(row))
        error = adapter.urllib.error.HTTPError("http://private", 500, "secret", {}, io.BytesIO(b'{"error":{"code":"token-secret"}}'))
        self.assertNotIn("error_code", adapter.http_failure(error, "/private-secret/path?password=secret", "GET"))
        self.assertEqual(adapter.route_template("/v1/sessions/secret/jobs/private/outputs/token?auth=secret"),
                         "/v1/sessions/{id}/jobs/{id}/outputs/{id}")
        row = adapter.http_failure(adapter.urllib.error.URLError("password-secret"), "/api/v1/health", "GET")
        self.assertEqual(row["exception_type"], "URLError")
        self.assertIsNone(row["http_status"])
        self.assertNotIn("secret", json.dumps(row))
        row = adapter.http_failure(json.JSONDecodeError("secret", "secret", 0), "/v1/capabilities", "GET", 200)
        self.assertEqual(row["http_status"], 200)

    def test_incomplete_http_read_records_only_byte_counts(self):
        for partial, expected in ((b"", 136), (b"token-secret C:/private", 11), (b"secret", None)):
            row = adapter.http_failure(adapter.http.client.IncompleteRead(partial, expected), "/v1/sessions", "POST", 201)
            self.assertEqual(row, {"route": "/v1/sessions", "method": "POST", "exception_type": "IncompleteRead",
                                   "http_status": 201, "partial_bytes": len(partial), "expected_remaining_bytes": expected})
            for forbidden in ("secret", "private", "token", "IncompleteRead("):
                self.assertNotIn(forbidden, json.dumps(row))

    def test_session_transport_metadata_is_counts_only_for_no_body_post_and_delete(self):
        post = adapter.urllib.request.Request("http://127.0.0.1:18777/v1/sessions", method="POST",
                                             headers={"Authorization": "Bearer token-secret"})
        response = SimpleNamespace(status=201, version=10, headers={"Content-Length": "136"})
        row = adapter.session_transport_metadata(post, response, b"secret-response" + b"x" * 121)
        self.assertEqual(row, {"route": "/v1/sessions", "method": "POST", "status": 201, "protocol": 10,
                               "request_body_bytes": 0, "request_content_type_present": False, "request_content_length": 0,
                               "response_content_length": 136, "actual_response_bytes": 136})
        self.assertNotIn("secret", json.dumps(row))
        delete = adapter.urllib.request.Request("http://127.0.0.1:18777/v1/sessions/" + "a" * 32, method="DELETE",
                                               headers={"X-PartyOps-Local-Token": "token-secret"})
        row = adapter.session_transport_metadata(delete, SimpleNamespace(status=204, version=11, headers={"Content-Length": "0"}), b"")
        self.assertEqual(row["route"], "/v1/sessions/{id}")
        self.assertEqual((row["status"], row["actual_response_bytes"], row["request_body_bytes"]), (204, 0, 0))
        self.assertIsNone(row["request_content_length"])
        self.assertNotIn("secret", json.dumps(row))
        other = adapter.urllib.request.Request("http://127.0.0.1:18777/v1/capabilities", method="GET")
        self.assertIsNone(adapter.session_transport_metadata(other, response, b"private"))

    @patch.object(adapter, "OUTPUT")
    def test_failure_snapshot_preserves_jobs_and_probe_before_cleanup_without_credentials(self, root):
        output = MagicMock()
        sidecar = root.__truediv__.return_value
        sidecar.name = "win10-formatter-012345abcdef.probe.json"
        sidecar.is_file.return_value = True
        sidecar.read_bytes.return_value = b'{"code":"CURRENT_NATIVE_INPUT_MISMATCH"}'
        job = adapter.safe_job({"id": "a" * 32, "state": "completed", "outputs": [], "token": "secret"})
        record = {"stage": "feature-jobs-and-native-probes", "error": "WPS_X64_READONLY_PROBE_FAILED",
                  "http_failures": [], "candidate": {"private": "secret"}}
        adapter.save_failure_evidence(output, record, {job["id"]: job}, ["win10-formatter-012345abcdef"])
        output.__truediv__.return_value.write_bytes.assert_called_once_with(sidecar.read_bytes.return_value)
        snapshot = json.loads(output.__truediv__.return_value.write_text.call_args.args[0])
        self.assertEqual(snapshot["safe_jobs"], [job])
        self.assertEqual(snapshot["probe_sidecars"], [sidecar.name])
        self.assertEqual(snapshot["stage"], "feature-jobs-and-native-probes")
        self.assertNotIn("secret", json.dumps(snapshot))
        # 核对实际finally顺序：保存证据先于DELETE；不能由后续清理吞掉原错误。
        tree = ast.parse(Path(adapter.__file__).read_text(encoding="utf-8"))
        exercise = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == "exercise")
        final = next(node for node in exercise.body if isinstance(node, ast.Try)).finalbody
        calls = [node for statement in final for node in ast.walk(statement) if isinstance(node, ast.Call)]
        save = next(node.lineno for node in calls if isinstance(node.func, ast.Name) and node.func.id == "save_failure_evidence")
        cleanup = next(node.lineno for node in calls if isinstance(node.func, ast.Attribute) and node.func.attr == "request")
        self.assertLess(save, cleanup)

    def test_pdf_font_use_requires_visible_matching_glyphs_not_font_declaration(self):
        traces = [{"font": "ABCDEF+" + aliases[-1], "type": 0, "opacity": 1.0, "size": 20,
                   "bbox": (0, 0, 100, 30), "chars": [(ord(c), i + 1) for i, c in enumerate(marker)]}
                  for _, marker, aliases in adapter.FONT_WITNESSES]
        result = adapter.font_trace_proof(traces)
        self.assertEqual(len(result), 5)
        for fault in ("missing", "substituted", "hidden", "transparent", "notdef", "wrong-text"):
            with self.subTest(fault=fault):
                changed = copy.deepcopy(traces)
                if fault == "missing": changed.pop()
                if fault == "substituted": changed[-1]["font"] = "SimSun"
                if fault == "hidden": changed[-1]["type"] = 3
                if fault == "transparent": changed[-1]["opacity"] = 0
                if fault == "notdef": changed[-1]["chars"] = [(char[0], 0) for char in changed[-1]["chars"]]
                if fault == "wrong-text": changed[-1]["chars"] = [(ord("错"), 1)]
                with self.assertRaises(RuntimeError): adapter.font_trace_proof(changed)

    def native_fixture(self):
        names = ["replace", "redheader", "rename", "convert-docx", "convert-pdf", "convert-txt", "convert-png", "convert-jpg", "pdf-to-word"]
        cases, proofs = [], {}
        for index, name in enumerate(names):
            job = f"{index:032x}"
            feature = "convert" if name.startswith("convert-") else name
            cases.append({"id": name, "feature_id": feature, "job": {"id": job}, "outputs": [{"sha256": "a" * 64}]})
            proofs[job] = {"feature": feature, "host_sha256": "b" * 64, "request_sha256": "c" * 64,
                "response_sha256": "d" * 64, "host_display_name": "" if feature == "pdf-to-word" else "WPS",
                "outputs": [{"extension": ".pdf" if name in {"convert-png", "convert-jpg"} else ".docx", "sha256": "a" * 64, "bytes": 100}]}
            if name in {"convert-png", "convert-jpg"}:
                proofs[job]["image_derivative"] = {"status": "passed", "native_pdf_sha256": "a" * 64,
                                                    "images": [{"sha256": "a" * 64}]}
        return {"status": "passed", "cases": cases}, proofs

    def test_native_cases_bind_current_jobs_download_hashes_and_wps(self):
        result, proofs = self.native_fixture()
        checked = adapter.verify_native_cases(result, proofs, "b" * 64)
        self.assertEqual(checked["feature_case_count"], 9)
        self.assertEqual(checked["pdf_to_word_engine"], "original-native-source-host-local-pdf-engine")
        for fault in ("missing", "host", "provider", "output", "request", "image-pdf", "duplicate-job", "unbound-image", "wrong-image-digest"):
            with self.subTest(fault=fault):
                cases, rows = copy.deepcopy(result), copy.deepcopy(proofs)
                first = next(iter(rows.values()))
                if fault == "missing": rows.pop(next(iter(rows)))
                if fault == "host": first["host_sha256"] = "f" * 64
                if fault == "provider": first["host_display_name"] = "Word"
                if fault == "output": first["outputs"][0]["sha256"] = "e" * 64
                if fault == "request": first["request_sha256"] = ""
                if fault == "image-pdf": rows[cases["cases"][6]["job"]["id"]]["outputs"][0]["extension"] = ".png"
                if fault == "duplicate-job": cases["cases"][1]["job"]["id"] = cases["cases"][0]["job"]["id"]
                if fault == "unbound-image": rows[cases["cases"][6]["job"]["id"]].pop("image_derivative")
                if fault == "wrong-image-digest": rows[cases["cases"][7]["job"]["id"]]["image_derivative"]["images"][0]["sha256"] = "e" * 64
                with self.assertRaises(RuntimeError): adapter.verify_native_cases(cases, rows, "b" * 64)

    def test_image_pixels_match_actual_product_renderer_pages_and_long_jpeg(self):
        import fitz
        from PIL import Image
        # 直接抽取产品纯渲染函数作为测试生产者，依赖注入仅替换文件命名/进度，不改像素算法。
        source = adapter.ROOT / "backend/app/official_format_features.py"
        tree = ast.parse(source.read_text(encoding="utf-8"))
        functions = [node for node in tree.body if isinstance(node, ast.FunctionDef)
                     and node.name in {"_parse_page_selection", "_render_pdf_images"}]
        namespace = {"fitz": fitz, "Image": Image, "io": io,
                     "_check_cancelled": lambda callback: None, "_progress": lambda *args: None,
                     "_unique_output": lambda workspace, stem, suffix, options: workspace / (stem + suffix),
                     "FeatureOutput": namedtuple("FeatureOutput", "path filename content_type")}
        compile_tree = ast.Module(body=[ast.ImportFrom(module="__future__", names=[ast.alias(name="annotations")], level=0), *functions], type_ignores=[])
        exec(compile(ast.fix_missing_locations(compile_tree), str(source), "exec"), namespace)
        folder = Path(__file__).parent / "formatter-image-contract-tests" / uuid.uuid4().hex
        folder.mkdir(parents=True)
        native = folder / "synthetic-native.pdf"
        with fitz.open() as pdf:
            for width, height, color in ((90, 70, (1, 0, 0)), (60, 80, (0, 0, 1))):
                page = pdf.new_page(width=width, height=height)
                page.draw_rect(fitz.Rect(8, 8, width - 8, height - 8), color=color, fill=color)
                page.insert_text((10, 25), "QA", fontsize=12, color=(1, 1, 1))
            pdf.save(native)
        digest = adapter.contract.digest(native)
        for kind, mode in (("png", "pages"), ("jpg", "long")):
            options = {"target_format": kind, "image_mode": mode, "dpi": 144, "page_selection": "all"}
            actual = namespace["_render_pdf_images"](native, folder, kind, options, lambda *args: None, lambda: False)
            paths = [row.path for row in actual]
            checked = adapter.verify_image_derivative(native, digest, paths, options)
            self.assertEqual(checked["native_pdf_pages"], 2)
            self.assertEqual(checked["page_pixels_at_144dpi"], [[180, 140], [120, 160]])
            self.assertEqual(checked["images"][0]["pixels"], [180, 140] if kind == "png" else [180, 300])
            with self.assertRaises(RuntimeError): adapter.verify_image_derivative(native, "f" * 64, paths, options)
            with self.assertRaises(RuntimeError): adapter.verify_image_derivative(native, digest, [], options)
            with self.assertRaises(RuntimeError): adapter.verify_image_derivative(native, digest, paths, {**options, "dpi": 72})
            if kind == "png":
                with self.assertRaises(RuntimeError): adapter.verify_image_derivative(native, digest, list(reversed(paths)), options)
            with Image.open(paths[0]) as image:
                wrong = Image.new("RGB", image.size, "white")
                wrong_path = folder / ("unrelated." + kind)
                wrong.save(wrong_path, format="PNG" if kind == "png" else "JPEG", quality=92)
                smaller = image.resize((image.width - 1, image.height))
                smaller_path = folder / ("wrong-dpi-size." + kind)
                smaller.save(smaller_path, format="PNG" if kind == "png" else "JPEG", quality=92)
                if kind == "png":
                    opaque = image.convert("RGBA")
                    opaque_path = folder / "opaque-rgba.png"
                    opaque.save(opaque_path)
                    adapter.verify_image_derivative(native, digest, [opaque_path, *paths[1:]], options)
                    for alpha_mode in ("all-transparent", "one-partial-pixel"):
                        transparent = image.convert("RGBA")
                        if alpha_mode == "all-transparent":
                            transparent.putalpha(0)
                        else:
                            r, g, b, _ = transparent.getpixel((0, 0))
                            transparent.putpixel((0, 0), (r, g, b, 254))
                        transparent_path = folder / (alpha_mode + ".png")
                        transparent.save(transparent_path)
                        with self.assertRaisesRegex(RuntimeError, "NATIVE_PDF_PNG_ALPHA_NOT_OPAQUE"):
                            adapter.verify_image_derivative(native, digest, [transparent_path, *paths[1:]], options)
                    changed = image.convert("RGB")
                    changed.putpixel((0, 0), (0, 0, 0))
                    changed_path = folder / "one-pixel-different.png"
                    changed.save(changed_path)
                    with self.assertRaises(RuntimeError): adapter.verify_image_derivative(native, digest, [changed_path, *paths[1:]], options)
            for invalid in (wrong_path, smaller_path):
                with self.assertRaises(RuntimeError): adapter.verify_image_derivative(native, digest, [invalid, *paths[1:]], options)

    def test_native_probe_is_bound_readonly_and_pdf_local_exception_is_explicit(self):
        script = adapter.native_host_script(r"C:\Users\partyopsuser\AppData\Local\Temp", "a" * 32, "b" * 32,
                                            "pdf-to-word", "c" * 64, "d" * 64)
        for required in ("CURRENT_NATIVE_REQUEST_MISMATCH", "CURRENT_NATIVE_INPUT_MISMATCH", "CANDIDATE_NATIVE_HOST_CHANGED",
                         "CURRENT_NATIVE_HOST_FAILED", "NATIVE_OUTPUT_PATH_INVALID", "$expected.feature -ne 'pdf-to-word'"):
            self.assertIn(required, script)
        for forbidden in ("Set-Content", "Copy-Item", "Remove-Item", "$response.message", "$jobs[0].message"):
            self.assertNotIn(forbidden, script)
        with self.assertRaises(RuntimeError):
            adapter.native_host_script(r"C:\Users\partyopsuser\AppData\Local\Temp", "bad", "b" * 32, "format", "c" * 64, "d" * 64)

    def test_job_allowlist_drops_secrets_paths_messages(self):
        raw = {"id": "a" * 32, "state": "completed", "token": "secret", "message": "document body",
               "outputs": [{"id": "b" * 32, "document_id": "c" * 32,
                            "filename": r"C:\private\合成-定稿.docx", "path": "private", "ticket": "secret"}]}
        safe = adapter.safe_job(raw)
        self.assertEqual(safe["outputs"][0]["filename"], "合成-定稿.docx")
        encoded = json.dumps(safe)
        for forbidden in ("secret", "private", "message", "ticket", "token"):
            self.assertNotIn(forbidden, encoded)
        for key, value in (("id", "../bad"), ("state", "unknown")):
            with self.assertRaises(RuntimeError): adapter.safe_job({**raw, key: value})

    def test_dynamic_listener_must_be_same_standard_process(self):
        row = {"pid": 10, "created": "stamp", "path": adapter.contract.INSTALL + r"\PartyOps.exe",
               "owner": "partyopsuser", "session_id": 1}
        actual = {"processes": [{"pid": 10, "created": "stamp"}]}
        desktop = {"desktop": [{"session_id": 1}]}
        adapter.validate_listener(row, actual, desktop)
        for key, value in (("pid", 11), ("created", "other"), ("path", r"C:\other.exe"),
                           ("owner", "Administrator"), ("session_id", 0)):
            with self.assertRaises(RuntimeError): adapter.validate_listener({**row, key: value}, actual, desktop)

    def test_x86_or_unreadable_font_cannot_pass(self):
        value = {"version": "12.1.0.28488", "pe_machine": 0x8664, "com_registered": True,
                 "executable_sha256": adapter.WPS_EXECUTABLE_SHA256, "signature_status": "Valid", "signer_is_kingsoft": True,
                 "username": "partyopsuser", "binary_scope": "target-user-profile", "registration_scope": "target-user",
                 "executable_path": r"C:\Users\partyopsuser\AppData\Local\Kingsoft\WPS Office\12.1.0.28488\office6\wps.exe",
                 "identity_sources": ["target-user-vendor"], "fonts": [{"font": name, "file_load_readable": True}
                   for name in ("宋体", "黑体", "仿宋_GB2312", "楷体_GB2312", "方正小标宋简体")]}
        adapter.validate_wps(value)
        adapter.validate_wps({**value, "version": "12,1,0,28488"})
        adapter.validate_wps({**value, "executable_path": r"C:\Program Files\Kingsoft\WPS Office\12.1.0.28488\office6\wps.exe",
                              "binary_scope": "shared-program-files", "registration_scope": "machine", "identity_sources": ["machine-vendor", "machine-app-path"]})
        for key, other in (("version", "12.1.0.28505"), ("pe_machine", 0x14c), ("fonts", []), ("com_registered", False),
                           ("signature_status", "NotSigned"), ("signer_is_kingsoft", False), ("executable_sha256", "f" * 64),
                           ("username", "partyopsqa"), ("registration_scope", "partyopsqa"), ("identity_sources", ["admin-uninstall"]),
                           ("executable_path", r"C:\Users\partyopsqa\AppData\Local\Kingsoft\WPS Office\12.1.0.28488\office6\wps.exe")):
            with self.assertRaises(RuntimeError): adapter.validate_wps({**value, key: other})
        bad = copy.deepcopy(value); bad["fonts"][0]["file_load_readable"] = False
        with self.assertRaises(RuntimeError): adapter.validate_wps(bad)

    def test_observed_numeric_versions_and_vendor_identity_sources(self):
        for version in ("12.1.0.28488", "12,1,0,28488", "12, 1, 0, 28488"):
            self.assertEqual(adapter.normalize_wps_version(version), "12.1.0.28488")
        for version in ("12.1.28488", "12.1.0.28488-extra", None):
            with self.assertRaises(RuntimeError): adapter.normalize_wps_version(version)
        script = adapter.wps_script("S-1-5-21-1-2-3-1001")
        for expected in (".GetValue('InstallRoot')", "CurrentVersion\\App Paths\\wps.exe", "'office6\\wps.exe'",
                         "FileMajorPart", "FilePrivatePart", "Get-AuthenticodeSignature", adapter.WPS_EXECUTABLE_SHA256,
                         "WPS_OTHER_USER_OR_UNOBSERVED_SCOPE_REJECTED", "WPS_TARGET_USER_COM_EXECUTABLE_MISMATCH"):
            self.assertIn(expected, script)
        for forbidden in ("$v.InstallLocation", "-Recurse", "HKCU:", "C:\\Users\\partyopsqa", "Set-ItemProperty", "Start-Process"):
            self.assertNotIn(forbidden, script)
        self.assertIn("for($i=0;$i -lt $classes.Count;$i++)", script)
        self.assertIn("$registrationScope='target-user'", script)

    def test_long_probe_uses_short_command_and_utf8_sftp(self):
        client, execute = MagicMock(), MagicMock(return_value='{"version":"test"}')
        script = "# 合成只读脚本\n" * 1000
        result = adapter.staged_readonly_probe(client, execute, script, "win10-formatter-012345abcdef")
        self.assertEqual(result, {"version": "test"})
        command = execute.call_args.args[1]
        self.assertLess(len(command), 300)
        self.assertNotIn("合成", command)
        fs = client.open_sftp.return_value.__enter__.return_value
        staged = fs.file.return_value.__enter__.return_value.write.call_args.args[0].decode("utf-8")
        self.assertIn(script, staged)
        self.assertIn("$_.Exception.Message", staged)
        self.assertNotIn("$_.ScriptStackTrace", staged)
        with self.assertRaises(RuntimeError): adapter.staged_readonly_probe(client, execute, script, "../other")

    @patch.object(adapter.contract, "exclusive")
    def test_probe_errors_preserve_only_declared_codes(self, write):
        script = "throw 'CURRENT_NATIVE_INPUT_MISMATCH'"
        for payload, expected in (
            ('{"qa_probe_error":"CURRENT_NATIVE_INPUT_MISMATCH"}', "CURRENT_NATIVE_INPUT_MISMATCH"),
            ('{"qa_probe_error":"C:/private/token-secret"}', "WPS_X64_READONLY_PROBE_FAILED"),
            ('{"qa_probe_error":["CURRENT_NATIVE_INPUT_MISMATCH"]}', "WPS_X64_READONLY_PROBE_FAILED"),
            ('not-json token-secret', "WPS_X64_READONLY_PROBE_FAILED"),
            ('[]', "WPS_X64_READONLY_PROBE_FAILED"),
        ):
            with self.subTest(payload=payload), self.assertRaisesRegex(RuntimeError, "^" + expected + "$"):
                adapter.staged_readonly_probe(MagicMock(), MagicMock(return_value=payload), script,
                                             "win10-formatter-012345abcdef")
        with self.assertRaisesRegex(RuntimeError, "^WPS_X64_READONLY_PROBE_FAILED$"):
            adapter.staged_readonly_probe(MagicMock(), MagicMock(side_effect=RuntimeError("token-secret")),
                                         script, "win10-formatter-012345abcdef")

    def test_guest_error_wrapper_emits_only_synthetic_safe_fields(self):
        # 本机只执行合成脚本，不连接 Guest、不读业务文件或写磁盘。
        for script, expected in (
            ("@{synthetic=$true}|ConvertTo-Json -Compress", {"synthetic": True}),
            ("throw 'CURRENT_NATIVE_INPUT_MISMATCH'", {"qa_probe_error": "CURRENT_NATIVE_INPUT_MISMATCH"}),
            ("throw ('token-'+'secret C:/private')", {"qa_probe_error": "WPS_X64_READONLY_PROBE_FAILED"}),
        ):
            client = MagicMock()
            adapter.staged_readonly_probe(client, MagicMock(return_value='{}'), script,
                                         "win10-formatter-012345abcdef")
            fs = client.open_sftp.return_value.__enter__.return_value
            wrapped = fs.file.return_value.__enter__.return_value.write.call_args.args[0].decode("utf-8")
            command = "$s=[Console]::In.ReadToEnd();& ([scriptblock]::Create($s))"
            result = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", command],
                                    input=wrapped, encoding="utf-8", capture_output=True)
            self.assertEqual(result.returncode, 0)
            self.assertEqual(result.stderr, "")
            actual = json.loads(result.stdout)
            self.assertEqual({key: actual[key] for key in expected}, expected)
            if "qa_probe_error" in actual:
                self.assertEqual(set(actual), {"qa_probe_error", "exception_type", "category", "script_line"})
                self.assertGreater(actual["script_line"], 0)
                self.assertNotIn("token-secret", result.stdout)
                self.assertNotIn("C:/private", result.stdout)

    @patch.object(adapter.contract, "exclusive")
    def test_failure_sidecar_has_only_safe_metadata_and_exact_synthetic_binding(self, write):
        binding = {"job": "a" * 32, "feature": "replace", "input_sha256": "b" * 64,
                   "host_sha256": "c" * 64, "token": "secret", "path": "C:/private"}
        payload = {"qa_probe_error": "WPS_X64_READONLY_PROBE_FAILED", "exception_type": "System.ArgumentException",
                   "category": "InvalidArgument", "script_line": 2, "message": "token-secret", "stderr": "C:/private"}
        with self.assertRaisesRegex(RuntimeError, "^WPS_X64_READONLY_PROBE_FAILED$"):
            adapter.staged_readonly_probe(MagicMock(), MagicMock(return_value=json.dumps(payload)),
                                         "# synthetic\nthrow 'CURRENT_NATIVE_INPUT_MISMATCH'", "win10-formatter-012345abcdef", binding)
        path, row = write.call_args.args
        self.assertEqual(path, adapter.OUTPUT / "win10-formatter-012345abcdef.probe.json")
        self.assertEqual(row, {"code": "WPS_X64_READONLY_PROBE_FAILED", "exception_type": "System.ArgumentException",
                              "category": "InvalidArgument", "script_line": 2,
                              "binding": {key: binding[key] for key in ("job", "feature", "input_sha256", "host_sha256")}})
        payload.update(exception_type="C:/private/token-secret", category=["InvalidData"], script_line=999999)
        with self.assertRaises(RuntimeError):
            adapter.staged_readonly_probe(MagicMock(), MagicMock(return_value=json.dumps(payload)),
                                         "# synthetic", "win10-formatter-012345abcdef", {"job": "../private", "feature": "secret"})
        self.assertEqual(write.call_args.args[1]["exception_type"], "Unknown")
        self.assertEqual(write.call_args.args[1]["category"], "NotSpecified")
        self.assertEqual(write.call_args.args[1]["script_line"], 0)
        self.assertEqual(write.call_args.args[1]["binding"], {})

    def test_native_json_reading_explicitly_preserves_bomless_utf8_paths(self):
        script = adapter.native_host_script(r"C:\Users\partyopsuser\AppData\Local\Temp", "a" * 32, "b" * 32,
                                            "replace", "c" * 64, "d" * 64)
        for variable in ("requestPath", "responsePath"):
            self.assertIn("Get-Content -LiteralPath $" + variable + " -Raw -Encoding UTF8|ConvertFrom-Json", script)
        # 合成无BOM JSON；模拟ANSI解码会损坏路径，显式UTF8保持完全一致。
        source = json.dumps({"source_paths": ["C:\\合成\\中文.docx"]}, ensure_ascii=False).encode("utf-8")
        command = ("$b=[byte[]]@(" + ",".join(str(value) for value in source) + ");"
                   "$u=[Text.Encoding]::UTF8.GetString($b)|ConvertFrom-Json;"
                   "$a=[Text.Encoding]::GetEncoding(1252).GetString($b)|ConvertFrom-Json;"
                   "@{different=($u.source_paths[0] -cne $a.source_paths[0]);bom=($b[0] -eq 239)}|ConvertTo-Json -Compress")
        result = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", command],
                                encoding="utf-8", capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout), {"different": True, "bom": False})

    def test_generated_guest_scripts_parse_without_execution(self):
        parser = "$s=[Console]::In.ReadToEnd();$e=$null;$t=$null;[void][Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){$e|ForEach-Object {$_.Message};exit 1}"
        for script in (adapter.listener_script(18775), adapter.listener_script(49251),
                       adapter.wps_script("S-1-5-21-1-2-3-1001"),
                       adapter.native_host_script(r"C:\Users\partyopsuser\AppData\Local\Temp", "a" * 32, "b" * 32, "replace", "c" * 64, "d" * 64)):
            result = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", parser],
                                    input=script, encoding="utf-8", capture_output=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
