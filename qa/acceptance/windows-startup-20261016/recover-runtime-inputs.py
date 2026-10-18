"""只读复用现有 QA 包内第三方运行库，逐文件对照原包清单，不复制业务数据。"""
from __future__ import annotations

import hashlib
import json
import shutil
from pathlib import Path, PurePosixPath

import paramiko

ROOT = Path(__file__).resolve().parents[3]
DEST = ROOT / "vendor/windows/libreoffice-headless-amd64"
REMOTE = "C:/PartyOps QA/中文 程序"
client = paramiko.SSHClient()
client.load_host_keys("D:/PartyOps-VM-Lab/keys/known_hosts")
client.connect("127.0.0.1", port=22110, username="partyopsqa",
               key_filename="D:/PartyOps-VM-Lab/keys/guest_ed25519",
               allow_agent=False, look_for_keys=False)
try:
    with client.open_sftp() as sftp:
        identity = json.loads(sftp.open("C:/ProgramData/PartyOps-VM-Lab/identity.json", "rb").read().decode("utf-8-sig"))
        if identity.get("uuid") != "f47cf265-72a2-4d51-9cf1-b1f5ae960cb2" or identity.get("purpose") != "disposable-qa":
            raise RuntimeError("QA_GUEST_IDENTITY_MISMATCH")
        raw = sftp.open(REMOTE + "/release-manifest.json", "rb").read()
        if hashlib.sha256(raw).hexdigest() != "1c5842c8bd7ba8cfe9b76d2d87e6d0554f7cb8d627cebc89a3f0fcc333a4ce8d":
            raise RuntimeError("INSTALLED_MANIFEST_CHANGED")
        manifest = json.loads(raw)
        entries = [row for row in manifest["files"] if row["path"].startswith("office-runtime/")]
        total = 0
        for row in entries:
            relative = PurePosixPath(row["path"]).relative_to("office-runtime")
            if ".." in relative.parts or relative.is_absolute():
                raise RuntimeError("RUNTIME_PATH_INVALID")
            target = DEST.joinpath(*relative.parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            if not target.exists() or hashlib.sha256(target.read_bytes()).hexdigest() != row["sha256"]:
                # 同机官方 LibreOffice 与原包逐文件哈希相等时直接复用，
                # 避免逐个通过模拟网卡传输。仅修复本脚本独占输出中的未完成复制。
                local = Path("C:/Program Files/LibreOffice").joinpath(*relative.parts)
                temporary = target.with_name(target.name + ".copying")
                if local.is_file() and hashlib.sha256(local.read_bytes()).hexdigest() == row["sha256"]:
                    shutil.copyfile(local, temporary)
                else:
                    sftp.get(REMOTE + "/" + row["path"], str(temporary))
                if hashlib.sha256(temporary.read_bytes()).hexdigest() != row["sha256"]:
                    raise RuntimeError("RUNTIME_HASH_MISMATCH:" + str(relative))
                temporary.replace(target)
            if target.stat().st_size != row["size"] or hashlib.sha256(target.read_bytes()).hexdigest() != row["sha256"]:
                raise RuntimeError("RUNTIME_HASH_MISMATCH:" + str(relative))
            total += row["size"]
        evidence = {"source": "既有 Win10 QA 安装目录，仅第三方 office-runtime", "manifest_sha256": hashlib.sha256(raw).hexdigest(), "files": len(entries), "bytes": total, "file_hashes_verified": True, "destination": str(DEST)}
        (Path(__file__).parent / "runtime-inputs-recovery.json").write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps(evidence, ensure_ascii=False))
finally:
    client.close()
