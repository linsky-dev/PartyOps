"""通过 Win10 x86 已绑定 SSH 会话验收安装版 WPS 与公文逐页排版。"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import re
import secrets
import struct
import sys
import time
import urllib.parse
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
REPO = HERE.parent.parent
sys.path.insert(0, str(HERE))

from evidence import now, safe_child, sha256, write_json
from identity import probe, runtime_binding
from lab import fingerprint, inventory, load_configuration
from provenance import bind_package
from providers import QemuLab


def load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec is not None and spec.loader is not None
    spec.loader.exec_module(module)
    return module


windows = load(HERE / "scripts/exercise-windows-business.py", "win10_wps_windows_business")
installer = load(HERE / "scripts/exercise-windows-install.py", "win10_wps_installer_transport")
oracle = load(REPO / "scripts/verify-document-formatter-parity.py", "win10_wps_formatter_oracle")
require = windows.require
TARGET = "win10-x86"
VERSION = "12.1.0.28505"
SETUP_NAME = "WPS_Setup_28505.exe"


def pe32(path: Path, expected_sha256: str) -> dict:
    """本地安装器必须是已钉住哈希的 32 位 PE，而不是只凭文件名。"""
    require(path.is_file() and path.name == SETUP_NAME, "WPS_INSTALLER_INPUT_MISSING")
    require(re.fullmatch(r"[0-9a-f]{64}", expected_sha256) is not None, "WPS_INSTALLER_SHA256_INVALID")
    require(sha256(path) == expected_sha256, "WPS_INSTALLER_SHA256_MISMATCH")
    with path.open("rb") as stream:
        head = stream.read(64)
        require(len(head) == 64 and head[:2] == b"MZ", "WPS_INSTALLER_NOT_PE")
        offset = struct.unpack_from("<I", head, 60)[0]
        require(64 <= offset <= path.stat().st_size - 26, "WPS_INSTALLER_PE_OFFSET_INVALID")
        stream.seek(offset)
        header = stream.read(26)
    require(header[:4] == b"PE\0\0" and struct.unpack_from("<H", header, 4)[0] == 0x14C
            and struct.unpack_from("<H", header, 24)[0] == 0x10B, "WPS_INSTALLER_NOT_PE32_X86")
    return {"path": str(path.resolve()), "sha256": expected_sha256, "bytes": path.stat().st_size,
            "pe_machine": 0x14C, "pe_optional_magic": 0x10B}


def product_binding(lab: QemuLab, install_report: Path) -> tuple[dict, dict, dict]:
    """以控制器当前状态重新绑定基线和包，不采信待导入回执作为预期值。"""
    state = lab.state(TARGET)
    require(lab.live(state), "WIN10_X86_GUEST_NOT_RUNNING")
    system = probe(lab, TARGET)
    require(system.get("installed_package") is True, "PARTYOPS_INSTALL_PRECONDITION_MISSING")
    environment = runtime_binding(lab, TARGET)
    packages, errors = inventory(lab.matrix, REPO / "artifacts")
    package_id = next(key for key, row in lab.matrix["packages"].items()
                      if TARGET in row["required_targets"])
    require(package_id not in errors, "CURRENT_PRODUCT_PACKAGE_INVALID")
    package = bind_package(lab, packages[package_id], fingerprint())
    path = safe_child(lab.root / "reports" / TARGET, install_report / "installed-probe.json")
    receipt = json.loads(path.read_text(encoding="utf-8"))
    require(receipt.get("target") == TARGET and receipt.get("guest_uuid") == state["uuid"]
            and receipt.get("environment") == environment
            and receipt.get("restore_generation") == state.get("restore_generation")
            and receipt.get("package") == package
            and receipt.get("install", {}).get("exit_code") == 0,
            "PRODUCT_INSTALL_RECEIPT_BINDING_MISMATCH")
    evidence = receipt.get("evidence", {}).get("install_log", {})
    log = safe_child(path.parent, path.parent / evidence.get("path", "missing"))
    require(log.is_file() and sha256(log) == evidence.get("sha256"), "PRODUCT_INSTALL_LOG_CHANGED")
    context = {"target": TARGET, "package": package, "environment": environment,
               "restore_generation": state.get("restore_generation")}
    return context, system, {"path": str(path), "sha256": sha256(path)}


def guest_wps_script(expected_sha256: str, version: str, sid: str) -> str:
    """只读读取 Guest 官方输入、系统卸载登记和普通用户 COM 登记。"""
    require(re.fullmatch(r"[0-9a-f]{64}", expected_sha256) is not None, "WPS_INSTALLER_SHA256_INVALID")
    require(version == VERSION and re.fullmatch(r"S-1-5-21-(?:\d+-){3}\d+", sid) is not None,
            "WPS_GUEST_IDENTITY_INVALID")
    return f"$expectedHash='{expected_sha256}'\n$expectedVersion='{version}'\n$sid='{sid}'\n" + r"""
