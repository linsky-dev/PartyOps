#!/usr/bin/env python3
"""从原排版源码和锁定的 WPS RPC SDK 定义生成 Linux WPS 虚表槽位表。

这些接口由类型库反编译得到，``_VtblGapN_M`` 表示省略了 M 个连续方法。
.NET 反射会主动隐藏这些占位方法，因此跨平台宿主必须在构建阶段生成槽位。
可选的 WPS SDK C 头文件校验会逐项比对真实 ``*Vtbl``；SIP 绑定允许省略成员，
不能用它推导虚表顺序。运行时不得猜测 DISPIDs 与虚表槽位之间的关系。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path

INTERFACE_RE = re.compile(r"^public interface (?P<name>[A-Za-z_]\w*)\b")
GAP_RE = re.compile(r"^void _VtblGap\d+_(?P<count>\d+)\(\);$")
METHOD_RE = re.compile(
    r"^(?:new\s+)?[A-Za-z_][\w.<>\[\],?]*\s+(?P<name>[A-Za-z_]\w*)\s*\("
)
PROPERTY_RE = re.compile(
    r"^(?:new\s+)?[A-Za-z_][\w.<>\[\],?]*\s+(?P<name>[A-Za-z_]\w*)$"
)
INDEXER_RE = re.compile(r"^(?:new\s+)?[A-Za-z_][\w.<>\[\],?]*\s+this\[")
SDK_VTABLE_RE = re.compile(
    r"typedef struct (?P<name>[A-Za-z_]\w*)Vtbl\s*\{(?P<body>.*?)\}\s*(?P=name)Vtbl\s*;",
    re.DOTALL,
)
SDK_METHOD_RE = re.compile(
    r"\(\s*STDMETHODCALLTYPE\s*\*(?P<name>[A-Za-z_]\w*)\s*\)"
)
WPS_MISSING_ALLOWED = {
    "Microsoft.Office.Interop.Word.ApplicationEvents4|DocumentBeforeClose",
}


def _source_digest(files: list[Path], root: Path) -> str:
    digest = hashlib.sha256()
    for path in files:
        relative = path.relative_to(root).as_posix().encode("utf-8")
        content = path.read_text(encoding="utf-8").replace("\r\n", "\n").replace("\r", "\n")
        digest.update(relative)
        digest.update(b"\0")
        digest.update(content.encode("utf-8"))
        digest.update(b"\n")
    return digest.hexdigest()


def _wps_sdk_slots(header: Path) -> dict[str, int]:
    if not header.is_file():
        raise RuntimeError(f"[WPS_SDK_HEADER_MISSING] {header}")
    content = header.read_text(encoding="utf-8", errors="strict")
    slots: dict[str, int] = {}
    for block in SDK_VTABLE_RE.finditer(content):
        interface_name = block.group("name")
        for slot, method in enumerate(SDK_METHOD_RE.finditer(block.group("body"))):
            key = f"{interface_name}|{method.group('name')}"
            if key in slots:
                raise RuntimeError(f"[WPS_SDK_VTABLE_DUPLICATE] {key}")
            slots[key] = slot
    if len(slots) < 10000:
        raise RuntimeError(f"[WPS_SDK_VTABLE_INCOMPLETE] 仅生成 {len(slots)} 个方法槽位")
    return slots


def _wps_key(source_key: str) -> tuple[str, ...]:
    interface_name, method_name = source_key.rsplit(".", 1)[-1].split("|", 1)
    if method_name.startswith("set_"):
        method_name = "put_" + method_name[4:]
    elif method_name == "get_Item":
        method_name = "Item"
    elif method_name == "GetEnumerator":
        method_name = "get__NewEnum"
    names = [interface_name]
    if interface_name.startswith("_"):
        names.append(interface_name[1:])
    return tuple(f"{name}|{method_name}" for name in names)


def generate(source: Path, wps_sdk_header: Path | None = None) -> dict[str, object]:
    interface_root = (
        source
        / "src"
        / "PartyOps.DocumentFormatter.AddIn"
        / "Microsoft"
        / "Office"
        / "Interop"
        / "Word"
    )
    if not interface_root.is_dir():
        raise RuntimeError(f"[WPS_VTABLE_SOURCE_MISSING] {interface_root}")
    files = sorted(interface_root.glob("*.cs"), key=lambda item: item.name.casefold())
    slots: dict[str, int] = {}
    interfaces: set[str] = set()

    for path in files:
        lines = path.read_text(encoding="utf-8").replace("\r\n", "\n").split("\n")
        interface_name: str | None = None
        slot = 7  # IUnknown(3) + IDispatch(4)
        property_name: str | None = None
        property_depth = 0

        for raw in lines:
            line = raw.strip()
            if interface_name is None:
                match = INTERFACE_RE.match(line)
                if match:
                    interface_name = match.group("name")
                    interfaces.add(interface_name)
                continue

            if property_name is not None:
                property_depth += line.count("{") - line.count("}")
                if line == "get;":
                    key = f"Microsoft.Office.Interop.Word.{interface_name}|get_{property_name}"
                    if key in slots:
                        raise RuntimeError(f"[WPS_VTABLE_DUPLICATE] {key}")
                    slots[key] = slot
                    slot += 1
                elif line == "set;":
                    key = f"Microsoft.Office.Interop.Word.{interface_name}|set_{property_name}"
                    if key in slots:
                        raise RuntimeError(f"[WPS_VTABLE_DUPLICATE] {key}")
                    slots[key] = slot
                    slot += 1
                if property_depth <= 0:
                    property_name = None
                continue

            gap = GAP_RE.match(line)
            if gap:
                slot += int(gap.group("count"))
                continue

            method = METHOD_RE.match(line)
            if method and not line.startswith("["):
                name = method.group("name")
                if not name.startswith("_VtblGap"):
                    key = f"Microsoft.Office.Interop.Word.{interface_name}|{name}"
                    if key in slots:
                        raise RuntimeError(f"[WPS_VTABLE_DUPLICATE] {key}")
                    slots[key] = slot
                    slot += 1
                continue

            indexer = INDEXER_RE.match(line)
            prop = PROPERTY_RE.match(line)
            if indexer or prop:
                property_name = "Item" if indexer else prop.group("name")
                property_depth = 0

    if len(slots) < 400:
        raise RuntimeError(f"[WPS_VTABLE_MAP_INCOMPLETE] 仅生成 {len(slots)} 个方法槽位")
    payload: dict[str, object] = {
        "schema": 1,
        "source": "PartyOps.DocumentFormatter.Source Word interop interfaces",
        "source_sha256": _source_digest(files, interface_root),
        "interface_count": len(interfaces),
        "method_count": len(slots),
    }
    if wps_sdk_header is not None:
        wps_slots = _wps_sdk_slots(wps_sdk_header)
        matched = 0
        overridden = 0
        missing: list[str] = []
        for source_key, source_slot in tuple(slots.items()):
            wps_slot = next(
                (wps_slots[key] for key in _wps_key(source_key) if key in wps_slots),
                None,
            )
            if wps_slot is None:
                missing.append(source_key)
                continue
            matched += 1
            if wps_slot != source_slot:
                overridden += 1
            slots[source_key] = wps_slot
        unexpected = sorted(set(missing) - WPS_MISSING_ALLOWED)
        if unexpected or matched != 528 or overridden != 0:
            raise RuntimeError(
                "[WPS_SDK_VTABLE_COVERAGE_INVALID] "
                f"matched={matched}, overridden={overridden}, unexpected={unexpected}"
            )
        payload.update(
            {
                "source": "PartyOps source contract verified against WPS RPC SDK C ABI",
                "wps_sdk_header_sha256": _source_digest(
                    [wps_sdk_header], wps_sdk_header.parent
                ),
                "wps_sdk_method_count": len(wps_slots),
                "wps_matched_method_count": matched,
                "wps_mismatched_method_count": overridden,
                "wps_unmapped_methods": sorted(missing),
            }
        )
    payload["slots"] = {key: slots[key] for key in sorted(slots)}
    return payload


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--wps-sdk-header", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        payload = generate(
            args.source.resolve(),
            args.wps_sdk_header.resolve() if args.wps_sdk_header else None,
        )
        args.output.parent.mkdir(parents=True, exist_ok=True)
        # 兼容 Win7 构建链锁定的 Python 3.8；Path.write_text 在该版本
        # 不接受 newline 参数。
        with args.output.open("w", encoding="utf-8", newline="\n") as stream:
            stream.write(
                json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True)
                + "\n"
            )
    except (OSError, UnicodeError, RuntimeError) as exc:
        print(str(exc))
        return 2
    print(
        "[WPS_VTABLE_MAP_OK] "
        f"interfaces={payload['interface_count']} methods={payload['method_count']} "
        f"matched={payload.get('wps_matched_method_count', 0)} "
        f"mismatched={payload.get('wps_mismatched_method_count', 0)} "
        f"sha256={payload['source_sha256']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
