using System.Collections.Generic;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Services.Formatting.Planning;
using DocumentRepository.Services.Mutations;

namespace DocumentRepository.Models.FormattingPlan;

public sealed class FormatMutationOperation : IDocumentMutation
{
	private static readonly IReadOnlyList<MutationTarget> NoTargets = new List<MutationTarget>().AsReadOnly();

	public string Id { get; set; }

	public string Description { get; set; }

	public DocumentMutationKind Kind { get; set; }

	public FormatMutationStage Stage { get; set; }

	public IReadOnlyList<MutationTarget> Targets => NoTargets;

	public bool AllowsTargetOverlap => true;

	public void Apply(MutationExecutionContext context)
	{
		FormatMutationApplicationService.Apply(this, context);
	}

	void IDocumentMutation.Apply(MutationExecutionContext context)
	{
		this.Apply(context);
	}
}
