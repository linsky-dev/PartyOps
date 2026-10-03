"""串行验证真实普通用户安装版九项公文作业，复用已有输入及内容合同。"""
from __future__ import annotations

import argparse
import re
import secrets
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path


def load(path, name):
    spec = spec_from_file_location(name, path)
    module = module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


HERE = Path(__file__).resolve().parent
formatter = load(HERE / "exercise-windows-native-formatter.py", "native_features_formatter")
native = formatter.native


def validate_outputs(result, output):
    """下载成品必须能实际解码；多页图片逐张检查，不只检查第一张。"""
    import fitz

    checks = []
    for case in result["cases"]:
        for row in case["outputs"]:
            path = output / row["filename"]
            native.require(path.parent == output and native.sha256(path) == row["sha256"],
                           "NATIVE_FEATURE_OUTPUT_CHANGED")
            if path.suffix in {".png", ".jpg", ".jpeg"}:
                pixmap = fitz.Pixmap(str(path))
                native.require(pixmap.width > 0 and pixmap.height > 0, "NATIVE_FEATURE_IMAGE_INVALID")
                checks.append({"file": path.name, "width": pixmap.width, "height": pixmap.height})
            elif path.suffix == ".pdf":
                with fitz.open(path) as document:
                    native.require(document.page_count > 0, "NATIVE_FEATURE_PDF_EMPTY")
                    for page in document:
                        page.get_pixmap()
                    checks.append({"file": path.name, "pages": document.page_count})
    return checks


CASE_IDS = ("replace", "redheader", "rename", "convert-docx", "convert-pdf", "convert-txt", "convert-png", "convert-jpg", "pdf-to-word")


