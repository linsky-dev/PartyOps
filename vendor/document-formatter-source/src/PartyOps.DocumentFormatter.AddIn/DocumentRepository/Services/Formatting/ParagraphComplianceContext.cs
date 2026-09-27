using System.Collections.Generic;
using DocumentRepository.Models;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

internal sealed class ParagraphComplianceContext
{
	private readonly Dictionary<ElementType, TextAppearanceStyleService.TextStyleExpectation> _expectations = new Dictionary<ElementType, TextAppearanceStyleService.TextStyleExpectation>();

	public TextAppearanceStyleService.TextStyleExpectation GetExpectation(Document document, ElementType type)
	{
		if (!_expectations.TryGetValue(type, out var value))
		{
			value = TextAppearanceStyleService.CaptureExpectation(document, type);
			_expectations[type] = value;
		}
		return value;
	}
}
