"""通过已绑定普通用户安装版 HTTP 执行真实 WPS 排版与逐页金样；不启动程序。"""
from __future__ import annotations

import argparse
import importlib.util
import json
import re
import secrets
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


native = load(Path(__file__).with_name("exercise-windows-native-business.py"), "native_formatter_business")
require, sha256, write_json = native.require, native.sha256, native.write_json


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *_args, **_kwargs):
        raise RuntimeError("NATIVE_FORMATTER_REDIRECT_REJECTED")


class InstalledFormatter:
    def __init__(self, run):
        self.run = run
        self.token = ""
        self.private_values = []
        self.job_records = []
        self.session_path = None
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        run.wait_health()
        run.login()
        ticket = run.request("/api/v1/official-format/local-ticket", "POST", {"origin": run.origin})
        self.private_values.append(ticket["ticket"])
        self.origin = native.loopback_origin(ticket["local_base_url"])
        require(self.origin not in {run.origin, run.wizard_origin}, "NATIVE_FORMATTER_PORT_OVERLAPS_APPLICATION")
        self.guard()
        session = self.request("/v1/sessions", "POST", {}, authorization="Bearer " + ticket["ticket"])
        require(re.fullmatch(r"[a-f0-9]{32}", str(session.get("session_id", "")))
                and isinstance(session.get("session_token"), str) and session["session_token"],
                "NATIVE_FORMATTER_SESSION_INVALID")
        self.session_path = "/v1/sessions/" + session["session_id"]
        self.token = session["session_token"]
        self.private_values.append(self.token)
        # 不将原始票据、会话令牌、Cookie 或完整会话响应写入 state/report。

    def guard(self):
        runtime = self.run.guard_url(self.run.origin + "/api/v1/health")
        port = urllib.parse.urlsplit(self.origin).port
        return native.validate_listener(self.run.inspect_listener(port), self.run.account, self.run.executable,
                                        self.run.install["installed_executable_sha256"], port,
                                        runtime["pid"], runtime["created_at"])

    def request(self, path, method="GET", data=None, content_type=None, authorization=None):
        require(path.startswith("/v1/") and "?" not in path and "#" not in path and ".." not in path,
                "NATIVE_FORMATTER_PATH_INVALID")
        self.guard()
        headers = {"Origin": self.run.origin}
        if self.token:
            headers["X-PartyOps-Local-Token"] = self.token
        if authorization:
            headers["Authorization"] = authorization
        if isinstance(data, dict):
            data = json.dumps(data, ensure_ascii=False).encode("utf-8")
            content_type = "application/json"
        if content_type:
            headers["Content-Type"] = content_type
        request = urllib.request.Request(self.origin + path, method=method, data=data, headers=headers)
        try:
            with self.opener.open(request, timeout=240) as response:
                body = response.read()
                return json.loads(body) if "application/json" in response.headers.get("Content-Type", "") else body
        except urllib.error.HTTPError as exc:
            code = exc.code
            exc.close()
            raise RuntimeError("NATIVE_FORMATTER_HTTP_STATUS_" + str(code)) from None

    def upload(self, path, filename):
        require(path.is_file() and path.stat().st_size < 16 * 1024**2, "NATIVE_GOLDEN_INPUT_MISSING_OR_TOO_LARGE")
        boundary = "PartyOpsQA" + secrets.token_hex(12)
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="document"; filename="{filename}"\r\n'
                'Content-Type: application/vnd.openxmlformats-officedocument.wordprocessingml.document\r\n\r\n').encode()
        body += path.read_bytes() + f"\r\n--{boundary}--\r\n".encode()
        result = self.request(self.session_path + "/documents", "POST", body, "multipart/form-data; boundary=" + boundary)
        require(re.fullmatch(r"[a-f0-9]{32}", str(result.get("document_id", ""))), "NATIVE_FORMATTER_DOCUMENT_ID_INVALID")
        return result["document_id"]

    def job(self, feature, documents, options, output, extension):
        job = self.request(self.session_path + "/jobs", "POST", {
            "feature_id": feature, "document_ids": list(documents), "options": {"compatibility_mode": "wps", **options}})
        job_id = str(job.get("id", ""))
        require(re.fullmatch(r"[a-f0-9]{32}", job_id), "NATIVE_FORMATTER_JOB_ID_INVALID")
        job_path = self.session_path + "/jobs/" + job_id
        record = {"job_id": job_id, "session_id": self.session_path.rsplit("/", 1)[1],
                  "feature": feature, "requested_documents": list(documents), "snapshots": [], "host_controls": []}
        self.job_records.append(record)
        job_evidence = output / ("job-" + job_id + ".json")

        def save_state():
            record["snapshots"].append({"observed_at": native.now(), "response": self.safe_job(job)})
            write_json(job_evidence, record)

        try:
            save_state()  # 提交成功立即保存 ID；失败和超时不能只留下聚合错误。
            deadline = time.monotonic() + 900
            while job["state"] in {"queued", "running"}:
                require(time.monotonic() < deadline, "NATIVE_FORMATTER_JOB_TIMEOUT")
                time.sleep(1)
                job = self.request(job_path)
                save_state()
            require(job.get("state") == "completed" and len(job.get("outputs", [])) == len(documents),
                    "NATIVE_FORMATTER_JOB_NOT_COMPLETED")
            proofs, downloaded = [], {}
            for item in job["outputs"]:
                document_id, output_id = str(item.get("document_id", "")), str(item.get("id", ""))
                require(document_id in documents and document_id not in downloaded
                        and re.fullmatch(r"[a-f0-9]{32}", output_id), "NATIVE_FORMATTER_OUTPUT_ID_INVALID")
                proof = wps_proof(self.run.work / "temp", job_id, document_id, feature, documents[document_id]["sha256"], list(documents).index(document_id))
                target = output / (documents[document_id]["label"] + extension)
                target.write_bytes(self.request(job_path + "/outputs/" + output_id))
                require(target.stat().st_size > 0, "NATIVE_FORMATTER_OUTPUT_EMPTY")
                proofs.append(proof)
                downloaded[document_id] = target
            return downloaded, proofs
        finally:
            # 服务删除 session 时会清理临时宿主回执，必须在清理前保留脱敏副本。
            try:
                record["host_controls"] = archive_host_controls(
                    self.run.work / "temp", job_id, documents, output, self.safe_value)
            except Exception as exc:  # noqa: BLE001 - 归档失败也保留原作业状态。
                record["archive_error"] = type(exc).__name__
            write_json(job_evidence, record)

    def safe_value(self, value):
        if isinstance(value, str):
            for private in self.private_values:
                value = value.replace(private, "[REDACTED]") if private else value
            return value[:8000]
        if isinstance(value, list):
            return [self.safe_value(item) for item in value]
        if isinstance(value, dict):
            return {key: self.safe_value(item) for key, item in value.items()
                    if not re.search(r"token|ticket|cookie|password|authorization", str(key), re.IGNORECASE)}
        return value

    def safe_job(self, job):
        record = {key: job[key] for key in ("id", "feature_id", "state", "progress", "message", "created_at", "updated_at") if key in job}
        record["items"] = [{key: row[key] for key in ("document_id", "filename", "state", "progress", "message", "error_code") if key in row}
                           for row in job.get("items", [])]
        record["outputs"] = [{key: row[key] for key in ("id", "document_id", "filename", "content_type", "downloaded") if key in row}
                             for row in job.get("outputs", [])]
        return self.safe_value(record)

    def close(self):
        if self.session_path:
            self.request(self.session_path, "DELETE")
            self.session_path = None
            self.token = ""


