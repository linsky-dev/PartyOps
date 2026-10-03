namespace DocumentRepository.Services.FileSafety;

public sealed class AtomicFileWriteResult
{
	public string TargetPath { get; internal set; }

	public long Length { get; internal set; }

	public bool ReplacedExistingFile { get; internal set; }
}
