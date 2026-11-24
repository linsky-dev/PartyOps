"""Use exact official pinned SQLite ARM64 binary; verify actual loaded CPython DLL."""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import urllib.request
import zipfile


def require(ok, msg):
    if not ok:
        raise RuntimeError(msg)


def main():
    out = Path(sys.argv[1]).resolve()
    out.mkdir(parents=True)
    url = "https://sqlite.org/2026/sqlite-dll-win-arm64-3530400.zip"
    expected = "4b2c43549b9c8d9b9eb4acf3dc524f05a1a0fb05364a93da2ad4dd9317c389c4"
    with urllib.request.urlopen(url, timeout=60) as resp:
        archive = resp.read(5 * 1024 * 1024 + 1)
    require(hashlib.sha3_256(archive).hexdigest() == expected, "official archive hash mismatch")
    zpath = out / "sqlite-dll-win-arm64-3530400.zip"
    zpath.write_bytes(archive)
    with zipfile.ZipFile(zpath) as z:
        require(z.namelist().count("sqlite3.dll") == 1, "unexpected archive")
        require(z.getinfo("sqlite3.dll").file_size < 16 * 1024 * 1024, "DLL size")
        data = z.read("sqlite3.dll")
    offset = struct.unpack_from("<I", data, 60)[0]
    require(data[:2] == b"MZ" and data[offset:offset+6] == b"PE\0\0\x64\xaa", "native PE")
    dll = out / "sqlite3.dll"
    dll.write_bytes(data)
    native = ctypes.WinDLL(str(dll))
    native.sqlite3_libversion.restype = ctypes.c_char_p
    require(native.sqlite3_libversion() == b"3.53.4", "SQLite version")
    with tempfile.TemporaryDirectory(prefix="partyops-arm64-sqlite-") as temp:
        copy = Path(temp) / "python"
        shutil.copytree(sys.base_prefix, copy, ignore=shutil.ignore_patterns("site-packages", "__pycache__"))
        targets = [p for p in copy.rglob("sqlite3.dll") if p.is_file()]
        require(bool(targets), "CPython SQLite library missing")
        for path in targets:
            shutil.copy2(dll, path)
        code = """
import ctypes,json,pathlib,sqlite3,sys
assert sqlite3.sqlite_version == '3.53.4'
k=ctypes.WinDLL('kernel32')
k.GetModuleHandleW.argtypes=[ctypes.c_wchar_p]
k.GetModuleHandleW.restype=ctypes.c_void_p
h=k.GetModuleHandleW('sqlite3.dll')
assert h
k.GetModuleFileNameW.argtypes=[ctypes.c_void_p,ctypes.c_wchar_p,ctypes.c_uint]
k.GetModuleFileNameW.restype=ctypes.c_uint
buf=ctypes.create_unicode_buffer(32768)
assert k.GetModuleFileNameW(h,buf,len(buf))
p=pathlib.Path(buf.value).resolve()
assert pathlib.Path(sys.base_prefix).resolve() in p.parents
import hashlib
assert hashlib.sha256(p.read_bytes()).hexdigest()==sys.argv[1]
db=sqlite3.connect(sys.argv[2])
assert db.execute('pragma journal_mode=WAL').fetchone()[0]=='wal'
db.execute('create table fixture(id integer primary key, name text)')
db.execute('insert into fixture values(1,?)',('合成数据',))
db.commit()
db.execute('update fixture set name=? where id=1',('rollback',))
db.rollback()
assert db.execute('select name from fixture').fetchone()[0]=='合成数据'
assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
assert db.execute("select json_extract(?, ?)", (json.dumps({"a":1}), "$.a")).fetchone()[0]==1
db.close()
print(json.dumps({'version':sqlite3.sqlite_version,'native_module_sha256':sys.argv[1],'wal':True,'rollback':True,'integrity':True,'json':True}))
"""
        result = subprocess.run([str(copy / "python.exe"), "-I", "-S", "-c", code,
                                 hashlib.sha256(data).hexdigest(), str(Path(temp) / "probe.db")],
                                capture_output=True, text=True, encoding="utf-8", timeout=90)
        (out / "native-probe.log").write_text(result.stdout + result.stderr, encoding="utf-8")
        require(result.returncode == 0, "native CPython SQLite test failed")
        probe = json.loads(result.stdout)
    (out / "SOURCE.json").write_text(json.dumps({
        "source_commit": os.environ["SOURCE_SHA"], "architecture":"arm64", "version":"3.53.4",
        "url":url, "archive_sha3_256":expected, "dll_sha256":hashlib.sha256(data).hexdigest(),
        "native_cpython_probe":probe, "status":"NATIVE_SQLITE_INPUT_PASS_ONLY",
        "installer_created":False, "release_ready":False,
    }, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
