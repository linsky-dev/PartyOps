#!/usr/bin/env python3
"""在当前 Python 运行时验证 SM3 与 RFC 8998 SM4-GCM 能力。"""

from __future__ import annotations

import argparse
import json
import os
import platform
import ssl
import sys
from importlib.metadata import PackageNotFoundError, version
from pathlib import Path

EXPECTED_CRYPTOGRAPHY_VERSION = "50.0.0"
SM3_ABC = "66c7f0f462eeedd9d1f2d46bdc10e4e24167c4875cf2f7a2297da02b8f4ba8e0"

# RFC 8998 Appendix A.1 SM4-GCM test vector.
RFC8998_KEY = bytes.fromhex("0123456789ABCDEFFEDCBA9876543210")
RFC8998_NONCE = bytes.fromhex("00001234567800000000ABCD")
RFC8998_PLAINTEXT = bytes.fromhex(
    "AAAAAAAAAAAAAAAABBBBBBBBBBBBBBBB"
    "CCCCCCCCCCCCCCCCDDDDDDDDDDDDDDDD"
    "EEEEEEEEEEEEEEEEFFFFFFFFFFFFFFFF"
    "EEEEEEEEEEEEEEEEAAAAAAAAAAAAAAAA"
)
RFC8998_AAD = bytes.fromhex("FEEDFACEDEADBEEFFEEDFACEDEADBEEFABADDAD2")
RFC8998_CIPHERTEXT = bytes.fromhex(
    "17F399F08C67D5EE19D0DC9969C4BB7D"
    "5FD46FD3756489069157B282BB200735"
    "D82710CA5C22F0CCFA7CBF93D496AC15"
    "A56834CBCF98C397B4024A2691233B8D"
)
RFC8998_TAG = bytes.fromhex("83DE3541E4C2B58177E065A9BF7B62EC")


def _runtime_metadata() -> dict[str, str]:
    metadata = {
        "runner_name": os.environ.get("RUNNER_NAME", "local"),
        "runner_os": os.environ.get("RUNNER_OS", platform.system()),
        "runner_architecture": os.environ.get("RUNNER_ARCH", platform.machine()),
        "system": platform.platform(),
        "python": sys.version.split()[0],
        "python_implementation": platform.python_implementation(),
        "python_architecture": platform.machine(),
        "python_ssl": ssl.OPENSSL_VERSION,
    }
    try:
        metadata["cryptography"] = version("cryptography")
    except PackageNotFoundError:
        metadata["cryptography"] = "missing"
    return metadata


def _verify_sm3() -> None:
    from cryptography.hazmat.primitives import hashes
    from cryptography.hazmat.primitives.hashes import Hash

    digest = Hash(hashes.SM3())
    digest.update(b"abc")
    if digest.finalize().hex() != SM3_ABC:
        raise AssertionError("SM3 abc vector mismatch")


def _verify_sm4_gcm() -> str:
    from cryptography.exceptions import InvalidTag
    from cryptography.hazmat.backends.openssl.backend import backend
    from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

    encryptor = Cipher(algorithms.SM4(RFC8998_KEY), modes.GCM(RFC8998_NONCE)).encryptor()
    encryptor.authenticate_additional_data(RFC8998_AAD)
    ciphertext = encryptor.update(RFC8998_PLAINTEXT) + encryptor.finalize()
    if ciphertext != RFC8998_CIPHERTEXT or encryptor.tag != RFC8998_TAG:
        raise AssertionError("RFC 8998 Appendix A.1 encryption vector mismatch")

    decryptor = Cipher(
        algorithms.SM4(RFC8998_KEY), modes.GCM(RFC8998_NONCE, RFC8998_TAG)
    ).decryptor()
    decryptor.authenticate_additional_data(RFC8998_AAD)
    plaintext = decryptor.update(RFC8998_CIPHERTEXT) + decryptor.finalize()
    if plaintext != RFC8998_PLAINTEXT:
        raise AssertionError("RFC 8998 Appendix A.1 decryption vector mismatch")

    tampered_tag = bytearray(RFC8998_TAG)
    tampered_tag[-1] ^= 1
    try:
        decryptor = Cipher(
            algorithms.SM4(RFC8998_KEY), modes.GCM(RFC8998_NONCE, bytes(tampered_tag))
        ).decryptor()
        decryptor.authenticate_additional_data(RFC8998_AAD)
        decryptor.update(RFC8998_CIPHERTEXT)
        decryptor.finalize()
    except InvalidTag:
        pass
    else:
        raise AssertionError("SM4-GCM accepted a modified authentication tag")

    try:
        decryptor = Cipher(
            algorithms.SM4(RFC8998_KEY), modes.GCM(RFC8998_NONCE, RFC8998_TAG)
        ).decryptor()
        decryptor.authenticate_additional_data(RFC8998_AAD[:-1] + b"\x00")
        decryptor.update(RFC8998_CIPHERTEXT)
        decryptor.finalize()
    except InvalidTag:
        pass
    else:
        raise AssertionError("SM4-GCM accepted modified additional authenticated data")

    return backend.openssl_version_text()


def probe() -> tuple[dict[str, object], int]:
    result: dict[str, object] = {
        "schema_version": 1,
        "status": "NOT_RUN",
        "runtime": _runtime_metadata(),
        "vectors": {
            "sm3_abc": "NOT_RUN",
            "sm4_gcm_rfc8998_appendix_a_1": "NOT_RUN",
            "sm4_gcm_modified_tag_rejected": "NOT_RUN",
            "sm4_gcm_modified_aad_rejected": "NOT_RUN",
        },
    }
    actual_version = result["runtime"]["cryptography"]
    if actual_version == "missing":
        result.update(status="BLOCKED", reason="cryptography is not installed")
        return result, 2
    if actual_version != EXPECTED_CRYPTOGRAPHY_VERSION:
        result.update(
            status="FAIL",
            reason=(
                f"expected cryptography {EXPECTED_CRYPTOGRAPHY_VERSION}, "
                f"found {actual_version}"
            ),
        )
        return result, 1

    try:
        from cryptography.exceptions import UnsupportedAlgorithm
    except (ImportError, OSError) as error:
        result.update(status="BLOCKED", reason=f"Cryptography runtime unavailable: {error}")
        return result, 2
    try:
        _verify_sm3()
        result["vectors"]["sm3_abc"] = "PASS"
        openssl_version = _verify_sm4_gcm()
        result["vectors"].update(
            sm4_gcm_rfc8998_appendix_a_1="PASS",
            sm4_gcm_modified_tag_rejected="PASS",
            sm4_gcm_modified_aad_rejected="PASS",
        )
        result["runtime"]["cryptography_openssl"] = openssl_version
    except (AttributeError, ImportError, UnsupportedAlgorithm) as error:
        result.update(status="BLOCKED", reason=f"SM provider or runtime probe failed: {error}")
        return result, 2
    except (AssertionError, OSError, RuntimeError, TypeError, ValueError) as error:
        result.update(status="FAIL", reason=f"SM runtime validation failed: {error}")
        return result, 1

    result["status"] = "PASS"
    return result, 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument(
        "--installation-error",
        help="Record a failed wheel installation as BLOCKED and retain the pip error.",
    )
    args = parser.parse_args()

    if args.installation_error:
        result: dict[str, object] = {
            "schema_version": 1,
            "status": "BLOCKED",
            "reason": "cryptography wheel installation failed; runtime support is unverified",
            "installation_error": args.installation_error,
            "runtime": _runtime_metadata(),
        }
        exit_code = 2
    else:
        result, exit_code = probe()

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
