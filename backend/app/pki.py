"""PartyOps 内部 CA 与主机 HTTPS 证书。"""

from __future__ import annotations

import ipaddress
import os
import socket
import tempfile
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import TypedDict

from cryptography import x509
from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.x509.oid import ExtendedKeyUsageOID, NameOID

from .config import Settings
from .windows_public_files import public_host_ca_path, write_public_control_file


def _write_private(path, data: bytes, mode: int = 0o600) -> None:
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    descriptor, filename = tempfile.mkstemp(dir=path.parent, prefix=f".{path.name}.")
    temporary = Path(filename)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
        if os.name != "nt":
            temporary.chmod(mode)
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def _load_key(path):
    return serialization.load_pem_private_key(path.read_bytes(), password=None)


def _public_key_bytes(key) -> bytes:
    return key.public_bytes(
        serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo,
    )


class TLSMaterial(TypedDict):
    """已生成证书的公共元数据；不包含私钥。"""

    ca_path: Path
    server_cert_path: Path
    fingerprint: str


def ensure_tls_material(settings: Settings) -> TLSMaterial:
    """按需生成并复用主机证书；密钥只存在 secrets/pki。"""

    advertised_host = getattr(settings, "network_advertise_host", settings.host)
    root = settings.secrets_dir / "pki"
    ca_key_path = root / "ca.key"
    ca_cert_path = root / "ca.pem"
    server_key_path = root / "server.key"
    server_cert_path = root / "server.pem"
    now = datetime.now(timezone.utc)
    if not ca_key_path.is_file() or not ca_cert_path.is_file():
        if root.exists() or root.is_symlink():
            raise ValueError("PartyOps 内部 CA 材料缺失，请核查主机备份后恢复。")
        ca_key = rsa.generate_private_key(public_exponent=65537, key_size=3072)
        ca_subject = x509.Name(
            [
                x509.NameAttribute(NameOID.ORGANIZATION_NAME, "PartyOps 内部 CA"),
                x509.NameAttribute(NameOID.COMMON_NAME, "PartyOps Local Root"),
            ]
        )
        ca_cert = (
            x509.CertificateBuilder()
            .subject_name(ca_subject)
            .issuer_name(ca_subject)
            .public_key(ca_key.public_key())
            .serial_number(x509.random_serial_number())
            .not_valid_before(now - timedelta(minutes=5))
            .not_valid_after(now + timedelta(days=3650))
            .add_extension(x509.BasicConstraints(ca=True, path_length=1), critical=True)
            .add_extension(
                x509.KeyUsage(
                    digital_signature=True,
                    key_encipherment=False,
                    key_cert_sign=True,
                    crl_sign=True,
                    content_commitment=False,
                    data_encipherment=False,
                    key_agreement=False,
                    encipher_only=False,
                    decipher_only=False,
                ),
                critical=True,
            )
            .sign(ca_key, hashes.SHA256())
        )
        _write_private(
            ca_key_path,
            ca_key.private_bytes(
                serialization.Encoding.PEM,
                serialization.PrivateFormat.PKCS8,
                serialization.NoEncryption(),
            ),
        )
        _write_private(
            ca_cert_path,
            ca_cert.public_bytes(serialization.Encoding.PEM),
            0o644,
        )
    try:
        if any(path.is_symlink() for path in (root, ca_key_path, ca_cert_path)):
            raise ValueError("PartyOps 内部 CA 必须位于普通本机文件")
        ca_key = _load_key(ca_key_path)
        ca_cert = x509.load_pem_x509_certificate(ca_cert_path.read_bytes())
        constraints = ca_cert.extensions.get_extension_for_class(
            x509.BasicConstraints
        ).value
        if not constraints.ca:
            raise ValueError("PartyOps 内部 CA 证书缺少 CA 约束")
        if _public_key_bytes(ca_key.public_key()) != _public_key_bytes(ca_cert.public_key()):
            raise ValueError("PartyOps 内部 CA 私钥与证书不匹配")
        ca_cert.verify_directly_issued_by(ca_cert)
    except (ValueError, TypeError, OSError, InvalidSignature, x509.ExtensionNotFound) as exc:
        # 不能静默重建根 CA；那会让所有已入网设备失去信任。保留原文件并
        # 给监督服务一个可分类的 TLS 诊断，由管理员决定恢复备份或重置。
        raise ValueError("PartyOps 内部 CA 完整性校验失败，原证书已保留") from exc
    regenerate_server = not server_key_path.exists() or not server_cert_path.exists()
    if not regenerate_server:
        try:
            if server_key_path.is_symlink() or server_cert_path.is_symlink():
                raise ValueError("主机证书必须使用普通本机文件")
            current = x509.load_pem_x509_certificate(server_cert_path.read_bytes())
            current.verify_directly_issued_by(ca_cert)
            if _public_key_bytes(_load_key(server_key_path).public_key()) != _public_key_bytes(current.public_key()):
                raise ValueError("主机证书与私钥不匹配")
            # cryptography 新版提供带时区的属性；仅在旧版不存在时才访问弃用属性。
            expiry = getattr(current, "not_valid_after_utc", None)
            if expiry is None:
                expiry = current.not_valid_after.replace(tzinfo=timezone.utc)
            regenerate_server = expiry <= now + timedelta(days=30)
            if not regenerate_server:
                san = current.extensions.get_extension_for_class(
                    x509.SubjectAlternativeName
                ).value
                host_match = False
                try:
                    host_match = x509.IPAddress(
                        ipaddress.ip_address(advertised_host)
                    ) in san
                except ValueError:
                    host_match = x509.DNSName(advertised_host) in san
                regenerate_server = not host_match
        except (ValueError, TypeError, OSError, InvalidSignature, x509.ExtensionNotFound):
            regenerate_server = True
    if regenerate_server:
        server_key = rsa.generate_private_key(public_exponent=65537, key_size=3072)
        names: list[x509.GeneralName] = [
            x509.DNSName("localhost"),
            x509.DNSName(socket.gethostname()[:253]),
            x509.IPAddress(ipaddress.ip_address("127.0.0.1")),
        ]
        try:
            names.extend(
                x509.IPAddress(ipaddress.ip_address(value))
                for value in socket.gethostbyname_ex(socket.gethostname())[2]
            )
        except (OSError, ValueError):
            pass
        try:
            names.append(
                x509.IPAddress(ipaddress.ip_address(advertised_host))
            )
        except ValueError:
            # 配置允许主机名；该名称同时加入 SAN，浏览器仍会严格校验。
            advertised = advertised_host
            if advertised and advertised not in {"0.0.0.0", "::"}:  # nosec B104 - 通配值被排除在证书 SAN 外。
                names.append(x509.DNSName(advertised[:253]))
        subject = x509.Name(
            [
                x509.NameAttribute(NameOID.ORGANIZATION_NAME, "PartyOps"),
                x509.NameAttribute(NameOID.COMMON_NAME, socket.gethostname()[:253]),
            ]
        )
        server_cert = (
            x509.CertificateBuilder()
            .subject_name(subject)
            .issuer_name(ca_cert.subject)
            .public_key(server_key.public_key())
            .serial_number(x509.random_serial_number())
            .not_valid_before(now - timedelta(minutes=5))
            .not_valid_after(now + timedelta(days=825))
            .add_extension(x509.SubjectAlternativeName(names), critical=False)
            .add_extension(x509.BasicConstraints(ca=False, path_length=None), critical=True)
            .add_extension(
                x509.ExtendedKeyUsage([ExtendedKeyUsageOID.SERVER_AUTH]),
                critical=False,
            )
            .sign(ca_key, hashes.SHA256())
        )
        _write_private(
            server_key_path,
            server_key.private_bytes(
                serialization.Encoding.PEM,
                serialization.PrivateFormat.PKCS8,
                serialization.NoEncryption(),
            ),
        )
        _write_private(
            server_cert_path,
            server_cert.public_bytes(serialization.Encoding.PEM),
            0o644,
        )
    if os.name == "nt" and settings.mode == "host" and settings.environment == "production" and os.getenv("PARTYOPS_ENVIRONMENT") != "test":
        # SYSTEM 只发布已验证根证书；私钥和业务数据继续留在受保护目录。
        write_public_control_file(public_host_ca_path(), ca_cert.public_bytes(serialization.Encoding.PEM))
    settings.tls_cert_file = server_cert_path
    settings.tls_key_file = server_key_path
    settings.tls_client_ca_file = ca_cert_path
    return {
        "ca_path": ca_cert_path,
        "server_cert_path": server_cert_path,
        "fingerprint": ca_cert.fingerprint(hashes.SHA256()).hex(),
    }


