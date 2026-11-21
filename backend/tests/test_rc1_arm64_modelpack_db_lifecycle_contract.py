"""Guard native ARM64 isolated real PartyOps model pack SQLAlchemy lifecycle."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "scripts/probe_arm64_modelpack_db_lifecycle.py"
WORKFLOW = ROOT / ".github/workflows/cloud-runner-smoke.yml"


def test_original_install_activate_and_integrity_are_tested_with_ephemeral_db() -> None:
    script = SOURCE.read_text(encoding="utf-8")
    assert "from app import model_packs" in script
    assert "from app.models import AIModelActivation, AIModelPack, User" in script
    assert "create_engine(\"sqlite+pysqlite:///:memory:\")" in script
    assert "with TemporaryDirectory(" in script
    assert "model_packs.install_model_pack(" in script
    assert "model_packs.activate_model_pack(" in script
    assert "model_packs.verify_installed_pack(pack)" in script
    assert "model_packs.active_model_pack(db, \"embedding\")" in script
    assert '"MODEL_PACK_ALREADY_INSTALLED"' in script
    assert '"MODEL_PACK_CORRUPT"' in script
    assert '"production_model_pack_tested": False' in script
    assert '"http_upload_api_tested": False' in script
    assert '"release_ready": False' in script
    assert "private-key.pem" not in WORKFLOW.read_text(encoding="utf-8")


def test_arm64_only_uses_locked_orm_and_has_no_production_write() -> None:
    script = SOURCE.read_text(encoding="utf-8")
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert 'platform.machine().lower() not in ("arm64", "aarch64")' in script
    assert 'model_packs.get_settings = lambda: test_settings' in script
    assert 'models_dir=storage' in script
    assert 'build_test_signed_pack(' in script
    assert 'full_app_dependency_lock_installed' in script
    assert 'SQLAlchemy==2.0.43' in workflow
    assert 'pydantic==2.13.4' in workflow
    assert 'pydantic-settings==2.10.1' in workflow
    assert 'scripts/probe_arm64_modelpack_db_lifecycle.py' in workflow
    assert 'artifacts/arm64-crypto-probe/modelpack-db-lifecycle.json' in workflow
