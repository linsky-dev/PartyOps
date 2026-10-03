using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Models;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.Features;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Auth;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Conversion;
using DocumentRepository.Services.Features;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.RedHeader;
using DocumentRepository.Services.Rename;
using DocumentRepository.Services.Replace;
using DocumentRepository.Services.Rules;
using DocumentRepository.Services.Ui;
using DocumentRepository.Services.UiText;
using DocumentRepository.UI.Features;
using Microsoft.Office.Interop.Word;
using Microsoft.Office.Tools;
using Microsoft.Office.Tools.Ribbon;
using Microsoft.Win32;

namespace DocumentRepository;

public class Ribbon1 : RibbonBase
{
	private string currentRedHeaderTemplateId = "down";

	private IContainer components = null;

	internal RibbonTab tab1;

	internal RibbonGroup groupLeft;

	internal RibbonGroup groupSpacer;

	internal RibbonGroup groupCenter;

	internal RibbonButton button1;

	internal RibbonSplitButton button5;

	internal RibbonButton btnTemplate0;

	internal RibbonButton btnTemplate1;

	internal RibbonButton btnTemplate2;

	internal RibbonButton btnTemplate3;

	internal RibbonButton btnTemplate4;

	internal RibbonButton btnTemplate5;

	internal RibbonButton btnTemplate6;

	internal RibbonButton btnTemplate7;

	internal RibbonButton btnTemplate8;

	internal RibbonButton btnTemplate9;

	internal RibbonSeparator separatorTemplateSettings;

	internal RibbonButton btnTemplateSettings;

	internal RibbonSplitButton buttonRename;

	internal RibbonButton btnRenameRule0;

	internal RibbonButton btnRenameRule1;

	internal RibbonButton btnRenameRule2;

	internal RibbonButton btnRenameRule3;

	internal RibbonButton btnRenameRule4;

	internal RibbonButton btnRenameRule5;

	internal RibbonButton btnRenameRule6;

	internal RibbonButton btnRenameRule7;

	internal RibbonSeparator separatorRenameSettings;

	internal RibbonButton btnRenameExecute;

	internal RibbonButton btnRenameSettings;

	internal RibbonSplitButton buttonReplace;

	internal RibbonButton btnReplacePlan0;

	internal RibbonButton btnReplacePlan1;

	internal RibbonButton btnReplacePlan2;

	internal RibbonButton btnReplacePlan3;

	internal RibbonButton btnReplacePlan4;

	internal RibbonButton btnReplacePlan5;

	internal RibbonButton btnReplacePlan6;

	internal RibbonButton btnReplacePlan7;

	internal RibbonSeparator separatorReplaceSettings;

	internal RibbonButton btnReplaceExecute;

	internal RibbonButton btnReplaceSettings;

	internal RibbonSplitButton buttonRedHeader;

	internal RibbonButton btnRedHeaderTemplate0;

	internal RibbonButton btnRedHeaderTemplate1;

	internal RibbonButton btnRedHeaderTemplate2;

	internal RibbonButton btnRedHeaderTemplate3;

	internal RibbonButton btnRedHeaderTemplate4;

	internal RibbonButton btnRedHeaderTemplate5;

	internal RibbonButton btnRedHeaderTemplate6;

	internal RibbonButton btnRedHeaderTemplate7;

	internal RibbonButton btnRedHeaderTemplate8;

	internal RibbonButton btnRedHeaderTemplate9;

	internal RibbonSeparator separatorRedHeaderSettings;

	internal RibbonButton btnRedHeaderExecute;

	internal RibbonButton btnRedHeaderSettings;

	internal RibbonSplitButton buttonConvert;

	internal RibbonButton btnConvertDocx;

	internal RibbonButton btnConvertPdf;

	internal RibbonButton btnConvertImage;

	internal RibbonButton btnConvertTxt;

	internal RibbonSeparator separatorPdfToWord;

	internal RibbonButton btnPdfToWord;

	internal RibbonSeparator separatorConvertSettings;

	internal RibbonButton btnConvertExecute;

	internal RibbonButton btnConvertSettings;

	internal RibbonButton spacerC;

	internal RibbonButton spacerR1;

	internal RibbonButton spacerR2;

