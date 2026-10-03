using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Hosting;

internal static class SessionTaskIdResolver
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Resolve(string existingTaskId)
	{
		if (!string.IsNullOrWhiteSpace(existingTaskId))
		{
			return existingTaskId;
		}
		return Guid.NewGuid().ToString("N");
	}
}