$ErrorActionPreference='Stop'
$staged='C:\PartyOps-QA\incoming\WPS_Setup_28505.exe'
if(-not(Test-Path -LiteralPath $staged -PathType Leaf)) {throw 'WPS_GUEST_INSTALLER_INPUT_MISSING'}
$hash=(Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash.ToLowerInvariant()
if($hash -ne $expectedHash) {throw 'WPS_GUEST_INSTALLER_HASH_MISMATCH'}
function Get-Pe32Machine([string]$path) {
  $reader=New-Object IO.BinaryReader([IO.File]::OpenRead($path))
  try {
    if($reader.ReadUInt16() -ne 23117) {throw 'WPS_GUEST_PE_INVALID'}
    $reader.BaseStream.Position=60;$offset=$reader.ReadUInt32()
    if($offset -lt 64 -or $offset -gt ($reader.BaseStream.Length-26)) {throw 'WPS_GUEST_PE_INVALID'}
    $reader.BaseStream.Position=$offset
    if($reader.ReadUInt32() -ne 17744) {throw 'WPS_GUEST_PE_INVALID'}
    $machine=$reader.ReadUInt16();$reader.BaseStream.Position=$offset+24
    $magic=$reader.ReadUInt16()
    if($machine -ne 332 -or $magic -ne 267) {throw 'WPS_GUEST_NOT_PE32_X86'}
    return [int]$machine
  }finally {$reader.Close()}
}
$null=Get-Pe32Machine $staged
$roots=@('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
  'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
  ('Registry::HKEY_USERS\'+$sid+'\Software\Microsoft\Windows\CurrentVersion\Uninstall'))
$rows=@(foreach($root in $roots) {if(Test-Path -LiteralPath $root) {
  Get-ChildItem -LiteralPath $root | ForEach-Object {
    $item=Get-ItemProperty -LiteralPath $_.PSPath
    if([string]$item.DisplayName -match 'WPS Office') {
      [ordered]@{name=[string]$item.DisplayName;version=[string]$item.DisplayVersion;
        location=[string]$item.InstallLocation;icon=[string]$item.DisplayIcon;registry=$_.Name}
    }
  }
}})
if($rows.Count -ne 1 -or $rows[0].version -ne $expectedVersion) {throw 'WPS_INSTALL_REGISTRATION_MISSING_OR_VERSION_MISMATCH'}
$location=$rows[0].location.Trim('"').TrimEnd('\')
if(-not $location -or -not(Test-Path -LiteralPath $location -PathType Container)) {throw 'WPS_REGISTERED_LOCATION_MISSING'}
$executables=@(Get-ChildItem -LiteralPath $location -Filter wps.exe -File -Recurse -ErrorAction Stop)
if($executables.Count -ne 1) {throw 'WPS_REGISTERED_EXECUTABLE_NOT_UNIQUE'}
$exe=$executables[0].FullName
$null=Get-Pe32Machine $exe
$actualVersion=[string](Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
if($actualVersion -ne $expectedVersion) {throw 'WPS_EXECUTABLE_VERSION_MISMATCH'}
$classRoots=@(('Registry::HKEY_USERS\'+$sid+'\Software\Classes'), 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes')
$servers=@(foreach($root in $classRoots) {
  $prog=Join-Path $root 'KWPS.Application\CLSID'
  if(Test-Path -LiteralPath $prog) {
    $clsid=[string](Get-Item -LiteralPath $prog).GetValue('')
    if($clsid) {$server=Join-Path $root ('CLSID\'+$clsid+'\LocalServer32')
      if(Test-Path -LiteralPath $server) {[string](Get-Item -LiteralPath $server).GetValue('')}}
  }
})
$servers=@($servers|Sort-Object -Unique)
if($servers.Count -lt 1 -or @($servers|Where-Object {$_ -notmatch [regex]::Escape($location)}).Count -gt 0) {
  throw 'WPS_STANDARD_USER_COM_NOT_REGISTERED'
}
[ordered]@{version=$actualVersion;installer_sha256=$hash;installed_executable=$exe;
  executable_sha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant();
  pe_machine=332;registration=$rows[0];com_server=$servers[0]}|ConvertTo-Json -Depth 5 -Compress
"""


def validate_wps_detection(row: dict, expected_sha256: str) -> None:
    require(row.get("version") == VERSION and row.get("installer_sha256") == expected_sha256
            and row.get("pe_machine") == 0x14C
            and re.fullmatch(r"[0-9a-f]{64}", str(row.get("executable_sha256", ""))) is not None
            and row.get("installed_executable") and row.get("com_server"),
            "WPS_GUEST_INSTALLATION_UNVERIFIED")


def guest_json(client, script: str, *, timeout: int = 120) -> dict:
    """远程 PowerShell 失败只暴露固定错误码，不把系统输出写进公开报告。"""
    try:
        return json.loads(installer.execute(client, script, timeout=timeout))
    except RuntimeError as exc:
        detail = str(exc)
        if "WPS_INSTALL_REGISTRATION_MISSING_OR_VERSION_MISMATCH" in detail or (
            "WPS_REGISTERED_LOCATION_MISSING" in detail
        ):
            raise RuntimeError("WPS_INSTALL_PRECONDITION_MISSING") from None
        for code in ("WPS_GUEST_INSTALLER_INPUT_MISSING", "WPS_GUEST_INSTALLER_HASH_MISMATCH",
                     "WPS_GUEST_NOT_PE32_X86", "WPS_EXECUTABLE_VERSION_MISMATCH",
                     "WPS_STANDARD_USER_COM_NOT_REGISTERED", "WPS_CURRENT_HOST_PROOF_MISSING_OR_AMBIGUOUS",
                     "WPS_CURRENT_HOST_REQUEST_MISMATCH", "WPS_CURRENT_HOST_INPUT_CHANGED",
                     "WPS_ACTUAL_ENGINE_NOT_PROVEN"):
            if code in detail:
                raise RuntimeError(code) from None
        raise RuntimeError("WPS_GUEST_PROBE_FAILED") from None


def session(run):
    run.wait_health()
    run.login()
    ticket = run.request("/api/v1/official-format/local-ticket", "POST", {"origin": run.origin})
    base = ticket["local_base_url"]
    parsed = urllib.parse.urlsplit(base)
    require(parsed.scheme == "http" and parsed.hostname == "127.0.0.1" and parsed.port
            and not parsed.username and not parsed.password and not parsed.path.strip("/")
            and not parsed.query and not parsed.fragment, "WPS_FORMATTER_NOT_GUEST_LOOPBACK")
    session_info = run.request("/v1/sessions", "POST", {}, base=base,
                               extra_headers={"Authorization": "Bearer " + ticket["ticket"]})
    require(re.fullmatch(r"[a-f0-9]{32}", str(session_info.get("session_id", ""))) is not None
            and isinstance(session_info.get("session_token"), str) and session_info["session_token"],
            "WPS_FORMATTER_SESSION_INVALID")
    prefix = "/v1/sessions/" + session_info["session_id"]
    headers = {"X-PartyOps-Local-Token": session_info["session_token"]}
    return base, prefix, headers


def upload(run, base, prefix, headers, path: Path, filename: str) -> tuple[str, str]:
    content = path.read_bytes()
    require(len(content) < 8 * 1024 * 1024, "WPS_FIXTURE_TOO_LARGE")
    boundary = "PartyOpsQAWps" + secrets.token_hex(12)
    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="document"; filename="{filename}"\r\n'
            'Content-Type: application/vnd.openxmlformats-officedocument.wordprocessingml.document\r\n\r\n').encode()
    body += content + f"\r\n--{boundary}--\r\n".encode()
    document = run.request(prefix + "/documents", "POST", body, base=base,
                           content_type="multipart/form-data; boundary=" + boundary,
                           extra_headers=headers)
    identifier = str(document.get("document_id", ""))
    require(re.fullmatch(r"[a-f0-9]{32}", identifier) is not None, "WPS_DOCUMENT_ID_INVALID")
    return identifier, hashlib.sha256(content).hexdigest()


def job(run, base, prefix, headers, feature: str, documents: dict, options: dict,
        output: Path, extension: str, temp: str) -> tuple[list[Path], list[dict]]:
    response = run.request(prefix + "/jobs", "POST", {"feature_id": feature,
        "document_ids": list(documents), "options": {"compatibility_mode": "wps", **options}},
        base=base, extra_headers=headers)
    job_id = str(response.get("id", ""))
    require(re.fullmatch(r"[a-f0-9]{32}", job_id) is not None, "WPS_JOB_ID_INVALID")
    deadline = time.monotonic() + 900
    while response.get("state") in {"queued", "running"}:
        require(time.monotonic() < deadline, "WPS_JOB_TIMEOUT_CHECK_EXISTING_JOB")
        time.sleep(2)
        response = run.request(prefix + "/jobs/" + job_id, base=base, extra_headers=headers)
    require(response.get("state") == "completed" and len(response.get("outputs", [])) == len(documents),
            "WPS_JOB_NOT_COMPLETED")
    paths, proofs = [], []
    for index, (document_id, data) in enumerate(documents.items()):
        matches = [row for row in response["outputs"] if row.get("document_id") == document_id]
        require(len(matches) == 1 and re.fullmatch(r"[a-f0-9]{32}", str(matches[0].get("id", ""))),
                "WPS_JOB_OUTPUT_ID_INVALID")
        evidence = host_proof(run.client, temp, job_id, document_id, feature, data["sha256"], index)
        target = output / (data["label"] + extension)
        target.write_bytes(run.request(prefix + "/jobs/" + job_id + "/outputs/" + matches[0]["id"],
                                       base=base, extra_headers=headers))
        require(target.stat().st_size > 0, "WPS_OUTPUT_EMPTY")
        paths.append(target)
        proofs.append(evidence)
    write_json(output / ("job-" + job_id + ".json"), {"id": job_id, "feature": feature,
               "state": response["state"], "outputs": [{"file": path.name, "sha256": sha256(path)} for path in paths],
               "host_proofs": proofs})
    return paths, proofs


def host_proof(client, temp: str, job_id: str, document_id: str, feature: str,
               input_hash: str, index: int) -> dict:
    require(all(re.fullmatch(r"[a-f0-9]{32}", value) for value in (job_id, document_id))
            and re.fullmatch(r"[a-f0-9]{64}", input_hash)
            and feature in {"format", "convert"} and 0 <= index < 50,
            "WPS_HOST_PROOF_ARGUMENT_INVALID")
    require(re.fullmatch(r"[A-Za-z]:\\Users\\[^\\'\r\n]+\\AppData\\Local\\Temp", temp) is not None,
            "WPS_USER_TEMP_PATH_INVALID")
    # 主机只接收当前随机作业的脱敏字段，不读取其他用户或旧作业。
    script = (f"$temp='{temp}'\n$job='{job_id}'\n$document='{document_id}'\n"
              f"$feature='{feature}'\n$expected='{input_hash}'\n$index={index}\n") + r"""
$ErrorActionPreference='Stop'
if(-not(Test-Path -LiteralPath $temp -PathType Container)) {throw 'WPS_USER_TEMP_MISSING'}
$root=[IO.Path]::GetFullPath($temp).TrimEnd('\')+'\'
$matches=@(foreach($folder in @(Get-ChildItem -LiteralPath $temp -Directory)) {
  if($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) {continue}
  $pieces=if($folder.Name -like 'partyops-official-format-*') {@('jobs',$job,$document)}
    elseif($folder.Name -like 'pf-*') {@('j',$job,[string]$index)} else {continue}
  $parent=$folder.FullName
  foreach($piece in $pieces) {$parent=Join-Path $parent $piece}
  foreach($suffix in @('.source-host','source-pdf\.source-host')) {
    $dir=Join-Path $parent $suffix
    if(Test-Path -LiteralPath (Join-Path $dir 'response.json') -PathType Leaf) {$dir}
  }
})
if($matches.Count -ne 1) {throw 'WPS_CURRENT_HOST_PROOF_MISSING_OR_AMBIGUOUS'}
$requestPath=Join-Path $matches[0] 'request.json';$responsePath=Join-Path $matches[0] 'response.json'
$request=Get-Content -LiteralPath $requestPath -Raw | ConvertFrom-Json
$response=Get-Content -LiteralPath $responsePath -Raw | ConvertFrom-Json
if($request.feature_id -ne $feature -or $request.host_preference -ne 'wps' -or @($request.source_paths).Count -ne 1) {
  throw 'WPS_CURRENT_HOST_REQUEST_MISMATCH'
}
$source=[IO.Path]::GetFullPath([string]$request.source_paths[0])
if(-not $source.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath $source -PathType Leaf)) {
  throw 'WPS_CURRENT_HOST_INPUT_OUTSIDE_USER_TEMP'
}
$actual=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
if($actual -ne $expected) {throw 'WPS_CURRENT_HOST_INPUT_CHANGED'}
$jobs=@($response.jobs)
if($response.fatal -or $jobs.Count -ne 1 -or $jobs[0].success -ne $true -or [string]$jobs[0].host_display_name -notmatch 'WPS') {
  throw 'WPS_ACTUAL_ENGINE_NOT_PROVEN'
}
[ordered]@{feature=$feature;input_sha256=$actual;host_display_name=[string]$jobs[0].host_display_name;
  request_sha256=(Get-FileHash -LiteralPath $requestPath -Algorithm SHA256).Hash.ToLowerInvariant();
  response_sha256=(Get-FileHash -LiteralPath $responsePath -Algorithm SHA256).Hash.ToLowerInvariant()}|ConvertTo-Json -Compress
"""
    return guest_json(client, script)


def exercise(args) -> Path:
    require(args.expected_version == VERSION, "WPS_EXPECTED_VERSION_NOT_APPROVED")
    pinned = pe32(args.installer_path, args.installer_sha256)
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    context, system, install_evidence = product_binding(lab, args.install_report)
    pointer_path = lab.root / "state/business-win10-x86.json"
    require(pointer_path.is_file(), "PARTYOPS_STANDARD_USER_CONFIGURATION_REQUIRED")
    pointer = json.loads(pointer_path.read_text(encoding="utf-8"))
    require(pointer.get("context") == context, "PRODUCT_BUSINESS_POINTER_BINDING_CHANGED")
    report = lab.root / "reports" / TARGET / ("wps-format-" + uuid.uuid4().hex[:12])
    report.mkdir(parents=True, exist_ok=False)
    result = {"scope": "win10-x86-installed-wps-formatter-only", "status": "failed",
              "runtime_environment_passed": False, "generated_at": now(),
              "guest_uuid": context["environment"]["vm_uuid"], "boot_id": system["boot_id"],
              "baseline": context["environment"], "restore_generation": context["restore_generation"],
              "product_package_sha256": context["package"]["sha256"],
              "product_install_report": install_evidence, "wps_installer": pinned, "checks": []}
    write_json(report / "wps-format.json", result)
    client = installer.connect(lab, matrix["targets"][TARGET])
    prefix = base = None
    headers = None
    try:
        run = windows.WindowsRun(client, lab, TARGET, "wps-format", context["package"],
                                 context, pointer["remote"], report)
        desktop = run.desktop("Inspect")
        require(desktop.get("administrator") is False and len(desktop.get("desktop", [])) == 1,
                "WPS_STANDARD_USER_DESKTOP_REQUIRED")
        sid = desktop["sid"]
        marker = guest_json(client, r"""
$marker=Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw|ConvertFrom-Json
$hardware=(Get-CimInstance Win32_ComputerSystemProduct).UUID
[ordered]@{uuid=[string]$marker.uuid;purpose=[string]$marker.purpose;hardware_uuid=[string]$hardware}|ConvertTo-Json -Compress
""")
        require(marker.get("uuid") == context["environment"]["vm_uuid"]
                and str(marker.get("hardware_uuid", "")).casefold() == marker["uuid"].casefold()
                and marker.get("purpose") == "disposable-qa", "WPS_GUEST_UUID_CHANGED")
        wps = guest_json(client, guest_wps_script(pinned["sha256"], VERSION, sid), timeout=240)
        validate_wps_detection(wps, pinned["sha256"])
        result["checks"].append({"id": "installed-wps-x86", "status": "passed", "result": wps})
        require(desktop.get("config_dir"), "WPS_STANDARD_USER_PROFILE_MISSING")
        temp = str(Path(desktop["config_dir"]).parent / "Temp")
        base, prefix, headers = session(run)
        capabilities = run.request("/v1/capabilities", base=base, extra_headers=headers)
        require(capabilities.get("source_host_ready") is True, "WPS_SOURCE_HOST_NOT_READY")
        fixtures = REPO / "backend/tests/fixtures/document-formatter-source"
        source, golden = fixtures / "input-manual-break.docx", fixtures / "expected-source-formatted.docx"
        original_hashes = (sha256(source), sha256(golden))
        source_documents = {}  # 两份输入各有独立 document_id。
        for index in (1, 2):
            document_id, digest = upload(run, base, prefix, headers, source, f"中文 空格{index}.docx")
            source_documents[document_id] = {"sha256": digest, "label": f"formatted-{index}"}
        outputs, format_proofs = job(run, base, prefix, headers, "format", source_documents,
                                     {"scope": "full"}, report, ".docx", temp)
        signature = oracle._semantic_equivalence_signature(oracle._document_signature(golden))
        for path in outputs:
            actual = oracle._document_signature(path)
            oracle._require_contract(actual)
            require(oracle._semantic_equivalence_signature(actual) == signature,
                    "WPS_FORMATTED_DOCUMENT_SEMANTIC_MISMATCH")
        render_documents = {}
        for index, path in enumerate((golden, *outputs)):
            document_id, digest = upload(run, base, prefix, headers, path, path.name)
            render_documents[document_id] = {"sha256": digest, "label": f"render-{index}"}
        pdfs, render_proofs = job(run, base, prefix, headers, "convert", render_documents,
                                  {"target_format": "pdf"}, report, ".pdf", temp)
        comparisons = [oracle._visual_page_comparison(pdfs[0], path, allow_rasterization_noise=False)
                       for path in pdfs[1:]]
        require(all(row["passed"] for row in comparisons), "WPS_FORMATTED_PAGES_DIFFER_FROM_GOLDEN")
        require((sha256(source), sha256(golden)) == original_hashes, "WPS_GOLDEN_INPUT_CHANGED")
        wps_after = guest_json(client, guest_wps_script(pinned["sha256"], VERSION, sid), timeout=240)
        require(wps_after == wps, "WPS_INSTALLATION_CHANGED_DURING_FORMATTING")
        result["checks"].append({"id": "actual-wps-format-and-page-golden", "status": "passed",
            "source_sha256": original_hashes[0], "golden_sha256": original_hashes[1],
            "oracle_sha256": sha256(REPO / "scripts/verify-document-formatter-parity.py"),
            "format_host_proofs": format_proofs, "render_host_proofs": render_proofs,
            "visual_comparisons": comparisons,
            "outputs": [{"name": path.name, "sha256": sha256(path)} for path in (*outputs, *pdfs)]})
        require(product_binding(lab, args.install_report)[0] == context,
                "PRODUCT_BINDING_CHANGED_DURING_WPS_CHECK")
        result["status"] = "passed"
    except Exception as exc:
        result["error"] = str(exc) if isinstance(exc, RuntimeError) and re.fullmatch(r"[A-Z0-9_]+", str(exc)) else type(exc).__name__
        raise
    finally:
        if prefix and base and headers:
            try:
                run.request(prefix, "DELETE", base=base, extra_headers=headers)
            except Exception:  # noqa: BLE001 - 保留原失败及临时证据。
                result["status"] = "failed"
                result["cleanup_error"] = "WPS_SESSION_CLEANUP_FAILED"
        try:
            client.close()
        except Exception:  # noqa: BLE001 - 传输清理失败不可写出通过状态。
            result["status"] = "failed"
            result["cleanup_error"] = "WPS_SSH_CLEANUP_FAILED"
        result["completed_at"] = now()
        write_json(report / "wps-format.json", result)
    require(result["status"] == "passed", result.get("cleanup_error", "WPS_CHECK_FAILED"))
    return report / "wps-format.json"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--install-report", type=Path, required=True)
    parser.add_argument("--installer-path", type=Path, required=True)
    parser.add_argument("--installer-sha256", required=True)
    parser.add_argument("--expected-version", required=True)
    args = parser.parse_args()
    try:
        print(json.dumps({"status": "passed", "evidence": str(exercise(args))}, ensure_ascii=False))
        return 0
    except (OSError, ValueError, RuntimeError, KeyError, json.JSONDecodeError) as exc:
        code = str(exc) if isinstance(exc, RuntimeError) and re.fullmatch(r"[A-Z0-9_]+", str(exc)) else type(exc).__name__
        print(json.dumps({"status": "blocked", "error": code}, ensure_ascii=False))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
