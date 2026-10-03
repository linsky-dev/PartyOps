using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Planning;

public static class FormatWorkloadEstimator
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static FormatWorkloadEstimate Estimate(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Paragraphs value2 = null;
		try
		{
			value = document.Content;
			int characterCount = Math.Max(0, value.End - value.Start);
			value2 = value.Paragraphs;
			int paragraphCount = Math.Max(0, value2.Count);
			return FromMetrics(characterCount, paragraphCount);
		}
		catch (Exception ex)
		{
			LogService.Warn("FormatWorkloadEstimator.Estimate", ex);
			return new FormatWorkloadEstimate
			{
				CharacterCount = -1,
				ParagraphCount = -1,
				ShowProgress = true
			};
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "FormatWorkloadEstimator.paragraphs");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "FormatWorkloadEstimator.content");
			}
		}
	}

	public static FormatWorkloadEstimate FromMetrics(int characterCount, int paragraphCount)
	{
		int characterCount2 = Math.Max(0, characterCount);
		int paragraphCount2 = Math.Max(0, paragraphCount);
		return new FormatWorkloadEstimate
		{
			CharacterCount = characterCount2,
			ParagraphCount = paragraphCount2,
			ShowProgress = FormatProgressPolicy.ShouldShowImmediately(characterCount2, paragraphCount2)
		};
	}
}
