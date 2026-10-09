"""ARM输入准入的离线回归；合成输入不等于原生运行证明。"""
import importlib.util
import io
import json
import tarfile
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location("arm_profile", ROOT / "scripts/macos-mono-input-profile.py")
profile = importlib.util.module_from_spec(spec)
spec.loader.exec_module(profile)


def fixture(tmp_path, monkeypatch):
    prefix = tmp_path / "sdk"
    entries = {"etc/mono/config": b"<configuration/>", "etc/mono/4.5/machine.config": b"<configuration/>",
               "include/mono-2.0/mono/jit/jit.h": b"jit", "lib/mono/4.5/mscorlib.dll": b"bcl",
               "lib/mono/4.5/Facades/System.Runtime.dll": b"facade", "lib/libmonosgen-2.0.a": b"static"}
    entries.update({"lib/" + name: b"native" + name.encode() for name, _ in profile.LIBRARIES.values()})
    archive = tmp_path / "bottle.tar.gz"
    with tarfile.open(archive, "w:gz") as tar:
        for name, data in entries.items():
            path = prefix / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            member = tarfile.TarInfo("mono/6.14.1/" + name)
            member.size = len(data)
            tar.addfile(member, io.BytesIO(data))
    (prefix / "lib/libmono-2.0.a").write_bytes(b"static")
    # 仅合成fixture的先验摘要；生产常量仍锁真实官方bottle。
    monkeypatch.setattr(profile, "BOTTLE_SHA", profile.sha256(archive))
    return archive, prefix


def audit(archive, prefix):
    return profile.audit_bottle_inputs(archive, prefix, prefix / "etc/mono/config", prefix / "lib/mono/4.5",
                                       prefix / "include/mono-2.0", prefix / "lib")


def test_bottle_complete_inputs(tmp_path, monkeypatch):
    archive, prefix = fixture(tmp_path, monkeypatch)
    bindings = audit(archive, prefix)
    assert "lib/mono/4.5/Facades/System.Runtime.dll" in bindings


@pytest.mark.parametrize("name", ["etc/mono/config", "etc/mono/4.5/machine.config", "include/mono-2.0/mono/jit/jit.h",
                                  "lib/mono/4.5/mscorlib.dll", "lib/libmono-2.0.a", "lib/libmono-native.0.dylib"])
def test_reject_mixed_sdk_bytes(tmp_path, monkeypatch, name):
    archive, prefix = fixture(tmp_path, monkeypatch)
    (prefix / name).write_bytes(b"other-sdk")
    with pytest.raises(RuntimeError, match="SDK_BYTES_MISMATCH"):
        audit(archive, prefix)


def test_reject_missing_facade(tmp_path, monkeypatch):
    archive, prefix = fixture(tmp_path, monkeypatch)
    (prefix / "lib/mono/4.5/Facades/System.Runtime.dll").unlink()
    with pytest.raises(RuntimeError, match="SDK_INCOMPLETE"):
        audit(archive, prefix)


def test_reject_unknown_bottle(tmp_path, monkeypatch):
    archive, prefix = fixture(tmp_path, monkeypatch)
    monkeypatch.setattr(profile, "BOTTLE_SHA", "0" * 64)
    with pytest.raises(RuntimeError, match="BOTTLE_MISMATCH"):
        audit(archive, prefix)


def test_archive_minimum_each_object():
    def body(name, value):
        return f"lib.a({name}):\nLoad command 0\n cmd LC_BUILD_VERSION\n minos {value}\n"
    assert profile.check_minimum(body("a.o", "15.0") + body("b.o", "15.0"), archive=True) == ["15.0"]
    with pytest.raises(RuntimeError, match="MIN_OS_TOO_NEW"):
        profile.check_minimum(body("a.o", "15.1"), archive=True)
    with pytest.raises(RuntimeError, match="MIN_OS_UNKNOWN"):
        profile.check_minimum(body("a.o", "15.0") + "lib.a(b.o):\n", archive=True)


def test_unknown_dllmap_rejected():
    with pytest.raises(RuntimeError, match="OFFICIAL_MAP_UNSUPPORTED"):
        profile.derive_config(b'<configuration><dllmap dll="System.Native" target="unknown"/></configuration>')


def official_config():
    catalog = json.loads((Path(__file__).parent / "native-run-37880422838/sdk-input-catalog.json").read_text(encoding="utf-8"))
    return next(item["content"].encode() for item in catalog["text_inputs"] if item["path"].endswith("etc/mono/config"))


def test_real_catalog_mapping_and_hash():
    original = official_config()
    derived, maps = profile.derive_config(original)
    assert len(maps) == 4
    assert maps[2]["os"] == "osx"
    assert b"@executable_path/libmono-native-compat.dylib" in derived
    assert b"@executable_path/libMonoPosixHelper.dylib" in derived
    assert b"$mono_libdir/libmono-btls-shared.dylib" in derived
    assert profile.hashlib.sha256(original).hexdigest() == profile.CONFIG_SHA


@pytest.mark.parametrize("change", ["duplicate", "os", "name", "children", "unrelated"])
def test_reject_altered_official_config(change):
    original = official_config()
    if change == "duplicate":
        modified = original.replace(b"</configuration>", b'<dllmap dll="System.Native" target="$mono_libdir/libmono-native.dylib" os="!windows" /></configuration>')
    elif change == "os":
        modified = original.replace(b'dll="System.Native" target="$mono_libdir/libmono-native.dylib" os="!windows"', b'dll="System.Native" target="$mono_libdir/libmono-native.dylib" os="osx"')
    elif change == "name":
        modified = original.replace(b'dll="System.Native"', b'dll="System.Native" name="extra"')
    elif change == "children":
        modified = original.replace(b'dll="System.Native" target="$mono_libdir/libmono-native.dylib" os="!windows" />', b'dll="System.Native" target="$mono_libdir/libmono-native.dylib" os="!windows"><dllentry name="extra"/></dllmap>')
    else:
        modified = original.replace(b'libintl.dylib', b'unknown.dylib')
    with pytest.raises(RuntimeError, match="OFFICIAL_MAP_UNSUPPORTED|CONFIG_HASH_MISMATCH"):
        profile.derive_config(modified)


def test_intel_and_arm_deployment_branches():
    shell = (ROOT / "scripts/build-document-formatter-host-unix.sh").read_text(encoding="utf-8")
    assert "export MACOSX_DEPLOYMENT_TARGET='11.0'" in shell
    assert "[[ \"$ARCHITECTURE\" != 'arm64' ]] || export MACOSX_DEPLOYMENT_TARGET='15.0'" in shell
    assert "87ac2d657bb9278bf9330c871dfd4fb8472b109334913a9b78a04f02663fa372" in shell
