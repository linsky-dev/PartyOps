using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.Features;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Features;

namespace DocumentRepository.UI.Features;

public sealed class WinFormsFeatureUiService : IFeatureUiService
{
	public static readonly WinFormsFeatureUiService Instance = new WinFormsFeatureUiService();

	private WinFormsFeatureUiService()
	{
	}

	public bool Confirm(string caption, string message)
	{
		return (int)AppleMessageDialog.Show(message, caption, (MessageBoxButtons)1, (MessageBoxIcon)32) == 1;
	}

	bool IFeatureUiService.Confirm(string caption, string message)
	{
		return this.Confirm(caption, message);
	}

	public void ShowMessage(string caption, string message, FeatureMessageKind kind)
	{
		MessageBoxIcon icon = (MessageBoxIcon)(kind switch
		{
			FeatureMessageKind.Warning => 48, 
			FeatureMessageKind.Error => 16, 
			_ => 64, 
		});
		AppleMessageDialog.Show(message, caption, (MessageBoxButtons)0, icon);
	}

	void IFeatureUiService.ShowMessage(string caption, string message, FeatureMessageKind kind)
	{
		this.ShowMessage(caption, message, kind);
	}

	public void ShowNonModalWarning(string caption, string message)
	{
		WarningMessageForm.ShowMessage(caption, message, 4500);
	}

	public void ShowRecoveryAssistant(string caption, string message, string recoveryId)
	{
		RecoveryAssistantForm.ShowAssistant(caption, message, recoveryId);
	}

	public void ShowTimedMessage(string caption, string message, int milliseconds)
	{
		TimedMessageForm.ShowMessage(message, caption, Math.Max(500, milliseconds));
	}

	void IFeatureUiService.ShowTimedMessage(string caption, string message, int milliseconds)
	{
		this.ShowTimedMessage(caption, message, milliseconds);
	}

	public void ShowCompletionMessage(string caption, string summary, string detail, int milliseconds)
	{
		CompletionMessageForm.ShowMessage(caption, summary, detail, Math.Max(500, milliseconds));
	}

	void IFeatureUiService.ShowCompletionMessage(string caption, string summary, string detail, int milliseconds)
	{
		this.ShowCompletionMessage(caption, summary, detail, milliseconds);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ITaskProgressReporter CreateProgress(string featureId, string title)
	{
		string taskTitle = ResolveTaskTitle(featureId, title);
		if (!string.Equals(featureId, "convert", StringComparison.OrdinalIgnoreCase))
		{
			ProgressForm form = new ProgressForm();
			form.SetTaskTitle(taskTitle);
			form.SetMaximum(100);
			SimpleTaskProgressFormReporter simpleTaskProgressFormReporter = new SimpleTaskProgressFormReporter((Form)(object)form, delegate(int current, int total, string message)
			{
				form.SetProgress(Math.Max(0, Math.Min(100, current * 100 / Math.Max(1, total))), message);
			}, () => form.CancelRequested, form.SetTaskTitle);
			simpleTaskProgressFormReporter.Show();
			return simpleTaskProgressFormReporter;
		}
		ConvertProgressForm convertForm = new ConvertProgressForm(string.IsNullOrWhiteSpace(title) ? "正在转换文档..." : title);
		convertForm.SetTaskTitle(taskTitle);
		SimpleTaskProgressFormReporter simpleTaskProgressFormReporter2 = new SimpleTaskProgressFormReporter((Form)(object)convertForm, convertForm.UpdateProgress, () => convertForm.CancelRequested, convertForm.SetTaskTitle);
		simpleTaskProgressFormReporter2.Show();
		return simpleTaskProgressFormReporter2;
	}

	ITaskProgressReporter IFeatureUiService.CreateProgress(string featureId, string title)
	{
		return this.CreateProgress(featureId, title);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ResolveTaskTitle(string featureId, string fallback)
	{
		try
		{
			FeatureDescriptor byId = FeatureRegistry.GetById(featureId);
			if (byId != null && !string.IsNullOrWhiteSpace(byId.DisplayName))
			{
				return byId.DisplayName.Trim();
			}
		}
		catch
		{
		}
		if (!string.IsNullOrWhiteSpace(fallback))
		{
			return fallback.Trim();
		}
		return "正在处理";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string SelectPdfForConversion(string initialDirectory)
	{
		OpenFileDialog val = new OpenFileDialog();
		try
		{
			((FileDialog)val).Title = "选择要转换为 Word 的 PDF";
			((FileDialog)val).Filter = "PDF 文件 (*.pdf)|*.pdf";
			val.Multiselect = false;
			((FileDialog)val).CheckFileExists = true;
			((FileDialog)val).CheckPathExists = true;
			if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
			{
				((FileDialog)val).InitialDirectory = initialDirectory;
			}
			return ((int)((CommonDialog)val).ShowDialog() == 1) ? ((FileDialog)val).FileName : null;
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	string IFeatureUiService.SelectPdfForConversion(string initialDirectory)
	{
		return this.SelectPdfForConversion(initialDirectory);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ConvertConflictDecision ResolveConvertConflict(string targetPath)
	{
		DialogResult val = AppleMessageDialog.Show("目标文件已存在。\r\n\r\n选择“是”覆盖，选择“否”自动改名，选择“取消”停止转换。", "一键转换", (MessageBoxButtons)3, (MessageBoxIcon)32);
		if ((int)val != 6)
		{
			if ((int)val == 7)
			{
				return ConvertConflictDecision.AutoRename;
			}
			return ConvertConflictDecision.Cancel;
		}
		return ConvertConflictDecision.Overwrite;
	}

	ConvertConflictDecision IFeatureUiService.ResolveConvertConflict(string targetPath)
	{
		return this.ResolveConvertConflict(targetPath);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void OpenFolder(string folderPath)
	{
		if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
		{
			throw new DirectoryNotFoundException("输出文件夹不存在：" + folderPath);
		}
		Process.Start("explorer.exe", folderPath);
	}

	void IFeatureUiService.OpenFolder(string folderPath)
	{
		this.OpenFolder(folderPath);
	}

	public CompilationConfirmationResult ConfirmCompilation(CompilationConfirmationRequest request)
	{
		CompilationConfirmationForm compilationConfirmationForm = new CompilationConfirmationForm(request, request?.OfferExpandToFullArticles ?? false);
		try
		{
			DialogResult val = ((Form)compilationConfirmationForm).ShowDialog();
			CompilationConfirmationResult result = compilationConfirmationForm.GetResult();
			result.Confirmed = (int)val == 1;
			return result;
		}
		finally
		{
			((IDisposable)compilationConfirmationForm)?.Dispose();
		}
	}

	CompilationConfirmationResult IFeatureUiService.ConfirmCompilation(CompilationConfirmationRequest request)
	{
		return this.ConfirmCompilation(request);
	}
}
