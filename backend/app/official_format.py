"""PartyOps 公文排版的可移植 OOXML 兼容层。

Windows 一键排版的正式路径由 ``official_format_host`` 复用原源码和本机
WPS/Word；本模块负责跨平台结构校验、旧格式转换及尚未切换的兼容能力。
文档始终只在 PartyOps 本机回环服务与私有临时目录中处理。
"""

from __future__ import annotations

import copy
import json
import os
import re
import shutil
import subprocess
import sys
import time
import zipfile
from dataclasses import asdict, dataclass
from http.server import BaseHTTPRequestHandler
from pathlib import Path, PurePosixPath
from typing import Any, Iterable, cast

from lxml import etree

VERSION = "1.4.5-rc.6"
MAX_FILE_BYTES = 50 * 1024 * 1024
MAX_PACKAGE_BYTES = 512 * 1024 * 1024
MAX_ZIP_RATIO = 200
IDLE_TIMEOUT_SECONDS = 15 * 60
SUPPORTED_EXTENSIONS = {".docx", ".doc", ".wps", ".rtf", ".pdf"}
W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
PR = "http://schemas.openxmlformats.org/package/2006/relationships"
CT = "http://schemas.openxmlformats.org/package/2006/content-types"
NS = {"w": W, "r": R}
XML_PARSER = etree.XMLParser(resolve_entities=False, no_network=True, huge_tree=False)

# 与 PartyOps.DocumentFormatter.Source 的默认配置逐项一致。原工具默认关闭
# 文档网格，WPS 保存后会保留 312/0 的兼容节点，但不能强制 linesAndChars，
# 否则会改变换行和分页。页面值以用户确认的正确输出为金标准。
GRID_CHARACTER_SPACE = 0
GRID_LINE_PITCH = "312"
PAGE_FOOTER_DISTANCE = "1418"


class OfficialFormatError(RuntimeError):
    def __init__(self, code: str, title: str, detail: str) -> None:
        super().__init__(detail)
        self.code = code
        self.title = title
        self.detail = detail


@dataclass(frozen=True)
class FormatIssue:
    code: str
    severity: str
    title: str
    detail: str
    clause: str


@dataclass(frozen=True)
class FormatReport:
    compliant: bool
    paragraph_count: int
    table_count: int
    changed_count: int
    issues: tuple[FormatIssue, ...]

    def as_dict(self) -> dict[str, Any]:
        payload = asdict(self)
        payload["issues"] = [asdict(item) for item in self.issues]
        return payload


@dataclass
class LocalDocument:
    source: Path
    original_stem: str
    converted: bool
    output: Path | None = None
    report: FormatReport | None = None


def _qn(local: str) -> str:
    return f"{{{W}}}{local}"


def _safe_xml(payload: bytes, part: str) -> etree._Element:
    if len(payload) > 32 * 1024 * 1024:
        raise OfficialFormatError(
            "OOXML_PART_TOO_LARGE", "文档结构异常", f"{part} 超出安全解析上限。"
        )
    try:
        return etree.fromstring(payload, parser=XML_PARSER)
    except etree.XMLSyntaxError as exc:
        raise OfficialFormatError(
            "OOXML_XML_INVALID", "文档结构损坏", f"{part} 不是有效的 OOXML。"
        ) from exc


def _validated_members(package: zipfile.ZipFile) -> list[zipfile.ZipInfo]:
    total = 0
    members: list[zipfile.ZipInfo] = []
    for info in package.infolist():
        path = PurePosixPath(info.filename)
        if path.is_absolute() or ".." in path.parts or "\\" in info.filename:
            raise OfficialFormatError(
                "OOXML_PATH_UNSAFE", "文档包路径异常", "压缩包包含越界路径，已拒绝处理。"
            )
        total += info.file_size
        if total > MAX_PACKAGE_BYTES:
            raise OfficialFormatError(
                "OOXML_EXPANSION_LIMIT", "文档解压体积异常", "文档展开后超过 512 MiB 安全上限。"
            )
        if info.compress_size > 0 and info.file_size / info.compress_size > MAX_ZIP_RATIO:
            raise OfficialFormatError(
                "OOXML_COMPRESSION_RATIO_UNSAFE", "文档压缩比异常", "文档疑似压缩炸弹，已拒绝处理。"
            )
        members.append(info)
    required = {"[Content_Types].xml", "word/document.xml"}
    if not required.issubset({item.filename for item in members}):
        raise OfficialFormatError(
            "OOXML_REQUIRED_PART_MISSING", "DOCX 结构不完整", "缺少正文或内容类型定义。"
        )
    return members


def _read_core_parts(path: Path) -> tuple[etree._Element, etree._Element | None]:
    try:
        with zipfile.ZipFile(path) as package:
            _validated_members(package)
            document = _safe_xml(package.read("word/document.xml"), "word/document.xml")
            settings = (
                _safe_xml(package.read("word/settings.xml"), "word/settings.xml")
                if "word/settings.xml" in package.namelist()
                else None
            )
            return document, settings
    except zipfile.BadZipFile as exc:
        raise OfficialFormatError(
            "DOCX_PACKAGE_INVALID", "DOCX 文件损坏", "文件不是有效的 OOXML 压缩包。"
        ) from exc


def _paragraph_text(paragraph: etree._Element) -> str:
    # XPath 字面量只选择文本；cast 仅收窄静态类型，不改变 XPath 或内容。
    return "".join(cast("list[str]", paragraph.xpath(".//w:t/text()", namespaces=NS))).strip()


def _normalize_manual_line_breaks(document: etree._Element) -> int:
    """把正文中的 Shift+Enter 拆成真实段落，保持书签和后续节点顺序。

    这是原源码清理链的第一步；如果跳过，称谓与正文会被识别成同一段，
    后续任何字体或缩进设置都会在语义上出错。
    """

    changed = 0
    while True:
        target: tuple[etree._Element, etree._Element, etree._Element] | None = None
        for paragraph in cast("list[etree._Element]", document.xpath(".//w:body/w:p | .//w:body/w:sdt/w:sdtContent/w:p", namespaces=NS)):
            if paragraph.xpath(".//w:drawing | .//w:pict | .//w:object", namespaces=NS):
                continue
            breaks = cast("list[etree._Element]", paragraph.xpath("./w:r/w:br[not(@w:type) or @w:type='textWrapping']", namespaces=NS))
            if breaks:
                target = (paragraph, cast("etree._Element", breaks[0].getparent()), breaks[0])
                break
        if target is None:
            break
        paragraph, run, line_break = target
        parent = paragraph.getparent()
        if parent is None:
            break
        new_paragraph = etree.Element(_qn("p"))
        paragraph_properties = paragraph.find(_qn("pPr"))
        if paragraph_properties is not None:
            new_paragraph.append(copy.deepcopy(paragraph_properties))

        new_run = etree.Element(_qn("r"))
        run_properties = run.find(_qn("rPr"))
        if run_properties is not None:
            new_run.append(copy.deepcopy(run_properties))
        split_index = run.index(line_break)
        for child in list(run)[split_index + 1 :]:
            run.remove(child)
            new_run.append(child)
        run.remove(line_break)
        if len(new_run) > (1 if run_properties is not None else 0):
            new_paragraph.append(new_run)

        run_index = paragraph.index(run)
        for child in list(paragraph)[run_index + 1 :]:
            paragraph.remove(child)
            new_paragraph.append(child)
        if len(run) == (1 if run_properties is not None else 0):
            paragraph.remove(run)
        parent.insert(parent.index(paragraph) + 1, new_paragraph)
        changed += 1
    return changed


