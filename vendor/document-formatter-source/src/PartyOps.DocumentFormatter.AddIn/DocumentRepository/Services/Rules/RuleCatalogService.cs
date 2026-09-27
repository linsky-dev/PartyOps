using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Rules;
using DocumentRepository.Services.Conversion;
using DocumentRepository.Services.RedHeader;
using DocumentRepository.Services.Replace;

namespace DocumentRepository.Services.Rules;

public static class RuleCatalogService
{
	public static IReadOnlyList<RuleCatalogItem> GetCatalog()
	{
		return CreateCatalog().AsReadOnly();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleCatalogItem GetById(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			throw new ArgumentException("Rule catalog id cannot be empty.", "id");
		}
		return GetCatalog().FirstOrDefault((RuleCatalogItem x) => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Rule catalog item does not exist: " + id);
	}

	public static RuleValidationResult ValidateCatalog()
	{
		return RuleValidationService.ValidateCatalog(GetCatalog());
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<RuleCatalogItem> CreateCatalog()
	{
		return new List<RuleCatalogItem>
		{
			CreateUserXmlItem(ConfigManager.GetStoreDescriptor(), "一键排版", "排版模板参数", "SettingsForm / AttachmentSettingsForm / TableSettingsForm", "系统默认模板为内置规则；三套用户模板保存到本地 XML。"),
			CreateUserXmlItem(RedHeaderTemplateService.GetStoreDescriptor(), "一键套红", "红头模板参数", "RedHeaderTemplateSettingsForm", "参数化生成红头，不依赖 dotx 模板。"),
			CreateUserXmlItem(RenameRuleManager.GetStoreDescriptor(), "一键命名", "命名规则", "RenameRuleSettingsForm", "规则已具备活动规则校验和轮替词推进能力。"),
			CreateUserXmlItem(ReplacePlanService.GetStoreDescriptor(), "一键替换", "替换方案", "ReplacePlanSettingsForm / ReplaceRuleEditorForm", "规则列表、执行顺序和活动方案已归口；执行范围统一为选区优先，否则全文。"),
			CreateUserXmlItem(ConvertSettingsService.GetStoreDescriptor(), "一键转换", "转换参数", "ConvertSettingsForm", "统一配置 DOCX、PDF、TXT 和图片输出方式。")
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static RuleCatalogItem CreateUserXmlItem(RuleStoreDescriptor descriptor, string featureName, string displayName, string uiEntry, string notes)
	{
		if (descriptor == null)
		{
			throw new InvalidOperationException("Rule store descriptor cannot be empty.");
		}
		descriptor.EnsureValid();
		return new RuleCatalogItem
		{
			Id = descriptor.Id,
			FeatureName = featureName,
			DisplayName = displayName,
			StoreKind = RuleStoreKind.UserXml,
			Status = RuleCatalogStatus.Centralized,
			StorePathDescription = ToPathDescription(descriptor.StorePath),
			OwnerService = descriptor.OwnerService,
			ModelTypeName = descriptor.ModelTypeName,
			UiEntry = uiEntry,
			Notes = notes
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ToPathDescription(string path)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			if (!string.IsNullOrWhiteSpace(folderPath) && path.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
			{
				return "%LocalAppData%" + path.Substring(folderPath.Length);
			}
			return path;
		}
		return path;
	}
}
