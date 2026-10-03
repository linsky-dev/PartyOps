using System.Collections.Generic;

namespace DocumentRepository.Services.Configuration;

public sealed class ConfigurationPackageManifest
{
	public string ProductId { get; set; }

	public string ProductName { get; set; }

	public string ProductVersion { get; set; }

	public int PackageSchemaVersion { get; set; }

	public string ExportedAt { get; set; }

	public List<ConfigurationPackageEntry> Entries { get; set; }

	public ConfigurationPackageManifest()
	{
		Entries = new List<ConfigurationPackageEntry>();
	}
}
