"""Source contract for the isolated official signed BGE ARM64 lifecycle smoke."""

import ast
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "scripts" / "probe_arm64_official_bge_lifecycle.py"
CHILD = ROOT / "scripts" / "probe_arm64_official_bge_child.py"
WORKFLOW = ROOT / ".github" / "workflows" / "cloud-runner-smoke.yml"


def test_unchanged_official_signature_manager_and_embedding_execution() -> None:
    source = SOURCE.read_text(encoding="utf-8")
    ast.parse(source, filename=str(SOURCE))
    assert "download_exact_official_package(source)" in source
    assert "OFFICIAL_SHA256" in source
    assert 'manifest.get("architecture")' in source
    assert 'manifest.get("architectures")' in source
    assert '"universal"' in source
    assert "update-public-key.txt" in source
    assert "model_packs._validate_manifest(manifest, archive)" in source
    assert "model_packs.install_model_pack(source, ASSET_NAME, admin, db)" in source
    assert "model_packs.activate_model_pack(db, pack, \"embedding\", admin.id)" in source
    assert 'model_packs.verify_installed_pack(pack)' in source
    assert 'model_packs.active_model_pack(db, "embedding")' in source
    assert "_load_unchanged_runtime(" in source
    assert "runtime.encode(pack, phrases)" in source
    assert "is_query=True" in source
    assert "MODEL_PACK_ALREADY_INSTALLED" in source
    assert '"sqlite+pysqlite:///{sqlite_path.as_posix()}"' in source
    assert 'reopened_engine = create_engine(sqlite_url)' in source
    assert '"sqlite_database_reopened"' in source
    assert "subprocess.run(" in source
    assert '"independent_python_process_reopened"' in source
    assert '"MODEL_PACK_CORRUPT"' in source
    assert '"OFFICIAL_BGE_TAMPER_FINGERPRINT_CHANGED"' in source
    assert "probe_arm64_official_bge_child.py" in source
    assert '"app_process_restarted"' in source
    assert "with TemporaryDirectory(" in source
    assert '"release_ready": False' in source
    assert '"full_application_installed": False' in source
    assert '"wps_modified": False' in source


def test_release_gate_stays_separate_and_native_only() -> None:
    workflow = WORKFLOW.read_text(encoding="utf-8")
    script = SOURCE.read_text(encoding="utf-8")
    assert "platform.machine().lower() not in {" in script
    assert "'windows-11-arm'" in workflow
    assert "python scripts/probe_arm64_official_bge_lifecycle.py" in workflow
    assert "artifacts/arm64-crypto-probe/official-bge-lifecycle.json" in workflow
    assert "PARTYOPS_ARM64_OFFICIAL_SIGNED_BGE_IMPORT_ACTIVATION_INFERENCE_PASS" in script
    assert "private-key.pem" not in script


def test_fresh_interpreter_helper_is_read_only_and_native_only() -> None:
    helper = CHILD.read_text(encoding="utf-8")
    ast.parse(helper, filename=str(CHILD))
    assert 'platform.machine().lower() not in {"arm64", "aarch64"}' in helper
    assert 'model_packs.verify_installed_pack(pack)' in helper
    assert 'model_packs.active_model_pack(db, "embedding")' in helper
    assert 'runtime.encode(pack, ["支部党员大会会议记录"])' in helper
    assert "PARTYOPS_ARM64_OFFICIAL_BGE_FRESH_INTERPRETER_REOPEN_PASS" in helper
    assert "db.commit()" not in helper
    assert "create_all(" not in helper
    assert "private-key.pem" not in helper
