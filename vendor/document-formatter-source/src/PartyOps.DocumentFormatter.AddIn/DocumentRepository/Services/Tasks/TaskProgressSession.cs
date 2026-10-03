using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Services.Tasks;

public sealed class TaskProgressSession : ITaskProgressReporter, IDisposable
{
	private readonly ITaskProgressReporter inner;

	private readonly string featureId;

	private readonly string taskName;

	private readonly Stopwatch stopwatch = new Stopwatch();

	private bool started;

	private bool terminal;

	private int lastPercent;

	private TaskProgressInfo lastInfo;

	public TaskCancellationSignal Cancellation { get; private set; }

	public bool CancellationRequested => inner.CancellationRequested;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public TaskProgressSession(ITaskProgressReporter inner, string featureId, string taskName)
	{
		this.inner = inner ?? throw new ArgumentNullException("inner");
		if (string.IsNullOrWhiteSpace(featureId))
		{
			throw new ArgumentException("Feature id cannot be empty.", "featureId");
		}
		this.featureId = featureId.Trim();
		if (string.IsNullOrWhiteSpace(taskName))
		{
			throw new ArgumentException("Task name cannot be empty.", "taskName");
		}
		this.taskName = taskName.Trim();
		Cancellation = new TaskCancellationSignal(() => CancellationRequested);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Start(string ignoredTaskName, int totalSteps, string message)
	{
		if (started)
		{
			throw new InvalidOperationException("任务进度会话不能重复开始。");
		}
		if (totalSteps <= 0)
		{
			throw new ArgumentOutOfRangeException("totalSteps");
		}
		started = true;
		stopwatch.Start();
		inner.Start(taskName, totalSteps, message);
		Report(TaskProgressInfo.Create(taskName, 0, totalSteps, message, "准备"));
	}

	void ITaskProgressReporter.Start(string ignoredTaskName, int totalSteps, string message)
	{
		this.Start(ignoredTaskName, totalSteps, message);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Report(TaskProgressInfo info)
	{
		EnsureRunning();
		if (info != null)
		{
			if (info.TotalSteps <= 0)
			{
				throw new InvalidOperationException("任务总步骤必须大于零。");
			}
			if (info.CurrentStep >= 0 && info.CurrentStep <= info.TotalSteps)
			{
				if (info.Percent >= lastPercent)
				{
					info.FeatureId = featureId;
					info.TaskName = taskName;
					info.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
					info.State = ((!CancellationRequested) ? TaskExecutionState.Running : TaskExecutionState.Cancelling);
					lastPercent = info.Percent;
					lastInfo = info;
					inner.Report(info);
					return;
				}
				throw new InvalidOperationException("任务进度不允许倒退：" + lastPercent + "% -> " + info.Percent + "%。");
			}
			throw new InvalidOperationException("任务当前步骤超出有效范围。");
		}
		throw new ArgumentNullException("info");
	}

	void ITaskProgressReporter.Report(TaskProgressInfo info)
	{
		this.Report(info);
	}

	public void Complete(string message)
	{
		Finish(TaskExecutionState.Succeeded, message, inner.Complete);
	}

	void ITaskProgressReporter.Complete(string message)
	{
		this.Complete(message);
	}

	public void CompleteWithWarnings(string message)
	{
		Finish(TaskExecutionState.SucceededWithWarnings, message, inner.CompleteWithWarnings);
	}

	void ITaskProgressReporter.CompleteWithWarnings(string message)
	{
		this.CompleteWithWarnings(message);
	}

	public void Cancel(string message)
	{
		Finish(TaskExecutionState.Cancelled, message, inner.Cancel);
	}

	void ITaskProgressReporter.Cancel(string message)
	{
		this.Cancel(message);
	}

	public void Fail(string message)
	{
		Finish(TaskExecutionState.Failed, message, inner.Fail);
	}

	void ITaskProgressReporter.Fail(string message)
	{
		this.Fail(message);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Dispose()
	{
		try
		{
			if (!terminal && started)
			{
				Fail("任务异常结束，未产生明确终态。");
			}
		}
		finally
		{
			inner.Dispose();
			stopwatch.Stop();
		}
	}

	void IDisposable.Dispose()
	{
		this.Dispose();
	}

	private void Finish(TaskExecutionState state, string message, Action<string> terminalAction)
	{
		EnsureRunning();
		terminal = true;
		stopwatch.Stop();
		TaskProgressInfo taskProgressInfo = lastInfo ?? TaskProgressInfo.Create(taskName, 0, 1, message);
		taskProgressInfo.FeatureId = featureId;
		taskProgressInfo.TaskName = taskName;
		taskProgressInfo.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
		taskProgressInfo.State = state;
		taskProgressInfo.Step = (string.IsNullOrWhiteSpace(message) ? taskProgressInfo.Step : message);
		inner.Report(taskProgressInfo);
		terminalAction(message);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void EnsureRunning()
	{
		if (started)
		{
			if (terminal)
			{
				throw new InvalidOperationException("任务已经结束，不能继续上报进度或修改终态。");
			}
			return;
		}
		throw new InvalidOperationException("任务进度会话尚未开始。");
	}
}
