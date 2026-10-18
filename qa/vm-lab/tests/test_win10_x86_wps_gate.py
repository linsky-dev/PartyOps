"""Win10 x86 WPS 输入与当前产品/基线绑定的离线反例。"""

from __future__ import annotations

import importlib.util
import json
import struct
from pathlib import Path
from types import SimpleNamespace

import pytest
from evidence import sha256

HERE = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "win10_x86_wps_gate", HERE / "scripts/verify-win10-x86-installed-wps.py"
)
assert SPEC is not None and SPEC.loader is not None
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)


def _setup(tmp_path: Path, machine: int = 0x14C, magic: int = 0x10B) -> Path:
    path = tmp_path / gate.SETUP_NAME
    data = bytearray(512)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 60, 128)
    data[128:132] = b"PE\0\0"
    struct.pack_into("<H", data, 132, machine)
    struct.pack_into("<H", data, 152, magic)
    path.write_bytes(data)
    return path


def test_installer_requires_real_hash_and_pe32_x86(tmp_path: Path) -> None:
    path = _setup(tmp_path)
    assert gate.pe32(path, sha256(path))["pe_machine"] == 0x14C
    with pytest.raises(RuntimeError, match="WPS_INSTALLER_SHA256_MISMATCH"):
        gate.pe32(path, "a" * 64)
    path = _setup(tmp_path, machine=0x8664, magic=0x20B)
    with pytest.raises(RuntimeError, match="WPS_INSTALLER_NOT_PE32_X86"):
        gate.pe32(path, sha256(path))


