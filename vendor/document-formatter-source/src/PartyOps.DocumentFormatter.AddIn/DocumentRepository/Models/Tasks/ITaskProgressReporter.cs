using System;

namespace DocumentRepository.Models.Tasks;

public interface ITaskProgressReporter : IDisposable
{
	bool CancellationRequested { get; }

	void Start(string taskName, int totalSteps, string message);

	void Report(TaskProgressInfo info);

	void Complete(string message);

	void CompleteWithWarnings(string message);

	void Cancel(string message);

	void Fail(string message);
}
