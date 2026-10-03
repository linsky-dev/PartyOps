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
