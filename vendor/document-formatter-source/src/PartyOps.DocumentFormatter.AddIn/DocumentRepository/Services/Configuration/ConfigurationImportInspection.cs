using System;
using System.Collections.Generic;

namespace DocumentRepository.Services.Configuration;

public sealed class ConfigurationImportInspection
{
	public string ProductVersion { get; internal set; }

	public DateTime? ExportedAt { get; internal set; }

	public List<string> Warnings { get; internal set; }
}
