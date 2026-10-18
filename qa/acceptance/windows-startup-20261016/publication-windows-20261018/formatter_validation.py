"""复用已验收严格排版验证器；正式buildreceipt由新候选显式传入，旧写入口不调用。"""
from pathlib import Path
import re
import release_driver as p

def qa_driver(filename):
    return p.load("new_formatter_readonly_verifier", p.HERE.parent / filename)


def formatter_evidence(path, value, build_receipt):
    p.require(path.name == "summary.json" and re.fullmatch(r"win10-formatter-[a-f0-9]{12}", path.parent.name)
              and value.get("scope") == "original-win10-x64-installed-http-formatter"
              and value.get("status") == "passed" and value.get("formatter_acceptance_passed") is True
              and value.get("executable_checks_status") == "passed" and value.get("font_render_use") == "passed"
              and value.get("six_feature_native_host_proof") == "passed" and value.get("completed_at")
              and not value.get("error") and not value.get("cleanup_error"), "FINAL_WPS_CONTROLLER_ACCEPTANCE_REQUIRED")
    formatter = qa_driver("win10-installed-formatter.py")
    checks = value.get("checks", [])
    p.require(len({c.get("id") for c in checks}) == len(checks), "DUPLICATE_FORMATTER_CHECKS")
    checks = {c["id"]: c for c in checks}
    p.require(set(checks) == {"installed-wps-x64-font-files", "feature-native-source-host", "six-feature-http-output-contracts",
                            "format-and-pdf-golden", "five-font-visible-pdf-glyphs"}, "FORMATTER_CHECKS_INCOMPLETE")
    formatter.validate_wps(checks["installed-wps-x64-font-files"]["result"])
    native, http = checks["feature-native-source-host"], checks["six-feature-http-output-contracts"]
    feature_folder = path.parent
    resume = value.get("resume_sources")
    if resume is not None:
        p.require(isinstance(resume, dict) and re.fullmatch(r"win10-formatter-[a-f0-9]{12}", str(resume.get("run", "")))
                  and resume["run"] != path.parent.name, "FORMATTER_RESUME_SOURCE_INVALID")
        feature_folder = path.parent.parent / resume["run"]
        shared = formatter.load(formatter.ROOT / "qa/vm-lab/scripts/verify-win10-x86-installed-wps.py", "publication_resume_oracle")
        inherited, recomputed_sources = formatter.resume_font_evidence(feature_folder, value["candidate"], value.get("native_host_sha256"), shared.oracle)
        p.require(resume == recomputed_sources and all(checks.get(row["id"]) == row for row in inherited),
                  "FORMATTER_RESUME_EVIDENCE_CHANGED")
    feature_file = feature_folder / "installed-features-evidence.json"
    features = p.read(feature_file)
    p.require(http.get("status") == "passed" and http.get("cases") == 9 and http.get("image_decode") == "passed"
              and native.get("result") == formatter.verify_native_cases(features, native.get("proofs", {}), value.get("native_host_sha256")),
              "WPS_NATIVE_FEATURE_OUTPUTS_REQUIRED")
    build = p.read(build_receipt); payload = Path(build["windows_payload"]["manifest"]["path"])
    p.require(p.digest(payload) == value["candidate"]["manifest_sha256"], "WPS_PAYLOAD_MANIFEST_CHANGED")
    hosts = [r for r in p.read(payload)["files"] if r.get("path") == "formatter-host/PartyOps.DocumentFormatter.Host.exe"]
    p.require(len(hosts) == 1 and hosts[0]["sha256"] == value.get("native_host_sha256"), "WPS_NATIVE_HOST_CANDIDATE_REQUIRED")
    outputs = []
    for case in features["cases"]:
        p.require(case.get("status") == "passed" and case.get("job", {}).get("state") == "completed", "FEATURE_TERMINAL_SUCCESS_REQUIRED")
        outputs += [(feature_folder, r["filename"], r["sha256"]) for r in case["outputs"]]
    golden, fonts = checks["format-and-pdf-golden"], checks["five-font-visible-pdf-glyphs"]
    p.require(golden.get("status") == fonts.get("status") == "passed"
              and len(golden.get("visual_comparisons", [])) == 2 and all(r.get("passed") is True for r in golden["visual_comparisons"])
              and len(golden.get("host_proofs", [])) == 5 and len(fonts.get("native_host_proofs", [])) == 1,
              "GOLDEN_AND_FONT_NATIVE_PROOFS_REQUIRED")
    for proof in golden["host_proofs"] + fonts["native_host_proofs"]:
        p.require("wps" in proof.get("host_display_name", "").lower() and proof.get("feature") in {"format", "convert"}
                  and all(re.fullmatch(r"[a-f0-9]{64}", proof.get(k, "")) for k in ("input_sha256", "request_sha256", "response_sha256")),
                  "GOLDEN_NATIVE_HOST_REQUIRED")
    font_rows = fonts.get("fonts", [])
    p.require(len(font_rows) == 5 and {r.get("font") for r in font_rows} == {r[0] for r in formatter.FONT_WITNESSES}
              and font_rows == formatter.font_render_proof(path.parent / "five-font-witness.pdf"),
              "ACTUAL_FIVE_FONT_RENDER_REQUIRED")
    outputs.append((path.parent, "five-font-witness.pdf", fonts.get("pdf_sha256")))
    outputs.append((path.parent, "five-font-witness.docx", fonts.get("input_sha256")))
    jobs = []
    for job_path in path.parent.glob("job-*.json"):
        job = p.read(job_path)
        p.require(re.fullmatch(r"[a-f0-9]{32}", str(job.get("id", ""))) and job_path.name == "job-" + job["id"] + ".json",
                  "FORMATTER_JOB_ID_BINDING_INVALID")
        jobs.append(job)
    expected_features = ["convert"] if resume is not None else ["convert", "convert", "format"]
    p.require(len(jobs) == len(expected_features) and sorted(j.get("feature") for j in jobs) == expected_features
              and all(j.get("state") == "completed" for j in jobs), "GOLDEN_DOWNLOADED_JOB_EVIDENCE_REQUIRED")
    proof_key = lambda row: tuple(row.get(key) for key in ("input_sha256", "request_sha256", "response_sha256"))
    expected_proofs = fonts["native_host_proofs"] if resume is not None else golden["host_proofs"] + fonts["native_host_proofs"]
    expected_proofs = {proof_key(row): row for row in expected_proofs}
    observed = set()
    for job in jobs:
        p.require(len(job.get("host_proofs", [])) == len(job["outputs"]) > 0, "FORMATTER_JOB_NATIVE_OUTPUT_PROOFS_REQUIRED")
        for item, proof in zip(job["outputs"], job["host_proofs"]):
            key = proof_key(proof)
            native_outputs = proof.get("outputs", [])
            target = (path.parent / item["file"]).resolve(strict=True)
            p.require(key not in observed and expected_proofs.get(key) == proof and len(native_outputs) == 1
                      and target.parent == path.parent and target.stat().st_size == native_outputs[0].get("bytes")
                      and target.suffix.lower() == native_outputs[0].get("extension")
                      and p.digest(target) == item["sha256"] == native_outputs[0].get("sha256"), "FORMATTER_NATIVE_DOWNLOAD_BINDING_CHANGED")
            observed.add(key)
            outputs.append((path.parent, item["file"], item["sha256"]))
    p.require(observed == set(expected_proofs), "FORMATTER_NATIVE_JOB_SET_CHANGED")
    p.require(fonts["native_host_proofs"][0]["input_sha256"] == fonts.get("input_sha256")
              and fonts["native_host_proofs"][0]["outputs"][0]["sha256"] == fonts.get("pdf_sha256"), "FONT_NATIVE_DOWNLOAD_BINDING_CHANGED")
    if resume is not None:
        outputs += [(feature_folder, name, p.digest(feature_folder / name)) for name in
                    ("formatted-1.docx", "formatted-2.docx", "render-0.pdf", "render-1.pdf", "render-2.pdf")]
    for folder, name, sha in outputs:
        target = (folder / name).resolve(strict=True)
        p.require(target.parent == folder and target.stat().st_size > 0 and p.digest(target) == sha, "FORMATTER_DOWNLOADED_OUTPUT_CHANGED")
    result = {"summary_sha256": p.digest(path), "feature_evidence_sha256": p.digest(feature_file), "output_count": len(outputs)}
    if resume is not None:
        result["resume_sources"] = recomputed_sources
    return result