def archive_host_controls(temp, job_id, documents, output, sanitize):
    """仅归档本次随机 job/document 的控制文件，不保留认证内容或其他用户作业。"""
    native.no_reparse(temp)
    records = []
    for index, document_id in enumerate(documents):
        matches = _control_directories(temp, job_id, document_id, index)
        require(len(matches) <= 1, "NATIVE_CURRENT_WPS_HOST_RESPONSE_AMBIGUOUS")
        if not matches:
            records.append({"document_id": document_id, "status": "host_control_missing"})
            continue
        control = matches[0]
        native.no_reparse(control)
        for name in ("request.json", "response.json", "progress.jsonl"):
            source = control / name
            if not source.exists():
                continue
            native.no_reparse(source)
            require(source.stat().st_size <= 1024**2, "NATIVE_WPS_CONTROL_FILE_TOO_LARGE")
            raw = source.read_text(encoding="utf-8-sig")
            data = [json.loads(line) for line in raw.splitlines() if line.strip()] if name.endswith("jsonl") else json.loads(raw)
            destination = output / "host-controls" / job_id / document_id / (name + ".redacted.json")
            write_json(destination, sanitize(data))
            records.append({"document_id": document_id, "name": name, "original_sha256": sha256(source),
                            "evidence": str(destination.relative_to(output)), "sha256": sha256(destination)})
    return records


