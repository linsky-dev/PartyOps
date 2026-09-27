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

public class ConvertCommand : ICommand
{
	public string Name
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			return "一键转换";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public CommandResult Execute(OperationContext context)
	{
		if (context == null || context.Document == null)
		{
			return CommandResult.FailResult("请先打开一个文档。");
		}
		ConvertOptions convertOptions = null;
		try
		{
			IFeatureUiService featureUiService = context.UserInterface ?? throw new InvalidOperationException("一键转换缺少用户交互服务。");
			convertOptions = ConvertSettingsService.Load();
			using TaskProgressSession taskProgressSession = new TaskProgressSession(featureUiService.CreateProgress("convert", "正在转换文档..."), "convert", Name);
			taskProgressSession.Start(Name, 100, "正在准备转换...");
			TaskRuntimeContext task = new TaskRuntimeContext(taskProgressSession, taskProgressSession.Cancellation, new TaskArtifactRegistry());
			ConvertResult convertResult = new ConvertPipeline().Execute(new ConvertRequest
			{
				Context = context,
				Options = convertOptions,
				Task = task,
				ConflictResolver = featureUiService.ResolveConvertConflict
			});
			string text = null;
			if (convertResult.Success && convertOptions.OpenFolderAfterConvert && !string.IsNullOrWhiteSpace(convertResult.OutputFolder))
			{
				try
				{
					featureUiService.OpenFolder(convertResult.OutputFolder);
				}
				catch (Exception ex)
				{
					text = "转换文件已生成，但未能自动打开输出文件夹。";
					LogService.Warn("ConvertCommand.OpenFolder", ex);
				}
			}
			if (!convertResult.Cancelled)
			{
				if (!convertResult.HasWarnings && string.IsNullOrWhiteSpace(text))
				{
					if (!convertResult.Success)
					{
						taskProgressSession.Fail(convertResult.Message);
					}
					else
					{
						taskProgressSession.Complete("转换完成");
					}
				}
				else
				{
					taskProgressSession.CompleteWithWarnings(text ?? convertResult.Message);
				}
			}
			else
			{
				taskProgressSession.Cancel(convertResult.Message);
			}
			if (convertResult.Cancelled)
			{
				return CommandResult.CancelledResult(convertResult.Message).WithHandledPresentation();
			}
			if (!string.IsNullOrWhiteSpace(text))
			{
				return CommandResult.WarningResult(convertResult.Message + "\r\n" + text);
			}
			if (!convertResult.HasWarnings)
			{
				if (convertResult.Success)
				{
					return CommandResult.SuccessResult(convertResult.Message);
				}
				CommandResult commandResult = CommandResult.FailResult(convertResult.Message);
				commandResult.OutcomeKind = convertResult.OutcomeKind;
				commandResult.FailureReasonCode = convertResult.FailureReasonCode;
				commandResult.FailureStage = convertResult.FailureStage;
				return commandResult;
			}
			return CommandResult.WarningResult(convertResult.Message);
		}
		catch (RuleStoreUnavailableException ex2)
		{
			LogService.Error("ConvertCommand.Configuration", ex2);
			return ConvertFailurePresentation.ToCommandResult(ConvertOperationException.Create(ConvertFailureReasonCode.ConfigurationUnavailable, ConvertFailureStage.Entry, ex2));
		}
		catch (Exception ex3)
		{
			LogService.Error("ConvertCommand.Execute", ex3);
			return ConvertFailurePresentation.ToCommandResult(ConvertFailureClassifier.Classify(ex3, ConvertFailureStage.Entry, convertOptions));
		}
	}

	CommandResult ICommand.Execute(OperationContext context)
	{
		return this.Execute(context);
	}
}
