using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Startup;

internal sealed class AddInStartupCoordinator
{
	private const long StartupBudgetMilliseconds = 300L;

	private readonly object _syncRoot = new object();

	private readonly List<string> _failedSteps = new List<string>();

	private readonly HashSet<string> _successfulSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly string _startupId;

	private readonly Stopwatch _startupStopwatch;

	private bool _coreReady;

	private bool _handlersReady;

	private bool _ribbonReady;

	private bool _startupCompleted;

	private AddInStartupCoordinator(string startupId)
	{
		_startupId = startupId;
		_startupStopwatch = Stopwatch.StartNew();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static AddInStartupCoordinator Begin(string version, string hostName, string hostVersion)
	{
		string text = Guid.NewGuid().ToString("N").Substring(0, 12);
		LogService.SetStartupSession(text);
		LogService.Info("STARTUP_SESSION BEGIN");
		LogService.LogEnvironment(version, hostName, hostVersion);
		StartupHealthService.Begin(text, version, hostName, hostVersion);
		return new AddInStartupCoordinator(text);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool RunStep(string name, StartupStepKind kind, Action action)
	{
		if (action == null)
		{
			return true;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		string text = "STARTUP_STEP name=" + name + " kind=" + kind;
		LogService.Info(text + " state=START");
		StartupHealthService.Mark("step-running", name, kind.ToString());
		try
		{
			StartupFaultInjection.ThrowIfRequested(name);
			action();
			stopwatch.Stop();
			lock (_syncRoot)
			{
				_successfulSteps.Add(name);
			}
			LogService.Info(text + " state=END elapsedMs=" + stopwatch.ElapsedMilliseconds);
			StartupHealthService.Mark("step-complete", name, "elapsedMs=" + stopwatch.ElapsedMilliseconds);
			return true;
		}
		catch (Exception ex)
		{
			stopwatch.Stop();
			RecordFailure(name);
			LogService.Error(text + " state=FAIL elapsedMs=" + stopwatch.ElapsedMilliseconds, ex);
			StartupHealthService.Mark("step-failed", name, kind.ToString() + ";" + ex.GetType().FullName + ";" + ex.Message);
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool RunDeferredStep(string name, Action action)
	{
		if (action == null)
		{
			return true;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		string text = "STARTUP_DEFERRED_STEP name=" + name;
		LogService.Info(text + " state=START");
		try
		{
			StartupFaultInjection.ThrowIfRequested(name);
			action();
			stopwatch.Stop();
			lock (_syncRoot)
			{
				_successfulSteps.Add(name);
			}
			LogService.Info(text + " state=END elapsedMs=" + stopwatch.ElapsedMilliseconds);
			StartupHealthService.Mark("startup-complete", "deferred-" + name, "state=END;elapsedMs=" + stopwatch.ElapsedMilliseconds + ";failures=" + FailureSummary());
			return true;
		}
		catch (Exception ex)
		{
			stopwatch.Stop();
			RecordFailure(name);
			LogService.Error(text + " state=FAIL elapsedMs=" + stopwatch.ElapsedMilliseconds, ex);
			StartupHealthService.Mark("startup-complete", "deferred-" + name, "state=FAIL;" + ex.GetType().FullName + ";" + ex.Message + ";failures=" + FailureSummary());
			return false;
		}
	}

	public bool WasStepSuccessful(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		lock (_syncRoot)
		{
			return _successfulSteps.Contains(name);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void MarkCoreReady()
	{
		lock (_syncRoot)
		{
			_coreReady = true;
		}
		LogService.Info("STARTUP_STATE core-ready failures=" + FailureSummary());
		StartupHealthService.Mark("core-ready", "core", FailureSummary());
		TryCompleteStartup();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void MarkHandlersReady()
	{
		lock (_syncRoot)
		{
			_handlersReady = true;
		}
		LogService.Info("STARTUP_STATE handlers-ready failures=" + FailureSummary());
		StartupHealthService.Mark("handlers-ready", "startup-handlers", FailureSummary());
		TryCompleteStartup();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void MarkRibbonReady()
	{
		lock (_syncRoot)
		{
			_ribbonReady = true;
		}
		LogService.Info("STARTUP_STATE ribbon-ready failures=" + FailureSummary());
		StartupHealthService.Mark("ribbon-ready", "ribbon", FailureSummary());
		TryCompleteStartup();
	}

	public void RecordFailure(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}
		lock (_syncRoot)
		{
			if (!_failedSteps.Contains(name))
			{
				_failedSteps.Add(name);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void MarkShutdownComplete()
	{
		LogService.Info("STARTUP_SESSION SHUTDOWN_COMPLETE startup=" + _startupId);
		StartupHealthService.Mark("shutdown-complete", "shutdown", FailureSummary());
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string FailureSummary()
	{
		lock (_syncRoot)
		{
			return (_failedSteps.Count == 0) ? "none" : string.Join(",", _failedSteps.ToArray());
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void TryCompleteStartup()
	{
		bool flag;
		string text;
		long elapsedMilliseconds;
		lock (_syncRoot)
		{
			if (_startupCompleted || !_coreReady || !_handlersReady || !_ribbonReady)
			{
				return;
			}
			_startupCompleted = true;
			flag = _failedSteps.Count > 0;
			text = ((_failedSteps.Count == 0) ? "none" : string.Join(",", _failedSteps.ToArray()));
			_startupStopwatch.Stop();
			elapsedMilliseconds = _startupStopwatch.ElapsedMilliseconds;
		}
		string text2 = "elapsedMs=" + elapsedMilliseconds + ";budgetMs=" + 300L + ";degraded=" + flag + ";failures=" + text;
		LogService.Info("STARTUP_SESSION COMPLETE " + text2);
		if (elapsedMilliseconds > 300)
		{
			LogService.Warn("STARTUP_BUDGET_EXCEEDED " + text2);
		}
		StartupHealthService.Mark("startup-complete", "complete", text2);
	}
}
