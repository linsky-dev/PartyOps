#!/usr/bin/env python3
"""用用户确认的正确 DOCX 对原源码宿主执行语义金标准比对。"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import os
import platform
import shutil
import subprocess
import sys
import tempfile
import uuid
import zipfile
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any

import fitz
from lxml import etree

NS = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}
W = f"{{{NS['w']}}}"
BEIJING = timezone(timedelta(hours=8))
DEFAULT_FIXTURE_ROOT = (
    Path(__file__).resolve().parents[1]
    / "backend"
    / "tests"
    / "fixtures"
    / "document-formatter-source"
)
DEFAULT_SOURCE = DEFAULT_FIXTURE_ROOT / "input-manual-break.docx"
DEFAULT_GOLDEN = DEFAULT_FIXTURE_ROOT / "expected-source-formatted.docx"


def _platform_name() -> str:
    if os.name == "nt":
        return "windows"
    return "macos" if sys.platform == "darwin" else "linux"


def _architecture_name(platform_name: str) -> str:
    machine = platform.machine().lower()
    if platform_name == "windows":
        return "x86" if machine in {"x86", "i386", "i686"} or sys.maxsize <= 2**32 else "x64"
    if platform_name == "macos":
        return "arm64" if machine in {"arm64", "aarch64"} else "x86_64"
    return "arm64" if machine in {"arm64", "aarch64"} else "amd64"


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _part(package: zipfile.ZipFile, name: str) -> etree._Element:
    parser = etree.XMLParser(resolve_entities=False, no_network=True, huge_tree=False)
    return etree.fromstring(package.read(name), parser=parser)


def _attr(element: etree._Element | None, name: str) -> str:
    return "" if element is None else str(element.get(W + name, ""))


def _property_signature(parent: etree._Element | None) -> dict[str, Any]:
    if parent is None:
        return {}
    result: dict[str, Any] = {}
    for child in parent:
        name = etree.QName(child).localname
        if name in {"rPrChange", "pPrChange"}:
            continue
        attributes = {
            etree.QName(key).localname: value
            for key, value in sorted(child.attrib.items())
            if etree.QName(key).localname not in {"rsid", "rsidR", "rsidRPr", "rsidRDefault"}
        }
        value: Any = attributes
        if len(child):
            value = {"attributes": attributes, "children": _property_signature(child)}
        result[name] = value
    return result


def _document_signature(path: Path) -> dict[str, Any]:
    with zipfile.ZipFile(path) as package:
        document = _part(package, "word/document.xml")
        styles = _part(package, "word/styles.xml")
        style_map: dict[str, etree._Element] = {}
        style_names: dict[str, str] = {}
        for style in styles.xpath("./w:style", namespaces=NS):
            style_id = _attr(style, "styleId")
            style_map[style_id] = style
            style_names[style_id] = _attr(style.find("w:name", NS), "val")

        paragraphs = []
        used_styles: set[str] = set()
        for paragraph in document.xpath("./w:body/w:p", namespaces=NS):
            style_element = paragraph.find("w:pPr/w:pStyle", NS)
            style_id = _attr(style_element, "val")
            if style_id:
                used_styles.add(style_id)
            runs = []
            for run in paragraph.xpath("./w:r", namespaces=NS):
                runs.append(
                    {
                        "text": "".join(run.xpath(".//w:t/text()", namespaces=NS)),
                        "tabs": len(run.xpath(".//w:tab", namespaces=NS)),
                        "breaks": [
                            _attr(item, "type") or "textWrapping"
                            for item in run.xpath(".//w:br", namespaces=NS)
                        ],
                        "properties": _property_signature(run.find("w:rPr", NS)),
                    }
                )
            paragraphs.append(
                {
                    "text": "".join(paragraph.xpath(".//w:t/text()", namespaces=NS)),
                    "style_id": style_id,
                    "style_name": style_names.get(style_id, ""),
                    "properties": _property_signature(paragraph.find("w:pPr", NS)),
                    "runs": runs,
                }
            )

        selected_styles = {}
        for style_id in sorted(used_styles):
            style = style_map[style_id]
            selected_styles[style_names.get(style_id, style_id)] = {
                "type": _attr(style, "type"),
                "paragraph": _property_signature(style.find("w:pPr", NS)),
                "run": _property_signature(style.find("w:rPr", NS)),
            }

        section = document.find(".//w:sectPr", NS)
        footers = []
        for name in sorted(item.filename for item in package.infolist() if item.filename.startswith("word/footer") and item.filename.endswith(".xml")):
            footer = _part(package, name)
            footers.append(
                {
                    "text": "".join(footer.xpath(".//w:t/text()", namespaces=NS)),
                    "fields": [value.strip() for value in footer.xpath(".//w:instrText/text()", namespaces=NS)],
                    "paragraphs": [
                        {
                            "properties": _property_signature(item.find("w:pPr", NS)),
                            "runs": [
                                {
                                    "text": "".join(run.xpath(".//w:t/text()", namespaces=NS)),
                                    "properties": _property_signature(run.find("w:rPr", NS)),
                                }
                                for run in item.xpath("./w:r", namespaces=NS)
                            ],
                        }
                        for item in footer.xpath(".//w:p", namespaces=NS)
                    ],
                }
            )
        return {
            "paragraphs": paragraphs,
            "styles": selected_styles,
            "section": _property_signature(section),
            "footers": footers,
        }


def _require_contract(signature: dict[str, Any]) -> None:
    paragraphs = signature["paragraphs"]
    if len(paragraphs) != 8:
        raise AssertionError(f"应有 8 个正文段落，实际 {len(paragraphs)} 个")
    if paragraphs[1]["text"] != "同志们：" or paragraphs[1]["style_name"] != "OfficialDoc.Salutation":
        raise AssertionError("称谓没有被独立识别为 OfficialDoc.Salutation")
    if paragraphs[2]["style_name"] != "OfficialDoc.Body":
        raise AssertionError("称谓后的正文没有被识别为 OfficialDoc.Body")
    if any(run["breaks"] for paragraph in paragraphs for run in paragraph["runs"]):
        raise AssertionError("手动换行未转换成真实段落")

    salutation = signature["styles"].get("OfficialDoc.Salutation", {})
    body = signature["styles"].get("OfficialDoc.Body", {})
    title = signature["styles"].get("OfficialDoc.MainTitle", {})
    salutation_indent = salutation.get("paragraph", {}).get("ind", {})
    if salutation_indent.get("firstLine") or salutation_indent.get("firstLineChars"):
        raise AssertionError("称谓样式不应带首行缩进")
    body_indent = body.get("paragraph", {}).get("ind", {})
    if body_indent.get("firstLine") != "420" or body_indent.get("firstLineChars") != "200":
        raise AssertionError(f"正文首行缩进应为 2 字符（420/200），实际 {body_indent}")
    if body.get("paragraph", {}).get("spacing", {}).get("line") != "560":
        raise AssertionError("正文固定行距应为 28 磅（560 twips）")
    if title.get("paragraph", {}).get("spacing", {}).get("line") != "640" or title.get("paragraph", {}).get("spacing", {}).get("after") != "560":
        raise AssertionError("主标题行距或段后距不符合源码默认值")

    section = signature["section"]
    expected_margins = {
        "top": "2098",
        "right": "1474",
        "bottom": "1984",
        "left": "1588",
        "header": "851",
        "footer": "1418",
        "gutter": "0",
    }
    if section.get("pgMar") != expected_margins:
        raise AssertionError(f"页边距不符合源码金标准：{section.get('pgMar')}")
    if section.get("docGrid") != {"linePitch": "312", "charSpace": "0"}:
        raise AssertionError(f"文档网格不符合源码金标准：{section.get('docGrid')}")
    if not signature["footers"] or "PAGE" not in signature["footers"][0]["fields"]:
        raise AssertionError("页脚缺少 PAGE 域")
    if signature["footers"][0]["text"] != "— 1 —":
        raise AssertionError("页码左右翼字符或空格不符合源码金标准")


def _semantic_equivalence_signature(signature: dict[str, Any]) -> dict[str, Any]:
    """消除 WPS 各平台 OOXML 序列化噪声，保留所有可见排版属性。"""

    result = copy.deepcopy(signature)

    def normalize(value: Any) -> None:
        if isinstance(value, dict):
            # Windows/国产 Linux WPS 对相同字符会分别写 w:lang@eastAsia
            # 或 w:lang@val；字体提示也可能写 hint=eastAsia/default。
            # 它们不是字体名、字号、间距、缩进或分页属性，逐页像素门禁会
            # 继续验证实际渲染结果，因此只在语义签名中移除这些序列化提示。
            value.pop("lang", None)
            fonts = value.get("rFonts")
            if isinstance(fonts, dict):
                fonts.pop("hint", None)
                # WPS Linux 会把页码样式已经继承的“宋体”冗余写成
                # ascii=SimSun；保留其他显式字体名，防止真正的字体偏差漏检。
                if fonts == {"ascii": "SimSun"} or not fonts:
                    value.pop("rFonts", None)
            for child in list(value.values()):
                normalize(child)
        elif isinstance(value, list):
            for child in value:
                normalize(child)

    normalize(result)
    for footer in result["footers"]:
        if not footer["text"] and not footer["fields"]:
            # WPS 会为未使用的空白首页页脚分配不同的内部样式编号；该页脚
            # 没有内容或域，不能影响版式，忽略其空段落内部编号。
            footer["paragraphs"] = []
    return result


def _render_page_hashes(source: Path, office: Path, work: Path) -> list[dict[str, Any]]:
    """用同一随包转换器渲染，锁定分页、缩进和可见像素。"""

    work.mkdir(parents=True, exist_ok=True)
    profile = tempfile.TemporaryDirectory(prefix="partyops-parity-office-")
    try:
        result = subprocess.run(
            [
                str(office),
                "--headless",
                f"-env:UserInstallation={Path(profile.name).resolve().as_uri()}",
                "--convert-to",
                "pdf",
                "--outdir",
                str(work),
                str(source),
            ],
            check=False,
            capture_output=True,
            timeout=180,
        )
    finally:
        # OpenCL 缓存文件名很长；使用 Windows 扩展路径避免普通路径超过
        # MAX_PATH 时被误判为不存在，进而导致父目录无法清理。
        if os.name == "nt":
            shutil.rmtree("\\\\?\\" + os.path.abspath(profile.name))
        profile.cleanup()
    if result.returncode != 0:
        raise AssertionError(f"视觉渲染转换失败，退出码 {result.returncode}")
    pdf_path = work / f"{source.stem}.pdf"
    if not pdf_path.is_file():
        raise AssertionError("视觉渲染没有生成 PDF")
    pages: list[dict[str, Any]] = []
    with fitz.open(pdf_path) as document:
        for index, page in enumerate(document):
            pixmap = page.get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
            pages.append(
                {
                    "page": index + 1,
                    "width": pixmap.width,
                    "height": pixmap.height,
                    "pixel_sha256": hashlib.sha256(pixmap.samples).hexdigest(),
                }
            )
    return pages


def _visual_page_comparison(
    golden_pdf: Path, output_pdf: Path, *, allow_rasterization_noise: bool
) -> dict[str, Any]:
    """比较渲染后的页面。

    Windows 上使用同一 Word 渲染器，保留逐字节像素门禁。Linux/macOS 的
    WPS 与随包 LibreOffice 可能对同一字形使用不同抗锯齿采样；此时页面尺寸、
    页数必须完全一致，并以固定阈值确认差异只来自亚像素渲染噪声，而不是
    换行、缩进或分页。OOXML 语义签名仍在调用方先行严格比对。
    """

    with fitz.open(golden_pdf) as golden, fitz.open(output_pdf) as output:
        if len(golden) != len(output):
            return {"passed": False, "reason": "page-count", "pages": []}
        pages: list[dict[str, Any]] = []
        for index, (golden_page, output_page) in enumerate(zip(golden, output), start=1):
            golden_pix = golden_page.get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
            output_pix = output_page.get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
            if (golden_pix.width, golden_pix.height) != (output_pix.width, output_pix.height):
                return {"passed": False, "reason": "page-size", "pages": pages}
            golden_bytes = golden_pix.samples
            output_bytes = output_pix.samples
            count = min(len(golden_bytes), len(output_bytes))
            total_delta = sum(abs(golden_bytes[i] - output_bytes[i]) for i in range(count))
            same_ratio = sum(golden_bytes[i] == output_bytes[i] for i in range(count)) / count
            mean_abs_delta = total_delta / count
            raw_equal = golden_bytes == output_bytes
            passed = raw_equal or (
                allow_rasterization_noise
                and mean_abs_delta <= 0.10
                and same_ratio >= 0.999
            )
            pages.append(
                {
                    "page": index,
                    "width": golden_pix.width,
                    "height": golden_pix.height,
                    "raw_equal": raw_equal,
                    "mean_abs_channel_delta": round(mean_abs_delta, 6),
                    "same_channel_ratio": round(same_ratio, 9),
                    "passed": passed,
                }
            )
        return {
            "passed": all(page["passed"] for page in pages),
            "mode": "raw-pixel" if not allow_rasterization_noise else "raw-pixel-or-antialias-noise",
            "pages": pages,
        }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--golden", type=Path, default=DEFAULT_GOLDEN)
    parser.add_argument("--host", type=Path, required=True)
    parser.add_argument(
        "--host-identity",
        type=Path,
        help="仅在跨环境代理测试时指定实际受测宿主；正式目标机测试应省略。",
    )
    parser.add_argument("--office-bin", type=Path, required=True)
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--platform", choices=("windows", "linux", "macos"))
    parser.add_argument("--architecture")
    args = parser.parse_args()

    root = args.root.resolve()
    source = args.source.resolve()
    golden = args.golden.resolve()
    host = args.host.resolve()
    host_identity = (args.host_identity or args.host).resolve()
    office = args.office_bin.resolve()
    workspace_root = args.workspace.resolve()
    evidence = args.evidence.resolve()
    platform_name = args.platform or _platform_name()
    architecture = args.architecture or _architecture_name(platform_name)
    for label, path in (
        ("原始样本", source),
        ("正确金标准", golden),
        ("源码宿主", host),
        ("源码宿主身份文件", host_identity),
        ("随包视觉渲染器", office),
    ):
        if not path.is_file():
            print(f"[FORMAT_PARITY_INPUT_MISSING] {label}不存在：{path}", file=sys.stderr)
            return 2
    workspace_root.mkdir(parents=True, exist_ok=True)
    evidence.parent.mkdir(parents=True, exist_ok=True)

    sys.path.insert(0, str(root / "backend"))
    os.environ.pop("PARTYOPS_FORMATTER_TEST_LOCAL_ENGINE", None)
    os.environ["PARTYOPS_DOCUMENT_FORMATTER_HOST"] = str(host)
    from app.official_format_features import execute_feature

    source_hash_before = _sha256(source)
    run_workspace = workspace_root / f"parity-{uuid.uuid4().hex}"
    events: list[dict[str, Any]] = []
    result = execute_feature(
        "format",
        source,
        run_workspace,
        {"scope": "full", "compatibility_mode": "wps"},
        progress=lambda percent, message: events.append(
            {"percent": percent, "message": message}
        ),
    )
    source_hash_after = _sha256(source)
    if source_hash_after != source_hash_before:
        raise AssertionError("源码宿主修改了用户原始文件")
    if len(result.outputs) != 1 or result.outputs[0].path.suffix.lower() != ".docx":
        raise AssertionError("一键排版必须只返回一个 DOCX 主结果")

    output = result.outputs[0].path
    output_signature = _document_signature(output)
    golden_signature = _document_signature(golden)
    _require_contract(output_signature)
    output_semantic_signature = _semantic_equivalence_signature(output_signature)
    golden_semantic_signature = _semantic_equivalence_signature(golden_signature)
    if output_semantic_signature != golden_semantic_signature:
        raise AssertionError("源码宿主输出与用户确认的正确 DOCX 语义签名不一致")
    golden_pages = _render_page_hashes(golden, office, run_workspace / "render-golden")
    output_pages = _render_page_hashes(output, office, run_workspace / "render-output")
    golden_pdf = next((run_workspace / "render-golden").glob("*.pdf"))
    output_pdf = next((run_workspace / "render-output").glob("*.pdf"))
    visual_comparison = _visual_page_comparison(
        golden_pdf,
        output_pdf,
        allow_rasterization_noise=platform_name in {"linux", "macos"},
    )
    if not visual_comparison["passed"]:
        raise AssertionError("源码宿主输出与正确 DOCX 的分页或可见像素不一致")

    record = {
        "schema": 2,
        "status": "passed",
        "verified_at": datetime.now(BEIJING).isoformat(timespec="seconds"),
        "timezone": "Asia/Shanghai",
        "platform": platform_name,
        "architecture": architecture,
        "provider": "wps",
        "host_sha256": _sha256(host_identity),
        "host": str(host),
        "host_identity": str(host_identity),
        "source": str(source),
        "source_sha256": source_hash_before,
        "golden": str(golden),
        "golden_sha256": _sha256(golden),
        "output": str(output),
        "output_sha256": _sha256(output),
        "semantic_signature_sha256": hashlib.sha256(
            json.dumps(output_semantic_signature, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
        ).hexdigest(),
        "paragraph_count": len(output_signature["paragraphs"]),
        "rendered_pages": output_pages,
        "visual_comparison": visual_comparison,
        "events": events,
    }
    # Python 3.8 的 Path.write_text 尚不支持 newline 参数；Windows 7 构建链
    # 固定使用 Python 3.8，因此通过 Path.open 显式保持证据文件为 LF。
    with evidence.open("w", encoding="utf-8", newline="\n") as stream:
        stream.write(json.dumps(record, ensure_ascii=False, indent=2) + "\n")
    print(f"[FORMAT_PARITY_OK] {evidence}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
