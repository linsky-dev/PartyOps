"""跨平台 WPS 静默桥接探针的离线契约测试。"""

from __future__ import annotations

import importlib.util
import json
import os
import secrets
import urllib.request
import zipfile
from pathlib import Path
from xml.etree import ElementTree

import pytest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "probe-wps-native-bridge.py"
SPEC = importlib.util.spec_from_file_location("partyops_wps_native_probe", SCRIPT)
assert SPEC and SPEC.loader
probe = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(probe)


def _write_docx(path: Path, *, style_indent: bool = False) -> None:
    namespace = probe.WORD_NS
    document = ElementTree.Element(f"{{{namespace}}}document")
    body = ElementTree.SubElement(document, f"{{{namespace}}}body")
    paragraph = ElementTree.SubElement(body, f"{{{namespace}}}p")
    ppr = ElementTree.SubElement(paragraph, f"{{{namespace}}}pPr")
    style = ElementTree.SubElement(ppr, f"{{{namespace}}}pStyle")
    style.set(f"{{{namespace}}}val", "Body")
    if not style_indent:
        indent = ElementTree.SubElement(ppr, f"{{{namespace}}}ind")
        indent.set(f"{{{namespace}}}firstLineChars", "200")
    run = ElementTree.SubElement(paragraph, f"{{{namespace}}}r")
    ElementTree.SubElement(run, f"{{{namespace}}}t").text = "正文"

    styles = ElementTree.Element(f"{{{namespace}}}styles")
    body_style = ElementTree.SubElement(styles, f"{{{namespace}}}style")
    body_style.set(f"{{{namespace}}}styleId", "Body")
    style_ppr = ElementTree.SubElement(body_style, f"{{{namespace}}}pPr")
    if style_indent:
        indent = ElementTree.SubElement(style_ppr, f"{{{namespace}}}ind")
        indent.set(f"{{{namespace}}}firstLineChars", "200")
    with zipfile.ZipFile(path, "w") as package:
        package.writestr("word/document.xml", ElementTree.tostring(document))
        package.writestr("word/styles.xml", ElementTree.tostring(styles))


def test_build_invocation_is_silent_and_token_bound(tmp_path: Path) -> None:
    token = secrets.token_urlsafe(48)
    command_id, body = probe.build_invocation(
        token=token,
        output_path=tmp_path / "输出.docx",
        paragraph_index=3,
        jsplugins_url="http://127.0.0.1:31337/jsplugins.xml",
    )
    payload = json.loads(body)
    assert len(command_id) == 32
    assert payload["id"] == command_id
    assert payload["app"] == "wps"
    assert payload["mode"] is True
    assert payload["timeout"] == 60_000
    assert payload["data"].startswith("ksowebstartupwps://")
    assert "serverId" not in payload
    assert "startparam" not in payload


def test_plugin_server_binds_token_to_single_output_scope(tmp_path: Path) -> None:
    token = secrets.token_urlsafe(48)
    output = tmp_path / "输出.docx"
    plugin = ROOT / "packaging" / "wps-formatter-adapter" / "plugin"
    server = probe.start_plugin_server(plugin, token, output, 3)
    try:
        base = f"http://127.0.0.1:{server.server_port}"
        with urllib.request.urlopen(base + "/config.js", timeout=2) as response:
            config = response.read().decode("utf-8")
        with urllib.request.urlopen(base + "/jsplugins.xml", timeout=2) as response:
            manifest = response.read().decode("utf-8")
    finally:
        requests = list(getattr(server, "partyops_requests", []))
        server.shutdown()
        server.server_close()
    assert token in config
    assert str(output.resolve()).replace("\\", "\\\\") in config
    assert '"paragraph_index":3' in config
    assert probe.PLUGIN_NAME in manifest
    assert base in manifest
    assert requests == ["/config.js", "/jsplugins.xml"]


def test_parse_probe_response_requires_real_silent_wps() -> None:
    token = secrets.token_urlsafe(48)
    valid = {
        "schema": 1,
        "passed": True,
        "provider": "wps",
        "token": token,
        "application": "WPS Office Linux",
        "character_unit_first_line_indent": 2,
        "visible": False,
    }
    assert probe.parse_probe_response(json.dumps(valid), token)["application"] == "WPS Office Linux"
    wrapped = json.dumps({"response": json.dumps(valid)})
    assert probe.parse_probe_response(wrapped, token)["provider"] == "wps"
    valid["visible"] = True
    with pytest.raises(RuntimeError, match="WPS_PROBE_CONTRACT_FAILED"):
        probe.parse_probe_response(json.dumps(valid), token)


@pytest.mark.parametrize("style_indent", [False, True])
def test_verify_saved_character_indent_accepts_direct_or_style(
    tmp_path: Path, style_indent: bool
) -> None:
    output = tmp_path / "output.docx"
    _write_docx(output, style_indent=style_indent)
    probe.verify_saved_character_indent(output, 1)


def test_verify_saved_character_indent_rejects_point_approximation(tmp_path: Path) -> None:
    output = tmp_path / "output.docx"
    _write_docx(output)
    with zipfile.ZipFile(output, "r") as source:
        entries = {name: source.read(name) for name in source.namelist()}
    entries["word/document.xml"] = entries["word/document.xml"].replace(
        b"firstLineChars=\"200\"", b"firstLine=\"420\""
    )
    with zipfile.ZipFile(output, "w") as target:
        for name, value in entries.items():
            target.writestr(name, value)
    with pytest.raises(RuntimeError, match="WPS_PROBE_INDENT_NOT_PERSISTED"):
        probe.verify_saved_character_indent(output, 1)


def test_plugin_contract_and_notice_are_present() -> None:
    plugin = ROOT / "packaging" / "wps-formatter-adapter" / "plugin"
    main = (plugin / "main.js").read_text(encoding="utf-8")
    license_text = (
        ROOT / "packaging" / "wps-formatter-adapter" / "LICENSE-WPS-SDK.txt"
    ).read_text(encoding="utf-8")
    assert "CharacterUnitFirstLineIndent = 2" in main
    assert "application.Visible = false" in main
    assert "PARTYOPS_WPS_BRIDGE_SCOPE_INVALID" in main
    assert "showToFront" not in main
    assert "Redistribution and use in source and binary forms" in license_text


def test_windows_relay_uses_registered_uri_handler(monkeypatch: pytest.MonkeyPatch) -> None:
    calls: list[str] = []
    monkeypatch.setattr(os, "startfile", calls.append, raising=False)
    probe._start_relay("windows")
    assert calls == ["ksoWPSCloudSvr://start=RelayHttpServer"]
