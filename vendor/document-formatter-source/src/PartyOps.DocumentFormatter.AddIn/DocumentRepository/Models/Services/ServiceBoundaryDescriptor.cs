namespace DocumentRepository.Models.Services;

public class ServiceBoundaryDescriptor
{
	public string NamespaceSegment { get; set; }

	public ServiceBoundaryKind Kind { get; set; }

	public string Responsibility { get; set; }

	public bool AllowsDocumentWrite { get; set; }

	public bool AllowsFileWrite { get; set; }

	public bool AllowsFeatureBusinessLogic { get; set; }
}
