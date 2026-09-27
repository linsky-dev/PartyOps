using System.Collections.Generic;
using DocumentRepository.Services.Mutations;

namespace DocumentRepository.Models.Mutations;

public interface IDocumentMutation
{
	string Id { get; }

	string Description { get; }

	DocumentMutationKind Kind { get; }

	IReadOnlyList<MutationTarget> Targets { get; }

	bool AllowsTargetOverlap { get; }

	void Apply(MutationExecutionContext context);
}