def _remove_empty_body_paragraphs(document: etree._Element) -> int:
    """删除原源码会清理的纯空白正文段，保留节、书签和浮动对象。"""

    changed = 0
    for paragraph in list(
        cast("list[etree._Element]", document.xpath(".//w:body/w:p | .//w:body/w:sdt/w:sdtContent/w:p", namespaces=NS))
    ):
        if _paragraph_text(paragraph):
            continue
        if paragraph.xpath(
            ".//w:sectPr | .//w:bookmarkStart | .//w:bookmarkEnd | .//w:drawing | .//w:pict | .//w:object",
            namespaces=NS,
        ):
            continue
        parent = paragraph.getparent()
        if parent is not None:
            parent.remove(paragraph)
            changed += 1
    return changed


def _paragraph_role(text: str, *, first_body: bool) -> str:
    compact = text.strip()
    if re.fullmatch(r"\d{6}", compact):
        return "copy_number"
    if re.match(r"^(?:绝密|机密|秘密)(?:\s*[★☆]\s*|\s+).+", compact):
        return "security"
    if compact in {"特急", "加急", "平急"}:
        return "urgency"
    if len(compact) <= 50 and re.search(r"(?:文件|命令|令|纪要)$", compact):
        return "issuing_authority"
    if re.fullmatch(r"(?:.+〔\d{4}〕\d+号|第\s*\d+\s*号)", compact):
        return "document_number"
    if compact.startswith("签发人：") or compact.startswith("签发人:"):
        return "signatory"
    if first_body and compact and len(compact) <= 80 and not re.search(r"[。；！？!?]$", compact):
        return "title"
    if re.match(r"^[一二三四五六七八九十百]+、", compact):
        return "heading1"
    if re.match(r"^（[一二三四五六七八九十百]+）", compact):
        return "heading2"
    if re.match(r"^\d{1,3}[.]", compact):
        return "heading3"
    if re.match(r"^（\d{1,3}）", compact):
        return "heading4"
    if re.fullmatch(r"附件\s*\d*", compact):
        return "attachment_heading"
    if re.match(r"^附件(?:\s*[:：]|\s*\d+[.．、])", compact):
        return "attachment"
    if compact.startswith("抄送：") or compact.startswith("抄送:"):
        return "copy_recipient"
    if re.search(r"\d{4}年\d{1,2}月\d{1,2}日印发$", compact):
        return "imprint"
    if re.match(r"^(?:出席|请假|列席)[：:]", compact):
        return "attendance"
    if compact.startswith("（") and compact.endswith("）") and len(compact) <= 120:
        return "note"
    if compact.endswith(("：", ":")) and len(compact) <= 120:
        return "addressee"
    if re.fullmatch(r"[〇○零一二三四五六七八九十百千\d]{4}年[〇○零一二三四五六七八九十百千\d]{1,3}月[〇○零一二三四五六七八九十百千\d]{1,3}日", compact):
        return "date"
    return "body"


def _classify_document_paragraphs(paragraphs: list[etree._Element]) -> list[tuple[etree._Element, str]]:
    """按上下文识别公文要素，避免把份号、密级或发文机关误当标题。

    规则只依据标准中可验证的外观信号。无法可靠判定的段落保持为正文，
    不猜测政治语义、机关层级或印章位置。
    """

    texts = [_paragraph_text(item) for item in paragraphs]
    kind = "general"
    for text in texts:
        compact = text.strip()
        if compact.endswith(("命令", "令")):
            kind = "order"
            break
        if compact.endswith("纪要"):
            kind = "minutes"
            break
        if compact.endswith("文件"):
            kind = "letter" if "函" in compact else "general"
            break

    classified: list[tuple[etree._Element, str]] = []
    title_seen = False
    # 公文标题经常由“机关名称/文件标题”两行或多行组成。旧实现只把
    # 第一行识别为 title，第二行会被当作正文并错误加首行缩进；这里依据
    # 连续短段落、居中属性和无句末标点建立有限标题块，不猜测正文语义。
    title_block_open = False
    for index, paragraph in enumerate(paragraphs):
        text = texts[index]
        if not text:
            continue
        allow_title = not title_seen and kind not in {"order", "minutes"}
        role = _paragraph_role(text, first_body=allow_title)
        properties = paragraph.find(_qn("pPr"))
        centered = bool(
            properties is not None
            and properties.find(_qn("jc")) is not None
            and cast("etree._Element", properties.find(_qn("jc"))).get(_qn("val")) == "center"
        )
        if (
            (not title_seen or title_block_open)
            and kind not in {"order", "minutes"}
            and len(text.strip()) <= 80
            and not re.search(r"[。；！？!?]$", text.strip())
            and (centered or role == "title")
        ):
            role = "title"
            title_block_open = True
        if role == "title":
            title_seen = True
        elif title_block_open:
            title_block_open = False
        classified.append((paragraph, role))

    # 成文日期上一条短机构名称通常是署名；只有同时满足位置、长度和机关后缀
    # 才识别，避免把普通正文静默右对齐。
    signature_suffixes = (
        "委员会", "人民政府", "党组", "党委", "党支部", "办公室", "工作部", "管理局", "中心", "机关",
    )
    for index, (paragraph, role) in enumerate(classified[:-1]):
        next_role = classified[index + 1][1]
        text = _paragraph_text(paragraph)
        if role == "body" and next_role == "date" and len(text) <= 50 and text.endswith(signature_suffixes):
            classified[index] = (paragraph, "signature")
    return classified


_CJK = r"\u3400-\u9fff"


def normalize_chinese_punctuation(text: str) -> tuple[str, int]:
    """只改 CJK 邻接的明确标点，保护 URL、邮箱、小数、缩写和条款编号。"""

    protected: dict[str, str] = {}

    def shelter(match: re.Match[str]) -> str:
        key = f"\ue000{len(protected)}\ue001"
        protected[key] = match.group(0)
        return key

    value = re.sub(
        r"(?:https?://|www\.)[^\s<>]+|[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}|\b\d+(?:\.\d+)+\b|\b[A-Z](?:\.[A-Z])+(?:\.)?",
        shelter,
        text,
    )
    before = value
    replacements = {",": "，", ":": "：", ";": "；", "?": "？", "!": "！"}
    for source, target in replacements.items():
        value = re.sub(f"(?<=[{_CJK}）》”’]){re.escape(source)}(?=[{_CJK}（《“‘]|$)", target, value)
    value = re.sub(rf"(?<=[{_CJK}])\((?=[{_CJK}])", "（", value)
    value = re.sub(rf"(?<=[{_CJK}])\)(?=[{_CJK}，。；：！？]|$)", "）", value)
    value = re.sub(f'(?<=[{_CJK}])"(?=[{_CJK}])', "“", value)
    value = re.sub(f'(?<=[{_CJK}])"(?=[{_CJK}，。；：！？]|$)', "”", value)
    changes = sum(1 for left, right in zip(before, value) if left != right) + abs(len(before) - len(value))
    for key, original in protected.items():
        value = value.replace(key, original)
    return value, changes


