using System;

namespace DocumentRepository.Models.Tasks;

public class TaskProgressInfo
{
	public string FeatureId { get; set; }

	public string TaskName { get; set; }

	public string Stage { get; set; }

	public string Step { get; set; }

	public int CurrentStep { get; set; }

	public int TotalSteps { get; set; }

	public string CurrentItem { get; set; }

	public long ElapsedMilliseconds { get; set; }

	public TaskExecutionState State { get; set; }

	public int SuccessCount { get; set; }

	public int FailedCount { get; set; }

	public int SkippedCount { get; set; }

	public int Percent
	{
		get
		{
			int num = Math.Max(1, TotalSteps);
			int num2 = Math.Max(0, Math.Min(CurrentStep, num));
			return Math.Max(0, Math.Min(100, num2 * 100 / num));
		}
	}

	public static TaskProgressInfo Create(string taskName, int currentStep, int totalSteps, string step, string stage = null)
	{
		return new TaskProgressInfo
		{
			TaskName = taskName,
			State = TaskExecutionState.Running,
			Stage = stage,
			Step = step,
			CurrentStep = currentStep,
			TotalSteps = totalSteps
		};
	}
}
