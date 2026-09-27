using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Tasks;

namespace DocumentRepository.Models.Tasks;

public sealed class TaskRuntimeContext
{
	public ITaskProgressReporter Progress { get; private set; }

	public TaskCancellationSignal Cancellation { get; private set; }

	public ITaskArtifactRegistry Artifacts { get; private set; }

	public ITaskEventJournal Events { get; set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public TaskRuntimeContext(ITaskProgressReporter progress, TaskCancellationSignal cancellation, ITaskArtifactRegistry artifacts)
	{
		Progress = progress ?? throw new ArgumentNullException("progress");
		Cancellation = cancellation ?? throw new ArgumentNullException("cancellation");
		Artifacts = artifacts ?? throw new ArgumentNullException("artifacts");
	}
}
