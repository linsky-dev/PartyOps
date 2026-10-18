"""只读调研正式冻结模块子集；不构成完整PYZ/入口/运行钩子等价证明，不用于发布复用。"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import struct
import types

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
LAB = Path("D:/PartyOps-VM-Lab")
OLD_SHA = "3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27"
SNAPSHOT = HERE / "historical-wps-domain.json"


def require(value, code):
    if not value:
        raise RuntimeError(code)


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def evidence(path):
    path = Path(path).resolve(strict=True)
    return {"path": str(path), "sha256": digest(path), "bytes": path.stat().st_size}


def exclusive(path, value):
    with Path(path).open("x", encoding="utf-8") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)


def canonical_code(value):
    """保留code全部语义/行表/常量/嵌套code；仅规范co_filename。"""
    if isinstance(value, types.CodeType):
        names = ("co_argcount", "co_posonlyargcount", "co_kwonlyargcount", "co_nlocals", "co_stacksize", "co_flags",
                 "co_code", "co_consts", "co_names", "co_varnames", "co_freevars", "co_cellvars", "co_name", "co_qualname",
                 "co_firstlineno", "co_linetable", "co_exceptiontable")
        return {name: canonical_code(getattr(value, name)) for name in names}
    if isinstance(value, bytes): return {"bytes": value.hex()}
    if isinstance(value, tuple): return {"tuple": [canonical_code(item) for item in value]}
    if isinstance(value, frozenset):
        return {"frozenset": sorted((canonical_code(item) for item in value), key=lambda row: json.dumps(row, sort_keys=True))}
    if isinstance(value, float): return {"float64": struct.pack(">d", value).hex()}
    if isinstance(value, complex): return {"complex": [canonical_code(value.real), canonical_code(value.imag)]}
    if value is Ellipsis: return {"ellipsis": True}
    require(value is None or type(value) in {str, bool, int}, "FROZEN_CODE_CONSTANT_UNSUPPORTED")
    return value


def code_digest(code):
    return hashlib.sha256(json.dumps(canonical_code(code), ensure_ascii=True, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def official_payload(receipt_path, package_sha):
    receipt_path = Path(receipt_path).resolve(strict=True)
    require(receipt_path == (LAB / "state/builds" / (package_sha + ".json")).resolve(), "FORMAL_BUILD_RECEIPT_LOCATION_REQUIRED")
    receipt = read(receipt_path)
    require(receipt.get("status") == "completed" and receipt.get("exit_code") == 0 and not receipt.get("errors")
            and receipt.get("output_created_during_build") is True and receipt.get("source_before") == receipt.get("source_after")
            and receipt.get("package", {}).get("sha256") == package_sha, "SUCCESSFUL_FORMAL_BUILD_REQUIRED")
    payload = receipt["windows_payload"]
    manifest_path = Path(payload["manifest"]["path"]).resolve(strict=True)
    require(manifest_path.is_relative_to((LAB / "reports").resolve()) and digest(manifest_path) == payload["manifest"]["sha256"]
            and manifest_path.stat().st_size == payload["manifest"]["bytes"]
            and payload.get("capture") == "controller-after-successful-build" and payload.get("package_sha256") == package_sha,
            "FORMAL_PAYLOAD_MANIFEST_CHANGED")
    log = Path(receipt["log"]["path"]).resolve(strict=True)
    require(log.is_relative_to((LAB / "reports").resolve()) and digest(log) == receipt["log"]["sha256"], "FORMAL_BUILD_LOG_CHANGED")
    manifest = read(manifest_path)
    require(manifest.get("product") == "PartyOps" and manifest.get("platform") == "windows" and manifest.get("architecture") == "amd64",
            "WINDOWS_AMD64_PAYLOAD_REQUIRED")
    files = {row["path"]: row for row in manifest["files"]}
    require(len(files) == len(manifest["files"]), "PAYLOAD_DUPLICATE_ENTRY")
    stage = Path(payload["staging_directory"]).resolve(strict=True)
    require(stage.is_relative_to((ROOT / "artifacts").resolve()), "FORMAL_STAGE_OUTSIDE_ARTIFACTS")
    executable = stage / "PartyOps.exe"
    require(digest(executable) == files["PartyOps.exe"]["sha256"] and executable.stat().st_size == files["PartyOps.exe"]["size"],
            "FORMAL_FROZEN_EXECUTABLE_CHANGED")
    return receipt, stage, files


def frozen_wps_domain(receipt_path, package_sha):
    from PyInstaller.archive.readers import CArchiveReader
    receipt, stage, files = official_payload(receipt_path, package_sha)
    archive = CArchiveReader(str(stage / "PartyOps.exe")).open_embedded_archive("PYZ.pyz")
    modules, sources = {}, {}
    prefixes = ("PIL", "docx", "fitz", "pymupdf", "lxml", "win32com", "pythoncom", "pywintypes")
    selected = sorted(name for name in archive.toc if name == "app" or name.startswith("app.")
                      or any(name == prefix or name.startswith(prefix + ".") for prefix in prefixes))
    require("app.official_format_service" in selected and "app.official_format_features" in selected, "OFFICIAL_FORMAT_MODULES_REQUIRED")
    for name in selected:
        if name == "app.setup_wizard":
            continue  # 本次确实修改，必须新包主机/恢复实测；绝不据此复用该域。
        code = archive.extract(name)
        require(isinstance(code, types.CodeType), "FROZEN_MODULE_CODE_REQUIRED")
        modules[name] = code_digest(code)
        if name == "app" or name.startswith("app."):
            source = ROOT / "backend" / name.replace(".", "/")
            source = source.with_suffix(".py") if source.with_suffix(".py").is_file() else source / "__init__.py"
            require(source.is_file(), "FROZEN_BUSINESS_SOURCE_MISSING")
            compiled = compile(source.read_bytes(), code.co_filename, "exec", dont_inherit=True, optimize=0)
            require(code_digest(compiled) == modules[name], "FROZEN_BUSINESS_SOURCE_MISMATCH:" + name)
            sources[source.relative_to(ROOT).as_posix()] = digest(source)
    # 除各launcher整体exe外，整个_internal及原生排版宿主/Office runtime按实际字节比较。
    components = {name: {"size": row["size"], "sha256": row["sha256"]} for name, row in files.items()
                  if name.startswith(("_internal/", "formatter-host/", "office-runtime/"))}
    require(any(name.startswith("formatter-host/") for name in components), "NATIVE_FORMATTER_COMPONENTS_REQUIRED")
    for name, row in components.items():
        path = (stage / name).resolve(strict=True)
        require(path.is_relative_to(stage) and path.stat().st_size == row["size"] and digest(path) == row["sha256"],
                "FORMAL_WPS_COMPONENT_BYTES_CHANGED")
    return {"schema": 1, "kind": "readonly-formal-pyz-current-source-compilation-correspondence",
            "package_sha256": package_sha, "build_receipt": evidence(receipt_path),
            "source_fingerprint_at_build": receipt["source_before"], "payload_manifest": receipt["windows_payload"]["manifest"],
            "frozen_executable_sha256": files["PartyOps.exe"]["sha256"],
            "modules": [{"name": name, "sha256": sha} for name, sha in modules.items()],
            "business_sources": [{"name": name, "sha256": sha} for name, sha in sources.items()],
            "components": [{"name": name, **row} for name, row in components.items()],
            "excluded_domain": {"app.setup_wizard": "changed-host-transactions-require-new-candidate-host-and-restore-evidence"},
            "claims": {"historical_per_file_source_inventory_at_original_build": False, "extracted_business_modules_executed": False}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--old-build-receipt", required=True, type=Path)
    args = parser.parse_args()
    result = frozen_wps_domain(args.old_build_receipt, OLD_SHA)
    exclusive(SNAPSHOT, result)
    print(json.dumps({"status": "historical-wps-domain-captured", "path": str(SNAPSHOT), "sha256": digest(SNAPSHOT),
                      "modules": len(result["modules"]), "sources": len(result["business_sources"]), "components": len(result["components"])}))


if __name__ == "__main__":
    main()
