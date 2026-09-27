"""核验 Linux 安装版 HTTP 下载成品；只读比较，不重新排版或修改输出。"""
from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path


def verify(evidence_path, golden, oracle_path, office):
    if sys.platform != "linux":
        raise RuntimeError("此探针仅适用于 Linux Guest 的既有渲染阈值")
    record = json.loads(evidence_path.read_text(encoding="utf-8"))
    if record.get("status") != "passed" or record.get("scope") != "installed-program-http-batch-formatter-only":
        raise RuntimeError("必须先完成安装版真实 HTTP 排版")
    if len(record.get("outputs", [])) != 2:
        raise RuntimeError("必须包含两个实际批量下载成品")
    spec = importlib.util.spec_from_file_location("formatter_golden_oracle", oracle_path)
    oracle = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(oracle)
    root = evidence_path.resolve().parent
    work = root / "download-golden-review"
    work.mkdir(exist_ok=False)
    result = {"status": "failed", "scope": "installed-http-download-golden-only",
              "runtime_environment_passed": False,
              "api_evidence_sha256": oracle._sha256(evidence_path),
              "golden_sha256": oracle._sha256(golden), "oracle_sha256": oracle._sha256(oracle_path),
              "office_sha256": oracle._sha256(office), "outputs": []}
    try:
        golden_signature = oracle._semantic_equivalence_signature(oracle._document_signature(golden))
        oracle._render_page_hashes(golden, office, work / "golden")
        golden_pdf = work / "golden" / (golden.stem + ".pdf")
        for index, item in enumerate(record["outputs"], 1):
            output = (root / item["filename"]).resolve(strict=True)
            if output.parent != root or oracle._sha256(output) != item["sha256"]:
                raise RuntimeError("下载成品越界或哈希不一致")
            signature = oracle._document_signature(output)
            oracle._require_contract(signature)
            if oracle._semantic_equivalence_signature(signature) != golden_signature:
                raise RuntimeError("下载成品与原工具金样语义不一致")
            pages = oracle._render_page_hashes(output, office, work / str(index))
            visual = oracle._visual_page_comparison(
                golden_pdf, work / str(index) / (output.stem + ".pdf"), allow_rasterization_noise=True
            )
            result["outputs"].append({**item, "paragraph_count": len(signature["paragraphs"]),
                                      "pages": pages, "visual_comparison": visual})
            if not visual["passed"]:
                raise RuntimeError("下载成品视觉金样不一致")
        result["status"] = "passed"
    finally:
        result["verified_at"] = datetime.now(timezone(timedelta(hours=8))).isoformat(timespec="seconds")
        result["timezone"] = "Asia/Shanghai"
        (work / "download-golden-evidence.json").write_text(
            json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )
    return result


def main():
    parser = argparse.ArgumentParser()
    for name in ("evidence", "golden", "oracle", "office"):
        parser.add_argument("--" + name, required=True, type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(args.evidence, args.golden, args.oracle, args.office), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
