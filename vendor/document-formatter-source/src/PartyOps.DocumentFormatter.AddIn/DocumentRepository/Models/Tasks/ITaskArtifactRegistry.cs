namespace DocumentRepository.Models.Tasks;

public interface ITaskArtifactRegistry
{
	void RegisterFile(string path, bool existedBefore);

	void RegisterDirectory(string path, bool existedBefore);

	TaskCleanupResult Cleanup();
}
