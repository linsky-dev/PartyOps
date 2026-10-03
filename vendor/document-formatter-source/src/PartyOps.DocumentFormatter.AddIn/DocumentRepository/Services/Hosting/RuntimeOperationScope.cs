using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Hosting;

public sealed class RuntimeOperationScope : IDisposable
{
	private readonly string operationType;

	private readonly string operationName;

	private readonly int templateIndex;

	private readonly Stopwatch stopwatch;

	private readonly RuntimeMetricSnapshot baseline;

	private bool completed;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private RuntimeOperationScope(string operationType, string operationName, Document document, int templateIndex)
	{
		this.operationType = Normalize(operationType);
		this.operationName = Normalize(operationName);
		this.templateIndex = templateIndex;
		stopwatch = Stopwatch.StartNew();
		baseline = RuntimeMetricsCollector.Capture(document, includeDocumentScale: true);
		LogService.Info("RUNTIME_METRICS phase=BEGIN, type=" + this.operationType + ", name=" + this.operationName + ", templateIndex=" + this.templateIndex + ", " + baseline.ToLogFields());
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuntimeOperationScope Begin(string operationType, string operationName, Document document, int templateIndex)
	{
		HostThreadRuntime.AssertAccess("RuntimeOperationScope.Begin." + Normalize(operationType));
		return new RuntimeOperationScope(operationType, operationName, document, templateIndex);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Complete(bool success, string status, Document document, string stage, string location)
	{
		if (completed)
		{
			return;
		}
		completed = true;
		stopwatch.Stop();
		RuntimeMetricSnapshot runtimeMetricSnapshot = null;
		try
		{
			runtimeMetricSnapshot = RuntimeMetricsCollector.Capture(document, includeDocumentScale: true);
			string[] obj = new string[20]
			{
				"RUNTIME_METRICS phase=END, type=", operationType, ", name=", operationName, ", templateIndex=", null, null, null, null, null,
				null, null, null, null, null, null, null, null, null, null
			};
			int num = templateIndex;
			obj[5] = num.ToString();
			obj[6] = ", success=";
			obj[7] = success.ToString();
			obj[8] = ", status=";
			obj[9] = Normalize(status);
			obj[10] = ", stage=";
			obj[11] = Normalize(stage);
			obj[12] = ", location=";
			obj[13] = Normalize(location);
			obj[14] = ", elapsedMs=";
			obj[15] = stopwatch.ElapsedMilliseconds.ToString();
			obj[16] = ", ";
			obj[17] = runtimeMetricSnapshot.ToLogFields();
			obj[18] = ", ";
			obj[19] = runtimeMetricSnapshot.BuildDelta(baseline);
			LogService.Info(string.Concat(obj));
		}
		catch (Exception ex)
		{
			LogService.Warn("RUNTIME_METRICS phase=END capture-failed, type=" + operationType + ", name=" + operationName + ", elapsedMs=" + stopwatch.ElapsedMilliseconds, ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Dispose()
	{
		if (!completed)
		{
			completed = true;
			stopwatch.Stop();
			LogService.Warn("RUNTIME_METRICS phase=ABANDONED, type=" + operationType + ", name=" + operationName + ", elapsedMs=" + stopwatch.ElapsedMilliseconds);
		}
	}

	void IDisposable.Dispose()
	{
		this.Dispose();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Normalize(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value;
		}
		return "unspecified";
	}
}