def _binding(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> tuple[object, Path]:
    root = tmp_path / "lab"
    report = root / "reports/win10-x86/install-fixture"
    report.mkdir(parents=True)
    (report / "install.log").write_text("synthetic", encoding="utf-8")
    package = {"id": "windows7_x86", "sha256": "a" * 64, "version": "1.4.5"}
    environment = {"vm_uuid": "fixture-uuid", "baseline_id": "baseline-1"}
    receipt = {"target": "win10-x86", "guest_uuid": "fixture-uuid",
               "environment": environment, "restore_generation": "restore-1",
               "package": package, "install": {"exit_code": 0},
               "evidence": {"install_log": {"path": "install.log",
                                            "sha256": sha256(report / "install.log")}}}
    (report / "installed-probe.json").write_text(json.dumps(receipt), encoding="utf-8")
    lab = SimpleNamespace(root=root, matrix={"packages": {"windows7_x86": {"required_targets": ["win10-x86"]}}},
                          state=lambda _: {"uuid": "fixture-uuid", "restore_generation": "restore-1"},
                          live=lambda _: True)
    monkeypatch.setattr(gate, "probe", lambda *_: {"installed_package": True, "boot_id": "boot-fixture"})
    monkeypatch.setattr(gate, "runtime_binding", lambda *_: environment)
    monkeypatch.setattr(gate, "inventory", lambda *_: ({"windows7_x86": package}, {}))
    monkeypatch.setattr(gate, "bind_package", lambda *_: package)
    monkeypatch.setattr(gate, "fingerprint", lambda: "source-fixture")
    return lab, report


@pytest.mark.parametrize("change", ["restore_generation", "guest_uuid", "package"])
def test_product_receipt_binds_guest_baseline_and_current_package(tmp_path: Path,
                                                                  monkeypatch: pytest.MonkeyPatch,
                                                                  change: str) -> None:
    lab, report = _binding(tmp_path, monkeypatch)
    context, system, evidence = gate.product_binding(lab, report)
    assert context["environment"]["baseline_id"] == "baseline-1"
    assert system["boot_id"] == "boot-fixture" and evidence["sha256"]
    path = report / "installed-probe.json"
    receipt = json.loads(path.read_text(encoding="utf-8"))
    if change == "package":
        receipt["package"]["sha256"] = "b" * 64
    else:
        receipt[change] = "old-identity"
    path.write_text(json.dumps(receipt), encoding="utf-8")
    with pytest.raises(RuntimeError, match="PRODUCT_INSTALL_RECEIPT_BINDING_MISMATCH"):
        gate.product_binding(lab, report)


def test_wps_registration_and_host_proof_are_required() -> None:
    with pytest.raises(RuntimeError, match="WPS_GUEST_INSTALLATION_UNVERIFIED"):
        gate.validate_wps_detection({"version": gate.VERSION, "installer_sha256": "a" * 64,
                                     "pe_machine": 0x14C}, "a" * 64)
    script = gate.guest_wps_script("a" * 64, gate.VERSION, "S-1-5-21-1-2-3-1001")
    assert "InstallLocation" in script and "KWPS.Application" in script
    assert "WPS_EXECUTABLE_VERSION_MISMATCH" in script
    with pytest.raises(RuntimeError, match="WPS_HOST_PROOF_ARGUMENT_INVALID"):
        gate.host_proof(None, r"C:\Users\partyopsuser\AppData\Local\Temp",
                        "wrong-job", "b" * 32, "format", "c" * 64, 0)


def test_host_proof_explicitly_reads_utf8_without_changing_native_assertions(monkeypatch):
    captured = []
    monkeypatch.setattr(gate, "guest_json", lambda client, script: captured.append(script) or {"synthetic": True})
    assert gate.host_proof(None, r"C:\Users\partyopsuser\AppData\Local\Temp", "a" * 32, "b" * 32,
                           "format", "c" * 64, 0) == {"synthetic": True}
    script = captured[0]
    for name in ("requestPath", "responsePath"):
        assert "Get-Content -LiteralPath $" + name + " -Raw -Encoding UTF8 | ConvertFrom-Json" in script
    for code in ("WPS_CURRENT_HOST_PROOF_MISSING_OR_AMBIGUOUS", "WPS_CURRENT_HOST_REQUEST_MISMATCH",
                 "WPS_CURRENT_HOST_INPUT_CHANGED", "WPS_ACTUAL_ENGINE_NOT_PROVEN", "WPS_CURRENT_HOST_OUTPUT_ROOT_INVALID",
                 "WPS_CURRENT_HOST_OUTPUT_PATH_INVALID", "WPS_CURRENT_HOST_OUTPUT_EMPTY", "WPS_CURRENT_HOST_OUTPUT_REQUIRED"):
        assert code in script
    assert "Split-Path $control -Parent" in script  # 不复用被-notmatch覆盖的PowerShell自动变量Matches。


@pytest.mark.parametrize("fault", [None, "hash", "bytes", "extension", "missing", "multiple"])
def test_download_must_match_single_current_native_output(tmp_path, monkeypatch, fault):
    content = b"synthetic-native-output"
    digest = gate.hashlib.sha256(content).hexdigest()
    native = {"bytes": len(content), "sha256": digest, "extension": ".docx"}
    if fault == "hash": native["sha256"] = "f" * 64
    if fault == "bytes": native["bytes"] += 1
    if fault == "extension": native["extension"] = ".pdf"
    rows = [] if fault == "missing" else [native, native] if fault == "multiple" else [native]
    monkeypatch.setattr(gate, "host_proof", lambda *args: {"outputs": rows})
    job = {"id": "a" * 32, "state": "completed", "outputs": [{"id": "b" * 32, "document_id": "c" * 32}]}
    run = SimpleNamespace(client=None, request=lambda path, *args, **kwargs: content if "/outputs/" in path else job)
    arguments = (run, "http://127.0.0.1:18777", "/v1/sessions/" + "d" * 32, {}, "format",
                 {"c" * 32: {"sha256": "e" * 64, "label": "synthetic"}}, {}, tmp_path, ".docx",
                 r"C:\Users\partyopsuser\AppData\Local\Temp")
    if fault:
        with pytest.raises(RuntimeError, match="WPS_SINGLE_NATIVE_OUTPUT_REQUIRED|WPS_NATIVE_AND_HTTP_OUTPUT_DIFFER"):
            gate.job(*arguments)
    else:
        paths, proofs = gate.job(*arguments)
        assert paths[0].read_bytes() == content and proofs[0]["outputs"] == [native]


def test_compact_only_proof_has_no_fabricated_document_and_script_parses(monkeypatch):
    import subprocess
    captured = []
    monkeypatch.setattr(gate, "guest_json", lambda client, script: captured.append(script) or {})
    gate.host_proof(None, r"C:\Users\partyopsuser\AppData\Local\Temp", "a" * 32, None, "convert", "c" * 64, 2)
    script = captured[0]
    assert "$document=$null" in script and "$index=2" in script
    assert "$document -and $folder.Name -like 'partyops-official-format-*'" in script
    parser = "$s=[Console]::In.ReadToEnd();$e=$null;$t=$null;[void][Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){exit 1}"
    result = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", parser],
                            input=script, encoding="utf-8", capture_output=True)
    assert result.returncode == 0


