using System;
using System.Windows.Forms;
using DocumentRepository.Models;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.Features;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Auth;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Conversion;
using DocumentRepository.Services.Features;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Rename;
using DocumentRepository.UI.Features;
using WordApplication = Microsoft.Office.Interop.Word.Application;

namespace DocumentRepository.Services.Hosting;

/// <summary>
/// 将宿主无关的业务入口暴露给 WPS 原生 COM 加载桥。
/// 此类只负责上下文、窗体和结果呈现，全部业务仍由 FeatureTaskExecutor 执行。
/// </summary>
public static class WpsComHostBridge
{
	public static void Initialize(object applicationObject)
	{
		if (applicationObject == null)
		{
			throw new ArgumentNullException(nameof(applicationObject));
		}
		HostThreadRuntime.Initialize("WpsComAddIn.OnConnection");
		System.Windows.Forms.Application.EnableVisualStyles();
		ConfigurationMigrationService.Ensure440Migration();
		LogService.Info("WPS_COM_BRIDGE initialized");
	}

	public static void Shutdown()
	{
		LogService.Info("WPS_COM_BRIDGE shutdown");
	}

	public static void ExecuteFeature(object applicationObject, string featureId, bool autoCloseSuccess)
	{
		FeatureDescriptor descriptor = null;
		try
		{
			WordApplication application = (WordApplication)applicationObject;
			HostThreadRuntime.AssertAccess("WpsComHostBridge.ExecuteFeature." + featureId);
			descriptor = FeatureRegistry.GetById(featureId);
			OperationContext context = OperationContext.FromApplication(application);
			if (descriptor.RequiresDocument && context.Document == null)
			{
				throw FeatureEntryOperationException.Create(FeatureEntryFailureReasonCode.NoActiveDocument);
			}
			context.UserInterface = WinFormsFeatureUiService.Instance;
			CommandResult result = FeatureTaskExecutor.Execute(featureId, context, FeatureExecutionOptions.External("wps-com-ribbon"));
			PresentResult(result, descriptor.DisplayName, autoCloseSuccess);
		}
		catch (Exception ex)
		{
			LogService.Error("WpsComHostBridge.ExecuteFeature." + featureId, ex);
			CommandResult result = ex is FeatureEntryOperationException entry
				? FeatureEntryFailurePresentation.FromException(entry, descriptor?.DisplayName)
				: FeatureEntryFailurePresentation.ToCommandResult(FeatureEntryFailureReasonCode.UnexpectedBeforeStart, descriptor?.DisplayName, ex);
			PresentResult(result, descriptor?.DisplayName ?? "partyops公文排版助手", false);
		}
	}

	public static void ExecuteConvert(object applicationObject, ConvertFormat format)
	{
		ConvertSettingsService.SetSelectedFormat(format);
		ExecuteFeature(applicationObject, "convert", true);
	}

	public static void OpenFormatSettings()
	{
		ShowDialog(new SettingsForm());
	}

	public static void OpenReplaceSettings()
	{
		if (EnsureRuleManagementAllowed("一键替换", "设置替换方案"))
		{
			ShowDialog(new ReplacePlanSettingsForm());
		}
	}

	public static void OpenRedHeaderSettings()
	{
		if (EnsureRuleManagementAllowed("一键套红", "修改红头模板"))
		{
			ShowDialog(new RedHeaderTemplateSettingsForm());
		}
	}

	public static void OpenRenameSettings(object applicationObject)
	{
		if (EnsureRuleManagementAllowed("一键命名", "设置命名规则"))
		{
			WordApplication application = (WordApplication)applicationObject;
			ShowDialog(new RenameRuleSettingsForm(RenameRulePreviewService.CreateFromApplication(application)));
		}
	}

	/// <summary>
	/// 独立版没有正在打开的宿主文档时，仍可编辑和保存完整命名规则；
	/// 有文档时的即时预览继续由执行命令基于当前副本生成。
	/// </summary>
	public static void OpenRenameSettings()
	{
		if (EnsureRuleManagementAllowed("一键命名", "设置命名规则"))
		{
			ShowDialog(new RenameRuleSettingsForm());
		}
	}

	public static void OpenConvertSettings()
	{
		ShowDialog(new ConvertSettingsForm());
	}

	private static void ShowDialog(Form form)
	{
		if (form == null)
		{
			return;
		}
		try
		{
			form.ShowDialog();
		}
		finally
		{
			form.Dispose();
		}
	}

	private static bool EnsureRuleManagementAllowed(string featureName, string actionName)
	{
		if (PermissionChecker.CanManageRules())
		{
			return true;
		}
		WinFormsFeatureUiService.Instance.ShowMessage(featureName, PermissionChecker.GetRuleManagementDeniedMessage(featureName, actionName), FeatureMessageKind.Information);
		return false;
	}

	private static void PresentResult(CommandResult result, string caption, bool autoCloseSuccess)
	{
		if (result == null || result.PresentationHandled || string.IsNullOrWhiteSpace(result.Message))
		{
			return;
		}

		WinFormsFeatureUiService ui = WinFormsFeatureUiService.Instance;
		switch (result.ResolveOutcomeKind())
		{
			case UserOutcomeKind.Completed:
				if (autoCloseSuccess)
				{
					ui.ShowTimedMessage(caption, result.Message, 2500);
				}
				break;
			case UserOutcomeKind.NoChanges:
				ui.ShowTimedMessage(caption, result.Message, 1800);
				break;
			case UserOutcomeKind.CompletedWithWarnings:
			case UserOutcomeKind.FailedButRecovered:
				ui.ShowNonModalWarning(caption, result.Message);
				break;
			case UserOutcomeKind.RecoveryRequired:
				ui.ShowRecoveryAssistant(caption, result.Message, result.RecoveryId);
				break;
			case UserOutcomeKind.Failed:
				ui.ShowMessage(caption, result.Message, FeatureMessageKind.Error);
				break;
			default:
				ui.ShowMessage(caption, result.Message, FeatureMessageKind.Information);
				break;
		}
	}
}
