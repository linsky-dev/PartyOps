using DocumentRepository.Models.Protection;

namespace DocumentRepository.Services.Hosting;

public sealed class DocumentSessionOptions
{
	public string FeatureId { get; set; }

	public string OperationName { get; set; }

	public bool DisableScreenUpdating { get; set; } = true;

	public bool SuppressAlerts { get; set; }

	public bool DisableEvents { get; set; } = true;

	public bool UseUndoRecord { get; set; }

	public bool RequireRecoveryCopy { get; set; }

	public RecoveryProtectionMode ProtectionMode { get; set; }

	public bool RefreshVisibleLayoutOnSuccess { get; set; }

	public SelectionRestoreMode SelectionRestoreMode { get; set; }

	public static DocumentSessionOptions Create(string featureId, string operationName)
	{
		return new DocumentSessionOptions
		{
			FeatureId = featureId,
			OperationName = operationName
		};
	}
}