def _get_or_add(parent: etree._Element, local: str, *, first: bool = False) -> etree._Element:
    node = parent.find(_qn(local))
    if node is None:
        node = etree.Element(_qn(local))
        if first:
            parent.insert(0, node)
        else:
            parent.append(node)
    return node


def _set_value(parent: etree._Element, local: str, value: str) -> etree._Element:
    node = _get_or_add(parent, local)
    node.set(_qn("val"), value)
    return node


def _remove_children(parent: etree._Element, names: Iterable[str]) -> None:
    for name in names:
        for node in list(parent.findall(_qn(name))):
            parent.remove(node)


def _format_run(
    run: etree._Element,
    *,
    font: str,
    latin_font: str | None = None,
    size: int,
    bold: bool,
    color: str = "000000",
    preserve_emphasis: bool = False,
) -> None:
    properties = _get_or_add(run, "rPr", first=True)
    fonts = _get_or_add(properties, "rFonts")
    fonts.set(_qn("ascii"), latin_font or font)
    fonts.set(_qn("hAnsi"), latin_font or font)
    fonts.set(_qn("eastAsia"), font)
    fonts.set(_qn("cs"), latin_font or font)
    _set_value(properties, "sz", str(size))
    _set_value(properties, "szCs", str(size))
    if bold or not preserve_emphasis:
        _set_value(properties, "b", "1" if bold else "0")
        _set_value(properties, "bCs", "1" if bold else "0")
    if not preserve_emphasis:
        _set_value(properties, "i", "0")
        _set_value(properties, "iCs", "0")
        _set_value(properties, "u", "none")
    _set_value(properties, "color", color)
    _set_value(properties, "vanish", "0")


def _format_paragraph(paragraph: etree._Element, role: str) -> int:
    properties = _get_or_add(paragraph, "pPr", first=True)
    _remove_children(properties, ("jc", "spacing", "ind", "keepNext", "keepLines", "pageBreakBefore"))
    font, size, bold, alignment, first_indent, left_indent, color = {
        "copy_number": ("仿宋_GB2312", 32, False, "left", 0, 0, "000000"),
        "security": ("黑体", 32, False, "left", 0, 0, "000000"),
        "urgency": ("黑体", 32, False, "left", 0, 0, "000000"),
        "issuing_authority": ("方正小标宋简体", 54, False, "center", 0, 0, "FF0000"),
        "document_number": ("仿宋_GB2312", 32, False, "center", 0, 0, "000000"),
        "signatory": ("仿宋_GB2312", 32, False, "right", 0, 0, "000000"),
        "title": ("方正小标宋简体", 44, False, "center", 0, 0, "000000"),
        "addressee": ("仿宋_GB2312", 32, False, "left", 0, 0, "000000"),
        "heading1": ("黑体", 32, False, "left", 0, 0, "000000"),
        "heading2": ("楷体_GB2312", 32, False, "left", 0, 0, "000000"),
        "heading3": ("仿宋_GB2312", 32, True, "left", 0, 0, "000000"),
        "heading4": ("仿宋_GB2312", 32, False, "left", 0, 0, "000000"),
        "attachment": ("仿宋_GB2312", 32, False, "left", 0, 640, "000000"),
        "attachment_heading": ("黑体", 32, False, "left", 0, 0, "000000"),
        "signature": ("仿宋_GB2312", 32, False, "right", 0, 0, "000000"),
        "date": ("仿宋_GB2312", 32, False, "right", 0, 0, "000000"),
        "note": ("仿宋_GB2312", 32, False, "left", 0, 640, "000000"),
        "copy_recipient": ("仿宋_GB2312", 28, False, "left", 0, 320, "000000"),
        "imprint": ("仿宋_GB2312", 28, False, "right", 0, 320, "000000"),
        "attendance": ("仿宋_GB2312", 32, False, "left", 0, 640, "000000"),
        "body": ("仿宋_GB2312", 32, False, "both", 420, 0, "000000"),
    }[role]
    _set_value(properties, "jc", alignment)
    spacing = _get_or_add(properties, "spacing")
    spacing.set(_qn("before"), "0")
    spacing.set(_qn("after"), "560" if role == "title" else "0")
    spacing.set(_qn("line"), "640" if role == "title" else "560")
    spacing.set(_qn("lineRule"), "exact")
    indentation = _get_or_add(properties, "ind")
    indentation.set(_qn("firstLine"), str(first_indent))
    indentation.set(_qn("firstLineChars"), "200" if role == "body" else "0")
    indentation.set(_qn("left"), str(left_indent))
    _set_value(properties, "snapToGrid", "0")
    if role.startswith("heading") or role == "attachment_heading":
        _get_or_add(properties, "keepNext")
        _get_or_add(properties, "keepLines")
    if role == "attachment_heading":
        _get_or_add(properties, "pageBreakBefore")
    changes = 0
    for text_node in cast("list[etree._Element]", paragraph.xpath(".//w:t", namespaces=NS)):
        normalized, count = normalize_chinese_punctuation(text_node.text or "")
        text_node.text = normalized
        changes += count
    for run in cast("list[etree._Element]", paragraph.xpath(".//w:r", namespaces=NS)):
        _format_run(
            run,
            font=font,
            latin_font="Times New Roman" if font == "仿宋_GB2312" else font,
            size=size,
            bold=bold,
            color=color,
            preserve_emphasis=role == "body",
        )
    return changes + 1


def _configure_sections(document: etree._Element) -> int:
    changed = 0
    for section in cast("list[etree._Element]", document.xpath(".//w:sectPr", namespaces=NS)):
        size = _get_or_add(section, "pgSz")
        size.set(_qn("w"), "11906")
        size.set(_qn("h"), "16838")
        size.attrib.pop(_qn("orient"), None)  # type: ignore[call-overload]  # lxml 支持丢弃默认值 None，当前桩未声明。
        margins = _get_or_add(section, "pgMar")
        for name, value in {
            "top": "2098", "bottom": "1984", "left": "1588", "right": "1474",
            "header": "851", "footer": PAGE_FOOTER_DISTANCE, "gutter": "0",
        }.items():
            margins.set(_qn(name), value)
        grid = _get_or_add(section, "docGrid")
        grid.attrib.pop(_qn("type"), None)  # type: ignore[call-overload]  # 同上，不改排版行为。
        grid.set(_qn("linePitch"), GRID_LINE_PITCH)
        grid.set(_qn("charSpace"), str(GRID_CHARACTER_SPACE))
        changed += 1
    return changed


