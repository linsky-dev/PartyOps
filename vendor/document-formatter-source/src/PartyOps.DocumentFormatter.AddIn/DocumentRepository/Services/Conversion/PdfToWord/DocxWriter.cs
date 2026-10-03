using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public static class DocxWriter
{
	private sealed class ImagePartInfo
	{
		public ImageBlock Block { get; set; }

		public int Index { get; set; }

		public string EntryName { get; set; }

		public string ContentType { get; set; }

		public string RelationshipId { get; set; }

		public long Cx { get; set; }

		public long Cy { get; set; }
	}

	private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

	private const string WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

	private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";

	private const string PIC = "http://schemas.openxmlformats.org/drawingml/2006/picture";

	private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

	private const string XMLNS = "http://www.w3.org/XML/1998/namespace";

	private const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";

	private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

	private const string ExtendedPropertiesNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

	private const string DocTypeUri = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

	private const string CorePropsUri = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";

	private const string ExtendedPropsUri = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";

	private const string StylesRelUri = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";

	private const string SettingsRelUri = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";

	private const string FontTableRelUri = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable";

	private const string ImageRelUri = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

	private const int ContentWidthDxa = 8844;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Write(Stream output, ReconstructedDocument document)
	{
		if (output != null)
		{
			if (document == null)
			{
				document = new ReconstructedDocument();
			}
			IList<ImagePartInfo> images = PrepareImages(document);
			using ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
			WriteContentTypes(archive, images);
			WriteRootRelationships(archive);
			WriteDocumentRelationships(archive, images);
			WriteCoreProps(archive);
			WriteAppProps(archive);
			WriteStyles(archive);
			WriteSettings(archive);
			WriteFontTable(archive);
			WriteImageParts(archive, images);
			WriteDocumentBody(archive, document, images);
			return;
		}
		throw new ArgumentNullException("output");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteContentTypes(ZipArchive archive, IList<ImagePartInfo> images)
	{
		XmlWriter val = CreateWriter(archive, "[Content_Types].xml");
		try
		{
			val.WriteStartDocument(true);
			val.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
			WriteContentTypeDefault(val, "rels", "application/vnd.openxmlformats-package.relationships+xml");
			WriteContentTypeDefault(val, "xml", "application/xml");
			bool flag = false;
			bool flag2 = false;
			for (int i = 0; i < images.Count; i++)
			{
				flag = flag || images[i].ContentType == "image/jpeg";
				flag2 = flag2 || images[i].ContentType == "image/png";
			}
			if (flag)
			{
				WriteContentTypeDefault(val, "jpeg", "image/jpeg");
			}
			if (flag2)
			{
				WriteContentTypeDefault(val, "png", "image/png");
			}
			WriteContentTypeOverride(val, "/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml");
			WriteContentTypeOverride(val, "/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml");
			WriteContentTypeOverride(val, "/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml");
			WriteContentTypeOverride(val, "/word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml");
			WriteContentTypeOverride(val, "/word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml");
			WriteContentTypeOverride(val, "/word/fontTable.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml");
			val.WriteEndElement();
			val.WriteEndDocument();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteRootRelationships(ZipArchive archive)
	{
		XmlWriter val = CreateWriter(archive, "_rels/.rels");
		try
		{
			val.WriteStartDocument(true);
			val.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
			WriteRelationship(val, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "word/document.xml");
			WriteRelationship(val, "rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml");
			WriteRelationship(val, "rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties", "docProps/app.xml");
			val.WriteEndElement();
			val.WriteEndDocument();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteDocumentRelationships(ZipArchive archive, IList<ImagePartInfo> images)
	{
		XmlWriter val = CreateWriter(archive, "word/_rels/document.xml.rels");
		try
		{
			val.WriteStartDocument(true);
			val.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
			WriteRelationship(val, "rIdStyles", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
			WriteRelationship(val, "rIdSettings", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings", "settings.xml");
			WriteRelationship(val, "rIdFontTable", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable", "fontTable.xml");
			for (int i = 0; i < images.Count; i++)
			{
				ImagePartInfo imagePartInfo = images[i];
				WriteRelationship(val, imagePartInfo.RelationshipId, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "media/" + Path.GetFileName(imagePartInfo.EntryName));
			}
			val.WriteEndElement();
			val.WriteEndDocument();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteCoreProps(ZipArchive archive)
	{
		XmlWriter val = CreateWriter(archive, "docProps/core.xml");
		try
		{
			val.WriteStartDocument(true);
			val.WriteStartElement("cp", "coreProperties", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
			val.WriteAttributeString("xmlns", "dc", (string)null, "http://purl.org/dc/elements/1.1/");
			val.WriteAttributeString("xmlns", "dcterms", (string)null, "http://purl.org/dc/terms/");
			val.WriteAttributeString("xmlns", "xsi", (string)null, "http://www.w3.org/2001/XMLSchema-instance");
			val.WriteStartElement("dc", "title", "http://purl.org/dc/elements/1.1/");
			val.WriteString("PDF 转 Word 转换结果");
			val.WriteEndElement();
			val.WriteStartElement("dc", "creator", "http://purl.org/dc/elements/1.1/");
			val.WriteString("partyops公文排版助手");
			val.WriteEndElement();
			val.WriteStartElement("dcterms", "created", "http://purl.org/dc/terms/");
			val.WriteAttributeString("xsi", "type", "http://www.w3.org/2001/XMLSchema-instance", "dcterms:W3CDTF");
			val.WriteString(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
			val.WriteEndElement();
			val.WriteEndElement();
			val.WriteEndDocument();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteAppProps(ZipArchive archive)
	{
		XmlWriter val = CreateWriter(archive, "docProps/app.xml");
		try
		{
			val.WriteStartDocument(true);
			val.WriteStartElement("Properties", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties");
			val.WriteStartElement("Application", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties");
			val.WriteString("partyops公文排版助手");
			val.WriteEndElement();
			val.WriteEndElement();
			val.WriteEndDocument();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteStyles(ZipArchive archive)
	{
		XmlWriter val = CreateWriter(archive, "word/styles.xml");
		try
		{
			val.WriteStartDocument(true);
			val.WriteStartElement("w", "styles", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
			val.WriteStartElement("w", "docDefaults", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
			val.WriteStartElement("w", "rPrDefault", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
			val.WriteStartElement("w", "rPr", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
			WEl(val, "rFonts");
			WAttr(val, "ascii", "Times New Roman");
			WAttr(val, "eastAsia", "仿宋");
			WAttr(val, "hAnsi", "Times New Roman");
			val.WriteEndElement();
			WEl(val, "sz");
			WAttr(val, "val", "24");
			val.WriteEndElement();
			WEl(val, "szCs");
			WAttr(val, "val", "24");
			val.WriteEndElement();
			val.WriteEndElement();
			val.WriteEndElement();
			val.WriteEndElement();
			val.WriteEndElement();
			val.WriteEndDocument();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteSettings(ZipArchive archive)
	{
		XmlWriter val = CreateWriter(archive, "word/settings.xml");
		try
		{
			val.WriteStartDocument(true);
			val.WriteStartElement("w", "settings", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
			WEl(val, "zoom");
			WAttr(val, "percent", "100");
			val.WriteEndElement();
			val.WriteEndElement();
			val.WriteEndDocument();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteFontTable(ZipArchive archive)
	{
		XmlWriter val = CreateWriter(archive, "word/fontTable.xml");
		try
		{
			val.WriteStartDocument(true);
			val.WriteStartElement("w", "fonts", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
			string[] array = new string[8] { "宋体", "黑体", "仿宋", "楷体", "微软雅黑", "Times New Roman", "Arial", "Calibri" };
			foreach (string value in array)
			{
				WEl(val, "font");
				WAttr(val, "name", value);
				val.WriteEndElement();
			}
			val.WriteEndElement();
			val.WriteEndDocument();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteDocumentBody(ZipArchive archive, ReconstructedDocument document, IList<ImagePartInfo> images)
	{
		Dictionary<ImageBlock, ImagePartInfo> dictionary = new Dictionary<ImageBlock, ImagePartInfo>();
		for (int i = 0; i < images.Count; i++)
		{
			dictionary[images[i].Block] = images[i];
		}
		XmlWriter val = CreateWriter(archive, "word/document.xml");
		try
		{
			val.WriteStartDocument(true);
			val.WriteStartElement("w", "document", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
			val.WriteStartElement("w", "body", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
			foreach (DocumentBlock block in document.Blocks)
			{
				if (block is ParagraphBlock)
				{
					WriteParagraph(val, (ParagraphBlock)block);
					continue;
				}
				if (!(block is TableBlock))
				{
					if (block is ImageBlock && dictionary.TryGetValue((ImageBlock)block, out var value))
					{
						WriteImage(val, value);
					}
					continue;
				}
				TableBlock tableBlock = (TableBlock)block;
				WriteTable(val, tableBlock);
				if (tableBlock.NeedsFollowingBoundary)
				{
					WriteEmptyTableBoundaryParagraph(val);
				}
			}
			WriteSectionProperties(val);
			val.WriteEndElement();
			val.WriteEndElement();
			val.WriteEndDocument();
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteEmptyTableBoundaryParagraph(XmlWriter xw)
	{
		xw.WriteStartElement("w", "p", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
		xw.WriteEndElement();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteSectionProperties(XmlWriter xw)
	{
		xw.WriteStartElement("w", "sectPr", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
		WEl(xw, "pgSz");
		WAttr(xw, "w", "11906");
		WAttr(xw, "h", "16838");
		xw.WriteEndElement();
		WEl(xw, "pgMar");
		WAttr(xw, "top", "2098");
		WAttr(xw, "right", "1474");
		WAttr(xw, "bottom", "1984");
		WAttr(xw, "left", "1588");
		WAttr(xw, "header", "851");
		WAttr(xw, "footer", "992");
		WAttr(xw, "gutter", "0");
		xw.WriteEndElement();
		xw.WriteEndElement();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteParagraph(XmlWriter xw, ParagraphBlock paragraph)
	{
		xw.WriteStartElement("w", "p", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
		bool flag = paragraph.SpaceBefore > 0.1f;
		bool flag2 = paragraph.Alignment != ParagraphAlignment.Left;
		bool flag3 = paragraph.FirstLineIndentChars > 0;
		if (flag || flag2 || flag3)
		{
			xw.WriteStartElement("w", "pPr", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
			if (flag)
			{
				WEl(xw, "spacing");
				WAttr(xw, "before", ((int)Math.Round(paragraph.SpaceBefore * 20f)).ToString(CultureInfo.InvariantCulture));
				WAttr(xw, "after", "0");
				xw.WriteEndElement();
			}
			if (flag2)
			{
				WEl(xw, "jc");
				string value = ((paragraph.Alignment == ParagraphAlignment.Center) ? "center" : "right");
				WAttr(xw, "val", value);
				xw.WriteEndElement();
			}
			if (flag3)
			{
				WEl(xw, "ind");
				WAttr(xw, "firstLineChars", (paragraph.FirstLineIndentChars * 100).ToString(CultureInfo.InvariantCulture));
				xw.WriteEndElement();
			}
			xw.WriteEndElement();
		}
		foreach (TextRun run in paragraph.Runs)
		{
			if (!string.IsNullOrEmpty(run.Text))
			{
				WriteRun(xw, run);
			}
		}
		xw.WriteEndElement();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteRun(XmlWriter xw, TextRun run)
	{
		xw.WriteStartElement("w", "r", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
		WriteRunProperties(xw, run);
		WEl(xw, "t");
		xw.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
		xw.WriteString(run.Text);
		xw.WriteEndElement();
		xw.WriteEndElement();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteRunProperties(XmlWriter xw, TextRun run)
	{
		bool num = IsCjkFontName(run.FontName);
		string value = (num ? "Times New Roman" : run.FontName);
		string value2 = (num ? run.FontName : "宋体");
		xw.WriteStartElement("w", "rPr", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
		WEl(xw, "rFonts");
		WAttr(xw, "ascii", value);
		WAttr(xw, "hAnsi", value);
		WAttr(xw, "eastAsia", value2);
		WAttr(xw, "cs", value);
		xw.WriteEndElement();
		if (run.Bold)
		{
			WEl(xw, "b");
			xw.WriteEndElement();
			WEl(xw, "bCs");
			xw.WriteEndElement();
		}
		if (run.Italic)
		{
			WEl(xw, "i");
			xw.WriteEndElement();
			WEl(xw, "iCs");
			xw.WriteEndElement();
		}
		int num2 = (int)Math.Round(run.FontSize * 2f);
		if (num2 < 8)
		{
			num2 = 8;
		}
		if (num2 > 144)
		{
			num2 = 144;
		}
		WEl(xw, "sz");
		WAttr(xw, "val", num2.ToString(CultureInfo.InvariantCulture));
		xw.WriteEndElement();
		WEl(xw, "szCs");
		WAttr(xw, "val", num2.ToString(CultureInfo.InvariantCulture));
		xw.WriteEndElement();
		xw.WriteEndElement();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsCjkFontName(string fontName)
	{
		if (!string.IsNullOrEmpty(fontName))
		{
			if (fontName.IndexOf("宋体", StringComparison.Ordinal) < 0 && fontName.IndexOf("黑体", StringComparison.Ordinal) < 0 && fontName.IndexOf("仿宋", StringComparison.Ordinal) < 0 && fontName.IndexOf("楷体", StringComparison.Ordinal) < 0 && fontName.IndexOf("微软雅黑", StringComparison.Ordinal) < 0)
			{
				return fontName.IndexOf("雅黑", StringComparison.Ordinal) >= 0;
			}
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteTable(XmlWriter xw, TableBlock table)
	{
		int[] array = ResolveColumnWidthsDxa(table);
		xw.WriteStartElement("w", "tbl", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
		xw.WriteStartElement("w", "tblPr", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
		WEl(xw, "tblW");
		if (array != null)
		{
			int num = 0;
			int[] array2 = array;
			foreach (int num2 in array2)
			{
				num += num2;
			}
			WAttr(xw, "w", num.ToString(CultureInfo.InvariantCulture));
			WAttr(xw, "type", "dxa");
		}
		else
		{
			WAttr(xw, "w", "0");
			WAttr(xw, "type", "auto");
		}
		xw.WriteEndElement();
		WEl(xw, "tblBorders");
		string[] array3 = new string[6] { "top", "left", "bottom", "right", "insideH", "insideV" };
		foreach (string text in array3)
		{
			WEl(xw, text);
			bool flag = table.BorderStyle == TableBorderStyle.FullGrid || (table.BorderStyle == TableBorderStyle.ThreeLine && (text == "top" || text == "bottom"));
			WAttr(xw, "val", flag ? "single" : "nil");
			if (flag)
			{
				WAttr(xw, "sz", "6");
				WAttr(xw, "color", "000000");
			}
			xw.WriteEndElement();
		}
		xw.WriteEndElement();
		WEl(xw, "tblLayout");
		WAttr(xw, "type", (array != null) ? "fixed" : "autofit");
		xw.WriteEndElement();
		xw.WriteEndElement();
		WEl(xw, "tblGrid");
		for (int j = 0; j < table.ColumnCount; j++)
		{
			WEl(xw, "gridCol");
			if (array != null)
			{
				WAttr(xw, "w", array[j].ToString(CultureInfo.InvariantCulture));
			}
			xw.WriteEndElement();
		}
		xw.WriteEndElement();
		foreach (TableRow row in table.Rows)
		{
			xw.WriteStartElement("w", "tr", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
			xw.WriteStartElement("w", "trPr", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
			WEl(xw, "cantSplit");
			xw.WriteEndElement();
			if (row.IsHeader)
			{
				WEl(xw, "tblHeader");
				xw.WriteEndElement();
			}
			xw.WriteEndElement();
			int num3 = 0;
			foreach (TableCell cell in row.Cells)
			{
				int num4 = ((cell.GridSpan <= 1) ? 1 : cell.GridSpan);
				xw.WriteStartElement("w", "tc", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
				xw.WriteStartElement("w", "tcPr", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
				WEl(xw, "tcW");
				if (array == null)
				{
					WAttr(xw, "w", "0");
					WAttr(xw, "type", "auto");
				}
				else
				{
					int num5 = 0;
					for (int k = 0; k < num4 && num3 + k < array.Length; k++)
					{
						num5 += array[num3 + k];
					}
					WAttr(xw, "w", num5.ToString(CultureInfo.InvariantCulture));
					WAttr(xw, "type", "dxa");
				}
				xw.WriteEndElement();
				if (num4 > 1)
				{
					WEl(xw, "gridSpan");
					WAttr(xw, "val", num4.ToString(CultureInfo.InvariantCulture));
					xw.WriteEndElement();
				}
				if (cell.VerticalMerge == VerticalMerge.Restart)
				{
					WEl(xw, "vMerge");
					WAttr(xw, "val", "restart");
					xw.WriteEndElement();
				}
				else if (cell.VerticalMerge == VerticalMerge.Continue)
				{
					WEl(xw, "vMerge");
					xw.WriteEndElement();
				}
				if (cell.VerticalAlign != CellVerticalAlignment.Center)
				{
					if (cell.VerticalAlign == CellVerticalAlignment.Bottom)
					{
						WEl(xw, "vAlign");
						WAttr(xw, "val", "bottom");
						xw.WriteEndElement();
					}
				}
				else
				{
					WEl(xw, "vAlign");
					WAttr(xw, "val", "center");
					xw.WriteEndElement();
				}
				if (table.BorderStyle == TableBorderStyle.ThreeLine && row.IsHeader)
				{
					WEl(xw, "tcBorders");
					WEl(xw, "bottom");
					WAttr(xw, "val", "single");
					WAttr(xw, "sz", "4");
					WAttr(xw, "color", "000000");
					xw.WriteEndElement();
					xw.WriteEndElement();
				}
				xw.WriteEndElement();
				WriteCellContent(xw, cell);
				xw.WriteEndElement();
				num3 += num4;
			}
			xw.WriteEndElement();
		}
		xw.WriteEndElement();
	}

	private static int[] ResolveColumnWidthsDxa(TableBlock table)
	{
		int columnCount = table.ColumnCount;
		if (columnCount > 0)
		{
			IList<float> columnWidths = table.ColumnWidths;
			if (columnWidths == null || columnWidths.Count < columnCount)
			{
				return null;
			}
			int[] array = new int[columnCount];
			long num = 0L;
			for (int i = 0; i < columnCount; i++)
			{
				if (columnWidths[i] <= 0f)
				{
					return null;
				}
				array[i] = Math.Max(1, (int)Math.Round(columnWidths[i] * 20f));
				num += array[i];
			}
			if (num > 8844)
			{
				long num2 = 0L;
				for (int j = 0; j < columnCount; j++)
				{
					array[j] = Math.Max(1, (int)((long)array[j] * 8844L / num));
					num2 += array[j];
				}
				int num3 = (int)(8844 - num2);
				array[columnCount - 1] = Math.Max(1, array[columnCount - 1] + num3);
			}
			return array;
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteCellContent(XmlWriter xw, TableCell cell)
	{
		xw.WriteStartElement("w", "p", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
		foreach (TextRun run2 in cell.Runs)
		{
			if (run2 == null || string.IsNullOrEmpty(run2.Text))
			{
				continue;
			}
			string[] array = run2.Text.Split(new string[3] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
			for (int i = 0; i < array.Length; i++)
			{
				if (i > 0)
				{
					xw.WriteEndElement();
					xw.WriteStartElement("w", "p", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
				}
				if (array[i].Length != 0)
				{
					TextRun run = new TextRun
					{
						Text = SanitizeXmlText(array[i]),
						FontName = run2.FontName,
						FontSize = run2.FontSize,
						Bold = run2.Bold,
						Italic = run2.Italic
					};
					WriteRun(xw, run);
				}
			}
		}
		xw.WriteEndElement();
	}

	private static string SanitizeXmlText(string text)
	{
		StringBuilder stringBuilder = null;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (c < ' ' && c != '\t')
			{
				if (stringBuilder == null)
				{
					stringBuilder = new StringBuilder(text.Length);
					stringBuilder.Append(text, 0, i);
				}
			}
			else
			{
				stringBuilder?.Append(c);
			}
		}
		if (stringBuilder != null)
		{
			return stringBuilder.ToString();
		}
		return text;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IList<ImagePartInfo> PrepareImages(ReconstructedDocument document)
	{
		List<ImagePartInfo> list = new List<ImagePartInfo>();
		if (document == null || document.Blocks == null)
		{
			return list;
		}
		for (int i = 0; i < document.Blocks.Count; i++)
		{
			ImageBlock imageBlock = document.Blocks[i] as ImageBlock;
			PdfImage pdfImage = imageBlock?.Image;
			byte[] array = ((pdfImage == null) ? null : (pdfImage.ResolvedBytes ?? pdfImage.Bytes));
			if (array == null || array.Length == 0)
			{
				continue;
			}
			string text;
			string contentType;
			if (IsJpeg(array))
			{
				text = ".jpeg";
				contentType = "image/jpeg";
			}
			else
			{
				if (!IsPng(array))
				{
					continue;
				}
				text = ".png";
				contentType = "image/png";
			}
			int index = list.Count + 1;
			list.Add(new ImagePartInfo
			{
				Block = imageBlock,
				Index = index,
				EntryName = "word/media/image" + index + text,
				ContentType = contentType,
				RelationshipId = "rIdImage" + index,
				Cx = (long)(pdfImage.Width * 12700f),
				Cy = (long)(pdfImage.Height * 12700f)
			});
		}
		return list;
	}

	private static void WriteImageParts(ZipArchive archive, IList<ImagePartInfo> images)
	{
		for (int i = 0; i < images.Count; i++)
		{
			ImagePartInfo imagePartInfo = images[i];
			using Stream stream = archive.CreateEntry(imagePartInfo.EntryName, CompressionLevel.Optimal).Open();
			PdfImage image = imagePartInfo.Block.Image;
			byte[] array = image.ResolvedBytes ?? image.Bytes;
			stream.Write(array, 0, array.Length);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteImage(XmlWriter xw, ImagePartInfo image)
	{
		xw.WriteStartElement("w", "p", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
		xw.WriteStartElement("w", "r", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
		xw.WriteStartElement("w", "drawing", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
		xw.WriteStartElement("wp", "inline", "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing");
		xw.WriteAttributeString("distT", "0");
		xw.WriteAttributeString("distB", "0");
		xw.WriteAttributeString("distL", "0");
		xw.WriteAttributeString("distR", "0");
		xw.WriteStartElement("wp", "extent", "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing");
		xw.WriteAttributeString("cx", image.Cx.ToString(CultureInfo.InvariantCulture));
		xw.WriteAttributeString("cy", image.Cy.ToString(CultureInfo.InvariantCulture));
		xw.WriteEndElement();
		xw.WriteStartElement("wp", "docPr", "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing");
		xw.WriteAttributeString("id", image.Index.ToString(CultureInfo.InvariantCulture));
		xw.WriteAttributeString("name", "Picture " + image.Index);
		xw.WriteEndElement();
		xw.WriteStartElement("a", "graphic", "http://schemas.openxmlformats.org/drawingml/2006/main");
		xw.WriteStartElement("a", "graphicData", "http://schemas.openxmlformats.org/drawingml/2006/main");
		xw.WriteAttributeString("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture");
		xw.WriteStartElement("pic", "pic", "http://schemas.openxmlformats.org/drawingml/2006/picture");
		xw.WriteStartElement("pic", "nvPicPr", "http://schemas.openxmlformats.org/drawingml/2006/picture");
		xw.WriteStartElement("pic", "cNvPr", "http://schemas.openxmlformats.org/drawingml/2006/picture");
		xw.WriteAttributeString("id", image.Index.ToString(CultureInfo.InvariantCulture));
		xw.WriteAttributeString("name", "Image " + image.Index);
		xw.WriteEndElement();
		xw.WriteStartElement("pic", "cNvPicPr", "http://schemas.openxmlformats.org/drawingml/2006/picture");
		xw.WriteEndElement();
		xw.WriteEndElement();
		xw.WriteStartElement("pic", "blipFill", "http://schemas.openxmlformats.org/drawingml/2006/picture");
		xw.WriteStartElement("a", "blip", "http://schemas.openxmlformats.org/drawingml/2006/main");
		xw.WriteAttributeString("r", "embed", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", image.RelationshipId);
		xw.WriteEndElement();
		xw.WriteStartElement("a", "stretch", "http://schemas.openxmlformats.org/drawingml/2006/main");
		xw.WriteStartElement("a", "fillRect", "http://schemas.openxmlformats.org/drawingml/2006/main");
		xw.WriteEndElement();
		xw.WriteEndElement();
		xw.WriteEndElement();
		xw.WriteStartElement("pic", "spPr", "http://schemas.openxmlformats.org/drawingml/2006/picture");
		xw.WriteStartElement("a", "xfrm", "http://schemas.openxmlformats.org/drawingml/2006/main");
		xw.WriteStartElement("a", "off", "http://schemas.openxmlformats.org/drawingml/2006/main");
		xw.WriteAttributeString("x", "0");
		xw.WriteAttributeString("y", "0");
		xw.WriteEndElement();
		xw.WriteStartElement("a", "ext", "http://schemas.openxmlformats.org/drawingml/2006/main");
		xw.WriteAttributeString("cx", image.Cx.ToString(CultureInfo.InvariantCulture));
		xw.WriteAttributeString("cy", image.Cy.ToString(CultureInfo.InvariantCulture));
		xw.WriteEndElement();
		xw.WriteEndElement();
		xw.WriteStartElement("a", "prstGeom", "http://schemas.openxmlformats.org/drawingml/2006/main");
		xw.WriteAttributeString("prst", "rect");
		xw.WriteStartElement("a", "avLst", "http://schemas.openxmlformats.org/drawingml/2006/main");
		xw.WriteEndElement();
		xw.WriteEndElement();
		xw.WriteEndElement();
		xw.WriteEndElement();
		xw.WriteEndElement();
		xw.WriteEndElement();
		xw.WriteEndElement();
		xw.WriteEndElement();
		xw.WriteEndElement();
		xw.WriteEndElement();
	}

	private static bool IsJpeg(byte[] bytes)
	{
		if (bytes.Length >= 3 && bytes[0] == byte.MaxValue && bytes[1] == 216)
		{
			return bytes[2] == byte.MaxValue;
		}
		return false;
	}

	private static bool IsPng(byte[] bytes)
	{
		if (bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26)
		{
			return bytes[7] == 10;
		}
		return false;
	}

	private static XmlWriter CreateWriter(ZipArchive archive, string entryName)
	{
		return XmlWriter.Create(archive.CreateEntry(entryName, CompressionLevel.Optimal).Open(), new XmlWriterSettings
		{
			Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			Indent = false,
			CloseOutput = true
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteContentTypeDefault(XmlWriter xw, string extension, string contentType)
	{
		xw.WriteStartElement("Default", "http://schemas.openxmlformats.org/package/2006/content-types");
		xw.WriteAttributeString("Extension", extension);
		xw.WriteAttributeString("ContentType", contentType);
		xw.WriteEndElement();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteContentTypeOverride(XmlWriter xw, string partName, string contentType)
	{
		xw.WriteStartElement("Override", "http://schemas.openxmlformats.org/package/2006/content-types");
		xw.WriteAttributeString("PartName", partName);
		xw.WriteAttributeString("ContentType", contentType);
		xw.WriteEndElement();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteRelationship(XmlWriter xw, string id, string type, string target)
	{
		xw.WriteStartElement("Relationship", "http://schemas.openxmlformats.org/package/2006/relationships");
		xw.WriteAttributeString("Id", id);
		xw.WriteAttributeString("Type", type);
		xw.WriteAttributeString("Target", target);
		xw.WriteEndElement();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WEl(XmlWriter xw, string localName)
	{
		xw.WriteStartElement("w", localName, "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WAttr(XmlWriter xw, string name, string value)
	{
		xw.WriteAttributeString("w", name, "http://schemas.openxmlformats.org/wordprocessingml/2006/main", value);
	}
}
