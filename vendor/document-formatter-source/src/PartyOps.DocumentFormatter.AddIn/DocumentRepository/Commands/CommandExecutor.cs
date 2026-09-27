using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Features;
using DocumentRepository.Services.Features;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Safety;

namespace DocumentRepository.Commands;

public static class CommandExecutor
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult Execute(ICommand command, OperationContext context, string commandType)
	{
		if (command != null)
		{
			string text = (string.IsNullOrWhiteSpace(command.Name) ? command.GetType().Name : command.Name);
			string text2 = (string.IsNullOrWhiteSpace(commandType) ? command.GetType().Name : commandType);
			Stopwatch stopwatch = Stopwatch.StartNew();
			RuntimeOperationScope runtimeOperationScope = null;
			bool flag = false;
			LogService.Info("Command start type=" + text2 + ", name=" + text + ", context=" + BuildContextSummary(context));
			try
			{
				HostThreadRuntime.AssertAccess("CommandExecutor.Execute." + text2);
				ExecutionWarningCollector.BeginOperation();
				flag = true;
				runtimeOperationScope = RuntimeOperationScope.Begin(text2, text, context?.Document, ConfigManager.CurrentTemplateIndex);
				CommandResult commandResult = command.Execute(context);
				if (commandResult == null)
				{
					throw FeatureEntryOperationException.Create(FeatureEntryFailureReasonCode.CommandReturnedNoResult);
				}
				stopwatch.Stop();
				runtimeOperationScope.Complete(commandResult.Success, commandResult.Status.ToString(), context?.Document, context?.Stage, context?.ObjectLocation);
				LogService.Info("Command finish type=" + text2 + ", success=" + commandResult.Success + ", status=" + commandResult.Status.ToString() + ", elapsedMs=" + stopwatch.ElapsedMilliseconds + ", execution=" + BuildExecutionSummary(context) + ", hasMessage=" + !string.IsNullOrWhiteSpace(commandResult.Message));
				return commandResult;
			}
			catch (Exception ex)
			{
				stopwatch.Stop();
				runtimeOperationScope?.Complete(success: false, "exception-" + ex.GetType().Name, context?.Document, context?.Stage, context?.ObjectLocation);
				LogService.Error("Command failed type=" + text2 + ", elapsedMs=" + stopwatch.ElapsedMilliseconds + ", execution=" + BuildExecutionSummary(context), ex);
				return (ex is FeatureEntryOperationException failure) ? FeatureEntryFailurePresentation.FromException(failure, text) : FeatureEntryFailurePresentation.ToCommandResult(FeatureEntryFailureReasonCode.UnexpectedDuringCommand, text, ex);
			}
			finally
			{
				if (flag)
				{
					ExecutionWarningCollector.EndOperation();
				}
				runtimeOperationScope?.Dispose();
			}
		}
		return FeatureEntryFailurePresentation.ToCommandResult(FeatureEntryFailureReasonCode.CommandUnavailable, "当前功能");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildContextSummary(OperationContext context)
	{
		if (context == null)
		{
			return "null";
		}
		return "hasApp=" + (context.Application != null) + ", hasDocument=" + (context.Document != null) + ", hasSelection=" + (context.Selection != null) + ", batch=" + context.IsBatchMode + ", suppressDialogs=" + context.SuppressUserDialogs + ", templateIndex=" + ConfigManager.CurrentTemplateIndex;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildExecutionSummary(OperationContext context)
	{
		if (context == null)
		{
			return "null";
		}
		return "taskId=" + (context.TaskId ?? "none") + ", feature=" + (context.FeatureId ?? "none") + ", source=" + (context.InvocationSource ?? "none") + ", stage=" + (context.Stage ?? "none") + ", hasLocation=" + !string.IsNullOrWhiteSpace(context.ObjectLocation);
	}
}