def _configure_normal_style(styles: etree._Element | None) -> int:
    """校准 Normal 字号，使 docGrid 的 28 字计算有确定基准。"""

    if styles is None:
        return 0
    normal = next(
        (
            item
            for item in styles.findall(_qn("style"))
            if item.get(_qn("styleId")) == "Normal" or item.get(_qn("default")) == "1"
        ),
        None,
    )
    if normal is None:
        normal = etree.Element(_qn("style"))
        normal.set(_qn("type"), "paragraph")
        normal.set(_qn("default"), "1")
        normal.set(_qn("styleId"), "Normal")
        styles.insert(0, normal)
    run_properties = _get_or_add(normal, "rPr")
    fonts = _get_or_add(run_properties, "rFonts")
    for name in ("ascii", "hAnsi", "eastAsia", "cs"):
        fonts.set(_qn(name), "仿宋_GB2312")
    _set_value(run_properties, "sz", "32")
    _set_value(run_properties, "szCs", "32")
    return 1


def _configure_east_asian_typography(settings: etree._Element) -> None:
    """启用中文行首行尾禁则与标点压缩，避免标点孤悬到下一行。"""

    _get_or_add(settings, "kinsoku")
    _set_value(settings, "characterSpacingControl", "compressPunctuation")
    _set_value(settings, "noPunctuationKerning", "0")


def _format_tables(document: etree._Element) -> int:
    changed = 0
    for table in cast("list[etree._Element]", document.xpath(".//w:tbl", namespaces=NS)):
        properties = _get_or_add(table, "tblPr", first=True)
        layout = _get_or_add(properties, "tblLayout")
        layout.set(_qn("type"), "fixed")
        for cell in cast("list[etree._Element]", table.xpath(".//w:tc", namespaces=NS)):
            cell_properties = _get_or_add(cell, "tcPr", first=True)
            _set_value(cell_properties, "vAlign", "center")
            margins = _get_or_add(cell_properties, "tcMar")
            for side, width in (("top", "0"), ("bottom", "0"), ("left", "72"), ("right", "72")):
                node = _get_or_add(margins, side)
                node.set(_qn("w"), width)
                node.set(_qn("type"), "dxa")
            for paragraph in cast("list[etree._Element]", cell.xpath("./w:p", namespaces=NS)):
                current_properties = paragraph.find(_qn("pPr"))
                current_alignment = None
                if current_properties is not None and current_properties.find(_qn("jc")) is not None:
                    current_alignment = cast("etree._Element", current_properties.find(_qn("jc"))).get(_qn("val"))
                _format_paragraph(paragraph, "body")
                ppr = _get_or_add(paragraph, "pPr", first=True)
                _set_value(ppr, "jc", current_alignment or "left")
                table_indent = _get_or_add(ppr, "ind")
                table_indent.set(_qn("firstLine"), "0")
                table_indent.set(_qn("firstLineChars"), "0")
        changed += 1
    return changed


def _footer_xml(alignment: str) -> bytes:
    root = etree.Element(_qn("ftr"), nsmap={"w": W})
    _append_page_footer_paragraph(root, alignment)
    return etree.tostring(root, xml_declaration=True, encoding="UTF-8", standalone=True)


def _append_page_footer_paragraph(root: etree._Element, alignment: str) -> etree._Element:
    paragraph = etree.SubElement(root, _qn("p"))
    ppr = etree.SubElement(paragraph, _qn("pPr"))
    jc = etree.SubElement(ppr, _qn("jc"))
    jc.set(_qn("val"), alignment)
    for text, field_type in (("— ", None), ("PAGE", "field"), (" —", None)):
        run = etree.SubElement(paragraph, _qn("r"))
        _format_run(run, font="宋体", size=28, bold=False)
        if field_type:
            start = etree.SubElement(run, _qn("fldChar"))
            start.set(_qn("fldCharType"), "begin")
            instruction = etree.SubElement(run, _qn("instrText"))
            instruction.set("{http://www.w3.org/XML/1998/namespace}space", "preserve")
            instruction.text = " PAGE "
            separator = etree.SubElement(run, _qn("fldChar"))
            separator.set(_qn("fldCharType"), "separate")
            result = etree.SubElement(run, _qn("t"))
            result.text = "1"
            end = etree.SubElement(run, _qn("fldChar"))
            end.set(_qn("fldCharType"), "end")
        else:
            node = etree.SubElement(run, _qn("t"))
            node.text = text
    return paragraph


def _page_field_paragraphs(root: etree._Element) -> list[etree._Element]:
    return cast("list[etree._Element]", root.xpath(
        ".//w:p[.//w:instrText[contains(translate(., 'page', 'PAGE'), 'PAGE')] "
        "or .//w:fldSimple[contains(translate(@w:instr, 'page', 'PAGE'), 'PAGE')]]",
        namespaces=NS,
    ))


def _standard_page_footer_paragraph(paragraph: etree._Element) -> bool:
    visible = "".join(cast("list[str]", paragraph.xpath(".//w:t/text()", namespaces=NS))).strip()
    return re.fullmatch(r"—\s*\d*\s*—", visible) is not None


def _remove_page_field_runs(paragraph: etree._Element) -> None:
    """从混合页脚中移除旧 PAGE 域，同时保留单位名称等其他页脚内容。"""

    for field in list(cast("list[etree._Element]", paragraph.xpath(".//w:fldSimple", namespaces=NS))):
        instruction = field.get(_qn("instr"), "")
        if "PAGE" in instruction.upper() and field.getparent() is not None:
            cast("etree._Element", field.getparent()).remove(field)
    runs = list(paragraph.findall(_qn("r")))
    page_indexes = [
        index
        for index, run in enumerate(runs)
        if any("PAGE" in (item.text or "").upper() for item in run.findall(_qn("instrText")))
    ]
    for page_index in reversed(page_indexes):
        begin_indexes = [
            index
            for index in range(page_index, -1, -1)
            if runs[index].xpath(".//w:fldChar[@w:fldCharType='begin']", namespaces=NS)
        ]
        end_indexes = [
            index
            for index in range(page_index, len(runs))
            if runs[index].xpath(".//w:fldChar[@w:fldCharType='end']", namespaces=NS)
        ]
        start = begin_indexes[0] if begin_indexes else page_index
        end = end_indexes[0] if end_indexes else page_index
        for run in runs[start : end + 1]:
            if run.getparent() is paragraph:
                paragraph.remove(run)


def _normalize_page_footer(root: etree._Element, alignment: str) -> None:
    page_paragraphs = _page_field_paragraphs(root)
    standard = next((item for item in page_paragraphs if _standard_page_footer_paragraph(item)), None)
    if standard is not None:
        ppr = _get_or_add(standard, "pPr", first=True)
        _set_value(ppr, "jc", alignment)
        return
    for paragraph in page_paragraphs:
        visible = "".join(cast("list[str]", paragraph.xpath(".//w:t/text()", namespaces=NS))).strip()
        if re.fullmatch(r"[\d\s—–－-]*", visible):
            parent = paragraph.getparent()
            if parent is not None:
                parent.remove(paragraph)
        else:
            _remove_page_field_runs(paragraph)
    _append_page_footer_paragraph(root, alignment)


