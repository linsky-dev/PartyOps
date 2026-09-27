using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Services.Mutations;

namespace DocumentRepository.Services.RedHeader;

public sealed class RedHeaderMutation : IDocumentMutation
{
	private readonly RedHeaderLayoutPlan layoutPlan;

	public string Id
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			return "red-header-apply";
		}
	}

	public string Description
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			return "套用红头布局";
		}
	}

	public DocumentMutationKind Kind => DocumentMutationKind.Structure;

	public IReadOnlyList<MutationTarget> Targets => new MutationTarget[0];

	public bool AllowsTargetOverlap => true;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public RedHeaderMutation(RedHeaderLayoutPlan layoutPlan)
	{
		if (layoutPlan != null)
		{
			this.layoutPlan = layoutPlan;
			return;
		}
		throw new ArgumentNullException("layoutPlan");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Apply(MutationExecutionContext context)
	{
		if (context == null)
		{
			throw new ArgumentNullException("context");
		}
		try
		{
			RedHeaderGenerationService.Apply(context.RequireWriteLease(), context.Document, layoutPlan);
		}
		catch (Exception error)
		{
			throw RedHeaderFailureClassifier.Classify(error, RedHeaderFailureStage.Generate);
		}
	}

	void IDocumentMutation.Apply(MutationExecutionContext context)
	{
		this.Apply(context);
	}
}
