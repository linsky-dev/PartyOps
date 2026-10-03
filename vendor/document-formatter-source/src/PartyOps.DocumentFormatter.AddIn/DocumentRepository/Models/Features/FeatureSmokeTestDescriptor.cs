namespace DocumentRepository.Models.Features;

public class FeatureSmokeTestDescriptor
{
	public string FeatureId { get; set; }

	public string Area { get; set; }

	public string Assertion { get; set; }

	public bool Required { get; set; }
}
