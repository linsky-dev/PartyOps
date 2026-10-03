using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Hosting;

public sealed class DocumentWriteLease
{
	private readonly WriteLeaseGuard guard;

	private readonly object token;

	private bool confirmedWrite;

	internal bool HasConfirmedWrite => confirmedWrite;

	internal DocumentWriteLease(WriteLeaseGuard guard, object token)
	{
		this.guard = guard;
		this.token = token;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal void AssertActive(string documentLifecycleId, string planId, string sourceSnapshotId)
	{
		if (guard == null)
		{
			throw new InvalidOperationException("写入凭证未绑定会话，拒绝写入。");
		}
		guard.AssertActive(token, documentLifecycleId, planId, sourceSnapshotId);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void ConfirmWriteOccurred()
	{
		if (guard == null)
		{
			throw new InvalidOperationException("写入凭证未绑定会话，拒绝确认写入。");
		}
		guard.AssertActiveToken(token);
		confirmedWrite = true;
	}
}
