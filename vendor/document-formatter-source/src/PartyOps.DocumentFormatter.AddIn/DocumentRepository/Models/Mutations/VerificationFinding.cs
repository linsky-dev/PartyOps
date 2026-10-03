using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Mutations;

public sealed class VerificationFinding
{
	public string Code { get; private set; }

	public VerificationSeverity Severity { get; private set; }

	public string ObjectLocation { get; private set; }

	public string UserMessageKey { get; private set; }

	public int AffectedCount { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public VerificationFinding(string code, VerificationSeverity severity, string objectLocation, string userMessageKey, int affectedCount = 1)
	{
		if (!Enum.IsDefined(typeof(VerificationSeverity), severity))
		{
			throw new ArgumentOutOfRangeException("severity");
		}
		if (affectedCount < 1)
		{
			throw new ArgumentOutOfRangeException("affectedCount");
		}
		Code = NormalizeToken(code, "unknown");
		Severity = severity;
		ObjectLocation = NormalizeToken(objectLocation, "unspecified");
		UserMessageKey = NormalizeToken(userMessageKey, "warn.unknown");
		AffectedCount = affectedCount;
	}

	private static string NormalizeToken(string value, string fallback)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return fallback;
		}
		string text = value.Trim();
		if (text.Length <= 128)
		{
			foreach (char c in text)
			{
				if ((c < 'a' || c > 'z') && (c < 'A' || c > 'Z') && (c < '0' || c > '9') && c != '-' && c != '_' && c != '.' && c != ':')
				{
					return fallback;
				}
			}
			return text;
		}
		return fallback;
	}
}