def exercise(run, selected=None):
    fixtures = load(HERE / "exercise-linux-formatter.py", "native_features_fixtures")
    verifier = load(HERE.parent / "guest/installed-formatter-features.py", "native_features_verifier")
    output = run.reports / ("formatter-features-" + secrets.token_hex(12))
    output.mkdir()
    manifest_path = fixtures.feature_inputs(output)
    manifest_data = native.read_json(manifest_path)
    if selected:
        native.require(set(selected) <= set(CASE_IDS), "NATIVE_FEATURE_SELECTION_INVALID")
        manifest_data["cases"] = [case for case in manifest_data["cases"] if case["id"] in selected]
        native.write_json(manifest_path, manifest_data)
    cases = manifest_data["cases"]
    record = {"scope": "native-standard-user-installed-formatter-features", "generated_at": native.now(),
              "context": run.context, "status": "failed", "runtime_environment_passed": False,
              "manifest_sha256": native.sha256(manifest_path), "requested_cases": [case["id"] for case in cases],
              "complete_feature_set": False, "jobs": []}
    client = None
    try:
        binding = native.read_json(run.run_directory / "install-binding.json")
        manifest = Path(native.INSTALL) / "release-manifest.json"
        native.require(binding["source_fingerprint"] == run.context["source_fingerprint"]
                       and binding["package"]["sha256"] == run.context["package_sha256"]
                       and native.sha256(manifest) == binding["windows_payload"]["manifest"]["sha256"],
                       "NATIVE_FEATURE_INSTALL_BINDING_CHANGED")
        host_name = "formatter-host/PartyOps.DocumentFormatter.Host.exe"
        hosts = [row for row in native.read_json(manifest)["files"] if row["path"] == host_name]
        native.require(len(hosts) == 1 and native.sha256(Path(native.INSTALL) / host_name) == hosts[0]["sha256"],
                       "NATIVE_INSTALLED_FORMATTER_HOST_CHANGED")
        record["host_sha256"] = hosts[0]["sha256"]
        client = formatter.InstalledFormatter(run)
        record["formatter_listener"] = client.guard()

        def request(base, route, method="GET", data=None, content_type=None):
            native.require(base == client.origin, "NATIVE_FEATURE_ORIGIN_CHANGED")
            response = client.request(route, method, data, content_type)
            if method == "POST" and route.endswith("/jobs"):
                index = len(record["jobs"])
                native.require(index < len(cases) and re.fullmatch(r"[a-f0-9]{32}", response["id"]),
                               "NATIVE_FEATURE_JOB_ID_INVALID")
                case = cases[index]
                native.require(data["feature_id"] == case["feature_id"] and len(data["document_ids"]) == 1,
                               "NATIVE_FEATURE_CASE_CHANGED")
                record["jobs"].append({"id": response["id"], "case": case["id"],
                    "feature": case["feature_id"], "documents": {data["document_ids"][0]: {"sha256": case["sha256"]}},
                    "snapshots": [client.safe_job(response)]})
                native.write_json(output / "evidence.json", record)
            elif method == "GET" and isinstance(response, dict) and "state" in response:
                matches = [job for job in record["jobs"] if route.endswith("/jobs/" + job["id"])]
                native.require(len(matches) == 1, "NATIVE_FEATURE_UNKNOWN_JOB")
                matches[0]["snapshots"].append(client.safe_job(response))
                native.write_json(output / "evidence.json", record)
            return response

        result = verifier.exercise(request, client.origin, client.session_path, manifest_path, output)
        native.require(result["case_count"] == len(cases) and len(cases) > 0, "NATIVE_FEATURE_CASES_MISSING")
        record["decoded_outputs"] = validate_outputs(result, output)
        for job in record["jobs"]:
            # PDF 转 Word 使用产品内置提取引擎，单独如实登记；其余作业必须证明实际 WPS。
            if job["feature"] == "pdf-to-word":
                job["engine_scope"] = "installed-pdf-to-word-content-verified"
                continue
            document_id = next(iter(job["documents"]))
            job["wps_proof"] = formatter.wps_proof(run.work / "temp", job["id"], document_id,
                job["feature"], job["documents"][document_id]["sha256"], 0)
        record["result_sha256"] = native.sha256(output / "installed-features-evidence.json")
        record["status"] = "passed"
        record["complete_feature_set"] = len(cases) == len(CASE_IDS)
    except Exception as exc:  # noqa: BLE001 - 验收出口脱敏，失败必须先保存证据。
        code = str(exc).split(":", 1)[0]
        record["error"] = code if isinstance(exc, RuntimeError) and re.fullmatch(r"[A-Z0-9_]+", code) else type(exc).__name__
    finally:
        if client:
            for job in record["jobs"]:
                try:
                    job["host_controls"] = formatter.archive_host_controls(run.work / "temp", job["id"],
                        job["documents"], output, client.safe_value)
                except Exception:  # noqa: BLE001 - 归档失败禁止通过，不覆盖原作业错误。
                    record["status"] = "failed"
                    job["archive_error"] = "NATIVE_FEATURE_CONTROL_ARCHIVE_FAILED"
            try:
                client.close()
            except Exception:  # noqa: BLE001 - 清理失败同样保留失败状态。
                record["status"] = "failed"
                record["cleanup_error"] = "NATIVE_FEATURE_SESSION_CLEANUP_FAILED"
        native.write_json(output / "evidence.json", record)
    return {"status": record["status"], "evidence": str(output / "evidence.json"),
            "error": record.get("error"), "complete_feature_set": record["status"] == "passed" and record["complete_feature_set"],
            "runtime_environment_passed": False}


def main():
    import json

    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("run-directory", "work", "data-directory", "identity-receipt", "runtime-receipt"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--case", action="append", choices=CASE_IDS, help="仅执行指定项；局部复测不会标记九项全部通过。")
    args = parser.parse_args()
    args.phase, args.wizard_receipt = "formatter-probe", None
    result = exercise(native.NativeRun(args), args.case)
    print(json.dumps(result, ensure_ascii=False))
    return 0 if result["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