def _control_directories(temp, job_id, document_id, document_index=None):
    patterns = ["partyops-official-format-*/jobs/" + job_id + "/" + document_id + "/.source-host"]
    if document_index is not None:
        require(isinstance(document_index, int) and 0 <= document_index < 50, "NATIVE_FORMATTER_DOCUMENT_INDEX_INVALID")
        patterns.append("pf-*/j/" + job_id + "/" + str(document_index) + "/.source-host")
    # 图片转换先由 WPS 在 source-pdf 子目录生成 PDF，再由随包运行时栅格化。
    patterns += [pattern.replace("/.source-host", "/source-pdf/.source-host") for pattern in list(patterns)]
    return [path for pattern in patterns for path in temp.glob(pattern)]


def wps_proof(temp, job_id, document_id, feature, input_hash, document_index=None):
    """仅取当前新 HTTP 作业的受控临时回执，确认实际引擎没有回退到 Word。"""
    require(all(re.fullmatch(r"[a-f0-9]{32}", value) for value in (job_id, document_id)), "NATIVE_FORMATTER_JOB_ID_INVALID")
    native.no_reparse(temp)
    matches = [path / "response.json" for path in _control_directories(temp, job_id, document_id, document_index)
               if (path / "response.json").is_file()]
    require(len(matches) == 1, "NATIVE_CURRENT_WPS_HOST_RESPONSE_MISSING")
    response_path = matches[0]
    response = native.read_json(response_path)
    request_path = response_path.with_name("request.json")
    request = native.read_json(request_path)
    require(request.get("feature_id") == feature and request.get("host_preference") == "wps"
            and isinstance(request.get("source_paths"), list) and len(request["source_paths"]) == 1,
            "NATIVE_CURRENT_WPS_REQUEST_MISMATCH")
    source = Path(request["source_paths"][0])
    native.no_reparse(source)
    require(source.resolve().is_relative_to(temp.resolve()) and sha256(source) == input_hash,
            "NATIVE_CURRENT_WPS_INPUT_MISMATCH")
    jobs = response.get("jobs", [])
    require(not response.get("fatal") and len(jobs) == 1 and jobs[0].get("success") is True
            and re.search(r"wps", str(jobs[0].get("host_display_name", "")), re.IGNORECASE), "NATIVE_ACTUAL_WPS_NOT_PROVEN")
    return {"feature": feature, "input_sha256": input_hash, "host_display_name": jobs[0]["host_display_name"],
            "request_sha256": sha256(request_path), "response_sha256": sha256(response_path)}


