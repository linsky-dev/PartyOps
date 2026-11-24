"""Native ARM64 signature acceptance of actual PartyOps model-pack verifier.

BUILD_ONLY. Test-only ephemeral Ed25519 key and synthetic, non-executable
model bytes. Invoke the unchanged PartyOps pack builder and AST-extracted
original verifier helpers; no production trust root, model weights, WPS or
application installation. No keys or model packs are uploaded as artifacts.
"""

from __future__ import annotations

import ast
import base64
import hashlib
import json
import os
import platform
import re
import subprocess
import sys
import zipfile
from pathlib import Path, PurePosixPath
from tempfile import TemporaryDirectory
from types import SimpleNamespace

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import (
    Ed25519PrivateKey,
    Ed25519PublicKey,
)


class TestProblemException(Exception):
    def __init__(self, status: int, code: str, title: str, detail: str):
        super().__init__(detail)
        self.status = status
        self.code = code


def load_unchanged_modelpack_verifier(path: Path, trusted_key: str):
    """Extract only original verifier functions and constants from the repository."""
    raw = path.read_bytes()
    tree = ast.parse(raw.decode("utf-8"), filename=str(path))
    selected_functions = {"_safe_member", "_manifest_signature_valid", "_validate_manifest"}
    selected_constants = {
        "_WINDOWS_RESERVED_NAMES",
        "SUPPORTED_MODEL_PACK_FORMAT_VERSIONS",
        "MAX_MEMBERS",
        "MAX_UNPACKED_BYTES",
    }
    nodes = []
    funcs_found: set[str] = set()
    constants_found: set[str] = set()
    for node in tree.body:
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and node.name in selected_functions:
            funcs_found.add(node.name)
            nodes.append(node)
        elif isinstance(node, ast.Assign):
            if any(isinstance(target, ast.Name) and target.id in selected_constants for target in node.targets):
                constants_found.update(
                    target.id for target in node.targets
                    if isinstance(target, ast.Name) and target.id in selected_constants
                )
                nodes.append(node)
    if funcs_found != selected_functions or constants_found != selected_constants:
        raise RuntimeError("MODEL_PACK_VERIFIER_SOURCE_CONTRACT_CHANGED")

    settings = SimpleNamespace(model_pack_public_key=trusted_key, update_public_key="")
    ns = {
        "base64": base64, "hashlib": hashlib, "json": json,
        "re": re, "zipfile": zipfile, "PurePosixPath": PurePosixPath,
        "Ed25519PublicKey": Ed25519PublicKey,
        "ProblemException": TestProblemException,
        "get_settings": lambda: settings,
    }
    compiled = ast.fix_missing_locations(ast.Module(body=[
        ast.ImportFrom(module="__future__", names=[ast.alias(name="annotations")], level=0),
        *nodes,
    ], type_ignores=[]))
    exec(compile(compiled, str(path), "exec"), ns)
    return ns["_validate_manifest"], settings, hashlib.sha256(raw).hexdigest()


def expect_denied(validator, path: Path, expected_code: str) -> None:
    with zipfile.ZipFile(path) as archive:
        manifest = json.loads(archive.read("manifest.json"))
        try:
            validator(manifest, archive)
        except TestProblemException as exc:
            if exc.code != expected_code:
                raise AssertionError(f"expected {expected_code}, got {exc.code}") from exc
        else:
            raise AssertionError(f"{expected_code} must fail closed")


def change_archive_member(source: Path, destination: Path, target: str, new_content: bytes) -> None:
    with zipfile.ZipFile(source) as original, zipfile.ZipFile(destination, "w") as edited:
        for item in original.infolist():
            value = new_content if item.filename == target else original.read(item.filename)
            edited.writestr(item.filename, value, compress_type=item.compress_type)


