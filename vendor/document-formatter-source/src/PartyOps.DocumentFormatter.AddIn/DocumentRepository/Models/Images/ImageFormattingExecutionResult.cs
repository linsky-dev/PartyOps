namespace DocumentRepository.Models.Images;

public sealed class ImageFormattingExecutionResult
{
	public int AppliedCount { get; set; }

	public int SkippedCount { get; set; }

	public int AppliedInlineToFloatingCount { get; set; }

	public int AppliedFloatingToInlineCount { get; set; }

	public int AppliedInlineDelta => AppliedFloatingToInlineCount - AppliedInlineToFloatingCount;

	public int AppliedFloatingDelta => AppliedInlineToFloatingCount - AppliedFloatingToInlineCount;
}
