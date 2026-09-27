using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DocumentRepository.Models;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

internal static class ParagraphFormatComplianceEvaluator
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ParagraphComplianceState Evaluate(Microsoft.Office.Interop.Word.Range paragraphRange, Document document, DocumentElement element, ElementType type, ElementType[] allTypes, int index, FormatTextStyleDefinition styleDefinition, ParagraphComplianceContext context)
	{
		if (paragraphRange == null)
		{
			throw new ArgumentNullException("paragraphRange");
		}
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (styleDefinition != null)
		{
			if (context != null)
			{
				if (element != null && !element.IsEmpty && IsEligible(type))
				{
					float? spaceBeforeOverride = null;
					float? spaceAfterOverride = null;
					if (ParagraphTypeSequenceNormalizer.IsTitleSpacingType(type))
					{
						spaceBeforeOverride = 0f;
						spaceAfterOverride = (ParagraphTypeSequenceNormalizer.ShouldKeepTitleAfterSpacing(type, allTypes, index) ? Math.Max(0f, styleDefinition.MainTitleSpaceAfter) : 0f);
					}
					try
					{
						if (!DocumentStyleManager.IsParagraphFormatCompliant(paragraphRange, document, type, spaceBeforeOverride, spaceAfterOverride))
						{
							return ParagraphComplianceState.NeedsFullRebuild;
						}
						if (!TextAppearanceStyleService.IsParagraphTextCompliant(paragraphRange, type, context.GetExpectation(document, type)))
						{
							return ParagraphComplianceState.NeedsFullRebuild;
						}
						if (!DocumentStyleManager.IsParagraphStyle(paragraphRange, type))
						{
							if (type == ElementType.Body)
							{
								return ParagraphComplianceState.NeedsStyleRefresh;
							}
							return ParagraphComplianceState.NeedsFullRebuild;
						}
						return ParagraphComplianceState.Compliant;
					}
					catch (COMException)
					{
						return ParagraphComplianceState.NeedsFullRebuild;
					}
				}
				return ParagraphComplianceState.NotApplicable;
			}
			throw new ArgumentNullException("context");
		}
		throw new ArgumentNullException("styleDefinition");
	}

	private static bool IsEligible(ElementType type)
	{
		if (type != ElementType.MainTitle && type != ElementType.SubTitle && type != ElementType.Level1Title && type != ElementType.Level2Title && type != ElementType.Level3Title && type != ElementType.Body)
		{
			return type == ElementType.Salutation;
		}
		return true;
	}
}
