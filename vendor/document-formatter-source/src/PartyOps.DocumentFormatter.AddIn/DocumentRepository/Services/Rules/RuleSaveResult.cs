namespace DocumentRepository.Services.Rules;

public sealed class RuleSaveResult
{
	public string StorePath { get; internal set; }

	public long Length { get; internal set; }

	public bool ReplacedExistingFile { get; internal set; }
}
