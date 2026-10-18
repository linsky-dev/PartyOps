"""复用固定协议生成Windows独立接收器；原十文件只读，仅新增Windows后缀文件可写。"""
import hashlib
import importlib.util
import json
import re
from pathlib import Path

PROFILE = "windows-startup-20261017-v1"
BASELINE_SHA = "b52807452932502a5f05a10eb609c87345fabe4addb17d3ffeae3a673e0d0e57"
NAME = "PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe"
SHA = "3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27"
SIZE = 449978846
SOURCE = Path("C:/PartyOps-VM-Lab/records/kylin-20261012/acceptance/publication-20261016/receiver_factory.py")
SOURCE_SHA = "124cc543bdf909f79c3ea632505242d0ca121c5869d60467679c40a26241dd43"


def candidate():
    return {"file_name": NAME, "bytes": SIZE, "sha256": SHA, "magic_hex": "4d5a5000", "content_type": "application/octet-stream"}


def build_receiver(retained, manifest_sha):
    if len(retained) != 10 or len({row["file_name"] for row in retained}) != 10 or NAME in {row["file_name"] for row in retained}:
        raise RuntimeError("TEN_UNIQUE_EXISTING_ASSETS_REQUIRED")
    if not re.fullmatch(r"[a-f0-9]{64}", manifest_sha) or any(
        not re.fullmatch(r"[A-Za-z0-9._-]+", row["file_name"]) or not re.fullmatch(r"[a-f0-9]{64}", row["sha256"])
        or not isinstance(row["bytes"], int) or row["bytes"] <= 0 or not re.fullmatch(r"[a-f0-9]{8}", row["magic_hex"])
        for row in retained):
        raise RuntimeError("FIXED_RECEIVER_ASSET_IDENTITY_REQUIRED")
    if hashlib.sha256(SOURCE.read_bytes()).hexdigest() != SOURCE_SHA:
        raise RuntimeError("PINNED_RECEIVER_FACTORY_CHANGED")
    spec = importlib.util.spec_from_file_location("windows_pinned_receiver_protocol", SOURCE)
    source = importlib.util.module_from_spec(spec); spec.loader.exec_module(source)
    script = source.build_receiver(retained, candidate(), manifest_sha)
    replacements = {
        '"kylin-full-replace-v1"': json.dumps(PROFILE),
        'meta.baseline_manifest_sha256 !== "32fb5e85874c6ab400d0a1c1c05b9864c3a2e232202b3cf74a452cc8ca12b71c"':
            'meta.baseline_manifest_sha256 !== "' + BASELINE_SHA + '"',
        ".partyops-kylin-full-20261016-": ".partyops-windows-startup-20261017-",
        "PartyOps Kylin full candidate receiver ready": "PartyOps Windows startup candidate receiver ready",
        "新麒麟候选批次": "新Windows候选批次",
    }
    for old, new in replacements.items():
        if script.count(old) != 1: raise RuntimeError("PINNED_WINDOWS_RECEIVER_ANCHOR_CHANGED")
        script = script.replace(old, new, 1)
    return script
