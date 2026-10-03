namespace DocumentRepository.Services.Configuration;

public sealed class ConfigurationMigrationResult
{
	public bool AlreadyCompleted { get; internal set; }

	public string BackupDirectory { get; internal set; }

	public int LegacyRedHeaderTemplateCount { get; internal set; }
}
