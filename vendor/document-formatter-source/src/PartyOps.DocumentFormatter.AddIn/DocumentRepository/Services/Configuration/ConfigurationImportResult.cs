using System.Collections.Generic;

namespace DocumentRepository.Services.Configuration;

public sealed class ConfigurationImportResult
{
	public string ProductVersion { get; internal set; }

	public string BackupDirectory { get; internal set; }

	public List<string> Warnings { get; internal set; }
}
