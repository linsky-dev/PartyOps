"""模拟安装目录迁移和错误外部配置，验证早期钩子及真实 RSA/Fernet 运算。"""
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path


def test_relocated_win7_hook_ignores_build_paths_and_keeps_crypto(tmp_path):
    root = Path(__file__).resolve().parents[2]
    runtime = tmp_path / "中文 程序"
    runtime.mkdir()
    shutil.copyfile(root / "packaging/windows/partyops-openssl.cnf", runtime / "partyops-openssl.cnf")
    environment = os.environ.copy()
    # 若钩子执行过晚或保留调用者设置，OpenSSL 将读取有意无效的配置。
    poison = tmp_path / "invalid.cnf"
    poison.write_text("openssl_conf = broken\n[broken\n", encoding="utf-8")
    environment.update(OPENSSL_CONF=str(poison), OPENSSL_MODULES="E:\\missing-build\\modules",
                       OPENSSL_ENGINES="E:\\missing-build\\engines", CRYPTOGRAPHY_OPENSSL_NO_LEGACY="0")
    script = r'''
import json, os, runpy, sys
sys._MEIPASS = sys.argv[1]
runpy.run_path(sys.argv[2])
from cryptography.fernet import Fernet
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding, rsa
payload = b"PartyOps relocated Win7 runtime"
fernet = Fernet(Fernet.generate_key())
assert fernet.decrypt(fernet.encrypt(payload)) == payload
private = rsa.generate_private_key(public_exponent=65537, key_size=2048)
signature = private.sign(payload, padding.PKCS1v15(), hashes.SHA256())
private.public_key().verify(signature, payload, padding.PKCS1v15(), hashes.SHA256())
print(json.dumps({key: os.environ[key] for key in (
    "OPENSSL_CONF", "OPENSSL_MODULES", "OPENSSL_ENGINES", "CRYPTOGRAPHY_OPENSSL_NO_LEGACY")}))
'''
    result = subprocess.run([sys.executable, "-c", script, str(runtime),
        str(root / "packaging/windows/win7_openssl_hook.py")], env=environment,
        capture_output=True, text=True, check=True, timeout=30)
    observed = json.loads(result.stdout)
    assert Path(observed["OPENSSL_CONF"]) == runtime / "partyops-openssl.cnf"
    assert observed["OPENSSL_MODULES"] == observed["OPENSSL_ENGINES"] == str(runtime)
    assert observed["CRYPTOGRAPHY_OPENSSL_NO_LEGACY"] == "1"
    assert "legacy provider failed" not in result.stderr
