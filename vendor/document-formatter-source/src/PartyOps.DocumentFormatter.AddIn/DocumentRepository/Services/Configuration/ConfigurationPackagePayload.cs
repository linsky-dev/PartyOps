using System.Collections.Generic;

namespace DocumentRepository.Services.Configuration;

internal sealed class ConfigurationPackagePayload
{
	public ConfigurationPackageManifest Manifest { get; set; }

	public Dictionary<string, byte[]> Contents { get; set; }
}
