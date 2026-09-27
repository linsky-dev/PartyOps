"""复用已登录的安装版 HTTP 会话，运行六类公文功能的实际作业。"""
import hashlib
import json
import secrets
import time
import zipfile
from pathlib import Path
from xml.etree import ElementTree


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def text(path):
    with zipfile.ZipFile(path) as package:
        xml = package.read("word/document.xml")
    root = ElementTree.fromstring(xml)
    return "".join(root.itertext()), xml.decode("utf-8")


def exercise(request, base, session_path, manifest_path, output):
    manifest = json.loads(manifest_path.read_text())
    results = []
    for case in manifest["cases"]:
        source = (manifest_path.parent / case["source"]).resolve(strict=True)
        if source.parent != manifest_path.parent.resolve() or sha256(source) != case["sha256"]:
            raise RuntimeError("FEATURE_INPUT_IDENTITY_MISMATCH")
        boundary = "PartyOpsQA" + secrets.token_hex(12)
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="document"; filename="{source.name}"\r\n'
                'Content-Type: application/octet-stream\r\n\r\n').encode()
        body += source.read_bytes() + f"\r\n--{boundary}--\r\n".encode()
        uploaded = request(base, session_path + "/documents", "POST", body,
                           "multipart/form-data; boundary=" + boundary)
        job = request(base, session_path + "/jobs", "POST", {
            "feature_id": case["feature_id"], "document_ids": [uploaded["document_id"]], "options": case["options"]})
        job_path = session_path + "/jobs/" + job["id"]
        deadline = time.monotonic() + 900
        while job["state"] in {"queued", "running"}:
            if time.monotonic() >= deadline:
                raise RuntimeError("FEATURE_JOB_TIMEOUT:" + case["id"])
            time.sleep(2)
            job = request(base, job_path)
        record = {"id": case["id"], "feature_id": case["feature_id"], "job": job, "outputs": [], "status": "failed"}
        results.append(record)
        evidence = output / "installed-features-evidence.json"
        evidence.write_text(json.dumps({"status": "running", "cases": results}, ensure_ascii=False, indent=2))
        if job["state"] != "completed" or not job["outputs"]:
            raise RuntimeError("FEATURE_JOB_FAILED:" + case["id"])
        matching = []
        for index, item in enumerate(job["outputs"]):
            filename = item.get("filename", item.get("name", ""))
            extension = Path(filename).suffix.lower()
            if extension not in {".docx", ".pdf", ".txt", ".png", ".jpg", ".jpeg"}:
                raise RuntimeError("FEATURE_OUTPUT_EXTENSION_UNKNOWN:" + repr(item))
            path = output / (case["id"] + "-" + str(index) + extension)
            path.write_bytes(request(base, job_path + "/outputs/" + item["id"]))
            if not path.stat().st_size:
                raise RuntimeError("FEATURE_OUTPUT_EMPTY")
            record["outputs"].append({"filename": path.name, "download_filename": filename, "sha256": sha256(path)})
            if extension == case["extension"]:
                matching.append((path, filename))
        if not matching:
            raise RuntimeError("FEATURE_EXPECTED_OUTPUT_MISSING:" + case["id"])
        path, filename = matching[0]
        content, xml = (text(path) if path.suffix == ".docx" else ("", ""))
        if path.suffix == ".txt":
            content = path.read_text(encoding="utf-8")
        if path.suffix == ".pdf" and not path.read_bytes().startswith(b"%PDF"):
            raise RuntimeError("FEATURE_PDF_INVALID")
        for expected in case.get("contains", []):
            if expected not in content:
                raise RuntimeError("FEATURE_CONTENT_MISSING:" + expected)
        for forbidden in case.get("excludes", []):
            if forbidden in content:
                raise RuntimeError("FEATURE_OLD_CONTENT_RETAINED:" + forbidden)
        if case["id"] == "replace" and (not ({"黑体", "SimHei"} & set(xml.split('"'))) or 'w:sz w:val="36"' not in xml):
            raise RuntimeError("FEATURE_FONT_REPLACE_FAILED")
        if case["id"] == "rename" and ("定稿" not in filename or "-" not in filename):
            raise RuntimeError("FEATURE_RENAME_RULE_NOT_APPLIED")
        if sha256(source) != case["sha256"]:
            raise RuntimeError("FEATURE_INPUT_CHANGED")
        record["status"] = "passed"
        evidence.write_text(json.dumps({"status": "running", "cases": results}, ensure_ascii=False, indent=2))
    result = {"scope": "installed-http-features-only", "status": "passed", "runtime_environment_passed": False,
              "case_count": len(results), "manifest_sha256": sha256(manifest_path), "cases": results}
    evidence.write_text(json.dumps(result, ensure_ascii=False, indent=2))
    return result
