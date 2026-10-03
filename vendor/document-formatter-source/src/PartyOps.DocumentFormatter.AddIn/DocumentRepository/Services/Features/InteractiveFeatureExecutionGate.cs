using System;
using System.Threading;

namespace DocumentRepository.Services.Features;

internal static class InteractiveFeatureExecutionGate
{
	private sealed class GateLease : IDisposable
	{
		private int disposed;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				Volatile.Write(ref active, 0);
			}
		}

		void IDisposable.Dispose()
		{
			this.Dispose();
		}
	}

	private static int active;

	internal static bool TryEnter(bool bypassForBatchChild, out IDisposable lease)
	{
		if (!bypassForBatchChild)
		{
			if (Interlocked.CompareExchange(ref active, 1, 0) == 0)
			{
				lease = new GateLease();
				return true;
			}
			lease = null;
			return false;
		}
		lease = null;
		return true;
	}
}
