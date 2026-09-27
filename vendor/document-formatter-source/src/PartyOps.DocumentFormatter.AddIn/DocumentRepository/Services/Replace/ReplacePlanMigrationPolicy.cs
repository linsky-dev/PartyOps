using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;

namespace DocumentRepository.Services.Replace;

internal static class ReplacePlanMigrationPolicy
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static bool AddIfMissing(ReplacePlanSet set, ReplacePlan candidate)
	{
		if (set != null)
		{
			if (set.Plans != null)
			{
				if (candidate != null && !string.IsNullOrWhiteSpace(candidate.Id))
				{
					if (set.Plans.Exists((ReplacePlan plan) => plan != null && string.Equals(plan.Id, candidate.Id, StringComparison.OrdinalIgnoreCase)))
					{
						return false;
					}
					set.Plans.Add(candidate);
					return true;
				}
				throw new InvalidOperationException("Migration candidate id cannot be empty.");
			}
			throw new InvalidOperationException("Replace plan list cannot be empty.");
		}
		throw new ArgumentNullException("set");
	}
}
