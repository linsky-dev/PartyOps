#!/usr/bin/env python3
"""锁定 Apple Silicon Mono 6.14.1 官方 bottle 输入；不执行或修补 SDK。"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import tarfile
from pathlib import Path
import xml.etree.ElementTree as ET

PROFILE = "winehq-mono-6.14.1-homebrew-arm64-sequoia"
BOTTLE_SHA = "ada6e9683f3e1c9c4f0dc372680250252cfca61ac77c1f8f6299ad3b8452cfd8"
STATIC_SHA = "1691588705499cfe9b925a217a7cd5e44fa3b9785743cb3b235ad9f108642570"
CONFIG_SHA = "2875560bc6a3dc12746ea1de88eb7ba1dcb4caf4b08f2d33eafa2e2dda53f545"
MACHINE_CONFIG_SHA = "d2fbbcc3c8fa4565fece9b1293ffdeb3dbd177f0a2a8e38e07eca2ad08b07b11"
DLLMAPS = {
    "System.Native": ("$mono_libdir/libmono-native.dylib", "!windows"),
    "System.Net.Security.Native": ("$mono_libdir/libmono-native.dylib", "!windows"),
    "System.Security.Cryptography.Native.Apple": ("$mono_libdir/libmono-native.dylib", "osx"),
    "MonoPosixHelper": ("$mono_libdir/libMonoPosixHelper.dylib", "!windows"),
}
LIBRARIES = {
    "libmono-native-compat.dylib": ("libmono-native.0.dylib", "c8bcfe5a40229532e853535bc1bc5c033e0021c2121974d2e4cc4b3f0362081d"),
    "libMonoPosixHelper.dylib": ("libMonoPosixHelper.dylib", "0ffcd93dfbf5eaa378815a83bcf6bc80475bd869feb369b42e25c8f1ed775801"),
}


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def minimum_versions(text):
    """只解析部署命令；静态 archive 的每个对象都必须有已知 minos。"""
    versions = []
    for block in re.finditer(r"^\s*cmd\s+(LC_BUILD_VERSION|LC_VERSION_MIN_MACOSX)\s*\n(.*?)(?=^\s*(?:cmd\s|Load command\s)|\Z)", text, re.M | re.S):
        field = "minos" if block.group(1) == "LC_BUILD_VERSION" else "version"
        versions.extend(re.findall(r"^\s*" + field + r"\s+(\d+(?:\.\d+){0,2})\s*$", block.group(2), re.M))
    return versions


def check_minimum(text, archive=False):
    bodies = [text]
    if archive:
        headers = list(re.finditer(r"^.*\(([^)]+)\):\s*$", text, re.M))
        if not headers:
            raise RuntimeError("[MAC_MONO_STATIC_MIN_OS_UNKNOWN] archive没有可核验对象。")
        bodies = [text[h.end():headers[i + 1].start() if i + 1 < len(headers) else len(text)] for i, h in enumerate(headers)]
    versions = []
    for body in bodies:
        values = minimum_versions(body)
        if len(values) != 1:
            raise RuntimeError("[MAC_MONO_MIN_OS_UNKNOWN] 每个对象须有唯一最低系统版本。")
        value = tuple(map(int, values[0].split(".")))
        if (value + (0, 0, 0))[:3] > (15, 0, 0):
            raise RuntimeError("[MAC_MONO_MIN_OS_TOO_NEW] 输入最低系统超过15.0。")
        versions.extend(values)
    return sorted(set(versions))


def audit_bottle_inputs(archive, prefix, config, framework, include, library):
    """归档先验 SHA 绑定所有实际编译输入；不解压或执行归档。"""
    if sha256(archive) != BOTTLE_SHA:
        raise RuntimeError("[MAC_MONO_BOTTLE_MISMATCH] 官方bottle摘要不匹配。")
    prefix = prefix.resolve(strict=True)
    expected_paths = {config.absolute(): "etc/mono/config", (config.parent / "4.5/machine.config").absolute(): "etc/mono/4.5/machine.config"}
    roots = ((include / "mono", "include/mono-2.0/mono", lambda p: p.is_file()),
             (framework, "lib/mono/4.5", lambda p: p.is_file() and p.suffix == ".dll"))
    for directory, relative, accept in roots:
        paths = [p for p in directory.rglob("*") if accept(p)]
        if not paths:
            raise RuntimeError("[MAC_MONO_SDK_INCOMPLETE] 头文件或完整BCL目录为空。")
        for path in paths:
            expected_paths[path.absolute()] = relative + "/" + path.relative_to(directory).as_posix()
    expected_paths[(library / "libmono-2.0.a").absolute()] = "lib/libmonosgen-2.0.a"
    for name, _ in LIBRARIES.values():
        expected_paths[(library / name).absolute()] = "lib/" + name
    for path in expected_paths:
        if not path.resolve(strict=True).is_relative_to(prefix):
            raise RuntimeError("[MAC_MONO_SDK_ROOT_MISMATCH] BCL/config/header/native必须同一SDK目录。")
    records = {}
    with tarfile.open(archive, "r:gz") as tar:
        all_members = tar.getmembers()
        members = {m.name: m for m in all_members}
        if len(members) != len(all_members):
            raise RuntimeError("[MAC_MONO_BOTTLE_ENTRY_INVALID] 归档包含重复路径。")
        # 除检查已提供文件，也检查所有官方头文件/BCL，拒绝静默漏封装。
        required = {name.removeprefix("mono/6.14.1/") for name, m in members.items() if (m.isfile() or m.issym() or m.islnk()) and
                    (name.startswith("mono/6.14.1/include/mono-2.0/mono/") or
                     name.startswith("mono/6.14.1/lib/mono/4.5/") and name.endswith(".dll"))}
        if not required.issubset(set(expected_paths.values())):
            raise RuntimeError("[MAC_MONO_SDK_INCOMPLETE] 缺少官方头文件或BCL程序集。")
        for path, relative in expected_paths.items():
            member = members.get("mono/6.14.1/" + relative)
            if member is None:
                raise RuntimeError("[MAC_MONO_BOTTLE_ENTRY_MISSING] 原bottle缺少输入：" + relative)
            # TarFile按归档内链接寻找目标；不触及宿主文件系统。
            with tar.extractfile(member) as stream:
                digest = hashlib.sha256(stream.read()).hexdigest()
            if sha256(path) != digest:
                raise RuntimeError("[MAC_MONO_SDK_BYTES_MISMATCH] 输入已变化：" + relative)
            records[relative] = digest
    return records


def derive_config(original):
    """只接受已锁 bottle 的Darwin native映射，不猜测未知 dllmap。"""
    xml = ET.fromstring(original)
    maps = []
    for name, (target, os_filter) in DLLMAPS.items():
        candidates = [m for m in xml.iter("dllmap") if m.get("dll") == name]
        if (len(candidates) != 1 or candidates[0].attrib != {"dll": name, "target": target, "os": os_filter}
                or len(candidates[0]) != 0):
            evidence = [dict(m.attrib) for m in xml.findall("dllmap") if m.get("dll") == name]
            detail = re.sub(r"/(?:Users|home|opt|usr/local)/[^\"\s]*", "[SDK绝对路径已隐藏]", json.dumps(evidence, ensure_ascii=False))
            raise RuntimeError("[MAC_MONO_OFFICIAL_MAP_UNSUPPORTED] 官方ARM dllmap未知：" + name + " " + detail)
        maps.append(dict(candidates[0].attrib))
    if hashlib.sha256(original).hexdigest() != CONFIG_SHA:
        raise RuntimeError("[MAC_MONO_CONFIG_HASH_MISMATCH] 配置不是已核官方ARM原件。")
    derived = original.replace(b"$mono_libdir/libmono-native.dylib", b"@executable_path/libmono-native-compat.dylib")
    derived = derived.replace(b"$mono_libdir/libMonoPosixHelper.dylib", b"@executable_path/libMonoPosixHelper.dylib")
    ET.fromstring(derived)
    return derived, maps


def check_link_headers(link_include, original_include):
    """pkg-config实际编译头文件也必须与原bottle头文件相同。"""
    originals = {p.relative_to(original_include).as_posix(): sha256(p) for p in (original_include / "mono").rglob("*") if p.is_file()}
    linked = {p.relative_to(link_include).as_posix(): sha256(p) for p in (link_include / "mono").rglob("*") if p.is_file()}
    if not originals or linked != originals:
        raise RuntimeError("[MAC_MONO_LINK_HEADERS_MISMATCH] 编译工具头文件与锁定bottle不符。")


def prepare(config, library, stage, framework, include, archive, probe):
    prefix = library.resolve(strict=True).parent
    bindings = audit_bottle_inputs(archive, prefix, config, framework, include, library)
    if bindings.get("etc/mono/config") != CONFIG_SHA or bindings.get("etc/mono/4.5/machine.config") != MACHINE_CONFIG_SHA:
        raise RuntimeError("[MAC_MONO_CONFIG_HASH_MISMATCH] 固定ARM配置摘要不符。")
    static = (library / "libmono-2.0.a").resolve(strict=True)
    if sha256(static) != STATIC_SHA or static.name != "libmonosgen-2.0.a":
        raise RuntimeError("[MAC_MONO_STATIC_RUNTIME_MISMATCH] ARM静态运行时非锁定原件。")
    if probe(["lipo", "-archs", str(static)]).strip().split() != ["arm64"]:
        raise RuntimeError("[MAC_MONO_STATIC_RUNTIME_ARCH_MISMATCH] 必须单ARM64。")
    static_minimum = check_minimum(probe(["otool", "-arch", "arm64", "-l", str(static)]), archive=True)
    original = config.read_bytes()
    derived, maps = derive_config(original)
    libraries = []
    for packaged, (source, digest) in LIBRARIES.items():
        actual = (library / source).resolve(strict=True)
        if actual.parent != library.resolve() or sha256(actual) != digest:
            raise RuntimeError("[MAC_MONO_LIBRARY_HASH_MISMATCH] 辅助库非锁定原件。")
        arches = probe(["lipo", "-archs", str(actual)]).strip().split()
        if arches != ["arm64"]:
            raise RuntimeError("[MAC_MONO_LIBRARY_ARCH_MISMATCH] 辅助库必须单ARM64。")
        loads = [line.strip().split(" (", 1)[0] for line in probe(["otool", "-arch", "arm64", "-L", str(actual)]).splitlines()[1:] if line.strip()]
        if not loads or any(not v.startswith(("/usr/lib/", "/System/Library/")) for v in loads[1:]):
            raise RuntimeError("[MAC_MONO_LIBRARY_DEPENDENCY_UNSUPPORTED] 辅助库含外部依赖。")
        minimum = check_minimum(probe(["otool", "-arch", "arm64", "-l", str(actual)]))
        target = stage / packaged
        with target.open("xb") as output, actual.open("rb") as stream:
            shutil.copyfileobj(stream, output)
        target.chmod(0o755)
        if sha256(target) != digest:
            raise RuntimeError("[MAC_MONO_SIDECAR_COPY_MISMATCH] 辅助库复制变化。")
        libraries.append({"registered_name": packaged, "source_name": source, "source_sha256": digest,
                          "packaged_sha256": digest, "bytes": actual.stat().st_size, "architectures": arches,
                          "minimum_macos": minimum[0], "install_name": loads[0], "dependencies": loads[1:],
                          "symlink_chain": [], "file_description": probe(["file", "-b", str(actual)]).strip()})
    with (stage / "mono-config.bundle.xml").open("xb") as stream:
        stream.write(derived)
    record = {"schema": 1, "platform": "macos", "official_sdk_selection": PROFILE,
              "runtime_input_profile": PROFILE, "bottle_sha256": BOTTLE_SHA, "sdk_version": "6.14.1",
              "minimum_macos": "15.0", "sdk_input_sha256": bindings,
              "original_config_sha256": hashlib.sha256(original).hexdigest(), "bundle_config_sha256": hashlib.sha256(derived).hexdigest(),
              "original_dllmaps": maps, "bundle_dllmap_target": "@executable_path/libmono-native-compat.dylib",
              "bundle_posix_target": "@executable_path/libMonoPosixHelper.dylib", "runtime_options": [], "embedded_environment": {},
              "bundling_mode": "custom-static", "child_environment": {"MONO_CONFIG": "/dev/null", "removed": ["MONO_ENV_OPTIONS", "MONO_BUNDLED_OPTIONS"]},
              "static_runtime": {"source_name": static.name, "source_sha256": STATIC_SHA, "bytes": static.stat().st_size,
                                 "architectures": ["arm64"], "minimum_macos": static_minimum},
              "libraries": libraries, "license": "LICENSE-MONO-RUNTIME.txt"}
    with (stage / "mono-native-source.json").open("x", encoding="utf-8") as stream:
        stream.write(json.dumps(record, ensure_ascii=False, indent=2) + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("config", "library-root", "stage", "framework-root", "include-root", "bottle-archive"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--link-include-root", type=Path, required=True)
    args = parser.parse_args()
    check_link_headers(args.link_include_root, args.include_root)
    prepare(args.config, args.library_root, args.stage, args.framework_root, args.include_root, args.bottle_archive,
            lambda command: subprocess.run(command, check=True, capture_output=True, text=True).stdout)


if __name__ == "__main__":
    main()
