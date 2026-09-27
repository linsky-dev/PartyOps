using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Hosting;

public static class RuntimeMetricsCollector
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuntimeMetricSnapshot Capture(Document document, bool includeDocumentScale)
	{
		RuntimeMetricSnapshot runtimeMetricSnapshot = new RuntimeMetricSnapshot
		{
			TimestampUtc = DateTime.UtcNow,
			ManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
			ManagedThreadId = Thread.CurrentThread.ManagedThreadId,
			ApartmentState = Thread.CurrentThread.GetApartmentState().ToString(),
			Gen0Collections = GC.CollectionCount(0),
			Gen1Collections = GC.CollectionCount(1),
			Gen2Collections = GC.CollectionCount(2),
			DocumentParagraphCount = -1,
			DocumentCharacterCount = -1
		};
		try
		{
			using Process process = Process.GetCurrentProcess();
			process.Refresh();
			runtimeMetricSnapshot.WorkingSetBytes = process.WorkingSet64;
			runtimeMetricSnapshot.PrivateMemoryBytes = process.PrivateMemorySize64;
			runtimeMetricSnapshot.HandleCount = process.HandleCount;
			runtimeMetricSnapshot.ProcessThreadCount = process.Threads.Count;
		}
		catch (Exception ex)
		{
			LogService.Warn("RUNTIME_METRICS process-capture-failed", ex);
		}
		if (!includeDocumentScale || document == null)
		{
			return runtimeMetricSnapshot;
		}
		HostThreadRuntime.AssertAccess("RuntimeMetricsCollector.Capture.DocumentScale");
		Paragraphs value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			runtimeMetricSnapshot.DocumentName = document.Name;
			value = document.Paragraphs;
			runtimeMetricSnapshot.DocumentParagraphCount = value?.Count ?? (-1);
			value2 = document.Content;
			runtimeMetricSnapshot.DocumentCharacterCount = ((value2 == null) ? (-1) : Math.Max(0, value2.End - value2.Start));
		}
		catch (Exception ex2)
		{
			LogService.Warn("RUNTIME_METRICS document-scale-capture-failed", ex2);
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "RuntimeMetricsCollector.Content");
			ComObjectRelease.Release(ref value, "RuntimeMetricsCollector.Paragraphs");
		}
		return runtimeMetricSnapshot;
	}
}
