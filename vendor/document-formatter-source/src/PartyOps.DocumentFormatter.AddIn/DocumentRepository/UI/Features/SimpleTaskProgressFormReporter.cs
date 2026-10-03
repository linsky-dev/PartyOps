using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.UI.Features;

public sealed class SimpleTaskProgressFormReporter : ITaskProgressReporter, IDisposable
{
	private const long MinimumRefreshIntervalMilliseconds = 100L;

	private static Form activeProgressForm;

	private readonly Form form;

	private readonly Action<int, int, string> updateProgress;

	private readonly Action<string> updateTaskName;

	private readonly Func<bool> cancellationRequested;

	private readonly Stopwatch refreshStopwatch = Stopwatch.StartNew();

	private int lastCurrent;

	private int lastTotal = 1;

	private int lastRenderedPercent = -1;

	private string lastRenderedStage;

	private string lastRenderedStep;

	private string lastRenderedTaskName;

	private bool disposed;

	public bool CancellationRequested => cancellationRequested();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public SimpleTaskProgressFormReporter(Form form, Action<int, int, string> updateProgress, Func<bool> cancellationRequested, Action<string> updateTaskName = null)
	{
		if (form != null)
		{
			if (updateProgress == null)
			{
				throw new ArgumentNullException("updateProgress");
			}
			this.form = form;
			this.updateProgress = updateProgress;
			this.cancellationRequested = cancellationRequested ?? ((Func<bool>)(() => false));
			this.updateTaskName = updateTaskName;
			return;
		}
		throw new ArgumentNullException("form");
	}

	public void Show()
	{
		activeProgressForm = form;
		((Control)form).Show();
		((Control)form).BringToFront();
		form.Activate();
		Application.DoEvents();
	}

	public void Start(string taskName, int totalSteps, string message)
	{
		Report(TaskProgressInfo.Create(taskName, 0, totalSteps, message));
	}

	void ITaskProgressReporter.Start(string taskName, int totalSteps, string message)
	{
		this.Start(taskName, totalSteps, message);
	}

	public void Report(TaskProgressInfo info)
	{
		if (info == null)
		{
			return;
		}
		lastTotal = Math.Max(1, info.TotalSteps);
		lastCurrent = Math.Max(0, Math.Min(info.CurrentStep, lastTotal));
		bool num = EnsureVisible();
		int num2 = lastCurrent * 100 / lastTotal;
		bool flag = !string.Equals(lastRenderedTaskName, info.TaskName, StringComparison.Ordinal);
		bool flag2 = !string.Equals(lastRenderedStage, info.Stage, StringComparison.Ordinal) || !string.Equals(lastRenderedStep, info.Step, StringComparison.Ordinal);
		bool flag3 = info.State == TaskExecutionState.Succeeded || info.State == TaskExecutionState.SucceededWithWarnings || info.State == TaskExecutionState.Cancelled || info.State == TaskExecutionState.Failed;
		bool flag4 = lastRenderedPercent < 0;
		bool flag5 = refreshStopwatch.ElapsedMilliseconds >= 100;
		if (num || flag4 || flag || flag2 || flag3 || flag5)
		{
			if (flag && updateTaskName != null)
			{
				updateTaskName(info.TaskName);
			}
			updateProgress(lastCurrent, lastTotal, BuildMessage(info));
			lastRenderedPercent = num2;
			lastRenderedStage = info.Stage;
			lastRenderedStep = info.Step;
			lastRenderedTaskName = info.TaskName;
			refreshStopwatch.Restart();
			PumpUiOnce();
		}
	}

	void ITaskProgressReporter.Report(TaskProgressInfo info)
	{
		this.Report(info);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Complete(string message)
	{
		RenderTerminal(lastTotal, string.IsNullOrWhiteSpace(message) ? "处理完成" : message);
	}

	void ITaskProgressReporter.Complete(string message)
	{
		this.Complete(message);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void CompleteWithWarnings(string message)
	{
		RenderTerminal(lastTotal, string.IsNullOrWhiteSpace(message) ? "处理完成，但有警告" : message);
	}

	void ITaskProgressReporter.CompleteWithWarnings(string message)
	{
		this.CompleteWithWarnings(message);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Cancel(string message)
	{
		RenderTerminal(lastCurrent, string.IsNullOrWhiteSpace(message) ? "任务已取消" : message);
	}

	void ITaskProgressReporter.Cancel(string message)
	{
		this.Cancel(message);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Fail(string message)
	{
		RenderTerminal(lastCurrent, string.IsNullOrWhiteSpace(message) ? "处理失败" : message);
	}

	void ITaskProgressReporter.Fail(string message)
	{
		this.Fail(message);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Dispose()
	{
		if (disposed)
		{
			return;
		}
		disposed = true;
		if (activeProgressForm == form)
		{
			activeProgressForm = null;
		}
		try
		{
			if (!((Control)form).IsDisposed)
			{
				form.Close();
			}
			((Component)(object)form).Dispose();
		}
		catch (Exception ex)
		{
			LogService.Warn("SimpleTaskProgressFormReporter.Dispose", ex);
		}
	}

	void IDisposable.Dispose()
	{
		this.Dispose();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void BringActiveToFront()
	{
		Form val = activeProgressForm;
		if (val == null || ((Control)val).IsDisposed)
		{
			return;
		}
		try
		{
			if (!((Control)val).Visible)
			{
				((Control)val).Show();
			}
			((Control)val).BringToFront();
			val.Activate();
		}
		catch (Exception ex)
		{
			LogService.Warn("SimpleTaskProgressFormReporter.BringActiveToFront", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildMessage(TaskProgressInfo info)
	{
		string text = ((!string.IsNullOrWhiteSpace(info.Stage) && !string.IsNullOrWhiteSpace(info.Step)) ? (info.Stage + "：" + info.Step) : ((!string.IsNullOrWhiteSpace(info.Step)) ? info.Step : (string.IsNullOrWhiteSpace(info.Stage) ? "正在处理..." : info.Stage)));
		if (!string.IsNullOrWhiteSpace(info.CurrentItem))
		{
			text = text + "｜" + info.CurrentItem;
		}
		if (info.ElapsedMilliseconds > 0)
		{
			text = text + "｜已用时 " + Math.Max(1L, info.ElapsedMilliseconds / 1000) + " 秒";
		}
		return text;
	}

	private bool EnsureVisible()
	{
		if (((Control)form).Visible)
		{
			return false;
		}
		((Control)form).Show();
		((Control)form).BringToFront();
		form.Activate();
		return true;
	}

	private void RenderTerminal(int current, string message)
	{
		lastCurrent = Math.Max(0, Math.Min(current, lastTotal));
		updateProgress(lastCurrent, lastTotal, message);
		lastRenderedPercent = lastCurrent * 100 / Math.Max(1, lastTotal);
		lastRenderedStage = null;
		lastRenderedStep = message;
		refreshStopwatch.Restart();
		PumpUiOnce();
	}

	private static void PumpUiOnce()
	{
		Application.DoEvents();
	}
}
