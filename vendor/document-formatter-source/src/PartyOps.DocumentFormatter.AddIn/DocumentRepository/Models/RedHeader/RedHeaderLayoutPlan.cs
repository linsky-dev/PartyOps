using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.RedHeader;

namespace DocumentRepository.Models.RedHeader;

public sealed class RedHeaderLayoutPlan
{
	private string planId;

	private string ruleCatalogId;

	private int ruleSchemaVersion;

	private string ruleContentHash;

	private RedHeaderTemplate template;

	private FormatConfig config;

	private RedHeaderAnalysisSnapshot source;

	private IReadOnlyList<RedHeaderTopMarkLine> topMarkLines = new RedHeaderTopMarkLine[0];

	private string headerText;

	private string documentNumberText;

	private bool hasDocumentNumber;

	private int titleGapLines;

	private int headerParagraphIndex;

	private int documentNumberParagraphIndex;

	private int redLineParagraphIndex;

	private string expectedPrefixText;

	private string generatedHeaderText;

	private bool imprintEnabled;

	private string imprintSendText;

	private bool hasImprintSend;

	private int imprintRowCount;

	private int imprintOfficeRow;

	private string imprintOfficeLine;

	private float usableWidth;

	private float headerSideIndent;

	private float redLineAnchorX1;

	private float redLineAnchorX2;

	private float redLineAnchorTop;

	private float redLineWeight;

	private string normalizedRedLineStyle;

	private int expectedAddedShapes;

	private float imprintFontSize;

	private float imprintRowHeight;

	private float imprintCellPaddingPoints;

	private float imprintTargetY;

	private int expectedAddedTables;

	private string sourceBodyBookmarkName;

	private string imprintBookmarkName;

	private string generatedBreakBookmarkPrefix;

	private string redLineShapeNamePrefix;

	private DateTime taskDate;

	private string resolvedImprintDate;

	public string PlanId
	{
		get
		{
			return planId;
		}
		set
		{
			ThrowIfSealed();
			planId = value;
		}
	}

	public string RuleCatalogId
	{
		get
		{
			return ruleCatalogId;
		}
		set
		{
			ThrowIfSealed();
			ruleCatalogId = value;
		}
	}

	public int RuleSchemaVersion
	{
		get
		{
			return ruleSchemaVersion;
		}
		set
		{
			ThrowIfSealed();
			ruleSchemaVersion = value;
		}
	}

	public string RuleContentHash
	{
		get
		{
			return ruleContentHash;
		}
		set
		{
			ThrowIfSealed();
			ruleContentHash = value;
		}
	}

	public bool IsSealed { get; private set; }

	public RedHeaderAnalysisSnapshot Source
	{
		get
		{
			if (source != null)
			{
				return source.DeepCopy();
			}
			return null;
		}
		set
		{
			ThrowIfSealed();
			source = value?.DeepCopy();
		}
	}

	public RedHeaderTemplate Template
	{
		get
		{
			if (template != null)
			{
				return RedHeaderTemplateService.Clone(template);
			}
			return null;
		}
		set
		{
			ThrowIfSealed();
			template = ((value == null) ? null : RedHeaderTemplateService.Clone(value));
		}
	}

	public FormatConfig Config
	{
		get
		{
			if (config != null)
			{
				return config.DeepClone();
			}
			return null;
		}
		set
		{
			ThrowIfSealed();
			config = value?.DeepClone();
		}
	}

