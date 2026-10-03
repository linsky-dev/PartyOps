using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Tasks;

public static class TaskEventReporter
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string MapOutcome(TaskOutcomeStatus status)
	{
		return status switch
		{
			TaskOutcomeStatus.SucceededWithWarnings => "warning", 
			TaskOutcomeStatus.Succeeded => "success", 
			TaskOutcomeStatus.Cancelled => "cancelled", 
			_ => "failed", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Report(ITaskEventJournal journal, TaskEventType type, string taskId, string featureId, string invocationSource = null, string stage = null, string outcome = null, string reasonCode = null, string documentPath = null, long elapsedMilliseconds = 0L, Exception exception = null)
	{
		if (journal == null)
		{
			return;
		}
		try
		{
			journal.Record(new TaskEvent
			{
				TaskId = taskId,
				FeatureId = featureId,
				InvocationSource = invocationSource,
				EventType = type.ToString(),
				Stage = stage,
				Outcome = outcome,
				ReasonCode = reasonCode,
				DocumentPath = documentPath,
				ElapsedMilliseconds = elapsedMilliseconds,
				ExceptionType = exception?.GetType().Name
			});
		}
		catch (Exception ex)
		{
			LogService.Warn("TaskEventReporter.Report", ex);
		}
	}

	public static void Report(TaskRuntimeContext context, string taskId, TaskEventType type, string featureId, string invocationSource = null, string stage = null, string outcome = null, string reasonCode = null, string documentPath = null, long elapsedMilliseconds = 0L, Exception exception = null)
	{
		if (context != null && context.Events != null)
		{
			Report(context.Events, type, taskId, featureId, invocationSource, stage, outcome, reasonCode, documentPath, elapsedMilliseconds, exception);
		}
	}
}
