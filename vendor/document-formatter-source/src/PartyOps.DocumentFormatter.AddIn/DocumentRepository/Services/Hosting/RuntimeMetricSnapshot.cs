using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Hosting;

public sealed class RuntimeMetricSnapshot
{
	public DateTime TimestampUtc { get; set; }

	public long WorkingSetBytes { get; set; }

	public long PrivateMemoryBytes { get; set; }

	public long ManagedHeapBytes { get; set; }

	public int HandleCount { get; set; }

	public int ProcessThreadCount { get; set; }

	public int ManagedThreadId { get; set; }

	public string ApartmentState { get; set; }

	public int Gen0Collections { get; set; }

	public int Gen1Collections { get; set; }

	public int Gen2Collections { get; set; }

	public int DocumentParagraphCount { get; set; }

	public int DocumentCharacterCount { get; set; }

	public string DocumentName { get; set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string ToLogFields()
	{
		return "workingSetBytes=" + WorkingSetBytes + ", privateMemoryBytes=" + PrivateMemoryBytes + ", managedHeapBytes=" + ManagedHeapBytes + ", handleCount=" + HandleCount + ", processThreadCount=" + ProcessThreadCount + ", managedThreadId=" + ManagedThreadId + ", apartment=" + (ApartmentState ?? "unknown") + ", gc0=" + Gen0Collections + ", gc1=" + Gen1Collections + ", gc2=" + Gen2Collections + ", document=" + (DocumentName ?? "unknown") + ", paragraphs=" + DocumentParagraphCount + ", characters=" + DocumentCharacterCount;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string BuildDelta(RuntimeMetricSnapshot baseline)
	{
		if (baseline == null)
		{
			return "delta=unavailable";
		}
		return "workingSetDeltaBytes=" + (WorkingSetBytes - baseline.WorkingSetBytes) + ", privateMemoryDeltaBytes=" + (PrivateMemoryBytes - baseline.PrivateMemoryBytes) + ", managedHeapDeltaBytes=" + (ManagedHeapBytes - baseline.ManagedHeapBytes) + ", handleDelta=" + (HandleCount - baseline.HandleCount) + ", processThreadDelta=" + (ProcessThreadCount - baseline.ProcessThreadCount) + ", gc0Delta=" + (Gen0Collections - baseline.Gen0Collections) + ", gc1Delta=" + (Gen1Collections - baseline.Gen1Collections) + ", gc2Delta=" + (Gen2Collections - baseline.Gen2Collections);
	}
}
