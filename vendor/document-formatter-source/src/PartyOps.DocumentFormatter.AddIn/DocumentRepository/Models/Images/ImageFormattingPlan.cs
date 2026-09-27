using System.Collections.Generic;

namespace DocumentRepository.Models.Images;

public sealed class ImageFormattingPlan
{
	public ImageAnalysisSnapshot SourceSnapshot { get; set; }

	public ImageFormatOptions Options { get; set; }

	public int ScopeStart { get; set; }

	public int ScopeEnd { get; set; } = int.MaxValue;

	public List<ImageFormattingTarget> Targets { get; } = new List<ImageFormattingTarget>();

	public List<ImageObjectSnapshot> ProtectedEligibleObjects { get; } = new List<ImageObjectSnapshot>();

	public int InlineToFloatingCount { get; set; }

	public int FloatingToInlineCount { get; set; }

	public int ExpectedInlineDelta => FloatingToInlineCount - InlineToFloatingCount;

	public int ExpectedFloatingDelta => InlineToFloatingCount - FloatingToInlineCount;

	public bool HasTargets => Targets.Count > 0;
}
