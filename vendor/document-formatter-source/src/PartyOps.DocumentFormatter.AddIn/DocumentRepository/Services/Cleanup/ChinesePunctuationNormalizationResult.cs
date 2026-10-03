using System;

namespace DocumentRepository.Services.Cleanup;

internal sealed class ChinesePunctuationNormalizationResult
{
	public string Text { get; private set; }

	public int ConvertedCount { get; private set; }

	public int PreservedStraightQuoteCount { get; private set; }

	public int ProtectedTokenCount { get; private set; }

	public int ConservativeSkipCount { get; private set; }

	internal ChinesePunctuationNormalizationResult(string text, int convertedCount, int preservedStraightQuoteCount, int protectedTokenCount, int conservativeSkipCount)
	{
		Text = text ?? string.Empty;
		ConvertedCount = Math.Max(0, convertedCount);
		PreservedStraightQuoteCount = Math.Max(0, preservedStraightQuoteCount);
		ProtectedTokenCount = Math.Max(0, protectedTokenCount);
		ConservativeSkipCount = Math.Max(0, conservativeSkipCount);
	}
}
