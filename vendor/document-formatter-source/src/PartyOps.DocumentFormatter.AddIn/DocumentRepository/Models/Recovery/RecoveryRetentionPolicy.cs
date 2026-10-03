using System;

namespace DocumentRepository.Models.Recovery;

public static class RecoveryRetentionPolicy
{
	public const long QuotaBytes = 2147483648L;

	public const long FallbackQuotaBytes = 1073741824L;

	public const int MaxCleanupDirectoriesPerRun = 64;

	public static readonly TimeSpan CleanupBudget = TimeSpan.FromMilliseconds(250.0);

	public static readonly TimeSpan SuccessRetention = TimeSpan.FromDays(3.0);

	public static readonly TimeSpan FailureRetention = TimeSpan.FromDays(30.0);

	public static bool IsSuccessState(RecoveryCopyState state)
	{
		if (state != RecoveryCopyState.Committed && state != RecoveryCopyState.RolledBackVerified)
		{
			return state == RecoveryCopyState.Abandoned;
		}
		return true;
	}

	public static TimeSpan RetentionFor(RecoveryCopyState state)
	{
		if (!IsSuccessState(state))
		{
			return FailureRetention;
		}
		return SuccessRetention;
	}

	public static bool IsExpired(RecoveryCopyState state, DateTime stateUpdatedAtUtc, DateTime nowUtc)
	{
		return nowUtc - stateUpdatedAtUtc > RetentionFor(state);
	}
}