def main() -> int:
    if os.name != "nt" or platform.machine().lower() not in {"arm64", "aarch64"}:
        raise RuntimeError("MODEL_PACK_NATIVE_WINDOWS_ARM64_REQUIRED")
    from cryptography import __version__ as crypto_version
    if crypto_version != "50.0.0":
        raise RuntimeError(f"MODEL_PACK_CRYPTO_VERSION_MISMATCH:{crypto_version}")

    repo = Path(__file__).resolve().parents[1]
    builder = repo / "packaging" / "uos" / "build-model-pack.py"
    verifier_source = repo / "backend" / "app" / "model_packs.py"
    with TemporaryDirectory(prefix="partyops-arm64-test-key-") as temp:
        folder = Path(temp)
        key = Ed25519PrivateKey.generate()
        other_key = Ed25519PrivateKey.generate()
        private_path = folder / "ephemeral-test-private-key.pem"
        private_path.write_bytes(key.private_bytes(
            serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8,
            serialization.NoEncryption(),
        ))
        private_path.chmod(0o600)
        trusted = base64.b64encode(key.public_key().public_bytes(
            serialization.Encoding.Raw, serialization.PublicFormat.Raw,
        )).decode("ascii")
        public_path = folder / "test-only-public-key.txt"
        public_path.write_text(trusted + "\n", encoding="ascii")
        fake_onnx = folder / "not-a-real-model.onnx"
        fake_onnx.write_bytes(b"PartyOps test-only nonexecutable synthetic ONNX placeholder")
        fake_tokenizer = folder / "not-a-real-tokenizer.json"
        fake_tokenizer.write_text('{"scope":"test-only-not-a-real-tokenizer"}\n', encoding="utf-8")
        license_path = folder / "TEST-NOTICE.txt"
        license_path.write_text(
            "PartyOps test-only synthetic data. No third-party model weights.\n",
            encoding="utf-8",
        )
        package = folder / "synthetic-arm64.partyops-modelpack"
        command = [
            sys.executable, str(builder),
            "--embedding", str(fake_onnx),
            "--tokenizer", str(fake_tokenizer),
            "--license", str(license_path),
            "--private-key", str(private_path),
            "--public-key", str(public_path),
            "--architecture", "arm64", "--platform", "windows",
            "--version", "0.0.0-test-only",
            "--model-id", "synthetic-non-executable-test-only",
            "--name", "PartyOps ARM64 signature test-only",
            "--model-source", "test-only-synthetic-data",
            "--license-name", "TEST-ONLY-NOT-A-RELEASE",
            "--output", str(package),
        ]
        built = subprocess.run(command, capture_output=True, text=True, check=False, timeout=30)
        if built.returncode:
            raise RuntimeError(f"MODEL_PACK_BUILDER_FAILED:{built.stderr[:400]}")
        if not package.is_file():
            raise AssertionError("MODEL_PACK_TEST_PACKAGE_MISSING")

        verify, settings, source_sha = load_unchanged_modelpack_verifier(verifier_source, trusted)
        with zipfile.ZipFile(package) as archive:
            manifest = json.loads(archive.read("manifest.json"))
            files, valid_signature = verify(manifest, archive)
            assert valid_signature
            assert manifest["format_version"] == 2
            assert manifest["architectures"] == ["arm64"]
            assert manifest["platforms"] == ["windows"]
            assert len(files) == 3
            model_path = manifest["components"]["embedding"]["model_file"]
            assert model_path in files
            assert manifest["model_id"] == "synthetic-non-executable-test-only"

        wrong_trust = base64.b64encode(other_key.public_key().public_bytes(
            serialization.Encoding.Raw, serialization.PublicFormat.Raw
        )).decode("ascii")
        settings.model_pack_public_key = wrong_trust
        expect_denied(verify, package, "MODEL_PACK_SIGNATURE_INVALID")
        settings.model_pack_public_key = trusted

        bad_hash = folder / "payload-modified.partyops-modelpack"
        change_archive_member(package, bad_hash, model_path, b"forged-payload")
        expect_denied(verify, bad_hash, "MODEL_PACK_HASH_MISMATCH")

        altered = dict(manifest)
        altered["name"] = "attacker-altered-after-signing"
        tampered_manifest = folder / "manifest-modified.partyops-modelpack"
        change_archive_member(
            package, tampered_manifest, "manifest.json",
            json.dumps(altered, ensure_ascii=False).encode("utf-8"),
        )
        expect_denied(verify, tampered_manifest, "MODEL_PACK_SIGNATURE_INVALID")

        path_attack = folder / "path-escape.partyops-modelpack"
        with zipfile.ZipFile(package) as original, zipfile.ZipFile(path_attack, "w") as malicious:
            for member in original.infolist():
                malicious.writestr(member.filename, original.read(member.filename), compress_type=member.compress_type)
            malicious.writestr("../escape.txt", "malicious")
        expect_denied(verify, path_attack, "MODEL_PACK_PATH_INVALID")

        evidence = {
            "schema_version": 1,
            "status": "PASS",
            "scope": "TEST_ONLY_SIGNED_SYNTHETIC_MODEL_PACK_WITH_UNCHANGED_PRODUCTION_VERIFIER",
            "commit": os.environ.get("GITHUB_SHA", ""),
            "architecture": platform.machine(),
            "python": sys.version.split()[0],
            "crypto_version": crypto_version,
            "original_modelpack_verifier_sha256": source_sha,
            "original_builder_sha256": hashlib.sha256(builder.read_bytes()).hexdigest(),
            "test_package_sha256": hashlib.sha256(package.read_bytes()).hexdigest(),
            "test_only_package_created": True,
            "test_only_ed25519_key_generated_in_runner": True,
            "production_key_used": False,
            "accepted_valid_ephemeral_signed_package": True,
            "wrong_external_trust_root_rejected": True,
            "payload_hash_tampering_rejected": True,
            "manifest_signature_tampering_rejected": True,
            "unsafe_member_path_rejected": True,
            "production_signed_modelpack_tested": False,
            "production_model_pack_import_activated": False,
            "real_model_weights_tested_here": False,
            "whole_app_installed": False,
            "wps_modified": False,
            "production_lock_modified": False,
            "release_ready": False,
        }
    # By this point the entire temporary folder, keys and packages are deleted.
    output = repo / "artifacts" / "arm64-crypto-probe" / "modelpack-signature.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("PARTYOPS_ARM64_EPHEMERAL_SIGNED_MODEL_PACK_VERIFIER_PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
