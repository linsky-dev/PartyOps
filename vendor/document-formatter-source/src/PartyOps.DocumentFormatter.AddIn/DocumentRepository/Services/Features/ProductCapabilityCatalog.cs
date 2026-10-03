using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DocumentRepository.Models.Features;

namespace DocumentRepository.Services.Features;

/// <summary>
/// 固化产品验收能力与实际实现类型之间的映射，防止恢复工程在后续重构中出现功能空壳。
/// </summary>
public static class ProductCapabilityCatalog
{
	private static readonly Lazy<IReadOnlyList<ProductCapabilityDescriptor>> Capabilities =
		new Lazy<IReadOnlyList<ProductCapabilityDescriptor>>(() => CreateDescriptors().AsReadOnly());

	public static IReadOnlyList<ProductCapabilityDescriptor> GetAll()
	{
		return Capabilities.Value;
	}

	public static FeatureRegistryValidationResult Validate()
	{
		FeatureRegistryValidationResult result = new FeatureRegistryValidationResult();
		IReadOnlyList<ProductCapabilityDescriptor> all = GetAll();
		HashSet<string> featureIds = new HashSet<string>(
			FeatureRegistry.GetAll().Select(feature => feature.Id),
			StringComparer.OrdinalIgnoreCase);
		Assembly assembly = typeof(ProductCapabilityCatalog).Assembly;

		foreach (ProductCapabilityDescriptor capability in all)
		{
			if (string.IsNullOrWhiteSpace(capability.FeatureId))
			{
				result.AddError("产品能力缺少功能编号：" + capability.CapabilityId);
			}
			else if (!featureIds.Contains(capability.FeatureId))
			{
				result.AddError("产品能力引用了未注册功能：" + capability.CapabilityId + " -> " + capability.FeatureId);
			}
			if (string.IsNullOrWhiteSpace(capability.CapabilityId))
			{
				result.AddError("产品能力编号不能为空。");
			}
			if (string.IsNullOrWhiteSpace(capability.Description))
			{
				result.AddError("产品能力说明不能为空：" + capability.CapabilityId);
			}
			if (capability.OwnerTypeNames == null || capability.OwnerTypeNames.Count == 0)
			{
				result.AddError("产品能力没有源码责任类型：" + capability.CapabilityId);
				continue;
			}
			foreach (string ownerTypeName in capability.OwnerTypeNames)
			{
				if (string.IsNullOrWhiteSpace(ownerTypeName) || assembly.GetType(ownerTypeName, false, false) == null)
				{
					result.AddError("产品能力的源码责任类型不存在：" + capability.CapabilityId + " -> " + ownerTypeName);
				}
			}
		}

		foreach (string duplicate in all
			.Where(capability => !string.IsNullOrWhiteSpace(capability.CapabilityId))
			.GroupBy(capability => capability.CapabilityId, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key))
		{
			result.AddError("产品能力编号重复：" + duplicate);
		}

		foreach (string featureId in featureIds)
		{
			if (!all.Any(capability => string.Equals(capability.FeatureId, featureId, StringComparison.OrdinalIgnoreCase)))
			{
				result.AddError("注册功能没有产品能力映射：" + featureId);
			}
		}
		return result;
	}

