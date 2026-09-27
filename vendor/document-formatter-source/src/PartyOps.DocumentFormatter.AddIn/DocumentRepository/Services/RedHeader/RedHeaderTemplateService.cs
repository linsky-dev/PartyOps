using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Models.Rules;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Rules;

namespace DocumentRepository.Services.RedHeader;

public static class RedHeaderTemplateService
{
	public const string RuleCatalogId = "redheader.templates";

	public const int RuleSchemaVersion = 1;

	private static readonly object SyncRoot = new object();

	private static readonly string StorePath = ApplicationDataPaths.RedHeaderTemplates;

	public static RuleLoadResult<RedHeaderTemplateSet> LastLoadResult { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleStoreDescriptor GetStoreDescriptor()
	{
		return new RuleStoreDescriptor
		{
			Id = "redheader.templates",
			StorePath = StorePath,
			OwnerService = "RedHeaderTemplateService",
			ModelTypeName = "RedHeaderTemplateSet"
		};
	}

	public static RuleLoadResult<RedHeaderTemplateSet> LoadResult()
	{
		lock (SyncRoot)
		{
			LastLoadResult = XmlRuleStore.LoadWithSelfHealing(GetStoreDescriptor(), CreateDefaults, Normalize);
			return LastLoadResult;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RedHeaderTemplateSet Load()
	{
		RuleLoadResult<RedHeaderTemplateSet> ruleLoadResult = LoadResult();
		if (!ruleLoadResult.Usable)
		{
			throw RuleStoreUnavailableException.From(ruleLoadResult, "套红模板库");
		}
		return ruleLoadResult.Value;
	}

	public static RuleLoadResult<RedHeaderTemplateSet> ResetToDefault()
	{
		lock (SyncRoot)
		{
			return XmlRuleStore.ResetToDefault(GetStoreDescriptor(), CreateDefaults, Normalize);
		}
	}

	public static void Save(RedHeaderTemplateSet set)
	{
		lock (SyncRoot)
		{
			XmlRuleStore.Save(GetStoreDescriptor(), set, Normalize);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void NormalizeAndPersistStore()
	{
		lock (SyncRoot)
		{
			RuleLoadResult<RedHeaderTemplateSet> ruleLoadResult = LoadResult();
			if (!ruleLoadResult.Usable)
			{
				throw RuleStoreUnavailableException.From(ruleLoadResult, "套红模板库");
			}
			XmlRuleStore.Save(GetStoreDescriptor(), ruleLoadResult.Value, Normalize);
		}
	}

	public static RedHeaderTemplate GetActiveTemplate()
	{
		RedHeaderTemplateSet set = Load();
		return Clone(set.Templates.First((RedHeaderTemplate t) => t.Id == set.ActiveTemplateId));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void SetActiveTemplate(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			throw new ArgumentException("Red header template id cannot be empty.", "id");
		}
		RedHeaderTemplateSet redHeaderTemplateSet = Load();
		if (!redHeaderTemplateSet.Templates.Any((RedHeaderTemplate t) => t.Id == id))
		{
			throw new InvalidOperationException("Red header template does not exist: " + id);
		}
		redHeaderTemplateSet.ActiveTemplateId = id;
		Save(redHeaderTemplateSet);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RedHeaderTemplate Clone(RedHeaderTemplate source)
	{
		if (source == null)
		{
			throw new ArgumentNullException("source");
		}
		return new RedHeaderTemplate
		{
			Id = source.Id,
			Name = source.Name,
			HeaderText = source.HeaderText,
			DocumentNumberText = source.DocumentNumberText,
			TopMarks = CloneTopMarks(source.TopMarks),
			HeaderFont = source.HeaderFont,
			HeaderSize = source.HeaderSize,
			HeaderBold = source.HeaderBold,
			HeaderColor = source.HeaderColor,
			HeaderAlignment = source.HeaderAlignment,
			HeaderLineSpacing = source.HeaderLineSpacing,
			HeaderSpaceBefore = source.HeaderSpaceBefore,
			HeaderSpaceAfter = source.HeaderSpaceAfter,
			HeaderIndentChars = source.HeaderIndentChars,
			HeaderLayoutWidthPercent = source.HeaderLayoutWidthPercent,
			HeaderCharacterScalePercent = source.HeaderCharacterScalePercent,
			HeaderFitMode = source.HeaderFitMode,
			HeaderMinimumScalePercent = source.HeaderMinimumScalePercent,
			HeaderCharacterSpacing = source.HeaderCharacterSpacing,
			DocumentNumberFont = source.DocumentNumberFont,
			DocumentNumberSize = source.DocumentNumberSize,
			DocumentNumberLineSpacing = source.DocumentNumberLineSpacing,
			DocumentNumberSpaceBefore = source.DocumentNumberSpaceBefore,
			DocumentNumberSpaceAfter = source.DocumentNumberSpaceAfter,
			RedLineStyle = source.RedLineStyle,
			RedLineWidthPercent = source.RedLineWidthPercent,
			RedLineThickness = source.RedLineThickness,
			RedLineColor = source.RedLineColor,
			RedLineSpaceBefore = source.RedLineSpaceBefore,
			RedLineSpaceAfter = source.RedLineSpaceAfter,
			TitleGapLines = source.TitleGapLines,
			TitleGapLineSpacing = source.TitleGapLineSpacing,
			ImprintEnabled = source.ImprintEnabled,
			ImprintOnEvenPage = source.ImprintOnEvenPage,
			ImprintFont = source.ImprintFont,
			ImprintSize = source.ImprintSize,
			ImprintBottomOffset = source.ImprintBottomOffset,
			ImprintCellPaddingCm = source.ImprintCellPaddingCm,
			ImprintSend = source.ImprintSend,
			ImprintOffice = source.ImprintOffice,
			ImprintDate = source.ImprintDate,
			ImprintDateMode = source.ImprintDateMode,
			UseTimesNewRomanForNumbers = (source.UseTimesNewRomanForNumbers || source.ImprintDateUseTimesNewRoman),
			ImprintDateUseTimesNewRoman = false
		};
	}

	public static void ValidateTemplate(RedHeaderTemplate template)
	{
		RedHeaderTemplateValidator.Validate(template);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RedHeaderTemplateSet Normalize(RedHeaderTemplateSet set)
	{
		if (set != null)
		{
			if (set.Templates == null)
			{
				throw new InvalidOperationException("Red header template list cannot be empty.");
			}
			if (set.Templates.Count == 0)
			{
				throw new InvalidOperationException("At least one red header template is required.");
			}
			for (int i = 0; i < set.Templates.Count; i++)
			{
				set.Templates[i] = NormalizeTemplate(set.Templates[i]);
			}
			if (string.IsNullOrWhiteSpace(set.ActiveTemplateId) || !set.Templates.Any((RedHeaderTemplate t) => t.Id == set.ActiveTemplateId))
			{
				throw new InvalidOperationException("Active red header template does not exist: " + set.ActiveTemplateId);
			}
			return set;
		}
		throw new InvalidOperationException("Red header template set cannot be empty.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RedHeaderTemplateSet CreateDefaults()
	{
		return new RedHeaderTemplateSet
		{
			Templates = 
			{
				CreateDefault("down", "下行文", "某某市人民政府文件", "某府发〔2026〕8号", delegate(RedHeaderTemplate t)
				{
					t.HeaderSize = 48f;
				}),
				CreateDefault("up", "上行文", "某某市人民政府文件", "某府呈〔2026〕8号                 签发人：张局长", delegate(RedHeaderTemplate t)
				{
					t.HeaderSize = 48f;
					t.HeaderSpaceBefore = 85f;
					t.RedLineThickness = 3f;
					t.ImprintSend = "";
				}),
				CreateDefault("letter", "便函", "某某市人民政府", "", [MethodImpl(MethodImplOptions.NoInlining)] (RedHeaderTemplate t) =>
				{
					t.HeaderSize = 28f;
					t.HeaderAlignment = "分散对齐";
					t.HeaderSpaceBefore = 0f;
					t.HeaderSpaceAfter = 0f;
					t.DocumentNumberSize = 14f;
					t.DocumentNumberLineSpacing = 0f;
					t.DocumentNumberSpaceBefore = 0f;
					t.DocumentNumberSpaceAfter = 0f;
					t.RedLineStyle = "upperThickLowerThin";
					t.RedLineWidthPercent = 110f;
					t.RedLineThickness = 3f;
					t.ImprintEnabled = false;
					t.ImprintOnEvenPage = false;
					t.ImprintSend = "";
					t.ImprintOffice = "";
					t.ImprintDate = "";
				})
			},
			ActiveTemplateId = "down"
		};
	}

	private static RedHeaderTemplate CreateDefault(string id, string name, string headerText, string docNumberText, Action<RedHeaderTemplate> patch)
	{
		RedHeaderTemplate redHeaderTemplate = new RedHeaderTemplate
		{
			Id = id,
			Name = name,
			HeaderText = headerText,
			DocumentNumberText = docNumberText
		};
		patch?.Invoke(redHeaderTemplate);
		return redHeaderTemplate;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static RedHeaderTemplate NormalizeTemplate(RedHeaderTemplate source)
	{
		if (source == null)
		{
			throw new InvalidOperationException("Red header template cannot be empty.");
		}
		if (!string.IsNullOrWhiteSpace(source.Id))
		{
			if (string.IsNullOrWhiteSpace(source.Name))
			{
				throw new InvalidOperationException("Red header template name cannot be empty.");
			}
			if (!string.IsNullOrWhiteSpace(source.HeaderText))
			{
				if (source.DocumentNumberText == null)
				{
					source.DocumentNumberText = "";
				}
				if (source.TopMarks == null)
				{
					source.TopMarks = new RedHeaderTopMarkOptions();
				}
				if (source.TopMarks.CopyNumber == null)
				{
					source.TopMarks.CopyNumber = "";
				}
				if (source.TopMarks.SecurityLevel == null)
				{
					source.TopMarks.SecurityLevel = "无";
				}
				if (source.TopMarks.ConfidentialityPeriod == null)
				{
					source.TopMarks.ConfidentialityPeriod = "";
				}
				if (source.TopMarks.UrgencyLevel == null)
				{
					source.TopMarks.UrgencyLevel = "无";
				}
				if (source.HeaderFitMode == null)
				{
					source.HeaderFitMode = "manual";
				}
				if (source.RedLineStyle == null)
				{
					source.RedLineStyle = "";
				}
				if (source.ImprintSend == null)
				{
					source.ImprintSend = "";
				}
				if (source.ImprintOffice == null)
				{
					source.ImprintOffice = "";
				}
				if (source.ImprintDate == null)
				{
					source.ImprintDate = "";
				}
				if (source.ImprintDateUseTimesNewRoman)
				{
					source.UseTimesNewRomanForNumbers = true;
				}
				source.ImprintDateUseTimesNewRoman = false;
				source.ImprintCellPaddingCm = Math.Max(0f, Math.Min(1f, source.ImprintCellPaddingCm));
				RedHeaderTemplateValidator.Validate(source);
				return source;
			}
			throw new InvalidOperationException("Red header text cannot be empty: " + source.Name);
		}
		throw new InvalidOperationException("Red header template id cannot be empty.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ComputeRuleContentHash(RedHeaderTemplate template)
	{
		if (template == null)
		{
			throw new ArgumentNullException("template");
		}
		StringBuilder builder = new StringBuilder(512);
		Field("Id", template.Id);
		Field("Name", template.Name);
		Field("HeaderText", template.HeaderText);
		Field("DocumentNumberText", template.DocumentNumberText);
		Field("HeaderFont", template.HeaderFont);
		Field("HeaderSize", template.HeaderSize);
		Field("HeaderBold", template.HeaderBold);
		Field("HeaderColor", template.HeaderColor);
		Field("HeaderAlignment", template.HeaderAlignment);
		Field("HeaderLineSpacing", template.HeaderLineSpacing);
		Field("HeaderSpaceBefore", template.HeaderSpaceBefore);
		Field("HeaderSpaceAfter", template.HeaderSpaceAfter);
		Field("HeaderIndentChars", template.HeaderIndentChars);
		Field("HeaderLayoutWidthPercent", template.HeaderLayoutWidthPercent);
		Field("HeaderCharacterScalePercent", template.HeaderCharacterScalePercent);
		Field("HeaderFitMode", template.HeaderFitMode);
		Field("HeaderMinimumScalePercent", template.HeaderMinimumScalePercent);
		Field("HeaderCharacterSpacing", template.HeaderCharacterSpacing);
		Field("DocumentNumberFont", template.DocumentNumberFont);
		Field("DocumentNumberSize", template.DocumentNumberSize);
		Field("DocumentNumberLineSpacing", template.DocumentNumberLineSpacing);
		Field("DocumentNumberSpaceBefore", template.DocumentNumberSpaceBefore);
		Field("DocumentNumberSpaceAfter", template.DocumentNumberSpaceAfter);
		Field("RedLineStyle", template.RedLineStyle);
		Field("RedLineWidthPercent", template.RedLineWidthPercent);
		Field("RedLineThickness", template.RedLineThickness);
		Field("RedLineColor", template.RedLineColor);
		Field("RedLineSpaceBefore", template.RedLineSpaceBefore);
		Field("RedLineSpaceAfter", template.RedLineSpaceAfter);
		Field("TitleGapLines", template.TitleGapLines);
		Field("TitleGapLineSpacing", template.TitleGapLineSpacing);
		Field("ImprintEnabled", template.ImprintEnabled);
		Field("ImprintOnEvenPage", template.ImprintOnEvenPage);
		Field("ImprintFont", template.ImprintFont);
		Field("ImprintSize", template.ImprintSize);
		Field("ImprintBottomOffset", template.ImprintBottomOffset);
		Field("ImprintCellPaddingCm", template.ImprintCellPaddingCm);
		Field("ImprintSend", template.ImprintSend);
		Field("ImprintOffice", template.ImprintOffice);
		Field("ImprintDate", template.ImprintDate);
		Field("ImprintDateMode", template.ImprintDateMode);
		Field("UseTimesNewRomanForNumbers", template.UseTimesNewRomanForNumbers);
		Field("ImprintDateUseTimesNewRoman", template.ImprintDateUseTimesNewRoman);
		RedHeaderTopMarkOptions redHeaderTopMarkOptions = template.TopMarks ?? new RedHeaderTopMarkOptions();
		Field("CopyNumberEnabled", redHeaderTopMarkOptions.CopyNumberEnabled);
		Field("CopyNumber", redHeaderTopMarkOptions.CopyNumber);
		Field("SecurityLevel", redHeaderTopMarkOptions.SecurityLevel);
		Field("ConfidentialityPeriod", redHeaderTopMarkOptions.ConfidentialityPeriod);
		Field("UrgencyLevel", redHeaderTopMarkOptions.UrgencyLevel);
		Field("MarksFontName", redHeaderTopMarkOptions.FontName);
		Field("MarksFontSize", redHeaderTopMarkOptions.FontSize);
		Field("MarksLineSpacing", redHeaderTopMarkOptions.LineSpacing);
		Field("MarksLeftIndentChars", redHeaderTopMarkOptions.LeftIndentChars);
		Field("MarksColor", redHeaderTopMarkOptions.Color);
		using (SHA256 sHA = SHA256.Create())
		{
			byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
			StringBuilder stringBuilder = new StringBuilder(64);
			byte[] array2 = array;
			foreach (byte b in array2)
			{
				stringBuilder.Append(b.ToString("x2"));
			}
			return stringBuilder.ToString();
		}
		[MethodImpl(MethodImplOptions.NoInlining)]
		void Field(string name, object value)
		{
			string text = ((!(value is float num)) ? ((!(value is bool)) ? ((value == null) ? "" : value.ToString()) : (((bool)value) ? "1" : "0")) : num.ToString("R", CultureInfo.InvariantCulture));
			builder.Append(name).Append('#').Append(text.Length)
				.Append(':')
				.Append(text)
				.Append(';');
		}
	}

	private static RedHeaderTopMarkOptions CloneTopMarks(RedHeaderTopMarkOptions source)
	{
		source = source ?? new RedHeaderTopMarkOptions();
		return new RedHeaderTopMarkOptions
		{
			CopyNumberEnabled = source.CopyNumberEnabled,
			CopyNumber = source.CopyNumber,
			SecurityLevel = source.SecurityLevel,
			ConfidentialityPeriod = source.ConfidentialityPeriod,
			UrgencyLevel = source.UrgencyLevel,
			FontName = source.FontName,
			FontSize = source.FontSize,
			LineSpacing = source.LineSpacing,
			LeftIndentChars = source.LeftIndentChars,
			Color = source.Color
		};
	}
}
