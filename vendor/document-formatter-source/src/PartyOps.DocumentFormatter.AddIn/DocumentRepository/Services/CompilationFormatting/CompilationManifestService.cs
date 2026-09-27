using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationManifestService
{
	public const int CurrentSchemaVersion = 2;

	private const string PropertyVersion = "SXCF_MANIFEST_VERSION";

	private const string PropertyPayload = "SXCF_MANIFEST";

	public const string TocBeginBookmark = "SXCF_TOC_BEGIN";

	public const string TocEndBookmark = "SXCF_TOC_END";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Write(Document document, CompilationManifest manifest)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (manifest == null)
		{
			throw new ArgumentNullException("manifest");
		}
		SetStringProperty(document, "SXCF_MANIFEST_VERSION", manifest.SchemaVersion.ToString());
		JavaScriptSerializer val = new JavaScriptSerializer();
		val.MaxJsonLength = int.MaxValue;
		SetStringProperty(document, "SXCF_MANIFEST", val.Serialize((object)manifest));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CompilationManifest Read(Document document)
	{
		if (document != null)
		{
			string stringProperty = GetStringProperty(document, "SXCF_MANIFEST");
			if (string.IsNullOrWhiteSpace(stringProperty))
			{
				return null;
			}
			try
			{
				CompilationManifest compilationManifest = new JavaScriptSerializer
				{
					MaxJsonLength = int.MaxValue
				}.Deserialize<CompilationManifest>(stringProperty);
				if (compilationManifest == null)
				{
					return null;
				}
				if (compilationManifest.SchemaVersion <= 2 && compilationManifest.SchemaVersion >= 1)
				{
					if (compilationManifest.SchemaVersion == 1)
					{
						compilationManifest.Separators = new List<CompilationSeparatorRecord>();
						compilationManifest.SchemaVersion = 2;
					}
					return compilationManifest;
				}
				return null;
			}
			catch
			{
				return null;
			}
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool Exists(Document document)
	{
		return GetStringProperty(document, "SXCF_MANIFEST") != null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool ValidateManifestStructure(CompilationManifest manifest, out string error)
	{
		error = null;
		if (manifest == null)
		{
			error = "清单为空。";
			return false;
		}
		if (manifest.SchemaVersion != 2)
		{
			error = "清单版本 " + manifest.SchemaVersion + " 不受支持（当前支持版本 " + 2 + "）。";
			return false;
		}
		if (manifest.Articles == null || manifest.Articles.Count == 0)
		{
			error = "清单不含任何文章。";
			return false;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		int num = 0;
		while (true)
		{
			if (num < manifest.Articles.Count)
			{
				CompilationArticleInfo compilationArticleInfo = manifest.Articles[num];
				if (compilationArticleInfo == null)
				{
					break;
				}
				int num2 = num + 1;
				if (compilationArticleInfo.OrderIndex != num2)
				{
					error = "文章顺序不连续或不唯一：第 " + (num + 1) + " 项 OrderIndex=" + compilationArticleInfo.OrderIndex + "，应为 " + num2 + "。";
					return false;
				}
				string b = "SXCF_ART_" + num2.ToString("000") + "_BEGIN";
				string b2 = "SXCF_ART_" + num2.ToString("000") + "_END";
				if (!string.Equals(compilationArticleInfo.BeginBookmarkName, b, StringComparison.Ordinal) || !string.Equals(compilationArticleInfo.EndBookmarkName, b2, StringComparison.Ordinal))
				{
					error = "第 " + num2 + " 篇书签名称不符合预期命名。";
					return false;
				}
				if (!hashSet.Add(compilationArticleInfo.BeginBookmarkName) || !hashSet.Add(compilationArticleInfo.EndBookmarkName))
				{
					error = "第 " + num2 + " 篇书签名称重复。";
					return false;
				}
				num++;
				continue;
			}
			if (manifest.Separators != null)
			{
				HashSet<int> hashSet2 = new HashSet<int>();
				foreach (CompilationSeparatorRecord separator in manifest.Separators)
				{
					if (separator == null)
					{
						error = "分隔符记录包含空项。";
						return false;
					}
					if (separator.OrderIndex >= 1 && separator.OrderIndex <= manifest.Articles.Count)
					{
						if (hashSet2.Add(separator.OrderIndex))
						{
							if (separator.Kind != "page" && separator.Kind != "section")
							{
								error = "分隔符记录的类型不受支持：" + (separator.Kind ?? "(null)") + "。";
								return false;
							}
							string b3 = "SXCF_SEP_" + separator.OrderIndex.ToString("000");
							if (string.Equals(separator.AnchorBookmark, b3, StringComparison.Ordinal))
							{
								continue;
							}
							error = "分隔符记录的所有权锚点不合规：第 " + separator.OrderIndex + " 篇。";
							return false;
						}
						error = "分隔符记录的文章序号重复：" + separator.OrderIndex + "。";
						return false;
					}
					error = "分隔符记录的文章序号非法：" + separator.OrderIndex + "。";
					return false;
				}
			}
			if (!string.IsNullOrWhiteSpace(manifest.TocOptionsSnapshotJson))
			{
				try
				{
					CompilationTocOptions compilationTocOptions = new JavaScriptSerializer
					{
						MaxJsonLength = int.MaxValue
					}.Deserialize<CompilationTocOptions>(manifest.TocOptionsSnapshotJson);
					if (compilationTocOptions == null)
					{
						error = "目录参数快照解析结果为空。";
						return false;
					}
					CompilationTocOptionsValidator.Validate(compilationTocOptions, "清单目录快照");
				}
				catch (Exception ex)
				{
					LogService.Warn("CompilationManifestService.ValidateManifestStructure.TocSnapshot", ex);
					error = "目录参数快照无法解析或已损坏。";
					return false;
				}
			}
			return true;
		}
		error = "清单第 " + (num + 1) + " 项为空。";
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static void SetStringProperty(Document document, string name, string value)
	{
		dynamic customDocumentProperties = document.CustomDocumentProperties;
		try
		{
			int num = 0;
			try
			{
				num = customDocumentProperties.Count;
			}
			catch
			{
			}
			for (int i = 1; i <= num; i++)
			{
				string a = "";
				dynamic val = null;
				try
				{
					val = customDocumentProperties.Item(i);
					a = Convert.ToString(val.Name);
				}
				catch
				{
				}
				if (string.Equals(a, name, StringComparison.Ordinal))
				{
					try
					{
						val.Delete();
					}
					catch
					{
					}
					break;
				}
			}
		}
		catch
		{
		}
		customDocumentProperties.Add(name, false, 4, value, Type.Missing);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static string GetStringProperty(Document document, string name)
	{
		try
		{
			dynamic customDocumentProperties = document.CustomDocumentProperties;
			int num = 0;
			try
			{
				num = customDocumentProperties.Count;
			}
			catch
			{
			}
			for (int i = 1; i <= num; i++)
			{
				dynamic val = customDocumentProperties.Item(i);
				if (string.Equals(Convert.ToString(val.Name), name, StringComparison.Ordinal))
				{
					return ((object)val.Value)?.ToString();
				}
			}
			return null;
		}
		catch
		{
			return null;
		}
	}
}