def _add_page_footers(
    document: etree._Element,
    settings: etree._Element,
    relationships: etree._Element,
    content_types: etree._Element,
    package: zipfile.ZipFile,
) -> dict[str, bytes]:
    """补齐奇偶页外侧页码，并保留已有页脚的文字、图片和关系。"""

    relationship_ids = {
        item.get("Id", "") for item in relationships.findall(f"{{{PR}}}Relationship")
    }
    relationship_targets = {
        item.get("Id", ""): item.get("Target", "")
        for item in relationships.findall(f"{{{PR}}}Relationship")
        if item.get("Type", "") == f"{R}/footer" and item.get("TargetMode", "") != "External"
    }
    next_index = 1

    def next_id() -> str:
        nonlocal next_index
        while f"rIdPartyOpsFooter{next_index}" in relationship_ids:
            next_index += 1
        value = f"rIdPartyOpsFooter{next_index}"
        relationship_ids.add(value)
        next_index += 1
        return value

    outputs: dict[str, bytes] = {}
    references: dict[str, str] = {}
    sections = cast("list[etree._Element]", document.xpath(".//w:sectPr", namespaces=NS))
    for kind, filename, alignment in (
        ("default", "footer-partyops-odd.xml", "right"),
        ("even", "footer-partyops-even.xml", "left"),
    ):
        existing_refs = [
            ref
            for section in sections
            for ref in section.findall(_qn("footerReference"))
            if ref.get(_qn("type"), "default") == kind
        ]
        usable_ids: list[str] = []
        for reference in existing_refs:
            rel_id = reference.get(f"{{{R}}}id", "")
            target_name = relationship_targets.get(rel_id, "")
            target_path = PurePosixPath(target_name.lstrip("/"))
            if target_path.is_absolute() or ".." in target_path.parts:
                continue
            part_name = str(target_path)
            if not part_name.startswith("word/"):
                part_name = f"word/{part_name}"
            if part_name not in package.namelist():
                continue
            root = _safe_xml(outputs.get(part_name, package.read(part_name)), part_name)
            _normalize_page_footer(root, alignment)
            outputs[part_name] = etree.tostring(
                root, xml_declaration=True, encoding="UTF-8", standalone=True
            )
            usable_ids.append(rel_id)
        if usable_ids:
            references[kind] = usable_ids[0]
            continue

        rel_id = next_id()
        relationship = etree.SubElement(relationships, f"{{{PR}}}Relationship")
        relationship.set("Id", rel_id)
        relationship.set("Type", f"{R}/footer")
        relationship.set("Target", filename)
        references[kind] = rel_id
        outputs[f"word/{filename}"] = _footer_xml(alignment)
        if not content_types.xpath(
            "./ct:Override[@PartName=$part]",
            namespaces={"ct": CT},
            part=f"/word/{filename}",
        ):
            override = etree.SubElement(content_types, f"{{{CT}}}Override")
            override.set("PartName", f"/word/{filename}")
            override.set("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml")
    for section in sections:
        present = {
            ref.get(_qn("type"), "default")
            for ref in section.findall(_qn("footerReference"))
        }
        insert_at = 0
        for kind, rel_id in references.items():
            if kind in present:
                continue
            reference = etree.Element(_qn("footerReference"))
            reference.set(_qn("type"), kind)
            reference.set(f"{{{R}}}id", rel_id)
            section.insert(insert_at, reference)
            insert_at += 1
    _get_or_add(settings, "evenAndOddHeaders")
    return outputs


def _system_command_environment() -> dict[str, str]:
    """系统工具使用本机运行库；重复启动冻结程序时也清除继承的打包路径。"""
    environment = os.environ.copy()
    if sys.platform == "linux" and getattr(sys, "frozen", False):
        bundled = {Path(sys.executable).resolve().parent / "_internal"}
        if getattr(sys, "_MEIPASS", None):
            bundled.add(Path(sys._MEIPASS).resolve())
        original = environment.get("LD_LIBRARY_PATH_ORIG", "")
        paths = [value for value in original.split(":")
                 if value and Path(value).resolve() not in bundled]
        if paths:
            environment["LD_LIBRARY_PATH"] = ":".join(paths)
        else:
            environment.pop("LD_LIBRARY_PATH", None)
        # 子程序若再次启动冻结程序，不能恢复已剔除的打包库路径。
        environment.pop("LD_LIBRARY_PATH_ORIG", None)
    return environment


def _font_inventory() -> str:
    if os.name == "nt":
        import winreg

        values: list[str] = []
        # 每次排版重新读取两种安装范围；“仅为我安装”的字体也应立即识别。
        # 单个范围不存在或不可读，不应掩盖另一个范围的可用字体。
        for hive in (winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER):
            try:
                with winreg.OpenKey(hive, r"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts") as key:
                    index = 0
                    while True:
                        try:
                            name, value, _ = winreg.EnumValue(key, index)
                        except OSError:
                            break
                        values.extend((str(name), str(value)))
                        index += 1
            except OSError:
                continue
        return "\n".join(values).lower()
    matcher = shutil.which("fc-list")
    if matcher:
        try:
            return subprocess.run(
                [matcher, ":", "family"], capture_output=True, text=True,
                encoding="utf-8", errors="ignore", timeout=10, check=False,
                env=_system_command_environment(),
            ).stdout.lower()
        except (OSError, subprocess.TimeoutExpired):
            return ""
    font_dirs = [Path("/System/Library/Fonts"), Path("/Library/Fonts"), Path.home() / "Library/Fonts"]
    return "\n".join(path.name for root in font_dirs if root.is_dir() for path in root.rglob("*")).lower()


def _font_issues() -> list[FormatIssue]:
    inventory = _font_inventory()
    if not inventory:
        return [FormatIssue("FONT_CHECK_UNAVAILABLE", "error", "无法验证公文字体", "系统无法读取字体清单，不能判定成品符合标准。", "GB/T 9704-2012 5.2.2—5.2.5")]
    groups = {
        "方正小标宋简体": ("方正小标宋", "fzxiaobiaosong"),
        "仿宋_GB2312": ("仿宋", "fangsong"),
        "楷体_GB2312": ("楷体", "kaiti"),
        "黑体": ("黑体", "simhei", "heiti"),
    }
    missing = [name for name, aliases in groups.items() if not any(alias.lower() in inventory for alias in aliases)]
    if not missing:
        return []
    return [FormatIssue("REQUIRED_FONT_MISSING", "error", "缺少公文所需字体", "未检测到：" + "、".join(missing) + "。已写入标准字体名，但导出前必须安装并复核。", "GB/T 9704-2012 5.2.2—5.2.5")]


