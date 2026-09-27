using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Models.Rules;
using DocumentRepository.Services.CompilationFormatting;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Formatting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Rules;

namespace DocumentRepository;

public static class ConfigManager
{
	private static readonly object _lock;

	private static readonly string TemplatesPath;

	public const int MaxUserTemplateCount = 9;

	public static readonly FormatConfig SystemDefault;

	private static readonly string[] DefaultUserTemplateNames;

	private static readonly List<FormatConfig> UserTemplates;

	private static readonly List<string> UserTemplateNames;

	public static string[] TemplateNames
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			lock (_lock)
			{
				string[] array = new string[UserTemplateNames.Count + 1];
				array[0] = "系统默认";
				for (int i = 0; i < UserTemplateNames.Count; i++)
				{
					array[i + 1] = UserTemplateNames[i];
				}
				return array;
			}
		}
	}

	public static FormatConfig Current { get; private set; }

	public static int CurrentTemplateIndex { get; private set; }

	public static RuleLoadResult<TemplateCollection> LastStoreLoadResult { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	static ConfigManager()
	{
		_lock = new object();
		TemplatesPath = ApplicationDataPaths.FormatTemplates;
		SystemDefault = new FormatConfig();
		DefaultUserTemplateNames = new string[3] { "模板一", "模板二", "模板三" };
		UserTemplates = new List<FormatConfig>();
		UserTemplateNames = new List<string>();
		EnsureNewFeatureDefaults(SystemDefault);
		LoadTemplates();
		ApplyCurrentTemplate();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleStoreDescriptor GetStoreDescriptor()
	{
		return new RuleStoreDescriptor
		{
			Id = "format.templates",
			StorePath = TemplatesPath,
			OwnerService = "ConfigManager",
			ModelTypeName = "TemplateCollection / FormatConfig"
		};
	}

	public static FormatConfig GetCurrentCopy()
	{
		return DeepClone(Current);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void SwitchTemplate(int index)
	{
		lock (_lock)
		{
			if (index >= 0 && index <= UserTemplates.Count)
			{
				CurrentTemplateIndex = index;
				ApplyCurrentTemplate();
				SaveTemplates();
				return;
			}
			throw new ArgumentOutOfRangeException("index", "Template index is outside the available template list.");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int AddUserTemplate(string name, FormatConfig source)
	{
		lock (_lock)
		{
			if (UserTemplates.Count < 9)
			{
				string text = NormalizeTemplateName(name);
				EnsureUniqueTemplateName(text, -1);
				FormatConfig formatConfig = DeepClone(source ?? Current ?? SystemDefault);
				EnsureNewFeatureDefaults(formatConfig);
				EnsureValidConfig(formatConfig, "New template");
				UserTemplates.Add(formatConfig);
				UserTemplateNames.Add(text);
				CurrentTemplateIndex = UserTemplates.Count;
				ApplyCurrentTemplate();
				SaveTemplates();
				return CurrentTemplateIndex;
			}
			throw new InvalidOperationException("最多可保存 " + 9 + " 个自定义模板。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RenameUserTemplate(int index, string name)
	{
		lock (_lock)
		{
			if (index > 0 && index <= UserTemplates.Count)
			{
				string text = NormalizeTemplateName(name);
				EnsureUniqueTemplateName(text, index - 1);
				UserTemplateNames[index - 1] = text;
				SaveTemplates();
				return;
			}
			throw new InvalidOperationException("系统默认模板不可改名，请选择自定义模板。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void DeleteUserTemplate(int index)
	{
		lock (_lock)
		{
			if (index <= 0 || index > UserTemplates.Count)
			{
				throw new InvalidOperationException("系统默认模板不可删除，请选择自定义模板。");
			}
			int index2 = index - 1;
			FormatConfig item = UserTemplates[index2];
			string item2 = UserTemplateNames[index2];
			int currentTemplateIndex = CurrentTemplateIndex;
			FormatConfig current = Current;
			UserTemplates.RemoveAt(index2);
			UserTemplateNames.RemoveAt(index2);
			CurrentTemplateIndex = 0;
			ApplyCurrentTemplate();
			try
			{
				SaveTemplates();
				DocumentStyleManager.InvalidateCache();
			}
			catch
			{
				UserTemplates.Insert(index2, item);
				UserTemplateNames.Insert(index2, item2);
				CurrentTemplateIndex = currentTemplateIndex;
				Current = current;
				throw;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void SaveCurrentTemplate(FormatConfig config)
	{
		if (CurrentTemplateIndex == 0)
		{
			return;
		}
		EnsureNewFeatureDefaults(config);
		EnsureValidConfig(config, "Current template");
		lock (_lock)
		{
			UserTemplates[CurrentTemplateIndex - 1] = DeepClone(config);
			Current = UserTemplates[CurrentTemplateIndex - 1];
			SaveTemplates();
			DocumentStyleManager.InvalidateCache();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void SaveCurrentAttachmentOptions(AttachmentFormatOptions options)
	{
		if (CurrentTemplateIndex == 0)
		{
			return;
		}
		if (options != null)
		{
			lock (_lock)
			{
				FormatConfig editableCurrentTemplate = GetEditableCurrentTemplate();
				editableCurrentTemplate.AttachmentOptions = CloneAttachmentOptions(options);
				EnsureNewFeatureDefaults(editableCurrentTemplate);
				UserTemplates[CurrentTemplateIndex - 1] = editableCurrentTemplate;
				Current = editableCurrentTemplate;
				SaveTemplates();
				return;
			}
		}
		throw new ArgumentNullException("options");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void SaveCurrentTableOptions(TableFormatOptions options)
	{
		if (CurrentTemplateIndex == 0)
		{
			return;
		}
		if (options != null)
		{
			lock (_lock)
			{
				FormatConfig editableCurrentTemplate = GetEditableCurrentTemplate();
				editableCurrentTemplate.TableOptions = CloneTableOptions(options);
				EnsureNewFeatureDefaults(editableCurrentTemplate);
				UserTemplates[CurrentTemplateIndex - 1] = editableCurrentTemplate;
				Current = editableCurrentTemplate;
				SaveTemplates();
				return;
			}
		}
		throw new ArgumentNullException("options");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void SaveCurrentImageOptions(ImageFormatOptions options)
	{
		if (CurrentTemplateIndex == 0)
		{
			return;
		}
		if (options != null)
		{
			lock (_lock)
			{
				FormatConfig editableCurrentTemplate = GetEditableCurrentTemplate();
				editableCurrentTemplate.ImageOptions = CloneImageOptions(options);
				EnsureNewFeatureDefaults(editableCurrentTemplate);
				UserTemplates[CurrentTemplateIndex - 1] = editableCurrentTemplate;
				Current = editableCurrentTemplate;
				SaveTemplates();
				return;
			}
		}
		throw new ArgumentNullException("options");
	}

	public static void ResetCurrentTemplate()
	{
		if (CurrentTemplateIndex == 0)
		{
			return;
		}
		lock (_lock)
		{
			UserTemplates[CurrentTemplateIndex - 1] = DeepClone(SystemDefault);
			Current = UserTemplates[CurrentTemplateIndex - 1];
			SaveTemplates();
		}
	}

	public static void Reload()
	{
		lock (_lock)
		{
			LoadTemplates();
			ApplyCurrentTemplate();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void NormalizeAndPersistStore()
	{
		lock (_lock)
		{
			RuleLoadResult<TemplateCollection> ruleLoadResult = LoadStoreResult();
			if (!ruleLoadResult.Usable)
			{
				throw RuleStoreUnavailableException.From(ruleLoadResult, "排版模板库");
			}
			XmlRuleStore.Save(GetStoreDescriptor(), ruleLoadResult.Value, NormalizeTemplateCollection);
			ApplyCollection(ruleLoadResult.Value);
		}
	}

	public static RuleLoadResult<TemplateCollection> LoadStoreResult()
	{
		lock (_lock)
		{
			return LastStoreLoadResult = XmlRuleStore.LoadWithSelfHealing(GetStoreDescriptor(), CreateDefaultTemplateCollection, NormalizeTemplateCollection);
		}
	}

	public static void EnsureStoreAvailableForExecution()
	{
		lock (_lock)
		{
			EnsureStoreAvailable();
		}
	}

	public static RuleLoadResult<TemplateCollection> ResetStoreToDefault()
	{
		lock (_lock)
		{
			RuleLoadResult<TemplateCollection> ruleLoadResult = (LastStoreLoadResult = XmlRuleStore.ResetToDefault(GetStoreDescriptor(), CreateDefaultTemplateCollection, NormalizeTemplateCollection));
			if (ruleLoadResult.Usable)
			{
				ApplyCollection(ruleLoadResult.Value);
			}
			return ruleLoadResult;
		}
	}

	internal static TemplateCollection NormalizeImportCandidate(TemplateCollection collection)
	{
		return NormalizeTemplateCollection(collection);
	}

	public static void ResetToDefault()
	{
		lock (_lock)
		{
			if (CurrentTemplateIndex != 0)
			{
				ResetCurrentTemplate();
			}
			else
			{
				Current = DeepClone(SystemDefault);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void LoadTemplates()
	{
		RuleLoadResult<TemplateCollection> ruleLoadResult = LoadStoreResult();
		if (ruleLoadResult.Usable)
		{
			ApplyCollection(ruleLoadResult.Value);
			return;
		}
		LogService.Warn("ConfigManager template store unavailable; running with in-memory system default. StoreId=" + ruleLoadResult.StoreId + "; ReasonCode=" + ruleLoadResult.ReasonCode, ruleLoadResult.Error);
		ApplyCollection(new TemplateCollection
		{
			CurrentIndex = 0,
			Templates = new List<FormatConfig>(),
			TemplateNames = new List<string>()
		});
	}

	private static void ApplyCollection(TemplateCollection collection)
	{
		CurrentTemplateIndex = collection.CurrentIndex;
		UserTemplates.Clear();
		UserTemplateNames.Clear();
		for (int i = 0; i < collection.Templates.Count; i++)
		{
			UserTemplates.Add(DeepClone(collection.Templates[i]));
			UserTemplateNames.Add(collection.TemplateNames[i]);
		}
	}

	private static void SaveTemplates()
	{
		EnsureStoreAvailable();
		TemplateCollection templateCollection = new TemplateCollection
		{
			CurrentIndex = CurrentTemplateIndex,
			Templates = new List<FormatConfig>(),
			TemplateNames = new List<string>()
		};
		for (int i = 0; i < UserTemplates.Count; i++)
		{
			templateCollection.Templates.Add(UserTemplates[i]);
			templateCollection.TemplateNames.Add(UserTemplateNames[i]);
		}
		XmlRuleStore.Save(GetStoreDescriptor(), templateCollection, NormalizeTemplateCollection);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureStoreAvailable()
	{
		if (LastStoreLoadResult == null || !LastStoreLoadResult.Usable)
		{
			throw RuleStoreUnavailableException.From(LastStoreLoadResult, "排版模板库");
		}
	}

	private static TemplateCollection CreateDefaultTemplateCollection()
	{
		TemplateCollection templateCollection = new TemplateCollection
		{
			CurrentIndex = 1,
			Templates = new List<FormatConfig>(),
			TemplateNames = new List<string>()
		};
		for (int i = 0; i < 3; i++)
		{
			templateCollection.Templates.Add(DeepClone(SystemDefault));
			templateCollection.TemplateNames.Add(DefaultUserTemplateNames[i]);
		}
		return templateCollection;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static TemplateCollection NormalizeTemplateCollection(TemplateCollection collection)
	{
		if (collection == null)
		{
			throw new InvalidOperationException("Format template collection cannot be empty.");
		}
		if (collection.Templates != null)
		{
			if (collection.Templates.Count <= 9)
			{
				if (collection.CurrentIndex >= 0 && collection.CurrentIndex <= collection.Templates.Count)
				{
					if (collection.TemplateNames == null)
					{
						collection.TemplateNames = new List<string>();
					}
					while (collection.TemplateNames.Count < collection.Templates.Count)
					{
						int count = collection.TemplateNames.Count;
						collection.TemplateNames.Add((count < DefaultUserTemplateNames.Length) ? DefaultUserTemplateNames[count] : ("模板" + (count + 1)));
					}
					if (collection.TemplateNames.Count > collection.Templates.Count)
					{
						collection.TemplateNames.RemoveRange(collection.Templates.Count, collection.TemplateNames.Count - collection.Templates.Count);
					}
					for (int i = 0; i < collection.Templates.Count; i++)
					{
						EnsureNewFeatureDefaults(collection.Templates[i]);
						EnsureValidConfig(collection.Templates[i], "Template " + (i + 1));
						string candidate;
						string text = (candidate = NormalizeTemplateName(collection.TemplateNames[i]));
						int num = 2;
						while (collection.TemplateNames.Take(i).Any((string existing) => string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase)))
						{
							candidate = text + " " + num;
							num++;
						}
						collection.TemplateNames[i] = candidate;
					}
					return collection;
				}
				throw new InvalidOperationException("Current format template index is invalid: " + collection.CurrentIndex);
			}
			throw new InvalidOperationException("User format template count must be between 0 and " + 9 + ".");
		}
		throw new InvalidOperationException("Format template list cannot be empty.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyCurrentTemplate()
	{
		if (CurrentTemplateIndex == 0)
		{
			Current = DeepClone(SystemDefault);
		}
		else
		{
			FormatConfig formatConfig = UserTemplates[CurrentTemplateIndex - 1];
			EnsureValidConfig(formatConfig, "Current template");
			Current = DeepClone(formatConfig);
		}
		EnsureNewFeatureDefaults(Current);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static FormatConfig GetEditableCurrentTemplate()
	{
		if (CurrentTemplateIndex <= 0 || CurrentTemplateIndex > UserTemplates.Count)
		{
			throw new InvalidOperationException("Current editable template index is invalid: " + CurrentTemplateIndex);
		}
		FormatConfig formatConfig = UserTemplates[CurrentTemplateIndex - 1];
		EnsureValidConfig(formatConfig, "Current template");
		return formatConfig;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string NormalizeTemplateName(string name)
	{
		string text = (name ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			throw new InvalidOperationException("模板名称不能为空。");
		}
		if (text.Length > 24)
		{
			throw new InvalidOperationException("模板名称最多 24 个字符。");
		}
		if (string.Equals(text, "系统默认", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("“系统默认”是保留名称，请使用其他名称。");
		}
		return text;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureUniqueTemplateName(string name, int excludedUserIndex)
	{
		for (int i = 0; i < UserTemplateNames.Count; i++)
		{
			if (i != excludedUserIndex && string.Equals(UserTemplateNames[i], name, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("已存在同名模板，请换一个名称。");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static FormatConfig DeepClone(FormatConfig source)
	{
		if (source == null)
		{
			throw new InvalidOperationException("Format config cannot be empty.");
		}
		return source.DeepClone();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureValidConfig(FormatConfig cfg, string name)
	{
		if (cfg == null)
		{
			throw new InvalidOperationException(name + " cannot be empty.");
		}
		EnsureRange(cfg.TopMargin, 0.5f, 10f, name + ".TopMargin");
		EnsureRange(cfg.BottomMargin, 0.5f, 10f, name + ".BottomMargin");
		EnsureRange(cfg.LeftMargin, 0.5f, 10f, name + ".LeftMargin");
		EnsureRange(cfg.RightMargin, 0.5f, 10f, name + ".RightMargin");
		EnsureRange(cfg.HeaderDistance, 0.5f, 5f, name + ".HeaderDistance");
		EnsureRange(cfg.FooterDistance, 0.5f, 5f, name + ".FooterDistance");
		EnsureMode(cfg.YiShiMode, name + ".YiShiMode");
		EnsureMode(cfg.YiYaoMode, name + ".YiYaoMode");
		EnsureMode(cfg.DiYiMode, name + ".DiYiMode");
		if (cfg.SpacingUnitVersion != 2)
		{
			throw new InvalidOperationException(name + ".SpacingUnitVersion must be 2.");
		}
		EnsureValidTextStyle(cfg.MainTitle, name + ".MainTitle");
		EnsureValidTextStyle(cfg.Level1, name + ".Level1");
		EnsureValidTextStyle(cfg.Level2, name + ".Level2");
		EnsureValidTextStyle(cfg.Level3, name + ".Level3");
		EnsureValidTextStyle(cfg.Body, name + ".Body");
		if (cfg.AttachmentOptions != null)
		{
			if (cfg.TableOptions != null)
			{
				if (cfg.DocumentGridOptions != null)
				{
					if (cfg.SignatureOptions != null)
					{
						if (cfg.ImageOptions != null)
						{
							EnsureIntRange(cfg.DocumentGridOptions.LinesPerPage, 1, 50, name + ".DocumentGridOptions.LinesPerPage");
							EnsureIntRange(cfg.DocumentGridOptions.CharsPerLine, 1, 50, name + ".DocumentGridOptions.CharsPerLine");
							EnsureIntRange(cfg.SignatureOptions.BlankLinesBefore, 0, 10, name + ".SignatureOptions.BlankLinesBefore");
							EnsureRange(cfg.ImageOptions.WidthCm, 0.1f, 100f, name + ".ImageOptions.WidthCm");
							EnsureRange(cfg.ImageOptions.HeightCm, 0.1f, 100f, name + ".ImageOptions.HeightCm");
							EnsureRange(cfg.ImageOptions.MaxWidthCm, 0.1f, 100f, name + ".ImageOptions.MaxWidthCm");
							EnsureRange(cfg.ImageOptions.MaxHeightCm, 0.1f, 100f, name + ".ImageOptions.MaxHeightCm");
							EnsureRange(cfg.ImageOptions.ScalePercent, 10f, 300f, name + ".ImageOptions.ScalePercent");
							EnsureRange(cfg.ImageOptions.MinimumWidthCm, 0f, 100f, name + ".ImageOptions.MinimumWidthCm");
							EnsureRange(cfg.ImageOptions.MinimumHeightCm, 0f, 100f, name + ".ImageOptions.MinimumHeightCm");
							EnsureRange(cfg.ImageOptions.DistanceTopCm, 0f, 20f, name + ".ImageOptions.DistanceTopCm");
							EnsureRange(cfg.ImageOptions.DistanceBottomCm, 0f, 20f, name + ".ImageOptions.DistanceBottomCm");
							EnsureRange(cfg.ImageOptions.DistanceLeftCm, 0f, 20f, name + ".ImageOptions.DistanceLeftCm");
							EnsureRange(cfg.ImageOptions.DistanceRightCm, 0f, 20f, name + ".ImageOptions.DistanceRightCm");
							EnsureRange(cfg.ImageOptions.RotationDegrees, -180f, 180f, name + ".ImageOptions.RotationDegrees");
							EnsureIntRange(cfg.ImageOptions.CaptionSpaceBefore, 0, 100, name + ".ImageOptions.CaptionSpaceBefore");
							EnsureIntRange(cfg.ImageOptions.CaptionSpaceAfter, 0, 100, name + ".ImageOptions.CaptionSpaceAfter");
							if (ImageSizeModes.IsValid(cfg.ImageOptions.SizeMode))
							{
								if (ImageWrapModes.IsValid(cfg.ImageOptions.WrapMode))
								{
									if (!ImageAlignmentModes.IsValid(cfg.ImageOptions.AlignmentMode))
									{
										throw new InvalidOperationException(name + ".ImageOptions.AlignmentMode is invalid.");
									}
									if (!ImageBorderModes.IsValid(cfg.ImageOptions.BorderMode))
									{
										throw new InvalidOperationException(name + ".ImageOptions.BorderMode is invalid.");
									}
									if (!ImageParagraphFilterModes.IsValid(cfg.ImageOptions.ParagraphFilterMode))
									{
										throw new InvalidOperationException(name + ".ImageOptions.ParagraphFilterMode is invalid.");
									}
									if (string.IsNullOrWhiteSpace(cfg.ImageOptions.CaptionFontName))
									{
										throw new InvalidOperationException(name + ".ImageOptions.CaptionFontName cannot be empty.");
									}
									if (string.IsNullOrWhiteSpace(cfg.ImageOptions.CaptionFontSize))
									{
										throw new InvalidOperationException(name + ".ImageOptions.CaptionFontSize cannot be empty.");
									}
									EnsureValidCompilationOptions(cfg, name);
									return;
								}
								throw new InvalidOperationException(name + ".ImageOptions.WrapMode is invalid.");
							}
							throw new InvalidOperationException(name + ".ImageOptions.SizeMode is invalid.");
						}
						throw new InvalidOperationException(name + ".ImageOptions cannot be empty.");
					}
					throw new InvalidOperationException(name + ".SignatureOptions cannot be empty.");
				}
				throw new InvalidOperationException(name + ".DocumentGridOptions cannot be empty.");
			}
			throw new InvalidOperationException(name + ".TableOptions cannot be empty.");
		}
		throw new InvalidOperationException(name + ".AttachmentOptions cannot be empty.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static void ValidateCompilationOptionsForEdit(CompilationFormatOptions options)
	{
		if (options == null)
		{
			throw new InvalidOperationException("CompilationFormatOptions cannot be empty.");
		}
		EnsureValidCompilationOptions(new FormatConfig
		{
			CompilationFormatOptions = options
		}, "汇编参数");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureValidCompilationOptions(FormatConfig cfg, string name)
	{
		string text = name + ".CompilationFormatOptions";
		CompilationFormatOptions compilationFormatOptions = cfg.CompilationFormatOptions;
		if (compilationFormatOptions != null)
		{
			if (!Enum.IsDefined(typeof(CompilationFrontMatterMode), compilationFormatOptions.FrontMatterMode))
			{
				throw new InvalidOperationException(text + ".FrontMatterMode is invalid: " + (int)compilationFormatOptions.FrontMatterMode);
			}
			if (!Enum.IsDefined(typeof(CompilationTocPosition), compilationFormatOptions.TocPosition))
			{
				throw new InvalidOperationException(text + ".TocPosition is invalid: " + (int)compilationFormatOptions.TocPosition);
			}
			if (!Enum.IsDefined(typeof(CompilationPageNumberMode), compilationFormatOptions.PageNumberMode))
			{
				throw new InvalidOperationException(text + ".PageNumberMode is invalid: " + (int)compilationFormatOptions.PageNumberMode);
			}
			if (compilationFormatOptions.OptionsVersion >= 1 && compilationFormatOptions.OptionsVersion <= 1)
			{
				if (compilationFormatOptions.TocOptions == null)
				{
					throw new InvalidOperationException(text + ".TocOptions cannot be empty.");
				}
				CompilationTocOptionsValidator.Validate(compilationFormatOptions.TocOptions, text + ".TocOptions");
				return;
			}
			throw new InvalidOperationException(text + ".OptionsVersion is unsupported: " + compilationFormatOptions.OptionsVersion);
		}
		throw new InvalidOperationException(text + " cannot be empty.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureNewFeatureDefaults(FormatConfig cfg)
	{
		if (cfg != null)
		{
			EnsureTextStyleDefaults(cfg.MainTitle, "规范标题", "居中", "1级");
			EnsureTextStyleDefaults(cfg.Level1, "一、XX", "两端对齐", "2级");
			EnsureTextStyleDefaults(cfg.Level2, "（一）XX", "两端对齐", "3级");
			EnsureTextStyleDefaults(cfg.Level3, "1.XX", "两端对齐", "4级");
			EnsureTextStyleDefaults(cfg.Body, "", "两端对齐", "正文文本");
			cfg.PageNumberMode = PageNumberModes.Normalize(cfg.PageNumberMode, cfg.EnablePageNumbers);
			cfg.EnablePageNumbers = PageNumberModes.IsEnabled(cfg.PageNumberMode, cfg.EnablePageNumbers);
			FormatOptionMigrationPolicy.Normalize(cfg);
			if (cfg.AttachmentOptions != null)
			{
				if (cfg.TableOptions == null)
				{
					throw new InvalidOperationException("Table options cannot be empty.");
				}
				if (cfg.TableOptions.OptionsVersion < 1)
				{
					cfg.TableOptions.ClearCellIndents = true;
					cfg.TableOptions.OptionsVersion = 1;
				}
				if (cfg.TableOptions.HeaderRows < 0)
				{
					cfg.TableOptions.HeaderRows = 1;
				}
				if (cfg.TableOptions.HeaderRows > 5)
				{
					cfg.TableOptions.HeaderRows = 5;
				}
				if (!(cfg.TableOptions.RowHeight >= 0f))
				{
					cfg.TableOptions.RowHeight = 30f;
				}
				if (cfg.TableOptions.ColumnWidth < 0f)
				{
					cfg.TableOptions.ColumnWidth = 2f;
				}
				if (!(cfg.TableOptions.CellTopPadding >= 0f))
				{
					cfg.TableOptions.CellTopPadding = 0f;
				}
				if (!(cfg.TableOptions.CellBottomPadding >= 0f))
				{
					cfg.TableOptions.CellBottomPadding = 0f;
				}
				if (!(cfg.TableOptions.CellLeftPadding >= 0f))
				{
					cfg.TableOptions.CellLeftPadding = 0f;
				}
				if (cfg.TableOptions.CellRightPadding < 0f)
				{
					cfg.TableOptions.CellRightPadding = 0f;
				}
				if (string.IsNullOrWhiteSpace(cfg.TableOptions.HeaderFontName))
				{
					cfg.TableOptions.HeaderFontName = "黑体";
				}
				if (string.IsNullOrWhiteSpace(cfg.TableOptions.HeaderFontSize))
				{
					cfg.TableOptions.HeaderFontSize = "小四";
				}
				if (string.IsNullOrWhiteSpace(cfg.TableOptions.BodyFontName))
				{
					cfg.TableOptions.BodyFontName = "仿宋_GB2312";
				}
				if (string.IsNullOrWhiteSpace(cfg.TableOptions.BodyFontSize))
				{
					cfg.TableOptions.BodyFontSize = "小四";
				}
				if (string.IsNullOrWhiteSpace(cfg.TableOptions.HeaderAlignment))
				{
					cfg.TableOptions.HeaderAlignment = "居中";
				}
				if (string.IsNullOrWhiteSpace(cfg.TableOptions.BodyAlignment))
				{
					cfg.TableOptions.BodyAlignment = "两端对齐";
				}
				if (string.IsNullOrWhiteSpace(cfg.TableOptions.TableAlignment))
				{
					cfg.TableOptions.TableAlignment = "Center";
				}
				if (string.IsNullOrWhiteSpace(cfg.TableOptions.TextWrapping))
				{
					cfg.TableOptions.TextWrapping = "None";
				}
				if (string.IsNullOrWhiteSpace(cfg.TableOptions.RowHeightMode))
				{
					cfg.TableOptions.RowHeightMode = "AtLeast";
				}
				if (string.IsNullOrWhiteSpace(cfg.TableOptions.ColumnWidthMode))
				{
					cfg.TableOptions.ColumnWidthMode = "Window";
				}
				if (string.IsNullOrWhiteSpace(cfg.AttachmentOptions.AttachmentMarkerFontName))
				{
					cfg.AttachmentOptions.AttachmentMarkerFontName = "方正小标宋简体";
				}
				if (string.IsNullOrWhiteSpace(cfg.AttachmentOptions.AttachmentMarkerFontSize))
				{
					cfg.AttachmentOptions.AttachmentMarkerFontSize = "二号";
				}
				if (string.IsNullOrWhiteSpace(cfg.AttachmentOptions.AttachmentBodyFontName))
				{
					cfg.AttachmentOptions.AttachmentBodyFontName = "仿宋_GB2312";
				}
				if (string.IsNullOrWhiteSpace(cfg.AttachmentOptions.AttachmentBodyFontSize))
				{
					cfg.AttachmentOptions.AttachmentBodyFontSize = "三号";
				}
				if (cfg.CompilationFormatOptions == null)
				{
					cfg.EnableCompilationFormatting = false;
					cfg.CompilationFormatOptions = new CompilationFormatOptions();
					return;
				}
				if (cfg.CompilationFormatOptions.OptionsVersion < 1)
				{
					cfg.CompilationFormatOptions.OptionsVersion = 1;
				}
				if (cfg.CompilationFormatOptions.TocOptions == null)
				{
					cfg.CompilationFormatOptions.TocOptions = new CompilationTocOptions();
				}
				return;
			}
			throw new InvalidOperationException("Attachment options cannot be empty.");
		}
		throw new InvalidOperationException("Format config cannot be empty.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureTextStyleDefaults(TextStyle style, string recognitionStyle, string alignment, string outlineLevel)
	{
		if (style != null)
		{
			bool num = string.IsNullOrWhiteSpace(style.RecognitionStyle);
			if (num)
			{
				style.RecognitionStyle = recognitionStyle;
			}
			if (num && recognitionStyle == "规范标题" && alignment == "居中" && style.Alignment == "两端对齐")
			{
				style.Alignment = alignment;
			}
			if (string.IsNullOrWhiteSpace(style.Alignment))
			{
				style.Alignment = alignment;
			}
			if (string.IsNullOrWhiteSpace(style.OutlineLevel))
			{
				style.OutlineLevel = outlineLevel;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static AttachmentFormatOptions CloneAttachmentOptions(AttachmentFormatOptions source)
	{
		if (source == null)
		{
			throw new InvalidOperationException("Attachment options cannot be empty.");
		}
		return new AttachmentFormatOptions
		{
			FormatAttachmentList = source.FormatAttachmentList,
			FormatAttachmentBody = source.FormatAttachmentBody,
			AttachmentMarkerFontName = source.AttachmentMarkerFontName,
			AttachmentMarkerFontSize = source.AttachmentMarkerFontSize,
			AttachmentBodyFontName = source.AttachmentBodyFontName,
			AttachmentBodyFontSize = source.AttachmentBodyFontSize
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static TableFormatOptions CloneTableOptions(TableFormatOptions source)
	{
		if (source == null)
		{
			throw new InvalidOperationException("Table options cannot be empty.");
		}
		return new TableFormatOptions
		{
			OptionsVersion = source.OptionsVersion,
			HeaderRows = source.HeaderRows,
			FitWindow = source.FitWindow,
			UseBorders = source.UseBorders,
			ShadeHeader = source.ShadeHeader,
			RepeatHeaderRows = source.RepeatHeaderRows,
			ClearCellIndents = source.ClearCellIndents,
			HeaderFontName = source.HeaderFontName,
			HeaderFontSize = source.HeaderFontSize,
			BodyFontName = source.BodyFontName,
			BodyFontSize = source.BodyFontSize,
			HeaderAlignment = source.HeaderAlignment,
			BodyAlignment = source.BodyAlignment,
			TableAlignment = source.TableAlignment,
			TextWrapping = source.TextWrapping,
			RowHeightMode = source.RowHeightMode,
			ColumnWidthMode = source.ColumnWidthMode,
			RowHeight = source.RowHeight,
			ColumnWidth = source.ColumnWidth,
			CellTopPadding = source.CellTopPadding,
			CellBottomPadding = source.CellBottomPadding,
			CellLeftPadding = source.CellLeftPadding,
			CellRightPadding = source.CellRightPadding
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static ImageFormatOptions CloneImageOptions(ImageFormatOptions source)
	{
		if (source == null)
		{
			throw new InvalidOperationException("Image options cannot be empty.");
		}
		return new ImageFormatOptions
		{
			OptionsVersion = source.OptionsVersion,
			SizeMode = source.SizeMode,
			WrapMode = source.WrapMode,
			AlignmentMode = source.AlignmentMode,
			BorderMode = source.BorderMode,
			KeepAspectRatio = source.KeepAspectRatio,
			MainStoryOnly = source.MainStoryOnly,
			IncludeTableCellImages = source.IncludeTableCellImages,
			IncludeLinkedPictures = source.IncludeLinkedPictures,
			ParagraphFilterMode = source.ParagraphFilterMode,
			MinimumWidthCm = source.MinimumWidthCm,
			MinimumHeightCm = source.MinimumHeightCm,
			WidthCm = source.WidthCm,
			HeightCm = source.HeightCm,
			MaxWidthCm = source.MaxWidthCm,
			MaxHeightCm = source.MaxHeightCm,
			ScalePercent = source.ScalePercent,
			ApplyWrapDistances = source.ApplyWrapDistances,
			DistanceTopCm = source.DistanceTopCm,
			DistanceBottomCm = source.DistanceBottomCm,
			DistanceLeftCm = source.DistanceLeftCm,
			DistanceRightCm = source.DistanceRightCm,
			ApplyRotation = source.ApplyRotation,
			RotationDegrees = source.RotationDegrees,
			FormatExistingCaptions = source.FormatExistingCaptions,
			CaptionFontName = source.CaptionFontName,
			CaptionFontSize = source.CaptionFontSize,
			CaptionBold = source.CaptionBold,
			CaptionSpaceBefore = source.CaptionSpaceBefore,
			CaptionSpaceAfter = source.CaptionSpaceAfter
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureRange(float value, float min, float max, string name)
	{
		if (value < min || value > max)
		{
			throw new InvalidOperationException(name + " is out of range: " + value);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureIntRange(int value, int min, int max, string name)
	{
		if (value >= min && value <= max)
		{
			return;
		}
		throw new InvalidOperationException(name + " is out of range: " + value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureMode(int value, string name)
	{
		if (value >= 0 && value <= 2)
		{
			return;
		}
		throw new InvalidOperationException(name + " is out of range: " + value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureValidTextStyle(TextStyle style, string name)
	{
		if (style != null)
		{
			if (!string.IsNullOrWhiteSpace(style.FontName))
			{
				if (string.IsNullOrWhiteSpace(style.FontSize))
				{
					throw new InvalidOperationException(name + ".FontSize cannot be empty.");
				}
				if (OutlineLevels.IsValid(style.OutlineLevel))
				{
					return;
				}
				throw new InvalidOperationException(name + ".OutlineLevel is invalid: " + style.OutlineLevel);
			}
			throw new InvalidOperationException(name + ".FontName cannot be empty.");
		}
		throw new InvalidOperationException(name + " cannot be empty.");
	}
}
