using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Formatting.PageNumbers;
using DocumentRepository.Services.Formatting.Planning;
using DocumentRepository.Services.Formatting.Signatures;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Performance;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class DocumentStyleManager
{
	public static class StyleNames
	{
		public const string MainTitle = "OfficialDoc.MainTitle";

		public const string Level1Title = "OfficialDoc.Level1Title";

		public const string Level2Title = "OfficialDoc.Level2Title";

		public const string Level3Title = "OfficialDoc.Level3Title";

		public const string Body = "OfficialDoc.Body";

		public const string SubTitle = "OfficialDoc.SubTitle";

		public const string PageNumber = "OfficialDoc.PageNumber";

		public const string AttachmentMarker = "OfficialDoc.AttachmentMarker";

		public const string AttachmentTitle = "OfficialDoc.AttachmentTitle";

		public const string AttachmentSubTitle = "OfficialDoc.AttachmentSubTitle";

		public const string AttachmentListFirst = "OfficialDoc.AttachmentList.First";

		public const string AttachmentListSingle = "OfficialDoc.AttachmentList.Single";

		public const string AttachmentListContinuation = "OfficialDoc.AttachmentList.Continuation";

		public const string Salutation = "OfficialDoc.Salutation";

		public const string Signature = "OfficialDoc.Signature";

		public const string SignatureDate = "OfficialDoc.SignatureDate";
	}

	private sealed class StyleDef
	{
		public string FontName;

		public string EnglishFontName;

		public float FontSize;

		public bool Bold;

		public string Alignment;

		public float LineSpacing;

		public float FirstLineIndentChars;

		public float? LeftIndentPoints;

		public float? FirstLineIndentPoints;

		public float SpaceBefore;

		public float SpaceAfter;

		public float LineUnitBefore;

		public float LineUnitAfter;

		public bool AlignToDocumentGrid;

		public WdOutlineLevel OutlineLevel;
	}

	private sealed class DocumentStyleCacheEntry
	{
		public string ConfigHash { get; }

		public Dictionary<string, StyleDef> Definitions { get; }

		[MethodImpl(MethodImplOptions.NoInlining)]
		public DocumentStyleCacheEntry(string configHash, Dictionary<string, StyleDef> definitions)
		{
			ConfigHash = configHash;
			Definitions = definitions ?? throw new ArgumentNullException("definitions");
		}
	}

	private sealed class NamedStyleDef
	{
		public string Name;

		public StyleDef Def;
	}

	private static readonly Dictionary<string, string> LegacyParagraphStyleNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		{ "SX公文主标题", "OfficialDoc.MainTitle" },
		{ "SX公文一级标题", "OfficialDoc.Level1Title" },
		{ "SX公文二级标题", "OfficialDoc.Level2Title" },
		{ "SX公文三级标题", "OfficialDoc.Level3Title" },
		{ "SX公文正文", "OfficialDoc.Body" },
		{ "SX公文副标题", "OfficialDoc.SubTitle" },
		{ "SX公文页码", "OfficialDoc.PageNumber" },
		{ "SX公文附件标记", "OfficialDoc.AttachmentMarker" },
		{ "SX公文附件标题", "OfficialDoc.AttachmentTitle" },
		{ "SX公文附件副标题", "OfficialDoc.AttachmentSubTitle" },
		{ "SX公文附件说明首行", "OfficialDoc.AttachmentList.First" },
		{ "SX公文附件说明单行", "OfficialDoc.AttachmentList.Single" },
		{ "SX公文附件说明续行", "OfficialDoc.AttachmentList.Continuation" },
		{ "SX公文称谓", "OfficialDoc.Salutation" },
		{ "SX公文落款", "OfficialDoc.Signature" },
		{ "SX公文落款日期", "OfficialDoc.SignatureDate" }
	};

	private static readonly Dictionary<ElementType, string> ElementToStyleName = new Dictionary<ElementType, string>
	{
		{
			ElementType.MainTitle,
			"OfficialDoc.MainTitle"
		},
		{
			ElementType.Level1Title,
			"OfficialDoc.Level1Title"
		},
		{
			ElementType.Level2Title,
			"OfficialDoc.Level2Title"
		},
		{
			ElementType.Level3Title,
			"OfficialDoc.Level3Title"
		},
		{
			ElementType.Body,
			"OfficialDoc.Body"
		},
		{
			ElementType.SubTitle,
			"OfficialDoc.SubTitle"
		},
		{
			ElementType.Signature,
			"OfficialDoc.Signature"
		},
		{
			ElementType.SignatureDate,
			"OfficialDoc.SignatureDate"
		},
		{
			ElementType.AttachmentMarker,
			"OfficialDoc.AttachmentMarker"
		},
		{
			ElementType.AttachmentTitle,
			"OfficialDoc.AttachmentTitle"
		},
		{
			ElementType.AttachmentSubTitle,
			"OfficialDoc.AttachmentSubTitle"
		},
		{
			ElementType.AttachmentListFirst,
			"OfficialDoc.AttachmentList.First"
		},
		{
			ElementType.AttachmentListSingle,
			"OfficialDoc.AttachmentList.Single"
		},
		{
			ElementType.AttachmentListContinuation,
			"OfficialDoc.AttachmentList.Continuation"
		},
		{
			ElementType.Salutation,
			"OfficialDoc.Salutation"
		}
	};

	private static readonly Dictionary<string, DocumentStyleCacheEntry> DocumentCaches = new Dictionary<string, DocumentStyleCacheEntry>(StringComparer.Ordinal);

	private static readonly object CacheLock = new object();

	private const string StyleNamespaceVersion = "OfficialDoc-2";

	public static int CachedDocumentCount
	{
		get
		{
			lock (CacheLock)
			{
				return DocumentCaches.Count;
			}
		}
	}

	public static bool EnsureStyles(Document doc, FormatConfig cfg, StyleRefreshMode mode = StyleRefreshMode.Force)
	{
		return EnsureStyles(doc, cfg, mode, null, includePageNumber: true);
	}

	public static bool EnsureStyles(Document doc, FormatConfig cfg, StyleRefreshMode mode, ISet<ElementType> requiredElementTypes, bool includePageNumber)
	{
		return EnsureStylesScoped(doc, cfg, mode, requiredElementTypes, includePageNumber, skipLegacyMigration: false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static bool EnsureStylesScoped(Document doc, FormatConfig cfg, StyleRefreshMode mode, ISet<ElementType> requiredElementTypes, bool includePageNumber, bool skipLegacyMigration, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (cfg != null)
		{
			if (!skipLegacyMigration)
			{
				long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
				MigrateLegacyParagraphStyleNames(doc);
				diagnostics?.Accumulate("style-ensure.legacy-migration", startTimestamp, LegacyParagraphStyleNames.Count);
			}
			string text = ComputeConfigHash(cfg);
			string orCreate = DocumentLifecycleRegistry.GetOrCreate(doc);
			List<NamedStyleDef> definitions = BuildDefinitions(cfg);
			List<NamedStyleDef> list = SelectRequiredDefinitions(definitions, requiredElementTypes, includePageNumber);
			long startTimestamp2 = FirstFormatDiagnosticsSession.Timestamp();
			if (mode == StyleRefreshMode.IfChanged)
			{
				lock (CacheLock)
				{
					if (DocumentCaches.TryGetValue(orCreate, out var value) && value.ConfigHash == text && AllStylesExist(doc, list))
					{
						diagnostics?.Accumulate("style-ensure.query", startTimestamp2, list.Count);
						return false;
					}
				}
				if (AllStylesMatch(doc, list))
				{
					StoreDocumentCache(orCreate, text, definitions);
					diagnostics?.Accumulate("style-ensure.query", startTimestamp2, list.Count);
					return false;
				}
			}
			diagnostics?.Accumulate("style-ensure.query", startTimestamp2, list.Count);
			if (mode == StyleRefreshMode.CreateMissingOnly)
			{
				bool result = false;
				foreach (NamedStyleDef item in list)
				{
					Style value2 = FindStyle(doc, item.Name);
					if (value2 == null)
					{
						CreateStyle(doc, item.Name, item.Def, diagnostics);
						result = true;
					}
					else
					{
						HideFromStylePane(value2, item.Name);
						ComObjectRelease.Release(ref value2, "DocumentStyleManager.style");
					}
				}
				StoreDocumentCache(orCreate, text, definitions);
				return result;
			}
			foreach (NamedStyleDef item2 in list)
			{
				SynchronizeStyle(doc, item2.Name, item2.Def, diagnostics);
			}
			StoreDocumentCache(orCreate, text, definitions);
			return true;
		}
		throw new ArgumentNullException("cfg");
	}

	private static void MigrateLegacyParagraphStyleNames(Document document)
	{
		foreach (KeyValuePair<string, string> legacyParagraphStyleName in LegacyParagraphStyleNames)
		{
			MigrateLegacyStyleName(document, legacyParagraphStyleName.Key, legacyParagraphStyleName.Value);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void MigrateLegacyStyleName(Document document, string legacyName, string currentName)
	{
		Style value = null;
		Style value2 = null;
		try
		{
			value = FindStyle(document, legacyName);
			if (value == null)
			{
				return;
			}
			value2 = FindStyle(document, currentName);
			string text = ((value2 == null) ? currentName : FindAvailableLegacyStyleName(document, currentName + ".Legacy"));
			try
			{
				value.NameLocal = text;
				HideFromStylePane(value, text);
				LogService.Info("旧版插件样式已迁移为英文名称：" + legacyName + " -> " + text);
			}
			catch (COMException ex)
			{
				HideFromStylePane(value, legacyName);
				LogService.Warn("旧版插件样式英文改名失败，已继续隐藏：" + legacyName, ex);
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "DocumentStyleManager.MigrateLegacy.CurrentStyle");
			ComObjectRelease.Release(ref value, "DocumentStyleManager.MigrateLegacy.LegacyStyle");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string FindAvailableLegacyStyleName(Document document, string baseName)
	{
		for (int i = 0; i < 100; i++)
		{
			string text = ((i == 0) ? baseName : (baseName + "." + (i + 1)));
			Style value = null;
			try
			{
				value = FindStyle(document, text);
				if (value == null)
				{
					return text;
				}
			}
			finally
			{
				ComObjectRelease.Release(ref value, "DocumentStyleManager.FindLegacyStyle.Existing");
			}
		}
		throw new InvalidOperationException("无法为旧版插件样式生成可用的英文名称：" + baseName);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void StoreDocumentCache(string lifecycleId, string configHash, List<NamedStyleDef> definitions)
	{
		if (!string.IsNullOrWhiteSpace(lifecycleId))
		{
			if (!string.IsNullOrWhiteSpace(configHash))
			{
				if (definitions == null)
				{
					throw new ArgumentNullException("definitions");
				}
				Dictionary<string, StyleDef> dictionary = new Dictionary<string, StyleDef>(StringComparer.Ordinal);
				foreach (NamedStyleDef definition in definitions)
				{
					dictionary.Add(definition.Name, definition.Def);
				}
				lock (CacheLock)
				{
					DocumentCaches[lifecycleId] = new DocumentStyleCacheEntry(configHash, dictionary);
					return;
				}
			}
			throw new ArgumentException("样式配置哈希不能为空。", "configHash");
		}
		throw new ArgumentException("文档生命周期编号不能为空。", "lifecycleId");
	}

	public static void InvalidateCache(Document doc = null)
	{
		lock (CacheLock)
		{
			if (doc != null)
			{
				if (DocumentLifecycleRegistry.TryGet(doc, out var lifecycleId))
				{
					DocumentCaches.Remove(lifecycleId);
				}
			}
			else
			{
				DocumentCaches.Clear();
			}
		}
	}

	public static void ReleaseDocument(Document doc)
	{
		if (doc != null)
		{
			DocumentLifecycleRegistry.TryGet(doc, out var lifecycleId);
			InvalidateCache(doc);
			FormatIdempotencyStampService.Release(lifecycleId);
			SignatureWidthMeasureCache.Release(lifecycleId);
			DocumentLifecycleRegistry.Release(doc);
		}
	}

	public static void ResetAllCaches()
	{
		InvalidateCache();
		FormatIdempotencyStampService.Clear();
		SignatureWidthMeasureCache.Clear();
		DocumentLifecycleRegistry.Clear();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string GetStyleName(ElementType type)
	{
		if (!ElementToStyleName.TryGetValue(type, out var value))
		{
			throw new InvalidOperationException("未登记的可排版元素类型：" + type);
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyStyle(Paragraph para, ElementType type)
	{
		if (para == null)
		{
			throw new ArgumentNullException("para");
		}
		string styleName = GetStyleName(type);
		object styleName2 = styleName;
		Microsoft.Office.Interop.Word.Range value = null;
		Document value2 = null;
		try
		{
			value = para.Range;
			value2 = value.Document;
			ApplyStyleCore(value, styleName2, GetRegisteredStyleDef(value2, styleName), clearDirectFormatting: true);
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "DocumentStyleManager.ApplyStyle.Paragraph.Document");
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "DocumentStyleManager.range");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyStyle(Microsoft.Office.Interop.Word.Range range, ElementType type)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		string styleName = GetStyleName(type);
		object styleName2 = styleName;
		Document value = null;
		try
		{
			value = range.Document;
			ApplyStyleCore(range, styleName2, GetRegisteredStyleDef(value, styleName), clearDirectFormatting: true);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "DocumentStyleManager.ApplyStyle.Range.Document");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void PrepareDirectFormatting(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		ClearDirectFormattingBeforeStyle(range);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyPreparedStyle(Paragraph para, ElementType type, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (para == null)
		{
			throw new ArgumentNullException("para");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = para.Range;
			ApplyPreparedStyle(value, type, diagnostics);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "DocumentStyleManager.ApplyPreparedStyle.ParagraphRange");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyPreparedStyle(Microsoft.Office.Interop.Word.Range range, ElementType type, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		string styleName = GetStyleName(type);
		object styleName2 = styleName;
		Document value = null;
		try
		{
			value = range.Document;
			ApplyStyleCore(range, styleName2, GetRegisteredStyleDef(value, styleName), clearDirectFormatting: false, diagnostics);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "DocumentStyleManager.ApplyPreparedStyle.Document");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyStyleCore(Microsoft.Office.Interop.Word.Range range, object styleName, StyleDef definition, bool clearDirectFormatting, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (clearDirectFormatting)
		{
			ClearDirectFormattingBeforeStyle(range);
		}
		long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
		range.set_Style(ref styleName);
		diagnostics?.Accumulate("apply.parastyle-set", startTimestamp);
		startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
		MaterializeParagraphSpacing(range, definition);
		diagnostics?.Accumulate("apply.spacing-materialize", startTimestamp);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyPageNumberStyle(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		object prop = "OfficialDoc.PageNumber";
		Document value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		Font value3 = null;
		try
		{
			value = range.Document;
			StyleDef registeredStyleDef = GetRegisteredStyleDef(value, "OfficialDoc.PageNumber");
			range.set_Style(ref prop);
			MaterializeParagraphSpacing(range, registeredStyleDef);
			value2 = range.Duplicate;
			TrimParagraphEnd(value2);
			if (value2.End > value2.Start)
			{
				value3 = value2.Font;
				ApplyFontFormat(value3, registeredStyleDef, "OfficialDoc.PageNumber");
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value3, "DocumentStyleManager.ApplyPageNumberStyle.Font");
			ComObjectRelease.Release(ref value2, "DocumentStyleManager.ApplyPageNumberStyle.TextRange");
			ComObjectRelease.Release(ref value, "DocumentStyleManager.ApplyPageNumberStyle.Document");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsParagraphStyle(Paragraph para, ElementType type)
	{
		if (para == null)
		{
			throw new ArgumentNullException("para");
		}
		return string.Equals(GetParagraphStructuralStyleName(para), GetStyleName(type), StringComparison.OrdinalIgnoreCase);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsParagraphStyle(Microsoft.Office.Interop.Word.Range range, ElementType type)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		Paragraph value = null;
		try
		{
			value = range.Paragraphs[1];
			return IsParagraphStyle(value, type);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "DocumentStyleManager.IsParagraphStyle.Range");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static bool IsParagraphFormatCompliant(Microsoft.Office.Interop.Word.Range range, Document document, ElementType type, float? spaceBeforeOverride, float? spaceAfterOverride)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		ParagraphFormat value = null;
		try
		{
			StyleDef registeredStyleDef = GetRegisteredStyleDef(document, GetStyleName(type));
			value = range.ParagraphFormat;
			if (value.Alignment == ToWdAlignment(registeredStyleDef.Alignment))
			{
				if (value.OutlineLevel == registeredStyleDef.OutlineLevel)
				{
					if (value.LineSpacingRule != WdLineSpacing.wdLineSpaceExactly)
					{
						return false;
					}
					if (!Near(value.LineSpacing, registeredStyleDef.LineSpacing))
					{
						return false;
					}
					if (Near(value.CharacterUnitLeftIndent, 0f))
					{
						if (!Near(value.CharacterUnitRightIndent, 0f))
						{
							return false;
						}
						if (!Near(value.RightIndent, 0f))
						{
							return false;
						}
						if (!registeredStyleDef.LeftIndentPoints.HasValue && !registeredStyleDef.FirstLineIndentPoints.HasValue)
						{
							if (!Near(value.LeftIndent, 0f))
							{
								return false;
							}
							if (!Near(value.CharacterUnitFirstLineIndent, registeredStyleDef.FirstLineIndentChars))
							{
								return false;
							}
						}
						else
						{
							if (!Near(value.LeftIndent, registeredStyleDef.LeftIndentPoints.GetValueOrDefault()))
							{
								return false;
							}
							if (!Near(value.FirstLineIndent, registeredStyleDef.FirstLineIndentPoints.GetValueOrDefault()))
							{
								return false;
							}
						}
						if (!Near(value.LineUnitBefore, registeredStyleDef.LineUnitBefore))
						{
							return false;
						}
						if (!Near(value.LineUnitAfter, registeredStyleDef.LineUnitAfter))
						{
							return false;
						}
						if (value.SpaceBeforeAuto != 0 || value.SpaceAfterAuto != 0)
						{
							return false;
						}
						if (!Near(value.SpaceBefore, spaceBeforeOverride ?? registeredStyleDef.SpaceBefore))
						{
							return false;
						}
						if (Near(value.SpaceAfter, spaceAfterOverride ?? registeredStyleDef.SpaceAfter))
						{
							if (value.DisableLineHeightGrid != ((!registeredStyleDef.AlignToDocumentGrid) ? (-1) : 0))
							{
								return false;
							}
							if (value.WidowControl != 0)
							{
								return false;
							}
							return true;
						}
						return false;
					}
					return false;
				}
				return false;
			}
			return false;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "DocumentStyleManager.Compliance.ParagraphFormat");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string GetParagraphStructuralStyleName(Paragraph para)
	{
		if (para == null)
		{
			throw new ArgumentNullException("para");
		}
		object obj = null;
		try
		{
			obj = para.get_Style();
			return (obj is Style style) ? style.NameLocal : Convert.ToString(obj);
		}
		finally
		{
			ComObjectRelease.Release(obj, "DocumentStyleManager.ParagraphStructuralStyle");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string[] AllStyleNames()
	{
		return new string[16]
		{
			"OfficialDoc.MainTitle", "OfficialDoc.Level1Title", "OfficialDoc.Level2Title", "OfficialDoc.Level3Title", "OfficialDoc.Body", "OfficialDoc.SubTitle", "OfficialDoc.PageNumber", "OfficialDoc.AttachmentMarker", "OfficialDoc.AttachmentTitle", "OfficialDoc.AttachmentSubTitle",
			"OfficialDoc.AttachmentList.First", "OfficialDoc.AttachmentList.Single", "OfficialDoc.AttachmentList.Continuation", "OfficialDoc.Salutation", "OfficialDoc.Signature", "OfficialDoc.SignatureDate"
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool AllStylesExist(Document doc, List<NamedStyleDef> definitions)
	{
		foreach (NamedStyleDef definition in definitions)
		{
			Style value = FindStyle(doc, definition.Name);
			if (value == null)
			{
				return false;
			}
			HideFromStylePane(value, definition.Name);
			ComObjectRelease.Release(ref value, "DocumentStyleManager.style");
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<NamedStyleDef> SelectRequiredDefinitions(List<NamedStyleDef> definitions, ISet<ElementType> requiredElementTypes, bool includePageNumber)
	{
		if (requiredElementTypes == null)
		{
			return definitions;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (ElementType requiredElementType in requiredElementTypes)
		{
			if (ElementToStyleName.TryGetValue(requiredElementType, out var value))
			{
				hashSet.Add(value);
			}
		}
		if (includePageNumber)
		{
			hashSet.Add("OfficialDoc.PageNumber");
		}
		List<NamedStyleDef> list = new List<NamedStyleDef>();
		foreach (NamedStyleDef definition in definitions)
		{
			if (hashSet.Contains(definition.Name))
			{
				list.Add(definition);
			}
		}
		return list;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<NamedStyleDef> BuildDefinitions(FormatConfig cfg)
	{
		TextStyle textStyle = RequireStyle(cfg.Body, "正文");
		TextStyle textStyle2 = RequireStyle(cfg.MainTitle, "主标题");
		TextStyle textStyle3 = RequireStyle(cfg.Level1, "一级标题");
		TextStyle textStyle4 = RequireStyle(cfg.Level2, "二级标题");
		TextStyle textStyle5 = RequireStyle(cfg.Level3, "三级标题");
		AttachmentFormatOptions attachmentFormatOptions = RequireAttachmentOptions(cfg.AttachmentOptions);
		float num = FontSizeHelper.ToPoints(RequireText(textStyle.FontSize, "正文字号"));
		float lineSpacing = ResolveLineSpacing(textStyle.LineSpacing, cfg);
		float lineSpacing2 = ResolveLineSpacing(textStyle2.LineSpacing, cfg);
		string alignment = RequireText(textStyle.Alignment, "正文对齐方式");
		string text = RequireText(textStyle.FontName, "正文字体");
		string elementFontName = RequireText(textStyle2.FontName, "主标题字体");
		string text2 = RequireText(textStyle3.FontName, "一级标题字体");
		string text3 = RequireText(textStyle4.FontName, "二级标题字体");
		string elementFontName2 = RequireText(textStyle5.FontName, "三级标题字体");
		string text4 = RequireText(attachmentFormatOptions.AttachmentMarkerFontName, "附件标题字体");
		string englishFont = EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.Body, text);
		PageNumberFontSlots pageNumberFontSlots = PageNumberFontSlotPolicy.Resolve(text, RequireText(cfg.PageNumberFontName, "页码字体"));
		return new List<NamedStyleDef>
		{
			Def("OfficialDoc.Body", textStyle, englishFont, num, lineSpacing, ParseIndent(textStyle.FirstLineIndent), alignment, cfg.EnableDocumentGrid),
			Def("OfficialDoc.MainTitle", textStyle2, EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.MainTitle, elementFontName), FontSizeHelper.ToPoints(RequireText(textStyle2.FontSize, "主标题字号")), lineSpacing2, 0f, RequireText(textStyle2.Alignment, "主标题对齐方式"), cfg.EnableDocumentGrid),
			Def("OfficialDoc.Level1Title", textStyle3, EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.Level1Title, text2), FontSizeHelper.ToPoints(RequireText(textStyle3.FontSize, "一级标题字号")), ResolveLineSpacing(textStyle3.LineSpacing, cfg), ParseIndent(textStyle3.FirstLineIndent), RequireText(textStyle3.Alignment, "一级标题对齐方式"), cfg.EnableDocumentGrid),
			Def("OfficialDoc.Level2Title", textStyle4, EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.Level2Title, text3), FontSizeHelper.ToPoints(RequireText(textStyle4.FontSize, "二级标题字号")), ResolveLineSpacing(textStyle4.LineSpacing, cfg), ParseIndent(textStyle4.FirstLineIndent), RequireText(textStyle4.Alignment, "二级标题对齐方式"), cfg.EnableDocumentGrid),
			Def("OfficialDoc.Level3Title", textStyle5, EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.Level3Title, elementFontName2), FontSizeHelper.ToPoints(RequireText(textStyle5.FontSize, "三级标题字号")), ResolveLineSpacing(textStyle5.LineSpacing, cfg), ParseIndent(textStyle5.FirstLineIndent), RequireText(textStyle5.Alignment, "三级标题对齐方式"), cfg.EnableDocumentGrid),
			Def("OfficialDoc.SubTitle", text3, EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.SubTitle, text3), num, lineSpacing, 0f, "居中", bold: false, 0f, ToPointSpacing(textStyle2.SpaceAfter), cfg.EnableDocumentGrid),
			Def("OfficialDoc.Salutation", text, EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.Salutation, text), num, lineSpacing, 0f, alignment, bold: false, 0f, 0f, cfg.EnableDocumentGrid),
			Def("OfficialDoc.Signature", text, EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.Signature, text), num, lineSpacing, 0f, alignment, bold: false, 0f, 0f, cfg.EnableDocumentGrid),
			Def("OfficialDoc.SignatureDate", text, EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.SignatureDate, text), num, lineSpacing, 0f, alignment, bold: false, 0f, 0f, cfg.EnableDocumentGrid),
			Def("OfficialDoc.AttachmentMarker", text2, EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.AttachmentMarker, text2), num, lineSpacing, 0f, alignment, bold: false, 0f, 0f, cfg.EnableDocumentGrid),
			Def("OfficialDoc.AttachmentTitle", text4, EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.AttachmentTitle, text4), FontSizeHelper.ToPoints(RequireText(attachmentFormatOptions.AttachmentMarkerFontSize, "附件标题字号")), lineSpacing2, 0f, "居中", bold: false, 0f, ToPointSpacing(textStyle2.SpaceAfter), cfg.EnableDocumentGrid),
			Def("OfficialDoc.AttachmentSubTitle", text3, EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.AttachmentSubTitle, text3), num, lineSpacing, 0f, "居中", bold: false, 0f, ToPointSpacing(textStyle2.SpaceAfter), cfg.EnableDocumentGrid),
			AttachmentListDef("OfficialDoc.AttachmentList.First", textStyle, englishFont, num, lineSpacing, alignment, num * 5.8f, (0f - num) * 3.8f, cfg.EnableDocumentGrid),
			AttachmentListDef("OfficialDoc.AttachmentList.Single", textStyle, englishFont, num, lineSpacing, alignment, num * 5f, (0f - num) * 3f, cfg.EnableDocumentGrid),
			AttachmentListDef("OfficialDoc.AttachmentList.Continuation", textStyle, englishFont, num, lineSpacing, alignment, num * 6f, (0f - num) * 0.9f, cfg.EnableDocumentGrid),
			Def("OfficialDoc.PageNumber", pageNumberFontSlots.EastAsianFontName, pageNumberFontSlots.AsciiFontName, FontSizeHelper.ToPoints(RequireText(cfg.PageFontSize, "页码字号")), lineSpacing, 0f, "居中", cfg.PageNumberBold, 0f, 0f, cfg.EnableDocumentGrid)
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static NamedStyleDef Def(string name, TextStyle style, string englishFont, float fontSize, float lineSpacing, float firstLineIndentChars, string alignment, bool alignToDocumentGrid)
	{
		return Def(name, RequireText(style.FontName, name + "字体"), englishFont, fontSize, lineSpacing, firstLineIndentChars, alignment, style.Bold, ToPointSpacing(style.SpaceBefore), ToPointSpacing(style.SpaceAfter), alignToDocumentGrid, ParseOutlineLevel(style.OutlineLevel));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static NamedStyleDef Def(string name, string fontName, string englishFont, float fontSize, float lineSpacing, float firstLineIndentChars, string alignment, bool bold, float spaceBefore, float spaceAfter, bool alignToDocumentGrid, WdOutlineLevel outlineLevel = WdOutlineLevel.wdOutlineLevelBodyText)
	{
		return new NamedStyleDef
		{
			Name = name,
			Def = new StyleDef
			{
				FontName = RequireText(fontName, name + "字体"),
				EnglishFontName = RequireText(englishFont, name + "英文字体"),
				FontSize = fontSize,
				Bold = bold,
				Alignment = RequireText(alignment, name + "对齐方式"),
				LineSpacing = lineSpacing,
				FirstLineIndentChars = firstLineIndentChars,
				SpaceBefore = spaceBefore,
				SpaceAfter = spaceAfter,
				LineUnitBefore = 0f,
				LineUnitAfter = 0f,
				AlignToDocumentGrid = alignToDocumentGrid,
				OutlineLevel = outlineLevel
			}
		};
	}

	private static NamedStyleDef AttachmentListDef(string name, TextStyle body, string englishFont, float fontSize, float lineSpacing, string alignment, float leftIndentPoints, float firstLineIndentPoints, bool alignToDocumentGrid)
	{
		NamedStyleDef namedStyleDef = Def(name, body, englishFont, fontSize, lineSpacing, 0f, alignment, alignToDocumentGrid);
		namedStyleDef.Def.LeftIndentPoints = leftIndentPoints;
		namedStyleDef.Def.FirstLineIndentPoints = firstLineIndentPoints;
		return namedStyleDef;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SynchronizeStyle(Document doc, string name, StyleDef def, FirstFormatDiagnosticsSession diagnostics = null)
	{
		Style value = FindStyle(doc, name);
		if (value == null)
		{
			CreateStyle(doc, name, def, diagnostics);
			return;
		}
		long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
		Font value2 = null;
		ParagraphFormat value3 = null;
		try
		{
			value2 = value.Font;
			value3 = value.ParagraphFormat;
			ApplyFontFormat(value2, def, name);
			ApplyParagraphFormat(value3, def);
			HideFromStylePane(value, name);
		}
		finally
		{
			ComObjectRelease.Release(ref value3, "DocumentStyleManager.SynchronizeStyle.ParagraphFormat");
			ComObjectRelease.Release(ref value2, "DocumentStyleManager.SynchronizeStyle.Font");
			ComObjectRelease.Release(ref value, "DocumentStyleManager.style");
		}
		diagnostics?.Accumulate("style-ensure.sync", startTimestamp);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CreateStyle(Document doc, string name, StyleDef def, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (doc != null)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				throw new ArgumentException("样式名称不能为空。", "name");
			}
			if (def != null)
			{
				long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
				Style value = null;
				Font value2 = null;
				ParagraphFormat value3 = null;
				try
				{
					Styles styles = doc.Styles;
					object Type = WdStyleType.wdStyleTypeParagraph;
					value = styles.Add(name, ref Type);
					value2 = value.Font;
					ApplyFontFormat(value2, def, name);
					value3 = value.ParagraphFormat;
					ApplyParagraphFormat(value3, def);
					HideFromStylePane(value, name);
				}
				finally
				{
					if (value3 != null)
					{
						ComObjectRelease.Release(ref value3, "DocumentStyleManager.pf");
					}
					ComObjectRelease.Release(ref value2, "DocumentStyleManager.CreateStyle.Font");
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "DocumentStyleManager.style");
					}
				}
				diagnostics?.Accumulate("style-ensure.create", startTimestamp);
				return;
			}
			throw new ArgumentNullException("def");
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool AllStylesMatch(Document doc, List<NamedStyleDef> definitions)
	{
		foreach (NamedStyleDef definition in definitions)
		{
			Style value = FindStyle(doc, definition.Name);
			if (value == null)
			{
				return false;
			}
			try
			{
				HideFromStylePane(value, definition.Name);
				if (!StyleMatchesDefinition(value, definition.Def))
				{
					return false;
				}
			}
			finally
			{
				ComObjectRelease.Release(ref value, "DocumentStyleManager.style");
			}
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool StyleMatchesDefinition(Style style, StyleDef def)
	{
		Font value = null;
		ParagraphFormat value2 = null;
		try
		{
			value = style.Font;
			value2 = style.ParagraphFormat;
			if (!SameText(value.NameFarEast, def.FontName))
			{
				return false;
			}
			if (!SameText(value.NameAscii, def.EnglishFontName))
			{
				return false;
			}
			if (Near(value.Size, def.FontSize))
			{
				if (value.Bold != (def.Bold ? (-1) : 0))
				{
					return false;
				}
				if (!HasManagedTextColor(value))
				{
					return false;
				}
				if (value2.Alignment != ToWdAlignment(def.Alignment))
				{
					return false;
				}
				if (value2.OutlineLevel == def.OutlineLevel)
				{
					if (value2.LineSpacingRule != WdLineSpacing.wdLineSpaceExactly)
					{
						return false;
					}
					if (Near(value2.LineSpacing, def.LineSpacing))
					{
						if (Near(value2.SpaceBefore, def.SpaceBefore))
						{
							if (!Near(value2.SpaceAfter, def.SpaceAfter))
							{
								return false;
							}
							if (def.LeftIndentPoints.HasValue || def.FirstLineIndentPoints.HasValue)
							{
								if (!Near(value2.LeftIndent, def.LeftIndentPoints.GetValueOrDefault()))
								{
									return false;
								}
								if (!Near(value2.FirstLineIndent, def.FirstLineIndentPoints.GetValueOrDefault()))
								{
									return false;
								}
							}
							else if (!Near(value2.CharacterUnitFirstLineIndent, def.FirstLineIndentChars))
							{
								return false;
							}
							return true;
						}
						return false;
					}
					return false;
				}
				return false;
			}
			return false;
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "DocumentStyleManager.StyleMatches.ParagraphFormat");
			ComObjectRelease.Release(ref value, "DocumentStyleManager.StyleMatches.Font");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void HideFromStylePane(Style style, string styleName)
	{
		if (style == null)
		{
			return;
		}
		try
		{
			style.QuickStyle = false;
			style.UnhideWhenUsed = false;
			style.Visibility = false;
			style.Priority = 99;
		}
		catch (COMException ex)
		{
			LogService.Warn("隐藏插件样式失败，样式仍可正常使用：" + styleName, ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ClearDirectFormattingBeforeStyle(Microsoft.Office.Interop.Word.Range range)
	{
		ParagraphFormat value = null;
		Font value2 = null;
		try
		{
			value = range.ParagraphFormat;
			value.Reset();
			value2 = range.Font;
			value2.Reset();
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "DocumentStyleManager.ClearDirectFormatting.Font");
			ComObjectRelease.Release(ref value, "DocumentStyleManager.ClearDirectFormatting.ParagraphFormat");
		}
	}

	private static bool SameText(string actual, string expected)
	{
		return string.Equals((actual ?? string.Empty).Trim(), (expected ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
	}

	private static bool Near(float actual, float expected)
	{
		return Math.Abs(actual - expected) <= 0.1f;
	}

	private static bool HasManagedTextColor(Font font)
	{
		if (font == null)
		{
			return false;
		}
		bool num = font.Color == WdColor.wdColorAutomatic || font.Color == WdColor.wdColorBlack;
		bool flag = font.ColorIndex == WdColorIndex.wdAuto || font.ColorIndex == WdColorIndex.wdBlack;
		return num && flag;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void MaterializeParagraphSpacing(Microsoft.Office.Interop.Word.Range range, StyleDef def)
	{
		ParagraphFormat value = null;
		try
		{
			value = range.ParagraphFormat;
			value.LineUnitBefore = def.LineUnitBefore;
			value.LineUnitAfter = def.LineUnitAfter;
			value.SpaceBeforeAuto = 0;
			value.SpaceAfterAuto = 0;
			value.SpaceBefore = def.SpaceBefore;
			value.SpaceAfter = def.SpaceAfter;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "DocumentStyleManager.format");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Style FindStyle(Document doc, string name)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("样式名称不能为空。", "name");
		}
		Styles value = null;
		try
		{
			value = doc.Styles;
			try
			{
				Styles styles = value;
				object Index = name;
				return styles.get_Item(ref Index);
			}
			catch (COMException)
			{
				return null;
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "DocumentStyleManager.styles");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static StyleDef GetRegisteredStyleDef(Document document, string styleName)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		string orCreate = DocumentLifecycleRegistry.GetOrCreate(document);
		lock (CacheLock)
		{
			if (DocumentCaches.TryGetValue(orCreate, out var value) && value.Definitions.TryGetValue(styleName, out var value2))
			{
				return value2;
			}
		}
		throw new InvalidOperationException("当前文档尚未注册样式定义：" + styleName);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void TrimParagraphEnd(Microsoft.Office.Interop.Word.Range range)
	{
		while (range.End > range.Start)
		{
			string text = range.Text ?? string.Empty;
			if (text.EndsWith("\r", StringComparison.Ordinal) || text.EndsWith("\a", StringComparison.Ordinal))
			{
				range.End--;
				continue;
			}
			break;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyFontFormat(Font font, StyleDef def, string styleName = null)
	{
		if (font == null)
		{
			throw new ArgumentNullException("font");
		}
		if (def != null)
		{
			ApplyFontSlotWithDiagnostics(font, def.FontName, eastAsian: true, styleName);
			ApplyFontSlotWithDiagnostics(font, def.EnglishFontName, eastAsian: false, styleName);
			font.Size = def.FontSize;
			font.Bold = (def.Bold ? (-1) : 0);
			font.Italic = 0;
			font.ColorIndex = WdColorIndex.wdAuto;
			font.Color = WdColor.wdColorAutomatic;
			font.DisableCharacterSpaceGrid = !def.AlignToDocumentGrid;
			return;
		}
		throw new ArgumentNullException("def");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyFontSlotWithDiagnostics(Font font, string fontName, bool eastAsian, string styleName)
	{
		try
		{
			if (eastAsian)
			{
				DocumentFontSlotService.ApplyEastAsianIfNeeded(font, fontName);
			}
			else
			{
				DocumentFontSlotService.ApplyAsciiIfNeeded(font, fontName);
			}
		}
		catch (COMException ex)
		{
			LogService.Error("STYLE-FONT-ASSIGN style=" + SafeStyleDiagnosticToken(styleName) + ", slot=" + (eastAsian ? "east-asian" : "ascii"), ex);
			throw;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string SafeStyleDiagnosticToken(string styleName)
	{
		if (!string.Equals(styleName, "OfficialDoc.PageNumber", StringComparison.Ordinal))
		{
			if (string.IsNullOrWhiteSpace(styleName))
			{
				return "unspecified";
			}
			return "managed-style";
		}
		return "page-number";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyParagraphFormat(ParagraphFormat pf, StyleDef def)
	{
		if (pf != null)
		{
			if (def == null)
			{
				throw new ArgumentNullException("def");
			}
			pf.Alignment = ToWdAlignment(def.Alignment);
			pf.OutlineLevel = def.OutlineLevel;
			pf.LineSpacingRule = WdLineSpacing.wdLineSpaceExactly;
			pf.LineSpacing = def.LineSpacing;
			pf.CharacterUnitLeftIndent = 0f;
			pf.CharacterUnitRightIndent = 0f;
			pf.CharacterUnitFirstLineIndent = 0f;
			pf.LeftIndent = 0f;
			pf.RightIndent = 0f;
			pf.FirstLineIndent = 0f;
			if (def.LeftIndentPoints.HasValue || def.FirstLineIndentPoints.HasValue)
			{
				pf.LeftIndent = def.LeftIndentPoints.GetValueOrDefault();
				pf.FirstLineIndent = def.FirstLineIndentPoints.GetValueOrDefault();
			}
			else
			{
				pf.CharacterUnitFirstLineIndent = def.FirstLineIndentChars;
			}
			pf.LineUnitBefore = def.LineUnitBefore;
			pf.LineUnitAfter = def.LineUnitAfter;
			pf.SpaceBeforeAuto = 0;
			pf.SpaceAfterAuto = 0;
			pf.SpaceBefore = def.SpaceBefore;
			pf.SpaceAfter = def.SpaceAfter;
			pf.DisableLineHeightGrid = ((!def.AlignToDocumentGrid) ? (-1) : 0);
			pf.WidowControl = 0;
			return;
		}
		throw new ArgumentNullException("pf");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WdParagraphAlignment ToWdAlignment(string alignment)
	{
		return RequireText(alignment, "段落对齐方式") switch
		{
			"分散对齐" => WdParagraphAlignment.wdAlignParagraphDistribute, 
			"两端对齐" => WdParagraphAlignment.wdAlignParagraphJustify, 
			"居中" => WdParagraphAlignment.wdAlignParagraphCenter, 
			"左对齐" => WdParagraphAlignment.wdAlignParagraphLeft, 
			"右对齐" => WdParagraphAlignment.wdAlignParagraphRight, 
			_ => throw new FormatException("不支持的段落对齐方式：" + alignment), 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static TextStyle RequireStyle(TextStyle style, string name)
	{
		if (style == null)
		{
			throw new FormatException(name + "参数缺失。");
		}
		return style;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static AttachmentFormatOptions RequireAttachmentOptions(AttachmentFormatOptions options)
	{
		if (options == null)
		{
			throw new FormatException("附件排版参数缺失。");
		}
		return options;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string RequireText(string value, string fieldName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new FormatException(fieldName + "为空。");
		}
		return value.Trim();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static float ParseIndent(string value)
	{
		value = RequireText(value, "首行缩进");
		if (float.TryParse(value, out var result))
		{
			return result;
		}
		throw new FormatException("首行缩进不是有效数字：" + value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static float ResolveLineSpacing(string value, FormatConfig cfg)
	{
		if (cfg != null)
		{
			if (!cfg.EnableDocumentGrid)
			{
				return LineSpacingConverter.ToPoints(value);
			}
			DocumentGridOptions documentGridOptions = cfg.DocumentGridOptions ?? new DocumentGridOptions();
			return DocumentGridMetrics.CalculateVerticalPitchFromCentimeters(29.7f, cfg.TopMargin, cfg.BottomMargin, documentGridOptions.LinesPerPage);
		}
		throw new ArgumentNullException("cfg");
	}

	private static float ToPointSpacing(int points)
	{
		if (points > 0)
		{
			return points;
		}
		return 0f;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ComputeConfigHash(FormatConfig cfg)
	{
		TextStyle style = RequireStyle(cfg.Body, "正文");
		TextStyle style2 = RequireStyle(cfg.MainTitle, "主标题");
		TextStyle style3 = RequireStyle(cfg.Level1, "一级标题");
		TextStyle style4 = RequireStyle(cfg.Level2, "二级标题");
		TextStyle style5 = RequireStyle(cfg.Level3, "三级标题");
		AttachmentFormatOptions attachmentFormatOptions = RequireAttachmentOptions(cfg.AttachmentOptions);
		return "StyleNs=OfficialDoc-2|Grid=" + cfg.EnableDocumentGrid + "," + ((cfg.DocumentGridOptions == null) ? 22 : cfg.DocumentGridOptions.LinesPerPage) + "," + ((cfg.DocumentGridOptions == null) ? 28 : cfg.DocumentGridOptions.CharsPerLine) + "," + cfg.TopMargin + "," + cfg.BottomMargin + "|Body=" + StyleHash(style) + "|Main=" + StyleHash(style2) + "|L1=" + StyleHash(style3) + "|L2=" + StyleHash(style4) + "|L3=" + StyleHash(style5) + "|English=" + cfg.EnableEnglishFont + "," + cfg.EnglishNumberFontName + "|Attachment=" + attachmentFormatOptions.AttachmentMarkerFontName + "," + attachmentFormatOptions.AttachmentMarkerFontSize + "|Page=" + cfg.PageNumberFontName + "," + cfg.PageFontSize + "," + cfg.PageNumberBold;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string StyleHash(TextStyle style)
	{
		return string.Join("|", style.FontName, style.FontSize, style.LineSpacing, style.FirstLineIndent, style.Bold.ToString(), style.Alignment, OutlineLevels.Normalize(style.OutlineLevel), style.SpaceBefore.ToString(), style.SpaceAfter.ToString());
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WdOutlineLevel ParseOutlineLevel(string value)
	{
		return OutlineLevels.Normalize(value) switch
		{
			"2级" => WdOutlineLevel.wdOutlineLevel2, 
			"正文文本" => WdOutlineLevel.wdOutlineLevelBodyText, 
			"9级" => WdOutlineLevel.wdOutlineLevel9, 
			"6级" => WdOutlineLevel.wdOutlineLevel6, 
			"3级" => WdOutlineLevel.wdOutlineLevel3, 
			"7级" => WdOutlineLevel.wdOutlineLevel7, 
			"8级" => WdOutlineLevel.wdOutlineLevel8, 
			"5级" => WdOutlineLevel.wdOutlineLevel5, 
			"1级" => WdOutlineLevel.wdOutlineLevel1, 
			"4级" => WdOutlineLevel.wdOutlineLevel4, 
			_ => throw new FormatException("大纲级别无效：" + value), 
		};
	}
}