def diagnose_docx(path: Path, *, changed_count: int = 0) -> FormatReport:
    document, _ = _read_core_parts(path)
    paragraphs = cast("list[etree._Element]", document.xpath(".//w:body/w:p", namespaces=NS))
    tables = cast("list[etree._Element]", document.xpath(".//w:tbl", namespaces=NS))
    issues: list[FormatIssue] = []
    sections = cast("list[etree._Element]", document.xpath(".//w:sectPr", namespaces=NS))
    if not sections:
        issues.append(FormatIssue("SECTION_MISSING", "error", "缺少页面节设置", "无法校准 A4、版心和页码。", "GB/T 9704-2012 5.1"))
    else:
        for section in sections:
            size = section.find(_qn("pgSz"))
            margins = section.find(_qn("pgMar"))
            expected_size = size is not None and size.get(_qn("w")) == "11906" and size.get(_qn("h")) == "16838"
            expected_margins = margins is not None and all(
                margins.get(_qn(name)) == value
                for name, value in {
                    "top": "2098", "bottom": "1984", "left": "1588", "right": "1474",
                    "footer": PAGE_FOOTER_DISTANCE,
                }.items()
            )
            if not expected_size or not expected_margins:
                issues.append(FormatIssue("PAGE_GEOMETRY_INVALID", "error", "页面尺寸或版心不符合预设", "需要校准 A4、天头 37 mm、订口 28 mm 和 156×225 mm 版心。", "GB/T 9704-2012 5.1"))
                break
            grid = section.find(_qn("docGrid"))
            if (
                grid is None
                or grid.get(_qn("linePitch")) != GRID_LINE_PITCH
                or grid.get(_qn("charSpace")) != str(GRID_CHARACTER_SPACE)
            ):
                issues.append(FormatIssue(
                    "DOCUMENT_GRID_INVALID", "error" if changed_count else "warning",
                    "文档网格兼容参数与源码默认值不一致",
                    "原排版工具默认关闭强制字符网格；需要保留 312/0 兼容参数，避免改变换行和分页。",
                    "GB/T 9704-2012 5.2.3",
                ))
                break
    nonempty = [item for item in paragraphs if _paragraph_text(item)]
    if not nonempty:
        issues.append(FormatIssue("DOCUMENT_EMPTY", "error", "正文为空", "未识别到可排版正文。", "输入完整性"))
    else:
        classified = _classify_document_paragraphs(nonempty)
        title = next((paragraph for paragraph, role in classified if role == "title"), None)
        if title is not None:
            title_runs = cast("list[etree._Element]", title.xpath(".//w:r/w:rPr", namespaces=NS))
            title_properties = title.find(_qn("pPr"))
            title_centered = (
                title_properties is not None
                and title_properties.find(_qn("jc")) is not None
                and cast("etree._Element", title_properties.find(_qn("jc"))).get(_qn("val")) == "center"
            )
            title_standard = bool(title_runs) and all(
                properties.find(_qn("rFonts")) is not None
                and cast("etree._Element", properties.find(_qn("rFonts"))).get(_qn("eastAsia")) == "方正小标宋简体"
                and properties.find(_qn("sz")) is not None
                and cast("etree._Element", properties.find(_qn("sz"))).get(_qn("val")) == "44"
                for properties in title_runs
            )
            if not title_centered or not title_standard:
                issues.append(FormatIssue("TITLE_STYLE_INVALID", "warning", "标题样式需要校准", "标题未完整使用 2 号小标宋和居中规则。", "GB/T 9704-2012 7.3.1"))
        body_invalid = False
        for paragraph, role in classified:
            if role != "body":
                continue
            properties = paragraph.find(_qn("pPr"))
            runs = cast("list[etree._Element]", paragraph.xpath(".//w:r/w:rPr", namespaces=NS))
            if properties is None or not runs:
                body_invalid = True
                break
            spacing = properties.find(_qn("spacing"))
            indent = properties.find(_qn("ind"))
            if (
                spacing is None
                or spacing.get(_qn("line")) != "560"
                or indent is None
                or indent.get(_qn("firstLine")) != "420"
                or indent.get(_qn("firstLineChars")) != "200"
                or any(
                    run.find(_qn("rFonts")) is None
                    or cast("etree._Element", run.find(_qn("rFonts"))).get(_qn("eastAsia")) != "仿宋_GB2312"
                    or run.find(_qn("sz")) is None
                    or cast("etree._Element", run.find(_qn("sz"))).get(_qn("val")) != "32"
                    for run in runs
                )
            ):
                body_invalid = True
                break
        if body_invalid:
            issues.append(FormatIssue("BODY_STYLE_INVALID", "warning", "正文段落需要校准", "正文未完整使用 3 号仿宋、28 磅行距和首行二字符缩进。", "GB/T 9704-2012 5.2.3、5.2.4"))
    footer_standard = True
    with zipfile.ZipFile(path) as package:
        footer_parts = [name for name in package.namelist() if re.fullmatch(r"word/footer[^/]*\.xml", name)]
        has_page_field = any(b"PAGE" in package.read(name) for name in footer_parts)
        for name in footer_parts:
            payload = package.read(name)
            if b"PAGE" not in payload:
                continue
            footer = _safe_xml(payload, name)
            page_paragraphs = _page_field_paragraphs(footer)
            if not page_paragraphs or not all(
                _standard_page_footer_paragraph(item) for item in page_paragraphs
            ):
                footer_standard = False
                break
    if not document.xpath(".//w:sectPr/w:footerReference", namespaces=NS) or not has_page_field:
        issues.append(FormatIssue("PAGE_NUMBER_MISSING", "error" if changed_count else "warning", "未识别到有效页码", "排版时将按奇偶页分别置于版心下边缘。", "GB/T 9704-2012 7.5"))
    elif not footer_standard:
        issues.append(FormatIssue(
            "PAGE_NUMBER_STYLE_INVALID", "error", "页码一字线格式不完整",
            "奇数页和偶数页都必须使用四号半角宋体阿拉伯数字，并在数字左右各放一条一字线。",
            "GB/T 9704-2012 7.5",
        ))
    if document.xpath(".//w:txbxContent", namespaces=NS):
        issues.append(FormatIssue("TEXTBOX_REVIEW_REQUIRED", "warning", "包含文本框或浮动文字", "工具不移动文本框，需人工确认其字体、位置和遮挡关系。", "特殊对象复核"))
    if any(re.match(r"^\d+[、)]", _paragraph_text(item)) for item in nonempty):
        issues.append(FormatIssue("NUMBERING_REVIEW_REQUIRED", "warning", "发现非标准数字序号", "请确认是否应使用“1.”或“（1）”，法规条号不会自动强改。", "GB/T 9704-2012 5.2.3"))
    special_marks = [
        _paragraph_text(item)
        for item in nonempty
        if _paragraph_role(_paragraph_text(item), first_body=False) == "issuing_authority"
    ]
    if special_marks:
        issues.append(FormatIssue(
            "SPECIAL_LAYOUT_VISUAL_REVIEW_REQUIRED", "warning", "识别到版头或特定公文版式",
            "发文机关标志、红色分隔线、印章、信函、命令（令）或纪要必须按最终渲染页逐页复核；工具不会伪造机关标志或印章。",
            "GB/T 9704-2012 7.2、7.3.5、10",
        ))
    if any(_paragraph_role(_paragraph_text(item), first_body=False) == "signatory" for item in nonempty):
        issues.append(FormatIssue(
            "SIGNATORY_VISUAL_REVIEW_REQUIRED", "warning", "签发人区域需要人工复核",
            "请确认签发人姓名使用三号楷体，并核对多签发人换行与对齐；系统不猜测签发权限。",
            "GB/T 9704-2012 7.2.6",
        ))
    issues.extend(_font_issues())
    compliant = not any(item.severity == "error" for item in issues)
    return FormatReport(compliant, len(paragraphs), len(tables), changed_count, tuple(issues))