	private static List<ProductCapabilityDescriptor> CreateDescriptors()
	{
		return new List<ProductCapabilityDescriptor>
		{
			Capability("format", "format.element-recognition", "识别主标题、副标题、层级标题、正文、附件、落款和日期。", true,
				"DocumentRepository.Services.Analysis.DocumentAnalysisService",
				"DocumentRepository.Services.Analysis.ParagraphElementClassifier",
				"DocumentRepository.Services.Detection.MainTitleCandidateDetector",
				"DocumentRepository.Services.Detection.AttachmentDetector",
				"DocumentRepository.Services.Detection.SignatureDetector"),
			Capability("format", "format.execution-scopes", "支持全文、普通选区和汇编文章范围排版。", true,
				"DocumentRepository.Pipelines.Format.FormatPipeline",
				"DocumentRepository.Services.Formatting.Planning.FormatExecutionPlanBuilder",
				"DocumentRepository.Services.Formatting.SelectionFormattingScopeResolver"),
			Capability("format", "format.templates", "支持系统默认参数及多套用户排版模板切换。", false,
				"DocumentRepository.ConfigManager",
				"DocumentRepository.TemplateCollection"),
			Capability("format", "format.page-layout", "设置页边距、文档网格和页码。", true,
				"DocumentRepository.Services.Formatting.DocumentGridCompatibilityPolicy",
				"DocumentRepository.Services.Formatting.PageNumbers.PageNumberConfigurationInspector"),
			Capability("format", "format.images-and-tables", "按模板参数规划并执行图片和表格排版。", true,
				"DocumentRepository.Services.Formatting.Images.ImageFormattingPlanBuilder",
				"DocumentRepository.Services.Formatting.Tables.TableFormattingService"),

			Capability("replace", "replace.text-and-regex", "执行普通文字替换和带超时保护的正则替换。", true,
				"DocumentRepository.Services.Replace.TextReplaceService",
				"DocumentRepository.Services.Replace.ReplaceRegexService"),
			Capability("replace", "replace.wildcard", "执行 Word/WPS 通配符替换。", true,
				"DocumentRepository.Services.Replace.WildcardReplaceService"),
			Capability("replace", "replace.format", "按字体和段落条件执行格式替换。", true,
				"DocumentRepository.Services.Replace.FormatReplaceService"),
			Capability("replace", "replace.saved-plans", "保存、切换并自愈多套替换方案。", false,
				"DocumentRepository.Services.Replace.ReplacePlanService"),
			Capability("replace", "replace.batch-rules", "一次规划并执行同一方案中的多项替换任务。", true,
				"DocumentRepository.Pipelines.Replace.ReplacePipeline",
				"DocumentRepository.Services.Replace.ReplaceStagePlanner"),

			Capability("redheader", "redheader.document-types", "提供下行文、上行文和便函三类内置模板。", false,
				"DocumentRepository.Services.RedHeader.RedHeaderTemplateService"),
			Capability("redheader", "redheader.top-marks", "设置份号、密级、保密期限和紧急程度。", false,
				"DocumentRepository.Models.RedHeader.RedHeaderTopMarkOptions",
				"DocumentRepository.Services.RedHeader.RedHeaderTemplateValidator"),
			Capability("redheader", "redheader.agency-and-number", "生成发文机关、发文字号和签发人区域。", true,
				"DocumentRepository.Services.RedHeader.RedHeaderGenerationService",
				"DocumentRepository.Services.RedHeader.RedHeaderLayoutPlanner"),
			Capability("redheader", "redheader.red-line-and-imprint", "生成红线及版记页面布局。", true,
				"DocumentRepository.Services.RedHeader.RedHeaderLayoutPlanner",
				"DocumentRepository.Services.RedHeader.RedHeaderImprintPagePlanner"),

			Capability("rename", "rename.content-analysis", "从文档中提取标题、发文字号、副标题和日期。", true,
				"DocumentRepository.Services.Rename.RenameAnalysisService"),
			Capability("rename", "rename.composable-rules", "自由组合内容部件、自定义文字、日期和轮替词。", false,
				"DocumentRepository.RenameRulePart",
				"DocumentRepository.RenameRuleManager",
				"DocumentRepository.Services.Rename.RenamePlanBuilder"),
			Capability("rename", "rename.online", "在当前文档保持打开的情况下执行在线重命名。", true,
				"DocumentRepository.Services.Rename.RenameExecutor",
				"DocumentRepository.Services.Rename.SaveRenameService"),

			Capability("convert", "convert.document-formats", "导出 DOCX、PDF 和 TXT。", true,
				"DocumentRepository.Services.Conversion.DocumentExportService",
				"DocumentRepository.Services.Conversion.ConvertExecutor"),
			Capability("convert", "convert.image-modes", "导出分页图片或长图并执行内存预算保护。", true,
				"DocumentRepository.Services.Conversion.ImageConversionService",
				"DocumentRepository.Services.Conversion.ImageConversionBudgetService"),
			Capability("convert", "convert.page-selection", "支持全部页面、连续范围和离散指定页面。", false,
				"DocumentRepository.Services.Conversion.PageSelectionParser"),
			Capability("convert", "convert.output-policy", "支持图片格式、清晰度、保存位置及同名文件策略。", false,
				"DocumentRepository.Services.Conversion.OutputPathService",
				"DocumentRepository.Services.Conversion.ConvertPlanBuilder"),

			Capability("pdf-to-word", "pdf-to-word.local-reading", "使用本地 PDF 引擎读取文本、图像和页面几何。", false,
				"DocumentRepository.Services.Conversion.PdfToWord.PdfReadingAdapter",
				"DocumentRepository.Services.Conversion.PdfToWord.LocalPdfToWordEngine"),
			Capability("pdf-to-word", "pdf-to-word.layout-reconstruction", "重建阅读顺序、段落和 DOCX 文档结构。", false,
				"DocumentRepository.Services.Conversion.PdfToWord.LayoutReconstructionService",
				"DocumentRepository.Services.Conversion.PdfToWord.DocxWriter"),
			Capability("pdf-to-word", "pdf-to-word.tables", "分析表格候选并规划跨页续表。", false,
				"DocumentRepository.Services.Conversion.PdfToWord.Tables.TableCandidateAnalyzer",
				"DocumentRepository.Services.Conversion.PdfToWord.Tables.CrossPageTableContinuationPlanner"),
			Capability("pdf-to-word", "pdf-to-word.verification", "转换完成后校验 DOCX 结果。", false,
				"DocumentRepository.Services.Conversion.PdfToWordVerifier")
		};
	}

	private static ProductCapabilityDescriptor Capability(string featureId, string capabilityId, string description, bool requiresOfficeHost, params string[] ownerTypeNames)
	{
		ProductCapabilityDescriptor descriptor = new ProductCapabilityDescriptor
		{
			FeatureId = featureId,
			CapabilityId = capabilityId,
			Description = description,
			RequiresOfficeHost = requiresOfficeHost
		};
		foreach (string ownerTypeName in ownerTypeNames)
		{
			descriptor.OwnerTypeNames.Add(ownerTypeName);
		}
		return descriptor;
	}
}
