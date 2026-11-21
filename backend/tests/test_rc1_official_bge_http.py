"""Optional BUILD_ONLY FastAPI TestClient gate for official RC2 signed BGE.

The regular backend suite skips this test unless an external opt-in job passes
an original hash-pinned official release archive. This never uses customer data.
"""

from __future__ import annotations

import hashlib
import os
from pathlib import Path

import numpy as np
import pytest
from fastapi.testclient import TestClient

from app.config import get_settings
from app.database import db_runtime
from app.local_ai import EmbeddingRuntime
from app import model_packs
from app.models import AIModelPack

OFFICIAL_SHA256 = "57df3669e368497ad6d428dab309fb7665229a57edfe3a4fe1f2afd6a0f1a155"
OFFICIAL_BYTES = 94_965_776
ROOT = Path(__file__).resolve().parents[2]


def test_official_signed_bge_http_import_activation_and_inference(
    client: TestClient, admin: dict, monkeypatch: pytest.MonkeyPatch
) -> None:
    del admin
    filename = os.environ.get("PARTYOPS_RC1_OFFICIAL_BGE_HTTP_PACK", "").strip()
    if not filename:
        pytest.skip("Official signed BGE HTTP gate is opt-in (no automatic model download)")
    path = Path(filename).resolve(strict=True)
    assert path.is_file() and path.name.endswith(".partyops-modelpack")
    assert path.stat().st_size == OFFICIAL_BYTES
    sha = hashlib.sha256()
    with path.open("rb") as handle:
        while chunk := handle.read(1024 * 1024):
            sha.update(chunk)
    assert sha.hexdigest() == OFFICIAL_SHA256

    # Reuse the original external public root; no ephemeral signer or private key.
    public = (ROOT / "packaging/uos/update-public-key.txt").read_text(encoding="ascii").strip()
    assert public
    monkeypatch.setattr(get_settings(), "model_pack_public_key", public)
    with path.open("rb") as handle:
        result = client.post(
            "/api/v1/admin/ai/model-packs",
            files={"file": (path.name, handle, "application/octet-stream")},
        )
    assert result.status_code == 201, result.text
    imported = result.json()
    assert imported["signature_valid"] is True
    assert imported["status"] == "installed"

    pack_id = imported["id"]
    # A valid administrator cannot activate a capability the published
    # embedding-only manifest does not contain.
    wrong_capability = client.post(
        f"/api/v1/admin/ai/model-packs/{pack_id}/activate?capability=llm"
    )
    assert wrong_capability.status_code == 422, wrong_capability.text
    assert wrong_capability.json()["code"] == "MODEL_CAPABILITY_MISSING"

    enabled = client.post(
        f"/api/v1/admin/ai/model-packs/{pack_id}/activate?capability=embedding"
    )
    assert enabled.status_code == 200, enabled.text
    assert enabled.json()["status"] == "active"
    with db_runtime.session_factory() as db:
        pack = db.get(AIModelPack, pack_id)
        assert pack is not None and pack.signature_valid
        runtime = EmbeddingRuntime()
        try:
            rows = runtime.encode(
                pack, ["支部党员大会会议记录", "支部党员大会会议记录", "本周食堂菜单"]
            )
        finally:
            runtime.unload()
    vectors = [np.frombuffer(row, dtype=np.float32) for row in rows]
    assert len(vectors) == 3
    assert all(v.shape == (512,) and np.all(np.isfinite(v)) for v in vectors)
    assert all(abs(float(np.linalg.norm(v)) - 1.0) < 1e-4 for v in vectors)
    assert float(np.dot(vectors[0], vectors[1])) > 0.9999

    with path.open("rb") as handle:
        duplicate = client.post(
            "/api/v1/admin/ai/model-packs",
            files={"file": (path.name, handle, "application/octet-stream")},
        )
    assert duplicate.status_code == 409, duplicate.text
    assert duplicate.json()["code"] == "MODEL_PACK_ALREADY_INSTALLED"
    assert path.is_file() and path.stat().st_size == OFFICIAL_BYTES

    # Fail closed even if the installed copy is modified without changing
    # its size or mtime. This touches only the pytest-isolated extracted ONNX
    # payload, never the original signed release archive or signing key.
    with db_runtime.session_factory() as db:
        pack = db.get(AIModelPack, pack_id)
        assert pack is not None
        model_name = pack.manifest["components"]["embedding"]["model_file"]
        component = model_packs._safe_member(model_name)
        extracted = model_packs.model_pack_root(pack).joinpath(*component.parts)
        original = extracted.stat()
    assert original.st_size > 2
    with extracted.open("r+b") as handle:
        handle.seek(-1, os.SEEK_END)
        previous = handle.read(1)
        handle.seek(-1, os.SEEK_END)
        handle.write(bytes([previous[0] ^ 1]))
    os.utime(extracted, ns=(original.st_atime_ns, original.st_mtime_ns))
    assert extracted.stat().st_size == original.st_size
    assert extracted.stat().st_mtime_ns == original.st_mtime_ns
    refused = client.post(
        f"/api/v1/admin/ai/model-packs/{pack_id}/activate?capability=embedding"
    )
    assert refused.status_code == 409, refused.text
    assert refused.json()["code"] == "MODEL_PACK_CORRUPT"
    assert path.is_file() and path.stat().st_size == OFFICIAL_BYTES


def test_official_bge_admin_http_routes_reject_anonymous_and_staff(
    client: TestClient, admin: dict, staff: dict
) -> None:
    # Lightweight mandatory security test. No official model download and no
    # model material is needed; a second TestClient has no admin cookie.
    del admin, staff
    unauthenticated = TestClient(client.app)
    try:
        paths = [
            ("get", "/api/v1/admin/ai/model-packs"),
            ("post", "/api/v1/admin/ai/model-packs"),
            ("post", "/api/v1/admin/ai/model-packs/not-a-pack/activate?capability=embedding"),
        ]
        for verb, target in paths:
            kwargs = (
                {"files": {"file": ("untrusted.partyops-modelpack", b"invalid")}}
                if target == "/api/v1/admin/ai/model-packs" and verb == "post"
                else {}
            )
            result = getattr(unauthenticated, verb)(target, **kwargs)
            assert result.status_code == 401, result.text
            assert result.json()["code"] == "AUTH_REQUIRED"

        # staff is a real, distinct authenticated session but is not allowed
        # to even list, import or activate administrator-controlled models.
        login = unauthenticated.post(
            "/api/v1/auth/login",
            json={"username": "staff", "password": "PartyOps@2026"},
        )
        assert login.status_code == 200, login.text
        for verb, target in paths:
            kwargs = (
                {"files": {"file": ("untrusted.partyops-modelpack", b"invalid")}}
                if target == "/api/v1/admin/ai/model-packs" and verb == "post"
                else {}
            )
            denied = getattr(unauthenticated, verb)(target, **kwargs)
            assert denied.status_code == 403, denied.text
            assert denied.json()["code"] == "ADMIN_REQUIRED"
    finally:
        unauthenticated.close()
