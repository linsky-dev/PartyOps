"""只读补采指定当前run的金样/五字体原生输出证明，不创建会话或提交作业。"""
import argparse
import importlib.util
import json
import re
import time
import uuid
from pathlib import Path

OUTPUT = Path(__file__).resolve().parent
ROOT = OUTPUT.parents[2]


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def collect_job(shared, contract, client, folder, path):
    """只核对本run已落盘job与仍存在的同一原生请求，不伪造documentID。"""
    old = contract.read(path)
    identifier = old.get("id", "")
    shared.require(re.fullmatch(r"[a-f0-9]{32}", identifier) and path.name == "job-" + identifier + ".json",
                   "SUPPLEMENT_JOB_ID_INVALID")
    shared.require(old.get("state") == "completed" and old.get("feature") in {"format", "convert"}, "SUPPLEMENT_JOB_INVALID")
    outputs, proofs = old.get("outputs", []), old.get("host_proofs", [])
    shared.require(isinstance(outputs, list) and isinstance(proofs, list) and 0 < len(outputs) == len(proofs) <= 3,
                   "SUPPLEMENT_PROOF_COUNT_INVALID")
    result = {"job": identifier, "feature": old["feature"], "original_job_sha256": contract.digest(path), "outputs": []}
    for index, (output, proof) in enumerate(zip(outputs, proofs)):
        name = output.get("file", "")
        shared.require(isinstance(name, str) and Path(name).name == name and "\\" not in name
                       and (folder / name).resolve().parent == folder, "SUPPLEMENT_OUTPUT_PATH_INVALID")
        for key in ("input_sha256", "request_sha256", "response_sha256"):
            shared.require(re.fullmatch(r"[a-f0-9]{64}", str(proof.get(key, ""))), "SUPPLEMENT_ORIGINAL_HASH_INVALID")
        fresh = shared.host_proof(client, r"C:\Users\partyopsuser\AppData\Local\Temp", identifier, None,
                                  old["feature"], proof["input_sha256"], index)
        shared.require(all(fresh.get(key) == proof[key] for key in ("input_sha256", "request_sha256", "response_sha256"))
                       and fresh.get("job") == identifier and fresh.get("index") == index, "SUPPLEMENT_ORIGINAL_PROOF_CHANGED")
        native = fresh.get("outputs", [])
        local = folder / name
        shared.require(isinstance(native, list) and len(native) == 1, "SUPPLEMENT_SINGLE_NATIVE_OUTPUT_REQUIRED")
        shared.require(local.is_file() and contract.digest(local) == output.get("sha256") == native[0].get("sha256")
                       and local.stat().st_size == native[0].get("bytes") and local.suffix.lower() == native[0].get("extension"),
                       "SUPPLEMENT_NATIVE_AND_HTTP_OUTPUT_DIFFER")
        result["outputs"].append({"file": name, "sha256": output["sha256"], "bytes": local.stat().st_size,
                                  "native_proof": fresh})
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-dir", type=Path, required=True)
    parser.add_argument("--wait-seconds", type=int, default=900)
    args = parser.parse_args()
    folder = args.run_dir.resolve()
    if folder.parent != OUTPUT or not re.fullmatch(r"win10-formatter-[a-f0-9]{12}", folder.name) or not folder.is_dir():
        raise RuntimeError("SUPPLEMENT_RUN_DIRECTORY_INVALID")
    if not 0 <= args.wait_seconds <= 1800:
        raise RuntimeError("SUPPLEMENT_WAIT_INVALID")
    adapter = load(OUTPUT / "win10-installed-formatter.py", "supplement_adapter")
    shared = load(ROOT / "qa/vm-lab/scripts/verify-win10-x86-installed-wps.py", "supplement_shared")
    # 新宿主证明超过cmd稳定长度；复用现有SFTP暂存，仅执行短读取命令。
    shared.guest_json = lambda client, script, **kwargs: adapter.staged_readonly_probe(
        client, shared.installer.execute, script, "win10-formatter-" + uuid.uuid4().hex[:12])
    windows = shared.windows
    matrix, media = windows.load_configuration()
    matrix["defaults"].update(primary_root=str(adapter.LAB_ROOT), fallback_root=str(adapter.LAB_ROOT))
    lab = windows.QemuLab(matrix, adapter.LAB_ROOT, media)
    adapter.contract.bind_guest(lab, windows.qmp)
    client = windows.windows.connect(lab, matrix["targets"]["win10-x64"])
    attempt = uuid.uuid4().hex[:12]
    record = {"scope": "current-run-readonly-native-output-supplement", "run": folder.name, "attempt": attempt, "status": "failed", "jobs": []}
    deadline = time.monotonic() + args.wait_seconds
    seen = set()
    try:
        while True:
            for path in sorted(folder.glob("job-*.json")):
                if path.name in seen:
                    continue
                try:
                    result = collect_job(shared, adapter.contract, client, folder, path)
                except json.JSONDecodeError:
                    continue  # 写入方尚未完成文件；只重读本地JSON，不重发Guest操作。
                adapter.contract.exclusive(folder / ("native-output-supplement-" + result["job"] + "-" + attempt + ".json"), result)
                record["jobs"].append({"job": result["job"], "outputs": len(result["outputs"])})
                seen.add(path.name)
            if len(seen) == 3:
                record["status"] = "passed"
                break
            if time.monotonic() >= deadline:
                raise RuntimeError("SUPPLEMENT_WAIT_EXPIRED")
            if (folder / "summary.json").exists():
                raise RuntimeError("SUPPLEMENT_RUN_ALREADY_CLEANED")
            time.sleep(5)
    except Exception as error:
        value = str(error)
        record["error"] = value if isinstance(error, RuntimeError) and re.fullmatch(r"[A-Z][A-Z0-9_]+", value) else type(error).__name__
    finally:
        client.close()
        adapter.contract.exclusive(folder / ("native-output-supplement-summary-" + attempt + ".json"), record)
    print(json.dumps(record, ensure_ascii=False))
    return 0 if record["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
