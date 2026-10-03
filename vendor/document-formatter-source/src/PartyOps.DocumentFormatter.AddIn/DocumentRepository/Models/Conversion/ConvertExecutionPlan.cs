using System.Collections.Generic;

namespace DocumentRepository.Models.Conversion;

public class ConvertExecutionPlan
{
	public ConvertOptions Options { get; set; }

	public string SourcePath { get; set; }

	public string OutputFolder { get; set; }

	public string TargetPath { get; set; }

	public string ImageFolder { get; set; }

	public List<int> ImagePages { get; private set; }

	public bool SourceAlreadyDocx { get; set; }

	public string SuccessMessage { get; set; }

	public bool HasWarnings { get; set; }

	public ConvertExecutionPlan()
	{
		ImagePages = new List<int>();
	}
}
