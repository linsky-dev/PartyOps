using System.Collections.Generic;
using DocumentRepository.Models.Images;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Snapshots;

namespace DocumentRepository.Models.FormattingPlan;

public sealed class FormatExecutionPlan : DocumentMutationPlan
{
	public FormatContext FormatContext { get; set; }

	public DocumentSnapshot OriginalSnapshot { get; set; }

	public OutsideScopeSnapshot OutsideScopeSnapshot { get; set; }

	public FormatExecutionScope ExecutionScope { get; set; }

	public bool IsSelectionMode
	{
		get
		{
			return ExecutionScope == FormatExecutionScope.NormalSelection;
		}
		set
		{
			ExecutionScope = (value ? FormatExecutionScope.NormalSelection : FormatExecutionScope.FullDocument);
		}
	}

	public int ScopeStart { get; set; }

	public int ScopeEnd { get; set; }

	public List<PlannedTextRange> EnglishNumberFontRanges { get; } = new List<PlannedTextRange>();

	public ImageFormattingPlan ImagePlan { get; set; }
}