def exercise(run):
    output = run.reports / ("formatter-golden-" + secrets.token_hex(12))
    output.mkdir()
    run.last_formatter_evidence = output / "evidence.json"
    oracle_path = native.REPO / "scripts/verify-document-formatter-parity.py"
    oracle = load(oracle_path, "native_installed_golden_oracle")
    fixtures = native.REPO / "backend/tests/fixtures/document-formatter-source"
    source, golden = fixtures / "input-manual-break.docx", fixtures / "expected-source-formatted.docx"
    source_hash, golden_hash = sha256(source), sha256(golden)
    record = {"scope": "native-standard-user-installed-wps-golden", "generated_at": native.now(),
              "context": run.context, "status": "failed", "runtime_environment_passed": False,
              "source_sha256": source_hash, "golden_sha256": golden_hash, "oracle_sha256": sha256(oracle_path),
              "render_location": "same-ordinary-user-installed-WPS-HTTP", "outputs": []}
    client = None
    try:
        binding = native.read_json(run.run_directory / "install-binding.json")
        manifest_path = Path(native.INSTALL) / "release-manifest.json"
        require(binding.get("source_fingerprint") == run.context["source_fingerprint"]
                and binding.get("package", {}).get("sha256") == run.context["package_sha256"]
                and sha256(manifest_path) == binding["windows_payload"]["manifest"]["sha256"],
                "NATIVE_FORMATTER_INSTALL_MANIFEST_CHANGED")
        manifest = native.read_json(manifest_path)
        host_relative = "formatter-host/PartyOps.DocumentFormatter.Host.exe"
        entries = [row for row in manifest["files"] if row.get("path") == host_relative]
        host = Path(native.INSTALL) / host_relative
        native.no_reparse(host)
        require(len(entries) == 1 and sha256(host) == entries[0]["sha256"], "NATIVE_INSTALLED_FORMATTER_HOST_CHANGED")
        record["host_sha256"] = entries[0]["sha256"]
        record["installed_manifest_sha256"] = sha256(manifest_path)
        client = InstalledFormatter(run)
        record["formatter_listener"] = client.guard()
        capabilities = client.request("/v1/capabilities")
        require(capabilities.get("source_host_ready") is True, "NATIVE_SOURCE_HOST_NOT_READY")
        documents = {client.upload(source, name): {"sha256": source_hash, "label": "installed-result-" + str(index)}
                     for index, name in enumerate(("中文 空格一.docx", "中文 空格二.docx"), 1)}
        outputs, proofs = client.job("format", documents, {"scope": "full"}, output, ".docx")
        record["wps_engine_evidence"] = proofs
        signature = oracle._semantic_equivalence_signature(oracle._document_signature(golden))
        for path in outputs.values():
            actual = oracle._document_signature(path)
            oracle._require_contract(actual)
            require(oracle._semantic_equivalence_signature(actual) == signature, "NATIVE_GOLDEN_SEMANTIC_MISMATCH")
        render_sources = [golden, *outputs.values()]
        render_docs = {client.upload(path, path.name): {"sha256": sha256(path), "label": "render-" + str(index)}
                       for index, path in enumerate(render_sources)}
        pdfs, render_proofs = client.job("convert", render_docs, {"target_format": "pdf"}, output, ".pdf")
        record["wps_engine_evidence"] += render_proofs
        ordered = [pdfs[key] for key in render_docs]
        for path, pdf in zip(outputs.values(), ordered[1:]):
            comparison = oracle._visual_page_comparison(ordered[0], pdf, allow_rasterization_noise=False)
            record["outputs"].append({"filename": path.name, "sha256": sha256(path), "pdf_sha256": sha256(pdf),
                                      "semantic_contract": "passed", "visual_comparison": comparison})
            require(comparison["passed"], "NATIVE_GOLDEN_VISUAL_MISMATCH")
        require(sha256(source) == source_hash and sha256(golden) == golden_hash, "NATIVE_GOLDEN_INPUT_CHANGED")
        record["status"] = "passed"
    except Exception as exc:  # noqa: BLE001 - HTTP 验收出口脱敏，原错误不得携带会话令牌。
        record["error"] = str(exc) if isinstance(exc, RuntimeError) and re.fullmatch(r"[A-Z0-9_]+", str(exc)) else type(exc).__name__
        raise RuntimeError(record["error"]) from None
    finally:
        if client is not None:
            record["jobs"] = getattr(client, "job_records", [])
            if any(job.get("archive_error") for job in record["jobs"]):
                record["status"] = "failed"
                record["archive_error"] = "NATIVE_WPS_CONTROL_ARCHIVE_FAILED"
            try:
                client.close()
            except Exception:  # noqa: BLE001 - 清理失败保留失败状态，不能覆盖原始故障。
                record["status"] = "failed"
                record["cleanup_error"] = "NATIVE_FORMATTER_SESSION_CLEANUP_FAILED"
        write_json(output / "evidence.json", record)
    require(record["status"] == "passed", record.get("archive_error", "NATIVE_FORMATTER_SESSION_CLEANUP_FAILED"))
    return output / "evidence.json"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("run-directory", "work", "data-directory", "identity-receipt"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--wizard-receipt", type=Path)
    parser.add_argument("--runtime-receipt", type=Path)
    parser.add_argument("--port", type=int, required=True)
    args = parser.parse_args()
    args.phase = "formatter-probe"
    run = None
    try:
        run = native.NativeRun(args)
        result = exercise(run)
        print(json.dumps({"status": "passed", "evidence": str(result), "runtime_environment_passed": False}, ensure_ascii=False))
        return 0
    except Exception as exc:  # noqa: BLE001 - CLI 只输出稳定错误码，保全本机票据。
        code = str(exc) if isinstance(exc, RuntimeError) and re.fullmatch(r"[A-Z0-9_]+", str(exc)) else type(exc).__name__
        print(json.dumps({"status": "failed", "error": code,
                          "evidence": str(run.last_formatter_evidence) if run is not None and hasattr(run, "last_formatter_evidence") else None,
                          "runtime_environment_passed": False}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
