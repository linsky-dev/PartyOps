using System.Collections.Generic;

namespace DocumentRepository.Models.Conversion;

public class PageSelectionResult
{
	public bool Success { get; set; }

	public string Message { get; set; }

	public List<int> Pages { get; private set; }

	public ConvertFailureReasonCode? FailureReasonCode { get; private set; }

	public PageSelectionResult()
	{
		Pages = new List<int>();
	}

	public static PageSelectionResult Fail(string message, ConvertFailureReasonCode reasonCode)
	{
		return new PageSelectionResult
		{
			Success = false,
			Message = message,
			FailureReasonCode = reasonCode
		};
	}

	public static PageSelectionResult Ok(IEnumerable<int> pages)
	{
		PageSelectionResult pageSelectionResult = new PageSelectionResult
		{
			Success = true
		};
		if (pages != null)
		{
			pageSelectionResult.Pages.AddRange(pages);
		}
		return pageSelectionResult;
	}
}
