using System.Collections.Generic;

namespace DocumentRepository.Models.Conversion;

public sealed class ImageConversionBudget
{
	public bool Allowed { get; set; }

	public string RejectionReason { get; set; }

	public bool IsLongImage { get; set; }

	public bool Is64BitProcess { get; set; }

	public int PageCount { get; set; }

	public int OutputWidth { get; set; }

	public long OutputHeight { get; set; }

	public long OutputPixels { get; set; }

	public long EstimatedPeakBytes { get; set; }

	public long ProcessBudgetBytes { get; set; }

	public IReadOnlyList<ImagePageDimensions> Pages { get; set; }
}