	internal RedHeaderAnalysisSnapshot SourceForPlanning
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			if (IsSealed)
			{
				throw new InvalidOperationException("套红计划已封存，不能使用规划视图。");
			}
			return source;
		}
	}

	internal RedHeaderTemplate TemplateForPlanning
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			if (IsSealed)
			{
				throw new InvalidOperationException("套红计划已封存，不能使用规划视图。");
			}
			return template;
		}
	}

	internal RedHeaderAnalysisSnapshot SourceForExecution
	{
		get
		{
			EnsureSealed();
			return source;
		}
	}

	internal RedHeaderTemplate TemplateForExecution
	{
		get
		{
			EnsureSealed();
			return template;
		}
	}

	internal FormatConfig ConfigForExecution
	{
		get
		{
			EnsureSealed();
			return config;
		}
	}

	public string HeaderText
	{
		get
		{
			return headerText;
		}
		set
		{
			ThrowIfSealed();
			headerText = value;
		}
	}

	public string DocumentNumberText
	{
		get
		{
			return documentNumberText;
		}
		set
		{
			ThrowIfSealed();
			documentNumberText = value;
		}
	}

	public bool HasDocumentNumber
	{
		get
		{
			return hasDocumentNumber;
		}
		set
		{
			ThrowIfSealed();
			hasDocumentNumber = value;
		}
	}

	public int TitleGapLines
	{
		get
		{
			return titleGapLines;
		}
		set
		{
			ThrowIfSealed();
			titleGapLines = value;
		}
	}

	public IReadOnlyList<RedHeaderTopMarkLine> TopMarkLines
	{
		get
		{
			return topMarkLines;
		}
		[MethodImpl(MethodImplOptions.NoInlining)]
		set
		{
			ThrowIfSealed();
			if (value == null)
			{
				throw new ArgumentNullException("value");
			}
			topMarkLines = new List<RedHeaderTopMarkLine>(value).AsReadOnly();
		}
	}

	public int HeaderParagraphIndex
	{
		get
		{
			return headerParagraphIndex;
		}
		set
		{
			ThrowIfSealed();
			headerParagraphIndex = value;
		}
	}

	public int DocumentNumberParagraphIndex
	{
		get
		{
			return documentNumberParagraphIndex;
		}
		set
		{
			ThrowIfSealed();
			documentNumberParagraphIndex = value;
		}
	}

	public int RedLineParagraphIndex
	{
		get
		{
			return redLineParagraphIndex;
		}
		set
		{
			ThrowIfSealed();
			redLineParagraphIndex = value;
		}
	}

	public string ExpectedPrefixText
	{
		get
		{
			return expectedPrefixText;
		}
		set
		{
			ThrowIfSealed();
			expectedPrefixText = value;
		}
	}

	public string GeneratedHeaderText
	{
		get
		{
			return generatedHeaderText;
		}
		set
		{
			ThrowIfSealed();
			generatedHeaderText = value;
		}
	}

	public bool ImprintEnabled
	{
		get
		{
			return imprintEnabled;
		}
		set
		{
			ThrowIfSealed();
			imprintEnabled = value;
		}
	}

	public string ImprintSendText
	{
		get
		{
			return imprintSendText;
		}
		set
		{
			ThrowIfSealed();
			imprintSendText = value;
		}
	}

	public bool HasImprintSend
	{
		get
		{
			return hasImprintSend;
		}
		set
		{
			ThrowIfSealed();
			hasImprintSend = value;
		}
	}

	public int ImprintRowCount
	{
		get
		{
			return imprintRowCount;
		}
		set
		{
			ThrowIfSealed();
			imprintRowCount = value;
		}
	}

	public int ImprintOfficeRow
	{
		get
		{
			return imprintOfficeRow;
		}
		set
		{
			ThrowIfSealed();
			imprintOfficeRow = value;
		}
	}

	public string ImprintOfficeLine
	{
		get
		{
			return imprintOfficeLine;
		}
		set
		{
			ThrowIfSealed();
			imprintOfficeLine = value;
		}
	}

	public float UsableWidth
	{
		get
		{
			return usableWidth;
		}
		set
		{
			ThrowIfSealed();
			usableWidth = value;
		}
	}

	public float HeaderSideIndent
	{
		get
		{
			return headerSideIndent;
		}
		set
		{
			ThrowIfSealed();
			headerSideIndent = value;
		}
	}

	public float RedLineAnchorX1
	{
		get
		{
			return redLineAnchorX1;
		}
		set
		{
			ThrowIfSealed();
			redLineAnchorX1 = value;
		}
	}

	public float RedLineAnchorX2
	{
		get
		{
			return redLineAnchorX2;
		}
		set
		{
			ThrowIfSealed();
			redLineAnchorX2 = value;
		}
	}

	public float RedLineAnchorTop
	{
		get
		{
			return redLineAnchorTop;
		}
		set
		{
			ThrowIfSealed();
			redLineAnchorTop = value;
		}
	}

	public float RedLineWeight
	{
		get
		{
			return redLineWeight;
		}
		set
		{
			ThrowIfSealed();
			redLineWeight = value;
		}
	}

	public string NormalizedRedLineStyle
	{
		get
		{
			return normalizedRedLineStyle;
		}
		set
		{
			ThrowIfSealed();
			normalizedRedLineStyle = value;
		}
	}

	public int ExpectedAddedShapes
	{
		get
		{
			return expectedAddedShapes;
		}
		set
		{
			ThrowIfSealed();
			expectedAddedShapes = value;
		}
	}

	public float ImprintFontSize
	{
		get
		{
			return imprintFontSize;
		}
		set
		{
			ThrowIfSealed();
			imprintFontSize = value;
		}
	}

	public float ImprintRowHeight
	{
		get
		{
			return imprintRowHeight;
		}
		set
		{
			ThrowIfSealed();
			imprintRowHeight = value;
		}
	}

	public float ImprintCellPaddingPoints
	{
		get
		{
			return imprintCellPaddingPoints;
		}
		set
		{
			ThrowIfSealed();
			imprintCellPaddingPoints = value;
		}
	}

	public float ImprintTargetY
	{
		get
		{
			return imprintTargetY;
		}
		set
		{
			ThrowIfSealed();
			imprintTargetY = value;
		}
	}

	public int ExpectedAddedTables
	{
		get
		{
			return expectedAddedTables;
		}
		set
		{
			ThrowIfSealed();
			expectedAddedTables = value;
		}
	}

	public string SourceBodyBookmarkName
	{
		get
		{
			return sourceBodyBookmarkName;
		}
		set
		{
			ThrowIfSealed();
			sourceBodyBookmarkName = value;
		}
	}

	public string ImprintBookmarkName
	{
		get
		{
			return imprintBookmarkName;
		}
		set
		{
			ThrowIfSealed();
			imprintBookmarkName = value;
		}
	}

	public string GeneratedBreakBookmarkPrefix
	{
		get
		{
			return generatedBreakBookmarkPrefix;
		}
		set
		{
			ThrowIfSealed();
			generatedBreakBookmarkPrefix = value;
		}
	}

	public string RedLineShapeNamePrefix
	{
		get
		{
			return redLineShapeNamePrefix;
		}
		set
		{
			ThrowIfSealed();
			redLineShapeNamePrefix = value;
		}
	}

	public DateTime TaskDate
	{
		get
		{
			return taskDate;
		}
		set
		{
			ThrowIfSealed();
			taskDate = value;
		}
	}

	public string ResolvedImprintDate
	{
		get
		{
			return resolvedImprintDate;
		}
		set
		{
			ThrowIfSealed();
			resolvedImprintDate = value;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ThrowIfSealed()
	{
		if (IsSealed)
		{
			throw new InvalidOperationException("套红计划已经封存，拒绝修改。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal void Seal()
	{
		if (source != null && source.DocumentSnapshot != null)
		{
			if (template == null || config == null)
			{
				throw new InvalidOperationException("套红计划缺少模板或排版参数，不能封存。");
			}
			if (string.IsNullOrWhiteSpace(planId))
			{
				throw new InvalidOperationException("套红计划缺少计划标识，不能封存。");
			}
			if (string.IsNullOrWhiteSpace(ruleCatalogId) || ruleSchemaVersion <= 0)
			{
				throw new InvalidOperationException("套红计划缺少规则目录或结构版本，不能封存。");
			}
			if (IsSha256(ruleContentHash))
			{
				EnsureGeneratedObjectIdentities();
				IsSealed = true;
				return;
			}
			throw new InvalidOperationException("套红计划缺少有效的规则内容哈希，不能封存。");
		}
		throw new InvalidOperationException("套红计划缺少源快照，不能封存。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void EnsureSealed()
	{
		if (!IsSealed)
		{
			throw new InvalidOperationException("套红计划尚未封存，拒绝执行可继续变化的计划。");
		}
	}

	private static bool IsSha256(string value)
	{
		if (value != null && value.Length == 64)
		{
			foreach (char c in value)
			{
				if ((c < '0' || c > '9') && (c < 'a' || c > 'f'))
				{
					return false;
				}
			}
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void EnsureGeneratedObjectIdentities()
	{
		string text = BuildSafeIdentity(planId);
		if (string.IsNullOrWhiteSpace(redLineShapeNamePrefix))
		{
			redLineShapeNamePrefix = "SXRedLine_" + text;
		}
		if (string.IsNullOrWhiteSpace(sourceBodyBookmarkName))
		{
			sourceBodyBookmarkName = "SXBody_" + text;
		}
		if (string.IsNullOrWhiteSpace(imprintBookmarkName))
		{
			imprintBookmarkName = "SXImprint_" + text;
		}
		if (string.IsNullOrWhiteSpace(generatedBreakBookmarkPrefix))
		{
			generatedBreakBookmarkPrefix = "SXBreak_" + text;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildSafeIdentity(string value)
	{
		char[] array = new char[24];
		int num = 0;
		for (int i = 0; i < value.Length; i++)
		{
			if (num >= array.Length)
			{
				break;
			}
			char c = value[i];
			if (char.IsLetterOrDigit(c))
			{
				array[num++] = c;
			}
		}
		if (num != 0)
		{
			return new string(array, 0, num);
		}
		return "plan";
	}
}
