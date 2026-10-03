using System;

namespace DocumentRepository.Models.Tasks;

public sealed class TaskEvent
{
	public int SchemaVersion { get; set; } = 1;

	public long Sequence { get; set; }

	public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

	public string TaskId { get; set; }

	public string FeatureId { get; set; }

	public string InvocationSource { get; set; }

	public string EventType { get; set; }

	public string Stage { get; set; }

	public string Outcome { get; set; }

	public string ReasonCode { get; set; }

	public string DocumentPath { get; set; }

	public long ElapsedMilliseconds { get; set; }

	public string ExceptionType { get; set; }
}