def test_expanded_native_proof_uses_short_staged_command(monkeypatch):
    from unittest.mock import MagicMock
    captured = []
    monkeypatch.setattr(gate, "guest_json", lambda client, script: captured.append(script) or {})
    gate.host_proof(None, r"C:\Users\partyopsuser\AppData\Local\Temp", "a" * 32, None, "convert", "c" * 64, 2)
    script = captured[0]
    assert len(gate.installer.encoded_powershell(script)) > 8192
    acceptance = HERE.parents[1] / "qa/acceptance/windows-startup-20261016"
    monkeypatch.syspath_prepend(str(acceptance))
    adapter = gate.load(acceptance / "win10-installed-formatter.py", "synthetic_staged_adapter")
    client, execute = MagicMock(), MagicMock(return_value='{}')
    adapter.staged_readonly_probe(client, execute, script, "win10-formatter-012345abcdef")
    command = execute.call_args.args[1]
    assert len(gate.installer.encoded_powershell(command)) < 8192
    staged = client.open_sftp.return_value.__enter__.return_value.file.return_value.__enter__.return_value.write.call_args.args[0]
    assert script.encode("utf-8") in staged
    for filename in ("collect-win10-native-output-proofs.py", "win10-installed-formatter.py"):
        source = (acceptance / filename).read_text(encoding="utf-8")
        assert "shared.guest_json = lambda client, script, **kwargs:" in source
        assert "staged_readonly_probe(" in source


def test_original_x86_entry_stages_oversize_host_proof_without_caller_override(monkeypatch):
    from unittest.mock import MagicMock
    client = MagicMock()
    execute = MagicMock(return_value='{"synthetic":true}')
    monkeypatch.setattr(gate.installer, "execute", execute)
    result = gate.host_proof(client, r"C:\Users\partyopsuser\AppData\Local\Temp", "a" * 32, "b" * 32, "format", "c" * 64, 0)
    assert result == {"synthetic": True}
    command = execute.call_args.args[1]
    assert len(gate.installer.encoded_powershell(command)) < 8192
    stream = client.open_sftp.return_value.__enter__.return_value.file.return_value.__enter__.return_value
    script = stream.write.call_args.args[0].decode("utf-8")
    assert "WPS_CURRENT_HOST_OUTPUT_PATH_INVALID" in script and "$document='" + "b" * 32 + "'" in script


