using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Services.Hosting;

namespace DocumentRepository.Services.Ui;

public static class WarningOnceGate
{
	private static readonly HashSet<string> ShownCodes = new HashSet<string>(StringComparer.Ordinal);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool ShouldShowAndMark(IReadOnlyList<VerificationFinding> warnings)
	{
		HostThreadRuntime.AssertAccess("WarningOnceGate.ShouldShowAndMark");
		if (warnings == null || warnings.Count == 0)
		{
			return false;
		}
		bool flag = false;
		for (int i = 0; i < warnings.Count; i++)
		{
			if (warnings[i] != null && !string.IsNullOrWhiteSpace(warnings[i].Code) && !ShownCodes.Contains(warnings[i].Code))
			{
				flag = true;
			}
		}
		if (!flag)
		{
			return false;
		}
		for (int j = 0; j < warnings.Count; j++)
		{
			if (warnings[j] != null && !string.IsNullOrWhiteSpace(warnings[j].Code))
			{
				ShownCodes.Add(warnings[j].Code);
			}
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static void ResetForTesting()
	{
		HostThreadRuntime.AssertAccess("WarningOnceGate.ResetForTesting");
		ShownCodes.Clear();
	}
}
