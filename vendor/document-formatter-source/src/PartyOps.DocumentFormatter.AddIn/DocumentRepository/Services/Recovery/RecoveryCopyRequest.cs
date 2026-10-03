namespace DocumentRepository.Services.Recovery;

public sealed class RecoveryCopyRequest
{
	public string TaskId { get; set; }

	public string FeatureId { get; set; }

	public string DocumentLifecycleId { get; set; }

	public string SourcePath { get; set; }

	public bool SourceIsSaved { get; set; }

	public bool AllowFallback { get; set; }
}
