using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DocumentRepository.Commands;
using DocumentRepository.Models.Features;
using DocumentRepository.Models.Rules;
using DocumentRepository.Services.Rules;

namespace DocumentRepository.Services.Features;

public static class FeatureRegistry
{
	private static readonly Lazy<IReadOnlyList<FeatureDescriptor>> Features = new Lazy<IReadOnlyList<FeatureDescriptor>>(() => CreateDescriptors().AsReadOnly());

	public static IReadOnlyList<FeatureDescriptor> GetAll()
	{
		return Features.Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static FeatureDescriptor GetById(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			throw new ArgumentException("Feature id cannot be empty.", "id");
		}
		return GetAll().FirstOrDefault((FeatureDescriptor x) => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Feature descriptor does not exist: " + id);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static FeatureRegistryValidationResult Validate()
	{
		FeatureRegistryValidationResult featureRegistryValidationResult = new FeatureRegistryValidationResult();
		IReadOnlyList<FeatureDescriptor> all = GetAll();
		if (!all.Any())
		{
			featureRegistryValidationResult.AddError("Feature registry is empty.");
		}
		ValidateRequiredFields(all, featureRegistryValidationResult);
		ValidateUniqueIds(all, featureRegistryValidationResult);
		ValidateCommandTypes(all, featureRegistryValidationResult);
		ValidateRuleCatalogLinks(all, featureRegistryValidationResult);
		FeatureRegistryValidationResult productCapabilityResult = ProductCapabilityCatalog.Validate();
		foreach (string error in productCapabilityResult.Errors)
		{
			featureRegistryValidationResult.AddError(error);
		}
		return featureRegistryValidationResult;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void AssertValid()
	{
		FeatureRegistryValidationResult featureRegistryValidationResult = Validate();
		if (!featureRegistryValidationResult.Success)
		{
			throw new InvalidOperationException("Feature registry validation failed: " + string.Join("; ", featureRegistryValidationResult.Errors));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateRequiredFields(IEnumerable<FeatureDescriptor> descriptors, FeatureRegistryValidationResult result)
	{
		foreach (FeatureDescriptor descriptor in descriptors)
		{
			if (string.IsNullOrWhiteSpace(descriptor.Id))
			{
				result.AddError("Feature id cannot be empty.");
			}
			if (string.IsNullOrWhiteSpace(descriptor.DisplayName))
			{
				result.AddError("Feature display name cannot be empty: " + descriptor.Id);
			}
			if (string.IsNullOrWhiteSpace(descriptor.CommandType))
			{
				result.AddError("Feature command type cannot be empty: " + descriptor.Id);
			}
			if (string.IsNullOrWhiteSpace(descriptor.CommandClassName))
			{
				result.AddError("Feature command class name cannot be empty: " + descriptor.Id);
			}
			if (string.IsNullOrWhiteSpace(descriptor.PermissionCommandName))
			{
				result.AddError("Feature permission command name cannot be empty: " + descriptor.Id);
			}
			if (string.IsNullOrWhiteSpace(descriptor.IconName))
			{
				result.AddError("Feature icon name cannot be empty: " + descriptor.Id);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateUniqueIds(IEnumerable<FeatureDescriptor> descriptors, FeatureRegistryValidationResult result)
	{
		foreach (string item in from g in descriptors.Where((FeatureDescriptor x) => !string.IsNullOrWhiteSpace(x.Id)).GroupBy<FeatureDescriptor, string>((FeatureDescriptor x) => x.Id, StringComparer.OrdinalIgnoreCase)
			where g.Count() > 1
			select g.Key)
		{
			result.AddError("Duplicate feature id: " + item);
		}
		foreach (string item2 in from @group in descriptors.Where((FeatureDescriptor x) => !string.IsNullOrWhiteSpace(x.CommandType)).GroupBy<FeatureDescriptor, string>((FeatureDescriptor x) => x.CommandType, StringComparer.OrdinalIgnoreCase)
			where @group.Count() > 1
			select @group.Key)
		{
			result.AddError("Duplicate feature command type: " + item2);
		}
		foreach (string item3 in from @group in descriptors.Where((FeatureDescriptor x) => !string.IsNullOrWhiteSpace(x.CommandClassName)).GroupBy<FeatureDescriptor, string>((FeatureDescriptor x) => x.CommandClassName, StringComparer.OrdinalIgnoreCase)
			where @group.Count() > 1
			select @group.Key)
		{
			result.AddError("Duplicate feature command class: " + item3);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateCommandTypes(IEnumerable<FeatureDescriptor> descriptors, FeatureRegistryValidationResult result)
	{
		foreach (FeatureDescriptor descriptor in descriptors)
		{
			if (!string.IsNullOrWhiteSpace(descriptor.CommandClassName))
			{
				Type type = FeatureCommandFactory.ResolveCommandType(descriptor.CommandClassName.Trim());
				if (type == null)
				{
					result.AddError("Feature command class does not exist: " + descriptor.Id + " -> " + descriptor.CommandClassName);
				}
				else if (!typeof(ICommand).IsAssignableFrom(type))
				{
					result.AddError("Feature command class does not implement ICommand: " + descriptor.Id + " -> " + descriptor.CommandClassName);
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateRuleCatalogLinks(IEnumerable<FeatureDescriptor> descriptors, FeatureRegistryValidationResult result)
	{
		HashSet<string> hashSet = new HashSet<string>(from x in descriptors
			where !string.IsNullOrWhiteSpace(x.RuleCatalogId)
			select x.RuleCatalogId, StringComparer.OrdinalIgnoreCase);
		foreach (FeatureDescriptor item in descriptors.Where((FeatureDescriptor x) => !string.IsNullOrWhiteSpace(x.RuleCatalogId)))
		{
			try
			{
				RuleCatalogService.GetById(item.RuleCatalogId);
			}
			catch (Exception ex)
			{
				result.AddError("Feature references invalid rule catalog id: " + item.Id + " -> " + item.RuleCatalogId + ". " + ex.Message);
			}
		}
		foreach (RuleCatalogItem item2 in RuleCatalogService.GetCatalog())
		{
			if (!hashSet.Contains(item2.Id))
			{
				result.AddError("Rule catalog item has no feature mapping: " + item2.Id);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<FeatureDescriptor> CreateDescriptors()
	{
		return new List<FeatureDescriptor>
		{
			new FeatureDescriptor
			{
				Id = "format",
				DisplayName = "一键排版",
				CommandType = "Format",
				CommandClassName = "FormatCommand",
				RuleCatalogId = "format.templates",
				PermissionCommandName = "一键排版",
				IconName = "format_all.png",
				RequiresDocument = true,
				IsLongRunning = true,
				SupportsBatch = false,
				Notes = "按排版模板参数执行当前文档或选区排版。"
			},
			new FeatureDescriptor
			{
				Id = "replace",
				DisplayName = "一键替换",
				CommandType = "Replace",
				CommandClassName = "ReplaceCommand",
				RuleCatalogId = "replace.plans",
				PermissionCommandName = "一键替换",
				IconName = "replace.png",
				RequiresDocument = true,
				IsLongRunning = false,
				SupportsBatch = false,
				Notes = "按替换方案执行文字或格式替换。"
			},
			new FeatureDescriptor
			{
				Id = "redheader",
				DisplayName = "一键套红",
				CommandType = "RedHeader",
				CommandClassName = "RedHeaderCommand",
				RuleCatalogId = "redheader.templates",
				PermissionCommandName = "一键套红",
				IconName = "red_header.png",
				RequiresDocument = true,
				IsLongRunning = false,
				SupportsBatch = false,
				Notes = "按红头模板参数生成红头、红线和版记。"
			},
			new FeatureDescriptor
			{
				Id = "rename",
				DisplayName = "一键命名",
				CommandType = "Rename",
				CommandClassName = "RenameCommand",
				RuleCatalogId = "rename.rules",
				PermissionCommandName = "一键命名",
				IconName = "rename.png",
				RequiresDocument = true,
				IsLongRunning = false,
				SupportsBatch = false,
				Notes = "按命名规则识别标题、字号、时间等元素并生成文件名。"
			},
			new FeatureDescriptor
			{
				Id = "convert",
				DisplayName = "一键转换",
				CommandType = "Convert",
				CommandClassName = "ConvertCommand",
				RuleCatalogId = "convert.options",
				PermissionCommandName = "一键转换",
				IconName = "convert.png",
				RequiresDocument = true,
				IsLongRunning = true,
				SupportsBatch = false,
				Notes = "按转换参数导出 DOCX、PDF、TXT 或图片。"
			},
			new FeatureDescriptor
			{
				Id = "pdf-to-word",
				DisplayName = "PDF 转 Word",
				CommandType = "PdfToWord",
				CommandClassName = "PdfToWordCommand",
				RuleCatalogId = "convert.options",
				PermissionCommandName = "一键转换",
				IconName = "convert.png",
				RequiresDocument = false,
				IsLongRunning = true,
				SupportsBatch = false,
				Notes = "当前文档为 PDF 时直接转换，否则提示选择本地 PDF。"
			}
		};
	}
}
