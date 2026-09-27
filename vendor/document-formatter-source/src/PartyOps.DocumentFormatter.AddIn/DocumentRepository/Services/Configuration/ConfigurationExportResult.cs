namespace DocumentRepository.Services.Configuration;

public sealed class ConfigurationExportResult
{
	public string PackagePath { get; internal set; }

	public long Length { get; internal set; }

	public string ProductVersion { get; internal set; }
}
