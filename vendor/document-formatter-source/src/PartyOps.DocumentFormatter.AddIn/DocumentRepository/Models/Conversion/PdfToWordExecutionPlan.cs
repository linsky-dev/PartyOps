namespace DocumentRepository.Models.Conversion;

public class PdfToWordExecutionPlan
{
	public ConvertOptions Options { get; set; }

	public string SourcePath { get; set; }

	public string OutputFolder { get; set; }

	public string TargetPath { get; set; }
}
