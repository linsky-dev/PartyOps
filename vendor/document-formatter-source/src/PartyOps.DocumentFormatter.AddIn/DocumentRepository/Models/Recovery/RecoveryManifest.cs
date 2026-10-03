using System;

namespace DocumentRepository.Models.Recovery;

public sealed class RecoveryManifest
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; }

	public string RecoveryId { get; set; }

	public string TaskId { get; set; }

	public string FeatureId { get; set; }

	public string DocumentLifecycleId { get; set; }

	public string SourceFullPath { get; set; }

	public string SourceFileName { get; set; }

	public string SourceExtension { get; set; }

	public long SourceLength { get; set; }

	public DateTime SourceLastWriteUtc { get; set; }

	public string SourceSha256 { get; set; }

	public string CopySha256 { get; set; }

	public string CopyFileName { get; set; }

	public string Coverage { get; set; }

	public string State { get; set; }

	public DateTime CreatedAtUtc { get; set; }

	public DateTime StateUpdatedAtUtc { get; set; }

	public bool IsFallback { get; set; }

	public RecoveryManifest()
	{
		SchemaVersion = 1;
	}
}
