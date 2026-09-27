using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Services.Tasks;

public interface ITaskEventJournal
{
	void Record(TaskEvent taskEvent);
}
