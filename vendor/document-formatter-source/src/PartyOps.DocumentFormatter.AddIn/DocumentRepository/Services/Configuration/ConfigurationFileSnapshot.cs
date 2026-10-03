namespace DocumentRepository.Services.Configuration;

internal sealed class ConfigurationFileSnapshot
{
	public string Path { get; private set; }

	public string BackupPath { get; private set; }

	public bool Existed { get; private set; }

	public ConfigurationFileSnapshot(string path, string backupPath, bool existed)
	{
		Path = path;
		BackupPath = backupPath;
		Existed = existed;
	}
}