	internal RibbonButton spacerR3;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Ribbon1_Load(object sender, RibbonUIEventArgs e)
	{
		ConfigurationTransferService.ConfigurationImported -= OnConfigurationImported;
		ConfigurationTransferService.ConfigurationImported += OnConfigurationImported;
		ThisAddIn thisAddIn = Globals.ThisAddIn;
		thisAddIn?.NotifyRibbonLoadStarted();
		try
		{
			RunRibbonLoadStep(thisAddIn, "feature-metadata", ApplyFeatureMetadataToRibbon);
			RunRibbonLoadStep(thisAddIn, "dynamic-menu-tooltips", InitializeDynamicMenuTooltips);
		}
		finally
		{
			thisAddIn?.NotifyRibbonLoaded();
		}
		ScheduleDeferredRibbonInitialization(thisAddIn);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void OnConfigurationImported(object sender, EventArgs e)
	{
		try
		{
			UpdateTemplateMenuCheckState();
			UpdateRedHeaderTemplateMenuCheckState();
			UpdateRenameRuleMenuCheckState();
			UpdateReplacePlanMenuCheckState();
			UpdateConvertMenuCheckState();
		}
		catch (Exception ex)
		{
			LogService.Error("Ribbon1.OnConfigurationImported", ex);
		}
	}

	private void ScheduleDeferredRibbonInitialization(ThisAddIn addIn)
	{
		Action action = [MethodImpl(MethodImplOptions.NoInlining)] () =>
		{
			RunDeferredRibbonLoadStep(addIn, "button-images", LoadButtonImages);
			RunDeferredRibbonLoadStep(addIn, "format-template-state", UpdateTemplateMenuCheckState);
			RunDeferredRibbonLoadStep(addIn, "redheader-preference", LoadRedHeaderTemplatePreference);
			RunDeferredRibbonLoadStep(addIn, "redheader-template-state", UpdateRedHeaderTemplateMenuCheckState);
			RunDeferredRibbonLoadStep(addIn, "rename-rule-state", UpdateRenameRuleMenuCheckState);
			RunDeferredRibbonLoadStep(addIn, "replace-plan-state", UpdateReplacePlanMenuCheckState);
			RunDeferredRibbonLoadStep(addIn, "convert-state", UpdateConvertMenuCheckState);
		};
		if (addIn != null)
		{
			addIn.ScheduleDeferredRibbonStartup(action);
		}
		else
		{
			action();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RunDeferredRibbonLoadStep(ThisAddIn addIn, string name, Action action)
	{
		if (addIn != null)
		{
			addIn.RunDeferredRibbonStartupStep(name, action);
			return;
		}
		try
		{
			action?.Invoke();
		}
		catch (Exception ex)
		{
			LogService.Error("STARTUP_DEFERRED_STEP name=ribbon-" + name + " state=FAIL", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RunRibbonLoadStep(ThisAddIn addIn, string name, Action action)
	{
		if (addIn != null)
		{
			addIn.RunRibbonStartupStep(name, action);
			return;
		}
		try
		{
			action?.Invoke();
		}
		catch (Exception ex)
		{
			LogService.Error("STARTUP_STEP name=ribbon-" + name + " state=FAIL", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ApplyFeatureMetadataToRibbon()
	{
		SetFeatureLabel(button1, "format");
		SetFeatureLabel(buttonReplace, "replace");
		SetFeatureLabel(buttonRedHeader, "redheader");
		SetFeatureLabel(buttonRename, "rename");
		SetFeatureLabel(buttonConvert, "convert");
	}

	private void SetFeatureLabel(RibbonButton button, string featureId)
	{
		if (button != null)
		{
			button.Label = FeatureRegistry.GetById(featureId).DisplayName;
		}
	}

	private void SetFeatureLabel(RibbonSplitButton button, string featureId)
	{
		if (button != null)
		{
			button.Label = FeatureRegistry.GetById(featureId).DisplayName;
		}
	}

	private string GetFeatureDisplayName(string featureId)
	{
		return FeatureRegistry.GetById(featureId).DisplayName;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void LoadButtonImages()
	{
		List<string> list = new List<string>
		{
			Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "images"),
			Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase, "images"),
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images")
		};
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("Software\\PartyOps\\DocumentFormatter");
			if (registryKey != null)
			{
				string text = registryKey.GetValue("Path") as string;
				if (!string.IsNullOrEmpty(text))
				{
					list.Add(Path.Combine(text, "images"));
				}
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("Ribbon1.LoadButtonImages.ReadInstallPath", ex);
		}
		string text2 = null;
		foreach (string item in list)
		{
			if (Directory.Exists(item) && text2 == null)
			{
				text2 = item;
			}
		}
		if (!string.IsNullOrEmpty(text2))
		{
			TrySetImage(button1, Path.Combine(text2, "format_all.png"), "一键排版");
			TrySetImage(buttonReplace, Path.Combine(text2, "replace.png"), "一键替换");
			TrySetImage(btnReplaceExecute, Path.Combine(text2, "replace.png"), "一键替换");
			TrySetImage(btnReplaceSettings, Path.Combine(text2, "settings.png"), "方案设置");
			if (buttonReplace.Image != null && btnReplaceExecute.Image == null)
			{
				btnReplaceExecute.Image = buttonReplace.Image;
			}
			TrySetImage(buttonRedHeader, Path.Combine(text2, "red_header.png"), "一键套红");
			if (buttonRedHeader.Image == null)
			{
				TrySetImage(buttonRedHeader, Path.Combine(text2, "sample_doc.png"), "一键套红");
			}
			TrySetImage(btnRedHeaderExecute, Path.Combine(text2, "red_header.png"), "一键套红");
			TrySetImage(btnRedHeaderSettings, Path.Combine(text2, "red_header.png"), "模板设置");
			if (buttonRedHeader.Image != null)
			{
				if (btnRedHeaderExecute.Image == null)
				{
					btnRedHeaderExecute.Image = buttonRedHeader.Image;
				}
				if (btnRedHeaderSettings.Image == null)
				{
					btnRedHeaderSettings.Image = buttonRedHeader.Image;
				}
			}
			TrySetImage(buttonRename, Path.Combine(text2, "rename.png"), "一键命名");
			TrySetImage(btnRenameExecute, Path.Combine(text2, "rename.png"), "一键命名");
			TrySetImage(btnRenameSettings, Path.Combine(text2, "settings.png"), "规则设置");
			TrySetImage(buttonConvert, Path.Combine(text2, "convert.png"), "一键转换");
			TrySetImage(btnConvertExecute, Path.Combine(text2, "convert.png"), "一键转换");
			TrySetImage(btnConvertSettings, Path.Combine(text2, "settings.png"), "转换设置");
			if (buttonConvert.Image != null && btnConvertExecute.Image == null)
			{
				btnConvertExecute.Image = buttonConvert.Image;
			}
			TrySetImage(button5, Path.Combine(text2, "settings.png"), "参数设置");
			TrySetImage(btnTemplateSettings, Path.Combine(text2, "settings.png"), "参数设置");
		}
		object ribbonUi = OfficeInteropCompatibility.GetRibbonUi(this);
		if (ribbonUi != null)
		{
			OfficeInteropCompatibility.InvalidateControl(ribbonUi, ((RibbonComponent)button5).Name);
			OfficeInteropCompatibility.InvalidateControl(ribbonUi, ((RibbonComponent)buttonReplace).Name);
			OfficeInteropCompatibility.InvalidateControl(ribbonUi, ((RibbonComponent)btnReplaceExecute).Name);
			OfficeInteropCompatibility.InvalidateControl(ribbonUi, ((RibbonComponent)btnReplaceSettings).Name);
			OfficeInteropCompatibility.InvalidateControl(ribbonUi, ((RibbonComponent)buttonRedHeader).Name);
			OfficeInteropCompatibility.InvalidateControl(ribbonUi, ((RibbonComponent)btnRedHeaderExecute).Name);
			OfficeInteropCompatibility.InvalidateControl(ribbonUi, ((RibbonComponent)btnRedHeaderSettings).Name);
			OfficeInteropCompatibility.InvalidateControl(ribbonUi, ((RibbonComponent)buttonRename).Name);
			OfficeInteropCompatibility.InvalidateControl(ribbonUi, ((RibbonComponent)btnRenameExecute).Name);
			OfficeInteropCompatibility.InvalidateControl(ribbonUi, ((RibbonComponent)btnRenameSettings).Name);
			OfficeInteropCompatibility.InvalidateControl(ribbonUi, ((RibbonComponent)buttonConvert).Name);
			OfficeInteropCompatibility.InvalidateControl(ribbonUi, ((RibbonComponent)btnConvertExecute).Name);
			OfficeInteropCompatibility.InvalidateControl(ribbonUi, ((RibbonComponent)btnConvertSettings).Name);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void TrySetImage(RibbonButton button, string imagePath, string name)
	{
		try
		{
			if (File.Exists(imagePath))
			{
				button.Image = (Image)new Bitmap(imagePath);
			}
		}
		catch (Exception ex)
		{
			LogService.Error("TrySetImage", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void TrySetImage(RibbonSplitButton button, string imagePath, string name)
	{
		try
		{
			if (File.Exists(imagePath))
			{
				button.Image = (Image)new Bitmap(imagePath);
			}
		}
		catch (Exception ex)
		{
			LogService.Error("TrySetImage", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private CommandResult ExecuteFeatureCommand(string featureId, bool autoCloseSuccess)
	{
		FeatureDescriptor featureDescriptor = null;
		try
		{
			featureDescriptor = FeatureRegistry.GetById(featureId);
			OperationContext context = BuildRibbonOperationContext(featureDescriptor.RequiresDocument);
			CommandResult result = FeatureTaskExecutor.Execute(featureId, context, FeatureExecutionOptions.External("ribbon"));
			ShowRibbonCommandResult(result, featureDescriptor.DisplayName, autoCloseSuccess);
			return result;
		}
		catch (FeatureEntryOperationException ex)
		{
			LogService.Error("Ribbon.ExecuteFeature.Entry." + featureId + ", reason=" + ex.ReasonCode, ex);
			CommandResult result2 = FeatureEntryFailurePresentation.FromException(ex, featureDescriptor?.DisplayName);
			ShowRibbonCommandResult(result2, (featureDescriptor == null) ? "partyops公文排版助手" : featureDescriptor.DisplayName, autoCloseSuccess: false);
			return result2;
		}
		catch (Exception ex2)
		{
			LogService.Error("Ribbon.ExecuteFeature." + featureId, ex2);
			CommandResult result3 = FeatureEntryFailurePresentation.ToCommandResult(FeatureEntryFailureReasonCode.UnexpectedBeforeStart, featureDescriptor?.DisplayName, ex2);
			ShowRibbonCommandResult(result3, (featureDescriptor == null) ? "partyops公文排版助手" : featureDescriptor.DisplayName, autoCloseSuccess: false);
			return result3;
		}
	}

	private OperationContext BuildRibbonOperationContext(bool requireDocument)
	{
		Microsoft.Office.Interop.Word.Application application = Globals.ThisAddIn.Application;
		if (application != null)
		{
			if (requireDocument && application.Documents.Count == 0)
			{
				throw FeatureEntryOperationException.Create(FeatureEntryFailureReasonCode.NoActiveDocument);
			}
			OperationContext operationContext = OperationContext.FromApplication(application);
			operationContext.UserInterface = WinFormsFeatureUiService.Instance;
			return operationContext;
		}
		throw FeatureEntryOperationException.Create(FeatureEntryFailureReasonCode.HostUnavailable);
	}

	private void ShowRibbonCommandResult(CommandResult result, string caption, bool autoCloseSuccess)
	{
		if (result == null || result.PresentationHandled || string.IsNullOrWhiteSpace(result.Message))
		{
			return;
		}
		WinFormsFeatureUiService instance = WinFormsFeatureUiService.Instance;
		UserOutcomeKind userOutcomeKind = result.ResolveOutcomeKind();
		if (userOutcomeKind == UserOutcomeKind.Busy)
		{
			SimpleTaskProgressFormReporter.BringActiveToFront();
		}
		else if (userOutcomeKind == UserOutcomeKind.Cancelled)
		{
			instance.ShowMessage(caption, result.Message, FeatureMessageKind.Information);
		}
		else if (userOutcomeKind == UserOutcomeKind.CompletedWithWarnings)
		{
			if (result.Warnings == null || result.Warnings.Count == 0 || WarningOnceGate.ShouldShowAndMark(result.Warnings))
			{
				instance.ShowNonModalWarning(caption, result.Message);
			}
		}
		else if (userOutcomeKind == UserOutcomeKind.FailedButRecovered)
		{
			instance.ShowNonModalWarning(caption, result.Message);
		}
		else if (userOutcomeKind != UserOutcomeKind.NoChanges)
		{
			if (userOutcomeKind != UserOutcomeKind.ActionRequired)
			{
				if (!(userOutcomeKind == UserOutcomeKind.Completed && result.Success && autoCloseSuccess))
				{
					switch (userOutcomeKind)
					{
					case UserOutcomeKind.Failed:
						instance.ShowMessage(caption, result.Message, FeatureMessageKind.Error);
						break;
					case UserOutcomeKind.RecoveryRequired:
						instance.ShowRecoveryAssistant(caption, result.Message, result.RecoveryId);
						break;
					}
				}
				else
				{
					instance.ShowTimedMessage(caption, result.Message, 2500);
				}
			}
			else
			{
				instance.ShowMessage(caption, result.Message, FeatureMessageKind.Information);
			}
		}
		else
		{
			instance.ShowTimedMessage(caption, result.Message, 1800);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void button1_Click(object sender, RibbonControlEventArgs e)
	{
		ExecuteFeatureCommand("format", autoCloseSuccess: false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private bool HasMeaningfulSelection(Selection sel)
	{
		try
		{
			if (sel != null && sel.Range != null)
			{
				if (sel.Start == sel.End)
				{
					return false;
				}
				return !string.IsNullOrWhiteSpace((sel.Range.Text ?? string.Empty).Replace("\r", string.Empty).Replace("\a", string.Empty).Trim());
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void btnTemplate_Click(object sender, RibbonControlEventArgs e)
	{
		RibbonButton val = (RibbonButton)((sender is RibbonButton) ? sender : null);
		if (val != null)
		{
			int formatTemplateButtonIndex = GetFormatTemplateButtonIndex(val);
			string[] templateNames = ConfigManager.TemplateNames;
			if (formatTemplateButtonIndex >= 0 && formatTemplateButtonIndex < templateNames.Length)
			{
				ConfigManager.SwitchTemplate(formatTemplateButtonIndex);
				UpdateTemplateMenuCheckState();
				WinFormsFeatureUiService.Instance.ShowTimedMessage("模板切换", "已切换到「" + templateNames[formatTemplateButtonIndex] + "」，下次排版时生效。", 2200);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateTemplateMenuCheckState()
	{
		string[] templateNames = ConfigManager.TemplateNames;
		RibbonButton[] array = (RibbonButton[])(object)new RibbonButton[10] { btnTemplate0, btnTemplate1, btnTemplate2, btnTemplate3, btnTemplate4, btnTemplate5, btnTemplate6, btnTemplate7, btnTemplate8, btnTemplate9 };
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i] != null)
			{
				bool flag = i < templateNames.Length;
				((RibbonControl)array[i]).Visible = flag;
				bool flag2 = flag && ConfigManager.CurrentTemplateIndex == i;
				string label = (flag ? ((flag2 ? "● " : "") + templateNames[i]) : string.Empty);
				RibbonTextApplier.ApplyDynamic(array[i], label, flag ? ("切换为此排版模板，只影响下一次一键排版，不会立即修改文档。" + (flag2 ? " 当前正在使用此项。" : "")) : string.Empty);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int GetFormatTemplateButtonIndex(RibbonButton button)
	{
		if (button == null || string.IsNullOrEmpty(((RibbonComponent)button).Name))
		{
			return -1;
		}
		if (!((RibbonComponent)button).Name.StartsWith("btnTemplate", StringComparison.OrdinalIgnoreCase))
		{
			return -1;
		}
		if (!int.TryParse(((RibbonComponent)button).Name.Substring("btnTemplate".Length), out var result))
		{
			return -1;
		}
		return result;
	}

	private void button5_Click(object sender, RibbonControlEventArgs e)
	{
		SettingsForm settingsForm = new SettingsForm();
		try
		{
			((Form)settingsForm).ShowDialog();
		}
		finally
		{
			((IDisposable)settingsForm)?.Dispose();
		}
		UpdateTemplateMenuCheckState();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void buttonReplace_Click(object sender, RibbonControlEventArgs e)
	{
		ExecuteFeatureCommand("replace", autoCloseSuccess: true);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void btnReplacePlan_Click(object sender, RibbonControlEventArgs e)
	{
		if (!EnsureRuleManagementAllowed("一键替换", "切换替换方案"))
		{
			return;
		}
		int replacePlanButtonIndex = GetReplacePlanButtonIndex((RibbonButton)((sender is RibbonButton) ? sender : null));
		if (replacePlanButtonIndex < 0)
		{
			return;
		}
		try
		{
			List<ReplacePlan> plans = ReplacePlanService.Load().Plans;
			if (replacePlanButtonIndex < plans.Count)
			{
				ReplacePlanService.SetActivePlan(plans[replacePlanButtonIndex].Id);
				UpdateReplacePlanMenuCheckState();
			}
		}
		catch (RuleStoreUnavailableException exception)
		{
			RuleStoreResetPrompt.ShowUnavailable(null, exception);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private int GetReplacePlanButtonIndex(RibbonButton btn)
	{
		if (btn == null || string.IsNullOrEmpty(((RibbonComponent)btn).Name))
		{
			return -1;
		}
		if (!((RibbonComponent)btn).Name.StartsWith("btnReplacePlan", StringComparison.Ordinal))
		{
			return -1;
		}
		if (!int.TryParse(((RibbonComponent)btn).Name.Substring("btnReplacePlan".Length), out var result))
		{
			return -1;
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void btnReplaceSettings_Click(object sender, RibbonControlEventArgs e)
	{
		if (!EnsureRuleManagementAllowed("一键替换", "设置替换方案"))
		{
			return;
		}
		ReplacePlanSettingsForm replacePlanSettingsForm = new ReplacePlanSettingsForm();
		try
		{
			if ((int)((Form)replacePlanSettingsForm).ShowDialog() == 1)
			{
				UpdateReplacePlanMenuCheckState();
			}
		}
		finally
		{
			((IDisposable)replacePlanSettingsForm)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateReplacePlanMenuCheckState()
	{
		try
		{
			RibbonButton[] array = (RibbonButton[])(object)new RibbonButton[8] { btnReplacePlan0, btnReplacePlan1, btnReplacePlan2, btnReplacePlan3, btnReplacePlan4, btnReplacePlan5, btnReplacePlan6, btnReplacePlan7 };
			ReplacePlanSet replacePlanSet = ReplacePlanService.Load();
			for (int i = 0; i < array.Length; i++)
			{
				RibbonButton val = array[i];
				if (val != null)
				{
					if (i < replacePlanSet.Plans.Count)
					{
						ReplacePlan replacePlan = replacePlanSet.Plans[i];
						bool flag = replacePlan.Id == replacePlanSet.ActivePlanId;
						((RibbonControl)val).Visible = true;
						RibbonTextApplier.ApplyDynamic(val, (flag ? "● " : "") + replacePlan.Name, "切换为此替换方案，只影响下一次一键替换，不会立即修改文档。" + (flag ? " 当前正在使用此项。" : ""));
					}
					else
					{
						((RibbonControl)val).Visible = false;
						RibbonTextApplier.ApplyDynamic(val, string.Empty, string.Empty);
					}
				}
			}
		}
		catch (Exception ex)
		{
			LogService.Error("UpdateReplacePlanMenuCheckState", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void buttonRedHeader_Click(object sender, RibbonControlEventArgs e)
	{
		LoadRedHeaderTemplatePreference();
		ExecuteFeatureCommand("redheader", autoCloseSuccess: true);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void btnRedHeaderTemplate_Click(object sender, RibbonControlEventArgs e)
	{
		RibbonButton button = (RibbonButton)((sender is RibbonButton) ? sender : null);
		int redHeaderTemplateButtonIndex = GetRedHeaderTemplateButtonIndex(button);
		if (redHeaderTemplateButtonIndex < 0)
		{
			return;
		}
		try
		{
			RedHeaderTemplateSet redHeaderTemplateSet = RedHeaderTemplateService.Load();
			if (redHeaderTemplateButtonIndex < redHeaderTemplateSet.Templates.Count && EnsureRuleManagementAllowed("一键套红", "切换红头模板"))
			{
				currentRedHeaderTemplateId = redHeaderTemplateSet.Templates[redHeaderTemplateButtonIndex].Id;
				RedHeaderTemplateService.SetActiveTemplate(currentRedHeaderTemplateId);
				UpdateRedHeaderTemplateMenuCheckState();
			}
		}
		catch (RuleStoreUnavailableException exception)
		{
			RuleStoreResetPrompt.ShowUnavailable(null, exception);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateRedHeaderTemplateMenuCheckState()
	{
		try
		{
			RedHeaderTemplateSet redHeaderTemplateSet = RedHeaderTemplateService.Load();
			currentRedHeaderTemplateId = redHeaderTemplateSet.ActiveTemplateId;
			RibbonButton[] array = (RibbonButton[])(object)new RibbonButton[10] { btnRedHeaderTemplate0, btnRedHeaderTemplate1, btnRedHeaderTemplate2, btnRedHeaderTemplate3, btnRedHeaderTemplate4, btnRedHeaderTemplate5, btnRedHeaderTemplate6, btnRedHeaderTemplate7, btnRedHeaderTemplate8, btnRedHeaderTemplate9 };
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i] != null)
				{
					if (i >= redHeaderTemplateSet.Templates.Count)
					{
						((RibbonControl)array[i]).Visible = false;
						RibbonTextApplier.ApplyDynamic(array[i], string.Empty, string.Empty);
						continue;
					}
					RedHeaderTemplate redHeaderTemplate = redHeaderTemplateSet.Templates[i];
					bool flag = string.Equals(redHeaderTemplateSet.ActiveTemplateId, redHeaderTemplate.Id, StringComparison.OrdinalIgnoreCase);
					((RibbonControl)array[i]).Visible = true;
					RibbonTextApplier.ApplyDynamic(array[i], (flag ? "● " : "") + redHeaderTemplate.Name, "切换为此红头模板，只影响下一次一键套红，不会立即修改文档。" + (flag ? " 当前正在使用此项。" : ""));
				}
			}
		}
		catch (Exception ex)
		{
			LogService.Error("UpdateRedHeaderTemplateMenuCheckState", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private int GetRedHeaderTemplateButtonIndex(RibbonButton button)
	{
		if (button != null && !string.IsNullOrEmpty(((RibbonComponent)button).Name))
		{
			if (((RibbonComponent)button).Name.StartsWith("btnRedHeaderTemplate", StringComparison.OrdinalIgnoreCase))
			{
				if (!int.TryParse(((RibbonComponent)button).Name.Substring("btnRedHeaderTemplate".Length), out var result))
				{
					return -1;
				}
				return result;
			}
			return -1;
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void LoadRedHeaderTemplatePreference()
	{
		try
		{
			currentRedHeaderTemplateId = RedHeaderTemplateService.Load().ActiveTemplateId;
		}
		catch (Exception ex)
		{
			LogService.Error("LoadRedHeaderTemplatePreference", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void btnRedHeaderSettings_Click(object sender, RibbonControlEventArgs e)
	{
		try
		{
			if (!EnsureRuleManagementAllowed("一键套红", "修改红头模板"))
			{
				return;
			}
			RedHeaderTemplateSettingsForm redHeaderTemplateSettingsForm = new RedHeaderTemplateSettingsForm();
			try
			{
				if ((int)((Form)redHeaderTemplateSettingsForm).ShowDialog() == 1)
				{
					currentRedHeaderTemplateId = RedHeaderTemplateService.Load().ActiveTemplateId;
					UpdateRedHeaderTemplateMenuCheckState();
				}
			}
			finally
			{
				((IDisposable)redHeaderTemplateSettingsForm)?.Dispose();
			}
		}
		catch (Exception ex)
		{
			LogService.Error("btnRedHeaderSettings_Click", ex);
			AppleMessageDialog.Show("打开红头参数设置失败，详细原因已写入日志。", "模板设置", (MessageBoxButtons)0, (MessageBoxIcon)48);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void buttonConvert_Click(object sender, RibbonControlEventArgs e)
	{
		Cursor current = Cursor.Current;
		try
		{
			Cursor.Current = Cursors.WaitCursor;
			ExecuteFeatureCommand("convert", autoCloseSuccess: true);
		}
		finally
		{
			Cursor.Current = current ?? Cursors.Default;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void btnPdfToWord_Click(object sender, RibbonControlEventArgs e)
	{
		Cursor current = Cursor.Current;
		try
		{
			Cursor.Current = Cursors.WaitCursor;
			ExecuteFeatureCommand("pdf-to-word", autoCloseSuccess: true);
		}
		finally
		{
			Cursor.Current = current ?? Cursors.Default;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void btnConvertFormat_Click(object sender, RibbonControlEventArgs e)
	{
		RibbonButton val = (RibbonButton)((sender is RibbonButton) ? sender : null);
		if (val == null)
		{
			return;
		}
		try
		{
			if (val == btnConvertDocx)
			{
				ConvertSettingsService.SetSelectedFormat(ConvertFormat.Docx);
			}
			else if (val == btnConvertImage)
			{
				ConvertSettingsService.SetSelectedFormat(ConvertFormat.Image);
			}
			else if (val != btnConvertTxt)
			{
				ConvertSettingsService.SetSelectedFormat(ConvertFormat.Pdf);
			}
			else
			{
				ConvertSettingsService.SetSelectedFormat(ConvertFormat.Txt);
			}
			UpdateConvertMenuCheckState();
		}
		catch (Exception ex)
		{
			LogService.Error("Ribbon1.btnConvertFormat_Click", ex);
			AppleMessageDialog.Show("转换方式暂时无法切换，请稍后重试。", "一键转换", (MessageBoxButtons)0, (MessageBoxIcon)48);
		}
	}

	private void btnConvertSettings_Click(object sender, RibbonControlEventArgs e)
	{
		ConvertSettingsForm convertSettingsForm = new ConvertSettingsForm();
		try
		{
			if ((int)((Form)convertSettingsForm).ShowDialog() == 1)
			{
				UpdateConvertMenuCheckState();
			}
		}
		finally
		{
			((IDisposable)convertSettingsForm)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateConvertMenuCheckState()
	{
		try
		{
			ConvertOptions convertOptions = ConvertSettingsService.Load();
			ApplyConvertFormatTooltip(btnConvertDocx, "转为 DOCX", convertOptions.SelectedFormat == ConvertFormat.Docx);
			ApplyConvertFormatTooltip(btnConvertPdf, "转为 PDF", convertOptions.SelectedFormat == ConvertFormat.Pdf);
			ApplyConvertFormatTooltip(btnConvertImage, "转为图片", convertOptions.SelectedFormat == ConvertFormat.Image);
			ApplyConvertFormatTooltip(btnConvertTxt, "转为 TXT", convertOptions.SelectedFormat == ConvertFormat.Txt);
			buttonConvert.Label = GetFeatureDisplayName("convert");
		}
		catch (Exception ex)
		{
			LogService.Error("Ribbon1.UpdateConvertMenuCheckState", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeDynamicMenuTooltips()
	{
		ApplyMenuTooltipFallback((IEnumerable<RibbonButton>)(object)new RibbonButton[10] { btnTemplate0, btnTemplate1, btnTemplate2, btnTemplate3, btnTemplate4, btnTemplate5, btnTemplate6, btnTemplate7, btnTemplate8, btnTemplate9 }, "切换为此排版模板，只影响下一次一键排版，不会立即修改文档。");
		ApplyMenuTooltipFallback((IEnumerable<RibbonButton>)(object)new RibbonButton[8] { btnReplacePlan0, btnReplacePlan1, btnReplacePlan2, btnReplacePlan3, btnReplacePlan4, btnReplacePlan5, btnReplacePlan6, btnReplacePlan7 }, "切换为此替换方案，只影响下一次一键替换，不会立即修改文档。");
		ApplyMenuTooltipFallback((IEnumerable<RibbonButton>)(object)new RibbonButton[10] { btnRedHeaderTemplate0, btnRedHeaderTemplate1, btnRedHeaderTemplate2, btnRedHeaderTemplate3, btnRedHeaderTemplate4, btnRedHeaderTemplate5, btnRedHeaderTemplate6, btnRedHeaderTemplate7, btnRedHeaderTemplate8, btnRedHeaderTemplate9 }, "切换为此红头模板，只影响下一次一键套红，不会立即修改文档。");
		ApplyMenuTooltipFallback((IEnumerable<RibbonButton>)(object)new RibbonButton[8] { btnRenameRule0, btnRenameRule1, btnRenameRule2, btnRenameRule3, btnRenameRule4, btnRenameRule5, btnRenameRule6, btnRenameRule7 }, "切换为此命名规则，只影响下一次一键命名，不会立即修改文档。");
		ApplyMenuTooltipFallback((IEnumerable<RibbonButton>)(object)new RibbonButton[4] { btnConvertDocx, btnConvertPdf, btnConvertImage, btnConvertTxt }, "切换输出格式，只影响下一次一键转换，不会立即转换文档。");
	}

	private static void ApplyMenuTooltipFallback(IEnumerable<RibbonButton> buttons, string tooltip)
	{
		foreach (RibbonButton button in buttons)
		{
			if (button != null)
			{
				RibbonTextApplier.ApplyDynamic(button, button.Label, tooltip);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyConvertFormatTooltip(RibbonButton button, string label, bool active)
	{
		RibbonTextApplier.ApplyDynamic(button, (active ? "● " : "") + label, "切换输出格式，只影响下一次一键转换，不会立即转换文档。" + (active ? " 当前正在使用此项。" : ""));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void buttonRename_Click(object sender, RibbonControlEventArgs e)
	{
		ExecuteFeatureCommand("rename", autoCloseSuccess: true);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void btnRenameRule_Click(object sender, RibbonControlEventArgs e)
	{
		RibbonButton button = (RibbonButton)((sender is RibbonButton) ? sender : null);
		int renameRuleButtonIndex = GetRenameRuleButtonIndex(button);
		if (renameRuleButtonIndex < 0)
		{
			return;
		}
		try
		{
			RenameRuleSet renameRuleSet = RenameRuleManager.Load();
			if (renameRuleButtonIndex < renameRuleSet.Rules.Count && EnsureRuleManagementAllowed("一键命名", "切换命名规则"))
			{
				RenameRuleManager.SetActiveRule(renameRuleSet.Rules[renameRuleButtonIndex].Id);
				UpdateRenameRuleMenuCheckState();
			}
		}
		catch (RuleStoreUnavailableException exception)
		{
			RuleStoreResetPrompt.ShowUnavailable(null, exception);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void btnRenameSettings_Click(object sender, RibbonControlEventArgs e)
	{
		if (!EnsureRuleManagementAllowed("一键命名", "设置命名规则"))
		{
			return;
		}
		RenameRuleSettingsForm renameRuleSettingsForm = new RenameRuleSettingsForm(RenameRulePreviewService.CreateFromApplication(Globals.ThisAddIn.Application));
		try
		{
			if ((int)((Form)renameRuleSettingsForm).ShowDialog() == 1)
			{
				UpdateRenameRuleMenuCheckState();
			}
		}
		finally
		{
			((IDisposable)renameRuleSettingsForm)?.Dispose();
		}
	}

	private static bool EnsureRuleManagementAllowed(string featureName, string actionName)
	{
		if (PermissionChecker.CanManageRules())
		{
			return true;
		}
		AppleMessageDialog.Show(PermissionChecker.GetRuleManagementDeniedMessage(featureName, actionName), featureName, (MessageBoxButtons)0, (MessageBoxIcon)64);
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateRenameRuleMenuCheckState()
	{
		try
		{
			RenameRuleSet renameRuleSet = RenameRuleManager.Load();
			RibbonButton[] array = (RibbonButton[])(object)new RibbonButton[8] { btnRenameRule0, btnRenameRule1, btnRenameRule2, btnRenameRule3, btnRenameRule4, btnRenameRule5, btnRenameRule6, btnRenameRule7 };
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i] != null)
				{
					if (i < renameRuleSet.Rules.Count)
					{
						RenameRule renameRule = renameRuleSet.Rules[i];
						bool flag = string.Equals(renameRule.Id, renameRuleSet.ActiveRuleId, StringComparison.OrdinalIgnoreCase);
						((RibbonControl)array[i]).Visible = true;
						RibbonTextApplier.ApplyDynamic(array[i], (flag ? "● " : "") + renameRule.Name, "切换为此命名规则，只影响下一次一键命名，不会立即修改文档。" + (flag ? " 当前正在使用此项。" : ""));
					}
					else
					{
						((RibbonControl)array[i]).Visible = false;
						RibbonTextApplier.ApplyDynamic(array[i], string.Empty, string.Empty);
					}
				}
			}
		}
		catch (Exception ex)
		{
			LogService.Error("UpdateRenameRuleMenuCheckState", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private int GetRenameRuleButtonIndex(RibbonButton button)
	{
		if (button != null && !string.IsNullOrEmpty(((RibbonComponent)button).Name))
		{
			if (!((RibbonComponent)button).Name.StartsWith("btnRenameRule", StringComparison.OrdinalIgnoreCase))
			{
				return -1;
			}
			if (!int.TryParse(((RibbonComponent)button).Name.Substring("btnRenameRule".Length), out var result))
			{
				return -1;
			}
			return result;
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public Ribbon1()
		: base(((Factory)Globals.Factory).GetRibbonFactory())
	{
		InitializeComponent();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeComponent()
	{
		tab1 = base.Factory.CreateRibbonTab();
		groupLeft = base.Factory.CreateRibbonGroup();
		button5 = base.Factory.CreateRibbonSplitButton();
		btnTemplate0 = base.Factory.CreateRibbonButton();
		btnTemplate1 = base.Factory.CreateRibbonButton();
		btnTemplate2 = base.Factory.CreateRibbonButton();
		btnTemplate3 = base.Factory.CreateRibbonButton();
		btnTemplate4 = base.Factory.CreateRibbonButton();
		btnTemplate5 = base.Factory.CreateRibbonButton();
		btnTemplate6 = base.Factory.CreateRibbonButton();
		btnTemplate7 = base.Factory.CreateRibbonButton();
		btnTemplate8 = base.Factory.CreateRibbonButton();
		btnTemplate9 = base.Factory.CreateRibbonButton();
		separatorTemplateSettings = base.Factory.CreateRibbonSeparator();
		btnTemplateSettings = base.Factory.CreateRibbonButton();
		groupSpacer = base.Factory.CreateRibbonGroup();
		spacerR1 = base.Factory.CreateRibbonButton();
		spacerR2 = base.Factory.CreateRibbonButton();
		spacerR3 = base.Factory.CreateRibbonButton();
		groupCenter = base.Factory.CreateRibbonGroup();
		button1 = base.Factory.CreateRibbonButton();
		buttonReplace = base.Factory.CreateRibbonSplitButton();
		btnReplacePlan0 = base.Factory.CreateRibbonButton();
		btnReplacePlan1 = base.Factory.CreateRibbonButton();
		btnReplacePlan2 = base.Factory.CreateRibbonButton();
		btnReplacePlan3 = base.Factory.CreateRibbonButton();
		btnReplacePlan4 = base.Factory.CreateRibbonButton();
		btnReplacePlan5 = base.Factory.CreateRibbonButton();
		btnReplacePlan6 = base.Factory.CreateRibbonButton();
		btnReplacePlan7 = base.Factory.CreateRibbonButton();
		separatorReplaceSettings = base.Factory.CreateRibbonSeparator();
		btnReplaceExecute = base.Factory.CreateRibbonButton();
		btnReplaceSettings = base.Factory.CreateRibbonButton();
		buttonRedHeader = base.Factory.CreateRibbonSplitButton();
		btnRedHeaderTemplate0 = base.Factory.CreateRibbonButton();
		btnRedHeaderTemplate1 = base.Factory.CreateRibbonButton();
		btnRedHeaderTemplate2 = base.Factory.CreateRibbonButton();
		btnRedHeaderTemplate3 = base.Factory.CreateRibbonButton();
		btnRedHeaderTemplate4 = base.Factory.CreateRibbonButton();
		btnRedHeaderTemplate5 = base.Factory.CreateRibbonButton();
		btnRedHeaderTemplate6 = base.Factory.CreateRibbonButton();
		btnRedHeaderTemplate7 = base.Factory.CreateRibbonButton();
		btnRedHeaderTemplate8 = base.Factory.CreateRibbonButton();
		btnRedHeaderTemplate9 = base.Factory.CreateRibbonButton();
		separatorRedHeaderSettings = base.Factory.CreateRibbonSeparator();
		btnRedHeaderExecute = base.Factory.CreateRibbonButton();
		btnRedHeaderSettings = base.Factory.CreateRibbonButton();
		buttonConvert = base.Factory.CreateRibbonSplitButton();
		btnConvertDocx = base.Factory.CreateRibbonButton();
		btnConvertPdf = base.Factory.CreateRibbonButton();
		btnConvertImage = base.Factory.CreateRibbonButton();
		btnConvertTxt = base.Factory.CreateRibbonButton();
		separatorPdfToWord = base.Factory.CreateRibbonSeparator();
		btnPdfToWord = base.Factory.CreateRibbonButton();
		separatorConvertSettings = base.Factory.CreateRibbonSeparator();
		btnConvertExecute = base.Factory.CreateRibbonButton();
		btnConvertSettings = base.Factory.CreateRibbonButton();
		spacerC = base.Factory.CreateRibbonButton();
		buttonRename = base.Factory.CreateRibbonSplitButton();
		btnRenameRule0 = base.Factory.CreateRibbonButton();
		btnRenameRule1 = base.Factory.CreateRibbonButton();
		btnRenameRule2 = base.Factory.CreateRibbonButton();
		btnRenameRule3 = base.Factory.CreateRibbonButton();
		btnRenameRule4 = base.Factory.CreateRibbonButton();
		btnRenameRule5 = base.Factory.CreateRibbonButton();
		btnRenameRule6 = base.Factory.CreateRibbonButton();
		btnRenameRule7 = base.Factory.CreateRibbonButton();
		separatorRenameSettings = base.Factory.CreateRibbonSeparator();
		btnRenameExecute = base.Factory.CreateRibbonButton();
		btnRenameSettings = base.Factory.CreateRibbonButton();
		tab1.SuspendLayout();
		((RibbonComponent)groupLeft).SuspendLayout();
		((RibbonComponent)groupSpacer).SuspendLayout();
		((RibbonComponent)groupCenter).SuspendLayout();
		SuspendLayout();
		tab1.Groups.Add(groupLeft);
		tab1.Groups.Add(groupSpacer);
		tab1.Groups.Add(groupCenter);
		tab1.Label = "partyops公文排版助手";
		tab1.Name = "tab1";
		groupLeft.Items.Add((RibbonControl)button5);
		groupLeft.Label = " ";
		((RibbonComponent)groupLeft).Name = "groupLeft";
		OfficeInteropCompatibility.SetLargeControlSize(button5);
		button5.Label = "自定义参数";
		((RibbonComponent)button5).Name = "button5";
		RibbonTextApplier.Apply(button5, "Ribbon.Settings");
		button5.Click += new RibbonControlEventHandler(button5_Click);
		btnTemplate0.Label = "系统默认";
		((RibbonComponent)btnTemplate0).Name = "btnTemplate0";
		btnTemplate0.ShowImage = false;
		btnTemplate0.Click += new RibbonControlEventHandler(btnTemplate_Click);
		btnTemplate1.Label = "模板一";
		((RibbonComponent)btnTemplate1).Name = "btnTemplate1";
		btnTemplate1.ShowImage = false;
		btnTemplate1.Click += new RibbonControlEventHandler(btnTemplate_Click);
		btnTemplate2.Label = "模板二";
		((RibbonComponent)btnTemplate2).Name = "btnTemplate2";
		btnTemplate2.ShowImage = false;
		btnTemplate2.Click += new RibbonControlEventHandler(btnTemplate_Click);
		btnTemplate3.Label = "模板三";
		((RibbonComponent)btnTemplate3).Name = "btnTemplate3";
		btnTemplate3.ShowImage = false;
		btnTemplate3.Click += new RibbonControlEventHandler(btnTemplate_Click);
		((RibbonComponent)btnTemplate4).Name = "btnTemplate4";
		btnTemplate4.ShowImage = false;
		((RibbonControl)btnTemplate4).Visible = false;
		btnTemplate4.Click += new RibbonControlEventHandler(btnTemplate_Click);
		((RibbonComponent)btnTemplate5).Name = "btnTemplate5";
		btnTemplate5.ShowImage = false;
		((RibbonControl)btnTemplate5).Visible = false;
		btnTemplate5.Click += new RibbonControlEventHandler(btnTemplate_Click);
		((RibbonComponent)btnTemplate6).Name = "btnTemplate6";
		btnTemplate6.ShowImage = false;
		((RibbonControl)btnTemplate6).Visible = false;
		btnTemplate6.Click += new RibbonControlEventHandler(btnTemplate_Click);
		((RibbonComponent)btnTemplate7).Name = "btnTemplate7";
		btnTemplate7.ShowImage = false;
		((RibbonControl)btnTemplate7).Visible = false;
		btnTemplate7.Click += new RibbonControlEventHandler(btnTemplate_Click);
		((RibbonComponent)btnTemplate8).Name = "btnTemplate8";
		btnTemplate8.ShowImage = false;
		((RibbonControl)btnTemplate8).Visible = false;
		btnTemplate8.Click += new RibbonControlEventHandler(btnTemplate_Click);
		((RibbonComponent)btnTemplate9).Name = "btnTemplate9";
		btnTemplate9.ShowImage = false;
		((RibbonControl)btnTemplate9).Visible = false;
		btnTemplate9.Click += new RibbonControlEventHandler(btnTemplate_Click);
		((RibbonComponent)separatorTemplateSettings).Name = "separatorTemplateSettings";
		btnTemplateSettings.Label = "参数设置...";
		((RibbonComponent)btnTemplateSettings).Name = "btnTemplateSettings";
		btnTemplateSettings.ShowImage = true;
		RibbonTextApplier.Apply(btnTemplateSettings, "Ribbon.TemplateSettings");
		btnTemplateSettings.Click += new RibbonControlEventHandler(button5_Click);
		button5.Items.Add((RibbonControl)btnTemplate0);
		button5.Items.Add((RibbonControl)btnTemplate1);
		button5.Items.Add((RibbonControl)btnTemplate2);
		button5.Items.Add((RibbonControl)btnTemplate3);
		button5.Items.Add((RibbonControl)btnTemplate4);
		button5.Items.Add((RibbonControl)btnTemplate5);
		button5.Items.Add((RibbonControl)btnTemplate6);
		button5.Items.Add((RibbonControl)btnTemplate7);
		button5.Items.Add((RibbonControl)btnTemplate8);
		button5.Items.Add((RibbonControl)btnTemplate9);
		button5.Items.Add((RibbonControl)separatorTemplateSettings);
		button5.Items.Add((RibbonControl)btnTemplateSettings);
		groupSpacer.Items.Add((RibbonControl)spacerR1);
		groupSpacer.Items.Add((RibbonControl)spacerR2);
		groupSpacer.Items.Add((RibbonControl)spacerR3);
		groupSpacer.Label = " ";
		((RibbonComponent)groupSpacer).Name = "groupSpacer";
		OfficeInteropCompatibility.SetLargeControlSize(spacerR1);
		((RibbonControl)spacerR1).Enabled = false;
		spacerR1.Label = "";
		((RibbonComponent)spacerR1).Name = "spacerR1";
		spacerR1.ShowImage = false;
		spacerR1.ShowLabel = false;
		OfficeInteropCompatibility.SetLargeControlSize(spacerR2);
		((RibbonControl)spacerR2).Enabled = false;
		spacerR2.Label = "";
		((RibbonComponent)spacerR2).Name = "spacerR2";
		spacerR2.ShowImage = false;
		spacerR2.ShowLabel = false;
		OfficeInteropCompatibility.SetLargeControlSize(spacerR3);
		((RibbonControl)spacerR3).Enabled = false;
		spacerR3.Label = "";
		((RibbonComponent)spacerR3).Name = "spacerR3";
		spacerR3.ShowImage = false;
		spacerR3.ShowLabel = false;
		groupCenter.Items.Add((RibbonControl)button1);
		groupCenter.Items.Add((RibbonControl)buttonReplace);
		groupCenter.Items.Add((RibbonControl)buttonRedHeader);
		groupCenter.Items.Add((RibbonControl)buttonConvert);
		groupCenter.Items.Add((RibbonControl)buttonRename);
		groupCenter.Label = " ";
		((RibbonComponent)groupCenter).Name = "groupCenter";
		OfficeInteropCompatibility.SetLargeControlSize(button1);
		button1.Label = "一键排版";
		((RibbonComponent)button1).Name = "button1";
		button1.ShowImage = true;
		RibbonTextApplier.Apply(button1, "Ribbon.Format");
		button1.Click += new RibbonControlEventHandler(button1_Click);
		OfficeInteropCompatibility.SetLargeControlSize(buttonReplace);
		buttonReplace.Label = "一键替换";
		((RibbonComponent)buttonReplace).Name = "buttonReplace";
		RibbonTextApplier.Apply(buttonReplace, "Ribbon.Replace");
		buttonReplace.Click += new RibbonControlEventHandler(buttonReplace_Click);
		((RibbonComponent)btnReplacePlan0).Name = "btnReplacePlan0";
		btnReplacePlan0.ShowImage = false;
		btnReplacePlan0.Click += new RibbonControlEventHandler(btnReplacePlan_Click);
		((RibbonComponent)btnReplacePlan1).Name = "btnReplacePlan1";
		btnReplacePlan1.ShowImage = false;
		btnReplacePlan1.Click += new RibbonControlEventHandler(btnReplacePlan_Click);
		((RibbonComponent)btnReplacePlan2).Name = "btnReplacePlan2";
		btnReplacePlan2.ShowImage = false;
		btnReplacePlan2.Click += new RibbonControlEventHandler(btnReplacePlan_Click);
		((RibbonComponent)btnReplacePlan3).Name = "btnReplacePlan3";
		btnReplacePlan3.ShowImage = false;
		btnReplacePlan3.Click += new RibbonControlEventHandler(btnReplacePlan_Click);
		((RibbonComponent)btnReplacePlan4).Name = "btnReplacePlan4";
		btnReplacePlan4.ShowImage = false;
		btnReplacePlan4.Click += new RibbonControlEventHandler(btnReplacePlan_Click);
		((RibbonComponent)btnReplacePlan5).Name = "btnReplacePlan5";
		btnReplacePlan5.ShowImage = false;
		btnReplacePlan5.Click += new RibbonControlEventHandler(btnReplacePlan_Click);
		((RibbonComponent)btnReplacePlan6).Name = "btnReplacePlan6";
		btnReplacePlan6.ShowImage = false;
		btnReplacePlan6.Click += new RibbonControlEventHandler(btnReplacePlan_Click);
		((RibbonComponent)btnReplacePlan7).Name = "btnReplacePlan7";
		btnReplacePlan7.ShowImage = false;
		btnReplacePlan7.Click += new RibbonControlEventHandler(btnReplacePlan_Click);
		btnReplaceExecute.Label = "一键替换";
		((RibbonComponent)btnReplaceExecute).Name = "btnReplaceExecute";
		btnReplaceExecute.ShowImage = true;
		RibbonTextApplier.Apply(btnReplaceExecute, "Ribbon.Replace");
		btnReplaceExecute.Click += new RibbonControlEventHandler(buttonReplace_Click);
		btnReplaceSettings.Label = "方案设置...";
		((RibbonComponent)btnReplaceSettings).Name = "btnReplaceSettings";
		btnReplaceSettings.ShowImage = true;
		RibbonTextApplier.Apply(btnReplaceSettings, "Ribbon.ReplaceSettings");
		btnReplaceSettings.Click += new RibbonControlEventHandler(btnReplaceSettings_Click);
		buttonReplace.Items.Add((RibbonControl)btnReplacePlan0);
		buttonReplace.Items.Add((RibbonControl)btnReplacePlan1);
		buttonReplace.Items.Add((RibbonControl)btnReplacePlan2);
		buttonReplace.Items.Add((RibbonControl)btnReplacePlan3);
		buttonReplace.Items.Add((RibbonControl)btnReplacePlan4);
		buttonReplace.Items.Add((RibbonControl)btnReplacePlan5);
		buttonReplace.Items.Add((RibbonControl)btnReplacePlan6);
		buttonReplace.Items.Add((RibbonControl)btnReplacePlan7);
		buttonReplace.Items.Add((RibbonControl)separatorReplaceSettings);
		buttonReplace.Items.Add((RibbonControl)btnReplaceExecute);
		buttonReplace.Items.Add((RibbonControl)btnReplaceSettings);
		OfficeInteropCompatibility.SetLargeControlSize(buttonRedHeader);
		buttonRedHeader.Label = "一键套红";
		((RibbonComponent)buttonRedHeader).Name = "buttonRedHeader";
		RibbonTextApplier.Apply(buttonRedHeader, "Ribbon.RedHeader");
		buttonRedHeader.Click += new RibbonControlEventHandler(buttonRedHeader_Click);
		((RibbonComponent)btnRedHeaderTemplate0).Name = "btnRedHeaderTemplate0";
		btnRedHeaderTemplate0.ShowImage = false;
		btnRedHeaderTemplate0.Click += new RibbonControlEventHandler(btnRedHeaderTemplate_Click);
		((RibbonComponent)btnRedHeaderTemplate1).Name = "btnRedHeaderTemplate1";
		btnRedHeaderTemplate1.ShowImage = false;
		btnRedHeaderTemplate1.Click += new RibbonControlEventHandler(btnRedHeaderTemplate_Click);
		((RibbonComponent)btnRedHeaderTemplate2).Name = "btnRedHeaderTemplate2";
		btnRedHeaderTemplate2.ShowImage = false;
		btnRedHeaderTemplate2.Click += new RibbonControlEventHandler(btnRedHeaderTemplate_Click);
		((RibbonComponent)btnRedHeaderTemplate3).Name = "btnRedHeaderTemplate3";
		btnRedHeaderTemplate3.ShowImage = false;
		btnRedHeaderTemplate3.Click += new RibbonControlEventHandler(btnRedHeaderTemplate_Click);
		((RibbonComponent)btnRedHeaderTemplate4).Name = "btnRedHeaderTemplate4";
		btnRedHeaderTemplate4.ShowImage = false;
		btnRedHeaderTemplate4.Click += new RibbonControlEventHandler(btnRedHeaderTemplate_Click);
		((RibbonComponent)btnRedHeaderTemplate5).Name = "btnRedHeaderTemplate5";
		btnRedHeaderTemplate5.ShowImage = false;
		btnRedHeaderTemplate5.Click += new RibbonControlEventHandler(btnRedHeaderTemplate_Click);
		((RibbonComponent)btnRedHeaderTemplate6).Name = "btnRedHeaderTemplate6";
		btnRedHeaderTemplate6.ShowImage = false;
		btnRedHeaderTemplate6.Click += new RibbonControlEventHandler(btnRedHeaderTemplate_Click);
		((RibbonComponent)btnRedHeaderTemplate7).Name = "btnRedHeaderTemplate7";
		btnRedHeaderTemplate7.ShowImage = false;
		btnRedHeaderTemplate7.Click += new RibbonControlEventHandler(btnRedHeaderTemplate_Click);
		((RibbonComponent)btnRedHeaderTemplate8).Name = "btnRedHeaderTemplate8";
		btnRedHeaderTemplate8.ShowImage = false;
		btnRedHeaderTemplate8.Click += new RibbonControlEventHandler(btnRedHeaderTemplate_Click);
		((RibbonComponent)btnRedHeaderTemplate9).Name = "btnRedHeaderTemplate9";
		btnRedHeaderTemplate9.ShowImage = false;
		btnRedHeaderTemplate9.Click += new RibbonControlEventHandler(btnRedHeaderTemplate_Click);
		btnRedHeaderExecute.Label = "一键套红";
		((RibbonComponent)btnRedHeaderExecute).Name = "btnRedHeaderExecute";
		btnRedHeaderExecute.ShowImage = true;
		RibbonTextApplier.Apply(btnRedHeaderExecute, "Ribbon.RedHeader.Execute");
		btnRedHeaderExecute.Click += new RibbonControlEventHandler(buttonRedHeader_Click);
		btnRedHeaderSettings.Label = "模板设置...";
		((RibbonComponent)btnRedHeaderSettings).Name = "btnRedHeaderSettings";
		btnRedHeaderSettings.ShowImage = true;
		RibbonTextApplier.Apply(btnRedHeaderSettings, "Ribbon.RedHeader.Settings");
		btnRedHeaderSettings.Click += new RibbonControlEventHandler(btnRedHeaderSettings_Click);
		buttonRedHeader.Items.Add((RibbonControl)btnRedHeaderTemplate0);
		buttonRedHeader.Items.Add((RibbonControl)btnRedHeaderTemplate1);
		buttonRedHeader.Items.Add((RibbonControl)btnRedHeaderTemplate2);
		buttonRedHeader.Items.Add((RibbonControl)btnRedHeaderTemplate3);
		buttonRedHeader.Items.Add((RibbonControl)btnRedHeaderTemplate4);
		buttonRedHeader.Items.Add((RibbonControl)btnRedHeaderTemplate5);
		buttonRedHeader.Items.Add((RibbonControl)btnRedHeaderTemplate6);
		buttonRedHeader.Items.Add((RibbonControl)btnRedHeaderTemplate7);
		buttonRedHeader.Items.Add((RibbonControl)btnRedHeaderTemplate8);
		buttonRedHeader.Items.Add((RibbonControl)btnRedHeaderTemplate9);
		buttonRedHeader.Items.Add((RibbonControl)separatorRedHeaderSettings);
		buttonRedHeader.Items.Add((RibbonControl)btnRedHeaderExecute);
		buttonRedHeader.Items.Add((RibbonControl)btnRedHeaderSettings);
		OfficeInteropCompatibility.SetLargeControlSize(buttonConvert);
		buttonConvert.Label = "一键转换";
		((RibbonComponent)buttonConvert).Name = "buttonConvert";
		RibbonTextApplier.Apply(buttonConvert, "Ribbon.Convert");
		buttonConvert.Click += new RibbonControlEventHandler(buttonConvert_Click);
		btnConvertDocx.Label = "转为 DOCX";
		((RibbonComponent)btnConvertDocx).Name = "btnConvertDocx";
		btnConvertDocx.ShowImage = false;
		btnConvertDocx.Click += new RibbonControlEventHandler(btnConvertFormat_Click);
		btnConvertPdf.Label = "转为 PDF";
		((RibbonComponent)btnConvertPdf).Name = "btnConvertPdf";
		btnConvertPdf.ShowImage = false;
		btnConvertPdf.Click += new RibbonControlEventHandler(btnConvertFormat_Click);
		btnConvertImage.Label = "转为图片";
		((RibbonComponent)btnConvertImage).Name = "btnConvertImage";
		btnConvertImage.ShowImage = false;
		btnConvertImage.Click += new RibbonControlEventHandler(btnConvertFormat_Click);
		btnConvertTxt.Label = "转为 TXT";
		((RibbonComponent)btnConvertTxt).Name = "btnConvertTxt";
		btnConvertTxt.ShowImage = false;
		btnConvertTxt.Click += new RibbonControlEventHandler(btnConvertFormat_Click);
		btnPdfToWord.Label = "PDF 转 Word...";
		((RibbonComponent)btnPdfToWord).Name = "btnPdfToWord";
		btnPdfToWord.ShowImage = false;
		RibbonTextApplier.Apply(btnPdfToWord, "Ribbon.PdfToWord");
		btnPdfToWord.Click += new RibbonControlEventHandler(btnPdfToWord_Click);
		btnConvertExecute.Label = "一键转换";
		((RibbonComponent)btnConvertExecute).Name = "btnConvertExecute";
		btnConvertExecute.ShowImage = true;
		RibbonTextApplier.Apply(btnConvertExecute, "Ribbon.Convert.Execute");
		btnConvertExecute.Click += new RibbonControlEventHandler(buttonConvert_Click);
		btnConvertSettings.Label = "转换设置...";
		((RibbonComponent)btnConvertSettings).Name = "btnConvertSettings";
		btnConvertSettings.ShowImage = true;
		RibbonTextApplier.Apply(btnConvertSettings, "Ribbon.Convert.Settings");
		btnConvertSettings.Click += new RibbonControlEventHandler(btnConvertSettings_Click);
		buttonConvert.Items.Add((RibbonControl)btnConvertDocx);
		buttonConvert.Items.Add((RibbonControl)btnConvertPdf);
		buttonConvert.Items.Add((RibbonControl)btnConvertImage);
		buttonConvert.Items.Add((RibbonControl)btnConvertTxt);
		buttonConvert.Items.Add((RibbonControl)separatorPdfToWord);
		buttonConvert.Items.Add((RibbonControl)btnPdfToWord);
		buttonConvert.Items.Add((RibbonControl)separatorConvertSettings);
		buttonConvert.Items.Add((RibbonControl)btnConvertExecute);
		buttonConvert.Items.Add((RibbonControl)btnConvertSettings);
		OfficeInteropCompatibility.SetLargeControlSize(buttonRename);
		buttonRename.Label = "一键命名";
		((RibbonComponent)buttonRename).Name = "buttonRename";
		RibbonTextApplier.Apply(buttonRename, "Ribbon.Rename");
		buttonRename.Click += new RibbonControlEventHandler(buttonRename_Click);
		((RibbonComponent)btnRenameRule0).Name = "btnRenameRule0";
		btnRenameRule0.ShowImage = false;
		btnRenameRule0.Click += new RibbonControlEventHandler(btnRenameRule_Click);
		((RibbonComponent)btnRenameRule1).Name = "btnRenameRule1";
		btnRenameRule1.ShowImage = false;
		btnRenameRule1.Click += new RibbonControlEventHandler(btnRenameRule_Click);
		((RibbonComponent)btnRenameRule2).Name = "btnRenameRule2";
		btnRenameRule2.ShowImage = false;
		btnRenameRule2.Click += new RibbonControlEventHandler(btnRenameRule_Click);
		((RibbonComponent)btnRenameRule3).Name = "btnRenameRule3";
		btnRenameRule3.ShowImage = false;
		btnRenameRule3.Click += new RibbonControlEventHandler(btnRenameRule_Click);
		((RibbonComponent)btnRenameRule4).Name = "btnRenameRule4";
		btnRenameRule4.ShowImage = false;
		btnRenameRule4.Click += new RibbonControlEventHandler(btnRenameRule_Click);
		((RibbonComponent)btnRenameRule5).Name = "btnRenameRule5";
		btnRenameRule5.ShowImage = false;
		btnRenameRule5.Click += new RibbonControlEventHandler(btnRenameRule_Click);
		((RibbonComponent)btnRenameRule6).Name = "btnRenameRule6";
		btnRenameRule6.ShowImage = false;
		btnRenameRule6.Click += new RibbonControlEventHandler(btnRenameRule_Click);
		((RibbonComponent)btnRenameRule7).Name = "btnRenameRule7";
		btnRenameRule7.ShowImage = false;
		btnRenameRule7.Click += new RibbonControlEventHandler(btnRenameRule_Click);
		btnRenameExecute.Label = "一键命名";
		((RibbonComponent)btnRenameExecute).Name = "btnRenameExecute";
		btnRenameExecute.ShowImage = true;
		RibbonTextApplier.Apply(btnRenameExecute, "Ribbon.Rename.Execute");
		btnRenameExecute.Click += new RibbonControlEventHandler(buttonRename_Click);
		btnRenameSettings.Label = "规则设置...";
		((RibbonComponent)btnRenameSettings).Name = "btnRenameSettings";
		btnRenameSettings.ShowImage = true;
		RibbonTextApplier.Apply(btnRenameSettings, "Ribbon.Rename.Settings");
		btnRenameSettings.Click += new RibbonControlEventHandler(btnRenameSettings_Click);
		buttonRename.Items.Add((RibbonControl)btnRenameRule0);
		buttonRename.Items.Add((RibbonControl)btnRenameRule1);
		buttonRename.Items.Add((RibbonControl)btnRenameRule2);
		buttonRename.Items.Add((RibbonControl)btnRenameRule3);
		buttonRename.Items.Add((RibbonControl)btnRenameRule4);
		buttonRename.Items.Add((RibbonControl)btnRenameRule5);
		buttonRename.Items.Add((RibbonControl)btnRenameRule6);
		buttonRename.Items.Add((RibbonControl)btnRenameRule7);
		buttonRename.Items.Add((RibbonControl)separatorRenameSettings);
		buttonRename.Items.Add((RibbonControl)btnRenameExecute);
		buttonRename.Items.Add((RibbonControl)btnRenameSettings);
		base.Name = "Ribbon1";
		base.RibbonType = "Microsoft.Word.Document";
		base.Tabs.Add(tab1);
		base.Load += Ribbon1_Load;
		tab1.ResumeLayout(performLayout: false);
		tab1.PerformLayout();
		((RibbonComponent)groupLeft).ResumeLayout(performLayout: false);
		((RibbonComponent)groupLeft).PerformLayout();
		((RibbonComponent)groupSpacer).ResumeLayout(performLayout: false);
		((RibbonComponent)groupSpacer).PerformLayout();
		((RibbonComponent)groupCenter).ResumeLayout(performLayout: false);
		((RibbonComponent)groupCenter).PerformLayout();
		ResumeLayout(performLayout: false);
	}
}