def issue_device_certificate(
    settings: Settings,
    device_id: str,
    csr_pem: str | None,
) -> dict[str, str]:
    """签发短期 Agent 客户端证书；没有 CSR 时仅返回 CA 信息以兼容旧终端。"""

    ensure_tls_material(settings)
    ca_key = _load_key(settings.secrets_dir / "pki" / "ca.key")
    ca_cert = x509.load_pem_x509_certificate(
        (settings.secrets_dir / "pki" / "ca.pem").read_bytes()
    )
    if not csr_pem:
        return {
            "certificate_pem": "",
            "ca_certificate_pem": (settings.secrets_dir / "pki" / "ca.pem").read_text(
                encoding="utf-8"
            ),
            "certificate_fingerprint": "",
        }
    csr = x509.load_pem_x509_csr(csr_pem.encode("utf-8"))
    if not csr.is_signature_valid:
        raise ValueError("设备证书请求签名无效")
    public_key = csr.public_key()
    if not isinstance(public_key, rsa.RSAPublicKey) or public_key.key_size < 2048:
        raise ValueError("设备证书请求必须使用至少 2048 位 RSA 密钥")
    cert = (
        x509.CertificateBuilder()
        .subject_name(
            x509.Name(
                [
                    x509.NameAttribute(NameOID.ORGANIZATION_NAME, "PartyOps Device"),
                    x509.NameAttribute(NameOID.COMMON_NAME, device_id),
                ]
            )
        )
        .issuer_name(ca_cert.subject)
        .public_key(public_key)
        .serial_number(x509.random_serial_number())
        .not_valid_before(datetime.now(timezone.utc) - timedelta(minutes=5))
        .not_valid_after(datetime.now(timezone.utc) + timedelta(days=365))
        .add_extension(
            x509.ExtendedKeyUsage([ExtendedKeyUsageOID.CLIENT_AUTH]),
            critical=False,
        )
        .sign(ca_key, hashes.SHA256())
    )
    return {
        "certificate_pem": cert.public_bytes(serialization.Encoding.PEM).decode(),
        "ca_certificate_pem": (settings.secrets_dir / "pki" / "ca.pem").read_text(
            encoding="utf-8"
        ),
        "certificate_fingerprint": cert.fingerprint(hashes.SHA256()).hex(),
    }