def test_session_creation_matches_frontend_no_body_and_does_not_retry():
    from unittest.mock import MagicMock
    calls = []
    def request(path, method="GET", data=None, **kwargs):
        calls.append((path, method, data, kwargs))
        if path.endswith("local-ticket"):
            return {"local_base_url": "http://127.0.0.1:18777", "ticket": "synthetic-secret-ticket"}
        return {"session_id": "a" * 32, "session_token": "synthetic-secret-token"}
    run = SimpleNamespace(origin="http://127.0.0.1:18775", wait_health=MagicMock(), login=MagicMock(), request=request)
    base, prefix, headers = gate.session(run)
    assert base == "http://127.0.0.1:18777" and prefix == "/v1/sessions/" + "a" * 32
    assert headers == {"X-PartyOps-Local-Token": "synthetic-secret-token"}
    assert len(calls) == 2
    assert calls[0][:3] == ("/api/v1/official-format/local-ticket", "POST", {"origin": run.origin})
    assert calls[1][:3] == ("/v1/sessions", "POST", None)
    assert calls[1][3]["extra_headers"] == {"Authorization": "Bearer synthetic-secret-ticket"}
    run.request = MagicMock(side_effect=[{"local_base_url": base, "ticket": "synthetic-fresh-ticket"},
                                        RuntimeError("synthetic-failure-no-replay")])
    with pytest.raises(RuntimeError, match="synthetic-failure-no-replay"):
        gate.session(run)
    assert run.request.call_count == 2
    assert run.request.call_args.args == ("/v1/sessions", "POST")


@pytest.mark.parametrize("fault", [None, "request", "response", "input", "index", "output", "bytes", "extension", "escape"])
def test_readonly_collector_binds_old_proof_and_existing_download(tmp_path, monkeypatch, fault):
    script_path = HERE.parents[1] / "qa/acceptance/windows-startup-20261016/collect-win10-native-output-proofs.py"
    spec = importlib.util.spec_from_file_location("synthetic_collector", script_path)
    collector = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(collector)
    content = b"synthetic-current-native"
    target = tmp_path / "golden.docx"
    target.write_bytes(content)
    digest = gate.sha256(target)
    proof = {"input_sha256": "a" * 64, "request_sha256": "b" * 64, "response_sha256": "c" * 64}
    fresh = {**proof, "job": "d" * 32, "index": 0,
             "outputs": [{"sha256": digest, "bytes": len(content), "extension": ".docx"}]}
    original = {"id": "d" * 32, "state": "completed", "feature": "format", "host_proofs": [proof],
                "outputs": [{"file": "../escape.docx" if fault == "escape" else target.name, "sha256": digest}]}
    if fault in {"request", "response", "input"}: fresh[fault + "_sha256"] = "f" * 64
    if fault == "index": fresh["index"] = 1
    if fault == "output": fresh["outputs"][0]["sha256"] = "f" * 64
    if fault == "bytes": fresh["outputs"][0]["bytes"] += 1
    if fault == "extension": fresh["outputs"][0]["extension"] = ".pdf"
    path = tmp_path / ("job-" + "d" * 32 + ".json")
    path.write_text(json.dumps(original), encoding="utf-8")
    calls = []
    monkeypatch.setattr(gate, "host_proof", lambda *args: calls.append(args) or fresh)
    contract = SimpleNamespace(read=lambda p: json.loads(p.read_text(encoding="utf-8")), digest=gate.sha256)
    if fault:
        with pytest.raises(RuntimeError, match="SUPPLEMENT_"):
            collector.collect_job(gate, contract, None, tmp_path, path)
    else:
        result = collector.collect_job(gate, contract, None, tmp_path, path)
        assert result["outputs"][0]["sha256"] == digest
        assert result["original_job_sha256"] == gate.sha256(path)
        assert calls[0][3] is None


def test_missing_guest_wps_has_explicit_precondition(monkeypatch: pytest.MonkeyPatch) -> None:
    def failed(*_args, **_kwargs):
        raise RuntimeError("WINDOWS_REMOTE_COMMAND_FAILED: WPS_INSTALL_REGISTRATION_MISSING_OR_VERSION_MISMATCH")

    monkeypatch.setattr(gate.installer, "execute", failed)
    with pytest.raises(RuntimeError, match="^WPS_INSTALL_PRECONDITION_MISSING$"):
        gate.guest_json(None, "synthetic read-only probe")


def test_workflow_uses_semantic_and_page_oracle() -> None:
    source = (HERE / "scripts/verify-win10-x86-installed-wps.py").read_text(encoding="utf-8")
    assert "_semantic_equivalence_signature" in source
    assert "_visual_page_comparison" in source
    assert "host_proof(run.client" in source
    assert 'result["status"] = "passed"' in source
