using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Hosting.Standalone;

namespace PartyOps.DocumentFormatter.Desktop.Services;

/// <summary>
/// 全生命周期复用同一 STA 线程，确保 Office/WPS COM 对象始终由创建它的线程访问。
/// </summary>
internal sealed class StaWorkQueue : IDisposable
{
	private readonly BlockingCollection<Action> workItems = new BlockingCollection<Action>();
	private readonly Thread worker;
	private Exception initializationError;
	private bool disposed;

	public StaWorkQueue()
	{
		worker = new Thread(Run)
		{
			IsBackground = true,
			Name = "partyops排版文档处理线程"
		};
		worker.SetApartmentState(ApartmentState.STA);
		worker.Start();
	}

	public Task<T> RunAsync<T>(Func<T> work)
	{
		if (work == null)
		{
			throw new ArgumentNullException(nameof(work));
		}
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(StaWorkQueue));
		}
		TaskCompletionSource<T> completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		workItems.Add(delegate
		{
			if (initializationError != null)
			{
				completion.TrySetException(initializationError);
				return;
			}
			try
			{
				completion.TrySetResult(work());
			}
			catch (OperationCanceledException)
			{
				completion.TrySetCanceled();
			}
			catch (Exception ex)
			{
				completion.TrySetException(ex);
			}
		});
		return completion.Task;
	}

	private void Run()
	{
		ComBusyRetryMessageFilter messageFilter = null;
		try
		{
			HostThreadRuntime.Initialize("StandaloneDesktop.Worker");
			ConfigurationMigrationService.Ensure440Migration();
			messageFilter = ComBusyRetryMessageFilter.Register();
		}
		catch (Exception ex)
		{
			initializationError = ex;
		}
		try
		{
			foreach (Action workItem in workItems.GetConsumingEnumerable())
			{
				workItem();
			}
		}
		finally
		{
			messageFilter?.Dispose();
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}
		disposed = true;
		workItems.CompleteAdding();
		if (!worker.Join(TimeSpan.FromSeconds(8)))
		{
			// 后台线程不会阻止进程退出；此处不强制终止，避免在 COM 保存过程中损坏输出。
		}
		workItems.Dispose();
	}
}