def format_docx(
    source: Path,
    target: Path,
    *,
    paragraph_range: tuple[int, int] | None = None,
    apply_document_layout: bool = True,
) -> FormatReport:
    """直接改写必要 OOXML 部件，所有未触碰部件逐项复制。"""

    with zipfile.ZipFile(source) as package:
        members = _validated_members(package)
        payloads: dict[str, bytes] = {}
        document = _safe_xml(package.read("word/document.xml"), "word/document.xml")
        settings = _safe_xml(package.read("word/settings.xml"), "word/settings.xml") if "word/settings.xml" in package.namelist() else etree.Element(_qn("settings"), nsmap={"w": W})
        styles = (
            _safe_xml(package.read("word/styles.xml"), "word/styles.xml")
            if "word/styles.xml" in package.namelist()
            else None
        )
        relationships = _safe_xml(package.read("word/_rels/document.xml.rels"), "word/_rels/document.xml.rels") if "word/_rels/document.xml.rels" in package.namelist() else etree.Element(f"{{{PR}}}Relationships", nsmap={None: PR})  # type: ignore[dict-item]  # lxml 的默认命名空间键为 None，桩未覆盖。
        content_types = _safe_xml(package.read("[Content_Types].xml"), "[Content_Types].xml")

        changed = 0
        # 与原源码 CleanDocument 顺序一致：手动换行必须先转成段落，再删除
        # 纯空段，最后才允许识别称谓、标题和正文。
        changed += _normalize_manual_line_breaks(document)
        changed += _remove_empty_body_paragraphs(document)
        body_paragraphs = cast("list[etree._Element]", document.xpath(
            ".//w:body/w:p | .//w:body/w:sdt/w:sdtContent/w:p", namespaces=NS
        ))
        classified = _classify_document_paragraphs(body_paragraphs)
        if paragraph_range is not None:
            start, end = paragraph_range
            if start < 1 or end < start or start > len(classified):
                raise OfficialFormatError(
                    "FORMAT_SCOPE_INVALID",
                    "排版范围无效",
                    f"文档共有 {len(classified)} 个可排版段落，请重新选择起止段落。",
                )
            classified = classified[start - 1 : min(end, len(classified))]
        for paragraph, role in classified:
            changed += _format_paragraph(paragraph, role)
        if apply_document_layout:
            changed += _format_tables(document)
            changed += _configure_sections(document)
            _configure_east_asian_typography(settings)
            payloads.update(
                _add_page_footers(
                    document,
                    settings,
                    relationships,
                    content_types,
                    package,
                )
            )
        payloads["word/document.xml"] = etree.tostring(document, xml_declaration=True, encoding="UTF-8", standalone=True)
        payloads["word/settings.xml"] = etree.tostring(settings, xml_declaration=True, encoding="UTF-8", standalone=True)
        if styles is not None:
            payloads["word/styles.xml"] = etree.tostring(
                styles, xml_declaration=True, encoding="UTF-8", standalone=True
            )
        payloads["word/_rels/document.xml.rels"] = etree.tostring(relationships, xml_declaration=True, encoding="UTF-8", standalone=True)
        payloads["[Content_Types].xml"] = etree.tostring(content_types, xml_declaration=True, encoding="UTF-8", standalone=True)

        target.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as output:
            written: set[str] = set()
            for info in members:
                data = payloads.get(info.filename)
                if data is None:
                    data = package.read(info.filename)
                output.writestr(info, data)
                written.add(info.filename)
            for name, data in payloads.items():
                if name not in written:
                    output.writestr(name, data)
    return diagnose_docx(target, changed_count=changed)


def _office_candidates() -> list[Path]:
    explicit = os.getenv("PARTYOPS_OFFICE_BIN")
    path_candidates = [shutil.which("soffice"), shutil.which("libreoffice")]
    bundled_candidates: list[str] = []
    installed_candidates: list[str] = []
    # 原生安装包可把精简的 headless LibreOffice 放在 office-runtime 目录；
    # 通过显式环境变量或相邻目录查找，避免依赖用户 PATH，也避免把办公
    # 套件安装到全局位置后误用其它版本。
    try:
        executable_root = Path(sys.executable).resolve().parent
        module_root = Path(__file__).resolve().parent
    except (NotImplementedError, OSError):
        # 单元测试会临时切换 os.name 以覆盖 Windows 分支；Windows 路径
        # 不能由 PosixPath 构造，此时跳过相邻运行时探测即可。
        executable_root = None
        module_root = None
    if executable_root is not None and module_root is not None:
        bundled_candidates.extend(
            [
                str(
                    executable_root.parent
                    / "Resources"
                    / "office-runtime"
                    / "LibreOffice.app"
                    / "Contents"
                    / "MacOS"
                    / "soffice"
                ),
                str(executable_root / "office-runtime" / "program" / "soffice"),
                # Windows 的 soffice.exe 使用 GUI 子系统；被无窗口父进程捕获
                # stdout/stderr 时，官方 26.x 启动器可能一直等待。soffice.com
                # 是同一套件提供的控制台入口，应优先用于确定性的 headless
                # 转换。保留 .exe 仅用于旧版 LibreOffice 兼容回退。
                str(executable_root / "office-runtime" / "program" / "soffice.com"),
                str(executable_root / "office-runtime" / "program" / "soffice.exe"),
                str(executable_root.parent / "Resources" / "office-runtime" / "program" / "soffice"),
                str(module_root / "office-runtime" / "program" / "soffice"),
                str(module_root / "office-runtime" / "program" / "soffice.com"),
                str(module_root / "office-runtime" / "program" / "soffice.exe"),
            ]
        )
    if os.name == "nt":
        for root in (os.getenv("PROGRAMFILES"), os.getenv("PROGRAMFILES(X86)")):
            if root:
                installed_candidates.append(str(Path(root) / "LibreOffice" / "program" / "soffice.com"))
                installed_candidates.append(str(Path(root) / "LibreOffice" / "program" / "soffice.exe"))
    # 冻结包默认使用随包验证过的版本，PATH 与系统安装仅作为最后回退；
    # 开发态沿用原有 PATH 优先次序，显式覆盖始终最高优先级。
    if getattr(sys, "frozen", False):
        values = [explicit, *bundled_candidates, *path_candidates, *installed_candidates]
    else:
        values = [explicit, *path_candidates, *bundled_candidates, *installed_candidates]
    seen: set[str] = set()
    candidates: list[Path] = []
    for value in values:
        if not value:
            continue
        path = Path(value)
        key = str(path.resolve()).lower()
        if key in seen or not path.is_file():
            continue
        seen.add(key)
        candidates.append(path)
    return candidates


