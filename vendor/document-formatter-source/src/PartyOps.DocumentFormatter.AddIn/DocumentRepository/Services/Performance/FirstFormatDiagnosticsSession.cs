using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Performance;

public sealed class FirstFormatDiagnosticsSession : IDisposable
{
	private sealed class AccumulatedTiming
	{
		public long Ticks;

		public int Count;
	}

	private sealed class StageTiming
	{
		public string Stage { get; }

		public long DeltaMilliseconds { get; }

		public long TotalMilliseconds { get; }

		public int Count { get; }

		public bool Reliable { get; }

		public string Note { get; }

		public StageTiming(string stage, long deltaMilliseconds, long totalMilliseconds, int count, bool reliable, string note)
		{
			Stage = stage;
			DeltaMilliseconds = deltaMilliseconds;
			TotalMilliseconds = totalMilliseconds;
			Count = count;
			Reliable = reliable;
			Note = note;
		}
	}

	private readonly Stopwatch stopwatch;

	private readonly List<StageTiming> stages;

	private readonly Dictionary<string, AccumulatedTiming> accumulations;

	private readonly List<string> accumulationOrder;

	private long lastElapsedMilliseconds;

	private bool flushed;

	public string TaskId { get; }

	public string PlanId { get; set; }

	public string Fingerprint { get; }

	public FirstFormatDiagnosticsSession(string taskId, string planId = null, string fingerprint = null)
	{
		TaskId = taskId ?? string.Empty;
		PlanId = planId ?? string.Empty;
		Fingerprint = fingerprint ?? string.Empty;
		stopwatch = Stopwatch.StartNew();
		stages = new List<StageTiming>();
		accumulations = new Dictionary<string, AccumulatedTiming>(StringComparer.Ordinal);
		accumulationOrder = new List<string>();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public long Mark(string stage, int count = -1, bool reliable = true, string note = null)
	{
		if (string.IsNullOrWhiteSpace(stage))
		{
			stage = "unnamed";
		}
		try
		{
			long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
			long deltaMilliseconds = Math.Max(0L, elapsedMilliseconds - lastElapsedMilliseconds);
			lastElapsedMilliseconds = elapsedMilliseconds;
			stages.Add(new StageTiming(stage, deltaMilliseconds, elapsedMilliseconds, count, reliable, note));
			return elapsedMilliseconds;
		}
		catch
		{
			return stopwatch.ElapsedMilliseconds;
		}
	}

	public static long Timestamp()
	{
		return Stopwatch.GetTimestamp();
	}

	public void Accumulate(string stage, long startTimestamp, int count = 1)
	{
		if (string.IsNullOrWhiteSpace(stage) || count < 0)
		{
			return;
		}
		try
		{
			long num = Stopwatch.GetTimestamp() - startTimestamp;
			if (num >= 0)
			{
				if (!accumulations.TryGetValue(stage, out var value))
				{
					value = new AccumulatedTiming();
					accumulations.Add(stage, value);
					accumulationOrder.Add(stage);
				}
				value.Ticks += num;
				value.Count += count;
			}
		}
		catch
		{
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Flush(string outcome = null)
	{
		if (flushed)
		{
			return;
		}
		flushed = true;
		try
		{
			long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("[FORMAT-STAGES]");
			stringBuilder.Append(" taskId=").Append(Escape(TaskId));
			if (!string.IsNullOrEmpty(PlanId))
			{
				stringBuilder.Append(", planId=").Append(Escape(PlanId));
			}
			if (!string.IsNullOrEmpty(Fingerprint))
			{
				stringBuilder.Append(", fingerprint=").Append(Escape(Fingerprint));
			}
			if (!string.IsNullOrEmpty(outcome))
			{
				stringBuilder.Append(", outcome=").Append(Escape(outcome));
			}
			stringBuilder.Append(", totalMs=").Append(elapsedMilliseconds);
			stringBuilder.Append(", stages=[");
			for (int i = 0; i < stages.Count; i++)
			{
				StageTiming stageTiming = stages[i];
				if (i > 0)
				{
					stringBuilder.Append("; ");
				}
				stringBuilder.Append(stageTiming.Stage).Append("=").Append(stageTiming.DeltaMilliseconds)
					.Append("ms")
					.Append("(total=")
					.Append(stageTiming.TotalMilliseconds)
					.Append("ms");
				if (stageTiming.Count >= 0)
				{
					stringBuilder.Append(", count=").Append(stageTiming.Count);
				}
				stringBuilder.Append(", reliable=").Append(stageTiming.Reliable ? "1" : "0");
				if (!string.IsNullOrEmpty(stageTiming.Note))
				{
					stringBuilder.Append(", note=").Append(Escape(stageTiming.Note));
				}
				stringBuilder.Append(")");
			}
			stringBuilder.Append("]");
			if (accumulationOrder.Count > 0)
			{
				stringBuilder.Append(", accumulated=[");
				for (int j = 0; j < accumulationOrder.Count; j++)
				{
					string text = accumulationOrder[j];
					AccumulatedTiming accumulatedTiming = accumulations[text];
					if (j > 0)
					{
						stringBuilder.Append("; ");
					}
					long value = (long)Math.Round((double)accumulatedTiming.Ticks * 1000.0 / (double)Stopwatch.Frequency);
					stringBuilder.Append(Escape(text)).Append("=").Append(value)
						.Append("ms")
						.Append("(count=")
						.Append(accumulatedTiming.Count)
						.Append(")");
				}
				stringBuilder.Append("]");
			}
			LogService.Info(stringBuilder.ToString());
		}
		catch (Exception ex)
		{
			LogService.Warn("FORMAT-STAGES flush failed", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Dispose()
	{
		Flush("disposed");
		stopwatch?.Stop();
	}

	void IDisposable.Dispose()
	{
		this.Dispose();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Escape(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}
		string text = value.Replace("\r", "").Replace("\n", "").Replace("|", "")
			.Replace("]", "")
			.Replace("[", "")
			.Replace(";", ",");
		if (text.Length > 64)
		{
			text = text.Substring(0, 64);
		}
		return text;
	}
}
