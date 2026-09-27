namespace DocumentRepository.Services.Configuration;

public sealed class ConfigurationPackageEntry
{
	public string Id { get; set; }

	public string Path { get; set; }

	public long Length { get; set; }

	public string Sha256 { get; set; }
}
