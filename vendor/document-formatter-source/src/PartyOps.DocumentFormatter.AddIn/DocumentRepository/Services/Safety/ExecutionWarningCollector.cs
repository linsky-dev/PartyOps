using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Services.Hosting;

namespace DocumentRepository.Services.Safety;

public static class ExecutionWarningCollector
{
	[ThreadStatic]
	private static List<VerificationFinding> current;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void BeginOperation()
	{
		HostThreadRuntime.AssertAccess("ExecutionWarningCollector.BeginOperation");
		current = null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Report(string code, string objectLocation, string userMessageKey)
	{
		HostThreadRuntime.AssertAccess("ExecutionWarningCollector.Report");
		if (string.IsNullOrWhiteSpace(code))
		{
			return;
		}
		if (current == null)
		{
			current = new List<VerificationFinding>();
		}
		for (int i = 0; i < current.Count; i++)
		{
			if (string.Equals(current[i].Code, code, StringComparison.Ordinal))
			{
				VerificationFinding verificationFinding = current[i];
				current[i] = new VerificationFinding(verificationFinding.Code, verificationFinding.Severity, verificationFinding.ObjectLocation, verificationFinding.UserMessageKey, verificationFinding.AffectedCount + 1);
				return;
			}
		}
		current.Add(new VerificationFinding(code, VerificationSeverity.Warning, objectLocation, userMessageKey));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IReadOnlyList<VerificationFinding> Snapshot()
	{
		HostThreadRuntime.AssertAccess("ExecutionWarningCollector.Snapshot");
		if (current == null || current.Count == 0)
		{
			return new VerificationFinding[0];
		}
		return current.ToArray();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void EndOperation()
	{
		HostThreadRuntime.AssertAccess("ExecutionWarningCollector.EndOperation");
		current = null;
	}
}
