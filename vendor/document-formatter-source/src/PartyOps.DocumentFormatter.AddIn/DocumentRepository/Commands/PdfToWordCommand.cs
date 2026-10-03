using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.Features;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Pipelines.Convert;
using DocumentRepository.Services.Conversion;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Rules;
using DocumentRepository.Services.Tasks;

namespace DocumentRepository.Commands;

public sealed class PdfToWordCommand : ICommand
{
	public string Name
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			return "PDF 转 Word";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public CommandResult Execute(OperationContext context)
	{
		if (context == null || context.Application == null)
		{
			return CommandResult.FailResult("Word/WPS 应用不可用。");
		}
		ConvertOptions convertOptions = null;
		try
		{
			IFeatureUiService ui = context.UserInterface ?? throw new InvalidOperationException("PDF 转 Word 缺少用户交互服务。");
			string text = PdfToWordSourceResolver.Resolve(OutputPathService.GetDocumentPath(context.Document), (string initialDirectory) => ui.SelectPdfForConversion(initialDirectory));
			if (!string.IsNullOrWhiteSpace(text))
			{
				convertOptions = ConvertSettingsService.Load();
				using TaskProgressSession taskProgressSession = new TaskProgressSession(ui.CreateProgress("pdf-to-word", "正在将 PDF 转换为 Word..."), "pdf-to-word", Name);
				taskProgressSession.Start(Name, 100, "正在准备转换...");
				TaskRuntimeContext task = new TaskRuntimeContext(taskProgressSession, taskProgressSession.Cancellation, new TaskArtifactRegistry());
				ConvertResult convertResult = new PdfToWordPipeline().Execute(new PdfToWordRequest
				{
					Context = context,
					SourcePath = text,
					Options = convertOptions,
					Task = task,
					ConflictResolver = ui.ResolveConvertConflict
				});
				string text2 = null;
				if (convertResult.Success && convertOptions.OpenFolderAfterConvert && !string.IsNullOrWhiteSpace(convertResult.OutputFolder))
				{
					try
					{
						ui.OpenFolder(convertResult.OutputFolder);
					}
					catch (Exception ex)
					{
						text2 = "DOCX 已生成，但未能自动打开输出文件夹。请手动前往保存位置查看。";
						LogService.Warn("PdfToWordCommand.OpenFolder", ex);
					}
				}
				if (!convertResult.Cancelled)
				{
					if (!convertResult.HasWarnings && string.IsNullOrWhiteSpace(text2))
					{
						if (convertResult.Success)
						{
							taskProgressSession.Complete("转换完成");
						}
						else
						{
							taskProgressSession.Fail(convertResult.Message);
						}
					}
					else
					{
						taskProgressSession.CompleteWithWarnings(text2 ?? convertResult.Message);
					}
				}
				else
				{
					taskProgressSession.Cancel(convertResult.Message);
				}
				if (!convertResult.Cancelled)
				{
					if (string.IsNullOrWhiteSpace(text2))
					{
						if (convertResult.HasWarnings)
						{
							return CommandResult.WarningResult(convertResult.Message);
						}
						return convertResult.Success ? CommandResult.SuccessResult(convertResult.Message) : CommandResult.FailResult(convertResult.Message);
					}
					return CommandResult.WarningResult(convertResult.Message + "\r\n" + text2);
				}
				return CommandResult.CancelledResult(convertResult.Message);
			}
			return CommandResult.CancelledResult("已取消 PDF 转 Word。");
		}
		catch (RuleStoreUnavailableException ex2)
		{
			LogService.Error("PdfToWordCommand.Configuration", ex2);
			return ConvertFailurePresentation.ToCommandResult(ConvertOperationException.Create(ConvertFailureReasonCode.ConfigurationUnavailable, ConvertFailureStage.Entry, ex2));
		}
		catch (Exception ex3)
		{
			LogService.Error("PdfToWordCommand.Execute", ex3);
			return ConvertFailurePresentation.ToCommandResult(ConvertFailureClassifier.Classify(ex3, ConvertFailureStage.Entry, convertOptions));
		}
	}

	CommandResult ICommand.Execute(OperationContext context)
	{
		return this.Execute(context);
	}
}
