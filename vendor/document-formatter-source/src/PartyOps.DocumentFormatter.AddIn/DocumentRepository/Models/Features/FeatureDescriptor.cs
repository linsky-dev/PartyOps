namespace DocumentRepository.Models.Features;

public class FeatureDescriptor
{
	public string Id { get; set; }

	public string DisplayName { get; set; }

	public string CommandType { get; set; }

	public string CommandClassName { get; set; }

	public string RuleCatalogId { get; set; }

	public string PermissionCommandName { get; set; }

	public string IconName { get; set; }

	public bool RequiresDocument { get; set; }

	public bool IsLongRunning { get; set; }

	public bool SupportsBatch { get; set; }

	public string Notes { get; set; }
}
