using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Tasks;

public sealed class TaskCancellationSignal
{
	public static readonly TaskCancellationSignal None = new TaskCancellationSignal(() => false);

	private readonly Func<bool> isCancellationRequested;

	public bool IsCancellationRequested => isCancellationRequested();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public TaskCancellationSignal(Func<bool> isCancellationRequested)
	{
		this.isCancellationRequested = isCancellationRequested ?? throw new ArgumentNullException("isCancellationRequested");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void ThrowIfCancellationRequested(string safeCheckpoint)
	{
		if (!IsCancellationRequested)
		{
			return;
		}
		string text = (string.IsNullOrWhiteSpace(safeCheckpoint) ? "安全检查点" : safeCheckpoint.Trim());
		throw new OperationCanceledException("用户已取消任务（" + text + "）。");
	}
}
