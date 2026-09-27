using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Startup;

internal static class StartupFaultInjection
{
	private const string EnabledVariable = "SX_STARTUP_TEST_MODE";

	private const string StepsVariable = "SX_STARTUP_FAIL_STEPS";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ThrowIfRequested(string stepName)
	{
		if (!string.Equals(Environment.GetEnvironmentVariable("SX_STARTUP_TEST_MODE"), "1", StringComparison.Ordinal))
		{
			return;
		}
		string environmentVariable = Environment.GetEnvironmentVariable("SX_STARTUP_FAIL_STEPS");
		if (string.IsNullOrWhiteSpace(environmentVariable))
		{
			return;
		}
		string[] array = environmentVariable.Split(new char[2] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text == "*" || string.Equals(text, stepName, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("Startup fault injection requested for step: " + stepName);
			}
		}
	}
}