def _convert_with_libreoffice(source: Path, workspace: Path) -> Path | None:
    candidates = _office_candidates()
    if not candidates:
        return None
    profile = workspace / "office-profile"
    profile.mkdir(mode=0o700, exist_ok=True)
    environment = os.environ.copy()
    # 转换器处理的是不受信任文档。不要继承系统代理，更不能把 NO_PROXY
    # 设为 *（这会允许直连）；统一把网络代理指向不可用的本机 discard
    # 端口，并使用隔离配置、禁用恢复和扩展，降低宏、外链及崩溃恢复面。
    for key in ("HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy"):
        environment[key] = "http://127.0.0.1:9"
    environment["NO_PROXY"] = "127.0.0.1,localhost"
    environment["no_proxy"] = "127.0.0.1,localhost"
    environment["LIBO_DISABLE_CRASHREPORT"] = "1"
    environment["SAL_DISABLE_OPENCL"] = "1"
    command = [
        str(candidates[0]), "--headless", "--invisible", "--safe-mode", "--nologo", "--nodefault",
        "--norestore", "--nolockcheck", "--nofirststartwizard",
        f"-env:UserInstallation={profile.as_uri()}", "--convert-to", "docx", "--outdir", str(workspace), str(source),
    ]
    try:
        completed = subprocess.run(command, capture_output=True, timeout=90, env=environment, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise OfficialFormatError("OFFICE_CONVERSION_FAILED", "本机格式转换失败", "LibreOffice 未能在 90 秒内完成本地转换。") from exc
    expected = workspace / f"{source.stem}.docx"
    if completed.returncode != 0 or not expected.is_file():
        raise OfficialFormatError("OFFICE_CONVERSION_FAILED", "本机格式转换失败", "办公套件没有生成可验证的 DOCX；原文件未改变。")
    return expected


def _convert_with_windows_office(source: Path, workspace: Path) -> Path | None:
    if os.name != "nt":
        return None
    try:
        import win32com.client  # type: ignore[import-untyped]
    except ImportError:
        return None
    target = workspace / f"{source.stem}.docx"
    for program_id in ("Word.Application", "Kwps.Application", "Wps.Application"):
        application = None
        document = None
        try:
            application = win32com.client.DispatchEx(program_id)
            application.Visible = False
            application.DisplayAlerts = 0
            document = application.Documents.Open(str(source), ReadOnly=True, AddToRecentFiles=False)
            try:
                document.SaveAs2(str(target), FileFormat=16)
            except AttributeError:
                document.SaveAs(str(target), FileFormat=16)
            if target.is_file():
                return target
        except Exception:  # noqa: BLE001 - 逐个尝试本机办公套件，最终统一给出脱敏错误。
            continue
        finally:
            if document is not None:
                try:
                    document.Close(False)
                except Exception:  # noqa: BLE001
                    pass
            if application is not None:
                try:
                    application.Quit()
                except Exception:  # noqa: BLE001
                    pass
    return None


def prepare_docx(source: Path, workspace: Path) -> tuple[Path, bool]:
    extension = source.suffix.lower()
    if extension == ".docx":
        return source, False
    converted = _convert_with_libreoffice(source, workspace)
    if converted is None:
        raise OfficialFormatError(
            "BUNDLED_OFFICE_RUNTIME_MISSING",
            "内置转换引擎不可用",
            "PartyOps 安装包中的无窗口转换运行时缺失或损坏；不需要安装 Word、WPS 或其他办公软件，请修复安装 PartyOps。",
        )
    return converted, True


def _private_write(path: Path, payload: bytes) -> None:
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    descriptor = os.open(path, flags, 0o600)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(payload)
    except Exception:
        path.unlink(missing_ok=True)
        raise


def _append_stage_log(config_dir: Path, stage: str, started_at: float, code: str) -> None:
    """只记录阶段、耗时和错误码；不接收文档相关参数。"""

    path = config_dir / "official-format.log"
    try:
        if path.is_file() and path.stat().st_size > 512 * 1024:
            rotated = config_dir / "official-format.log.1"
            rotated.unlink(missing_ok=True)
            path.replace(rotated)
        line = json.dumps(
            {
                "version": VERSION,
                "stage": stage,
                "duration_ms": max(0, int((time.monotonic() - started_at) * 1000)),
                "result_code": code,
            },
            ensure_ascii=False,
            separators=(",", ":"),
        )
        with path.open("a", encoding="utf-8") as stream:
            stream.write(line + "\n")
        if os.name != "nt":
            path.chmod(0o600)
    except OSError:
        return


def _safe_stem(filename: str) -> str:
    stem = Path(filename.replace("\\", "/")).stem
    stem = re.sub(r"[\x00-\x1f<>:\"/\\|?*]", "_", stem).strip(" ._")
    return (stem or "公文")[:80]


def _extract_upload(handler: BaseHTTPRequestHandler) -> tuple[str, bytes]:
    try:
        length = int(handler.headers.get("Content-Length", "0"))
    except ValueError as exc:
        raise OfficialFormatError("UPLOAD_LENGTH_INVALID", "文件请求无效", "无法确认上传大小。") from exc
    if length <= 0 or length > MAX_FILE_BYTES + 1024 * 1024:
        raise OfficialFormatError("FILE_SIZE_LIMIT", "文件大小不符合要求", "单个文件不得超过 50 MiB。")
    content_type = handler.headers.get("Content-Type", "")
    match = re.search(r"boundary=(?:\"([^\"]+)\"|([^;]+))", content_type)
    if not content_type.lower().startswith("multipart/form-data") or not match:
        raise OfficialFormatError("UPLOAD_FORMAT_INVALID", "文件请求无效", "请选择 DOC、DOCX 或 WPS 文件。")
    boundary = (match.group(1) or match.group(2)).encode("ascii", "strict")
    body = handler.rfile.read(length)
    for part in body.split(b"--" + boundary):
        if b"Content-Disposition:" not in part or b'name="document"' not in part:
            continue
        header_bytes, separator, data = part.partition(b"\r\n\r\n")
        if not separator:
            continue
        filename_match = re.search(br'filename="([^"\r\n]*)"', header_bytes)
        filename = (filename_match.group(1) if filename_match else b"").decode("utf-8", "replace")
        payload = data[:-2] if data.endswith(b"\r\n") else data
        if not payload or len(payload) > MAX_FILE_BYTES:
            raise OfficialFormatError("FILE_SIZE_LIMIT", "文件大小不符合要求", "文件为空或超过 50 MiB。")
        extension = Path(filename).suffix.lower()
        if extension not in SUPPORTED_EXTENSIONS:
            raise OfficialFormatError("FORMAT_UNSUPPORTED", "文件格式不支持", "仅支持 DOC、DOCX 和 WPS；不接受宏文档或任意压缩包。")
        return filename, payload
    raise OfficialFormatError("UPLOAD_FILE_MISSING", "没有收到文件", "请重新选择文件。")


__all__ = [
    "FormatIssue", "FormatReport", "OfficialFormatError", "diagnose_docx",
    "format_docx", "normalize_chinese_punctuation", "prepare_docx",
]
