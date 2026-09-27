"""Win7 冻结入口提前固定 OpenSSL 路径，避免访问构建机盘符和空光驱。"""
import os
import sys

# PyInstaller 自定义运行钩子先于依赖钩子和业务导入执行；所有入口独立生效。
runtime = os.path.abspath(sys._MEIPASS)
os.environ["OPENSSL_CONF"] = os.path.join(runtime, "partyops-openssl.cnf")
os.environ["OPENSSL_MODULES"] = runtime
os.environ["OPENSSL_ENGINES"] = runtime
# PartyOps 使用默认提供器中的 RSA、AES/Fernet，不使用 RC2/RC4 等旧算法。
# Win7 静态回移库没有 legacy 模块，禁止按编译时 E: 路径查找它。
os.environ["CRYPTOGRAPHY_OPENSSL_NO_LEGACY"] = "1"
