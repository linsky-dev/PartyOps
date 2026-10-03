using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;

namespace DocumentRepository.Services.FileSafety;

public static class OutputFileIntegrityValidator
{
	private static readonly byte[] OleSignature = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

	public static void Validate(string path)
	{
		Validate(path, Path.GetExtension(path));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Validate(string path, string expectedExtension)
	{
		EnsureNonEmpty(path);
		string text = (expectedExtension ?? string.Empty).Trim().ToLowerInvariant();
		if (text == null)
		{
			return;
		}
		int length = text.Length;
		if (length != 4)
		{
			if (length != 5)
			{
				return;
			}
			switch (text[1])
			{
			case 'd':
				if (text == ".docx")
				{
					ValidateDocx(path);
				}
				return;
			case 't':
				if (!(text == ".tiff"))
				{
					return;
				}
				break;
			case 'j':
				if (!(text == ".jpeg"))
				{
					return;
				}
				break;
			default:
				return;
			}
		}
		else
		{
			switch (text[2])
			{
			case 'd':
				if (text == ".pdf")
				{
					ValidatePdf(path);
				}
				return;
			case 'i':
				if (text == ".tif")
				{
					break;
				}
				return;
			case 'm':
				if (!(text == ".bmp"))
				{
					return;
				}
				break;
			case 'x':
				_ = text == ".txt";
				return;
			case 'p':
				if (!(text == ".xps"))
				{
					if (text == ".jpg")
					{
						break;
					}
					_ = text == ".wps";
					return;
				}
				ValidateXps(path);
				return;
			case 'n':
				if (!(text == ".png"))
				{
					return;
				}
				break;
			case 'o':
				if (text == ".doc")
				{
					ValidateOle(path, "DOC");
				}
				return;
			default:
				return;
			}
		}
		ValidateImage(path);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureNonEmpty(string path)
	{
		if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
		{
			if (new FileInfo(path).Length > 0)
			{
				return;
			}
			throw new InvalidDataException("输出文件为空。" + path);
		}
		throw new InvalidDataException("输出文件未生成。" + path);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateOpenXmlPart(string path, string partUri, string label)
	{
		try
		{
			Package val = Package.Open(path, FileMode.Open, FileAccess.Read);
			try
			{
				if (!val.PartExists(new Uri(partUri, UriKind.Relative)))
				{
					throw new InvalidDataException(label + " 缺少必需文档部件：" + partUri);
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (Exception innerException)
		{
			throw new InvalidDataException(label + " 文件结构无效。", innerException);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateDocx(string path)
	{
		try
		{
			Package val = Package.Open(path, FileMode.Open, FileAccess.Read);
			try
			{
				Uri uri = new Uri("/word/document.xml", UriKind.Relative);
				if (!val.PartExists(uri))
				{
					throw new InvalidDataException("DOCX 缺少必需文档部件：/word/document.xml");
				}
				PackagePart part = val.GetPart(uri);
				XmlDocument val2 = new XmlDocument
				{
					PreserveWhitespace = true
				};
				using (Stream stream = part.GetStream(FileMode.Open, FileAccess.Read))
				{
					val2.Load(stream);
				}
				XmlNamespaceManager val3 = new XmlNamespaceManager(val2.NameTable);
				val3.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
				val3.AddNamespace("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
				val3.AddNamespace("pic", "http://schemas.openxmlformats.org/drawingml/2006/picture");
				val3.AddNamespace("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
				XmlNodeList val4 = ((XmlNode)val2).SelectNodes("//w:drawing", val3);
				if (val4 == null)
				{
					return;
				}
				foreach (XmlNode item in val4)
				{
					XmlNode obj = item.SelectSingleNode(".//a:graphic/a:graphicData", val3);
					XmlElement val5 = (XmlElement)(object)((obj is XmlElement) ? obj : null);
					if (val5 == null)
					{
						throw new InvalidDataException("DOCX 图片或图形结构无效。");
					}
					if (string.Equals(val5.GetAttribute("uri"), "http://schemas.openxmlformats.org/drawingml/2006/picture", StringComparison.Ordinal))
					{
						XmlNode val6 = ((XmlNode)val5).SelectSingleNode("./pic:pic", val3);
						XmlNode obj2 = ((XmlNode)val5).SelectSingleNode(".//a:blip", val3);
						XmlElement val7 = (XmlElement)(object)((obj2 is XmlElement) ? obj2 : null);
						string text = ((val7 == null) ? string.Empty : val7.GetAttribute("embed", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"));
						if (val6 == null || string.IsNullOrWhiteSpace(text))
						{
							throw new InvalidDataException("DOCX 图片引用结构无效。");
						}
						PackageRelationship relationship;
						try
						{
							relationship = part.GetRelationship(text);
						}
						catch (Exception innerException)
						{
							throw new InvalidDataException("DOCX 图片引用不存在。", innerException);
						}
						Uri uri2 = PackUriHelper.ResolvePartUri(part.Uri, relationship.TargetUri);
						if (!val.PartExists(uri2))
						{
							throw new InvalidDataException("DOCX 图片部件不存在。");
						}
					}
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (Exception innerException2)
		{
			throw new InvalidDataException("DOCX 文件结构无效。", innerException2);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateXps(string path)
	{
		try
		{
			Package val = Package.Open(path, FileMode.Open, FileAccess.Read);
			try
			{
				if (!((IEnumerable<PackagePart>)val.GetParts()).Any([MethodImpl(MethodImplOptions.NoInlining)] (PackagePart part) => part.Uri.OriginalString.EndsWith(".fdseq", StringComparison.OrdinalIgnoreCase)))
				{
					throw new InvalidDataException("XPS 缺少固定文档序列。");
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (Exception innerException)
		{
			throw new InvalidDataException("XPS 文件结构无效。", innerException);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidatePdf(string path)
	{
		byte[] array = new byte[5];
		byte[] array2;
		using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			if (fileStream.Read(array, 0, array.Length) != array.Length || Encoding.ASCII.GetString(array) != "%PDF-")
			{
				throw new InvalidDataException("PDF 文件头无效。");
			}
			int num = (int)Math.Min(2048L, fileStream.Length);
			array2 = new byte[num];
			fileStream.Position = fileStream.Length - num;
			fileStream.Read(array2, 0, array2.Length);
		}
		try
		{
			if (Encoding.ASCII.GetString(array2).IndexOf("%%EOF", StringComparison.Ordinal) < 0)
			{
				throw new InvalidDataException("PDF 文件尾不完整。");
			}
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
			Array.Clear(array2, 0, array2.Length);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateImage(string path)
	{
		try
		{
			Image val = Image.FromFile(path);
			try
			{
				if (val.Width > 0 && val.Height > 0)
				{
					return;
				}
				throw new InvalidDataException("图片尺寸无效。");
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (Exception innerException)
		{
			throw new InvalidDataException("图片文件结构无效。", innerException);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateOle(string path, string label)
	{
		byte[] array = new byte[OleSignature.Length];
		using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			if (fileStream.Read(array, 0, array.Length) != array.Length || !array.SequenceEqual(OleSignature))
			{
				throw new InvalidDataException(label + " 文件结构无效。");
			}
		}
		Array.Clear(array, 0, array.Length);
	}
}
