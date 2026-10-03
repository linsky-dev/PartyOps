namespace DocumentRepository.Models.Pipelines;

public class PipelineResponsibilityDescriptor
{
	public string FeatureId { get; set; }

	public PipelineResponsibilityKind Kind { get; set; }

	public string StepName { get; set; }

	public string OwnerTypeName { get; set; }

	public bool ReadsDocument { get; set; }

	public bool WritesDocument { get; set; }

	public bool WritesFileSystem { get; set; }

	public bool Required { get; set; }

	public string Notes { get; set; }
}
