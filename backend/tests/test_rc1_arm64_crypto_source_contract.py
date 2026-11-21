"""Ensure the isolated ARM64 crypto probe matches locked sources and never weakens release gates."""

from __future__ import annotations

import tomllib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "build-windows-arm64-crypto-probe.ps1"
WORKFLOW = ROOT / ".github" / "workflows" / "cloud-runner-smoke.yml"


def test_arm64_probe_uses_repository_locked_source_and_strict_native_context() -> None:
    script = SCRIPT.read_text(encoding="utf-8")
    lock = tomllib.loads((ROOT / "backend" / "uv.lock").read_text(encoding="utf-8"))
    package = next(item for item in lock["package"] if item["name"] == "cryptography")
    assert package["version"] == "50.0.0"
    assert package["sdist"]["hash"].startswith("sha256:")
    assert package["sdist"]["hash"].removeprefix("sha256:") in script
    assert package["sdist"]["url"] in script
    assert "openssl-3.5.7" in script
    assert "VC-WIN64-ARM" in script
    assert "ARM_CRYPTO_WRONG_HOST" in script
    assert "ARM_CRYPTO_WRONG_PYTHON" in script
    assert "validate-portable-tar.py" in script
    assert "probe-sm-crypto-runtime.py" in script
    assert "OPENSSL_STATIC=1" in script
    assert "pip wheel" in script
    assert "--no-index --no-deps --find-links" in script
    assert "wheel_sha256" in script
    assert "offline_wheel_install = 'PASS'" in script
    assert "usable_for_release = $false" in script


def test_arm64_probe_does_not_change_other_platform_validation() -> None:
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert "id: arm64-source-build" in workflow
    assert "matrix.label == 'windows-11-arm'" in workflow
    assert "steps.arm64-source-build.outcome" in workflow
    assert "scripts/build-windows-arm64-crypto-probe.ps1" in workflow
    assert "crypto-runtime.json" in workflow
    assert "continue-on-error: true" in workflow
    assert "Probe SM3 and SM4-GCM runtime support" in workflow
    assert "arm64-crypto-build-only-" in workflow
    assert "artifacts/arm64-crypto-probe/build-manifest.json" in workflow
