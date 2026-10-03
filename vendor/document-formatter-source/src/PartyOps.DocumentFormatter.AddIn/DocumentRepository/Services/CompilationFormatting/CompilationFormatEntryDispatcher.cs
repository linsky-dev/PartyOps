using DocumentRepository.Models.CompilationFormatting;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationFormatEntryDispatcher
{
	public static CompilationRouteDecision Decide(CompilationRouteFacts facts)
	{
		if (facts != null)
		{
			if (!facts.SelectionSpansTocAndBody)
			{
				if (!facts.CursorInsideCompilationToc)
				{
					if (facts.CompilationEnabled)
					{
						if (facts.HasArticleStructure)
						{
							if (facts.HasMeaningfulSelection && !facts.IsWholeDocumentSelection)
							{
								if (facts.SelectionInsideSingleArticle)
								{
									return new CompilationRouteDecision
									{
										Kind = CompilationRouteKind.LegacySelection
									};
								}
								if (facts.FirstSelectedArticleIndex < 0 || facts.LastSelectedArticleIndex < 0)
								{
									return CompilationRouteDecision.Reject(CompilationFormatFailureReasonCode.SelectionCoversNoArticle);
								}
								CompilationRouteDecision compilationRouteDecision = new CompilationRouteDecision
								{
									Kind = CompilationRouteKind.CompilationPartial,
									NeedsExpandConfirmation = (facts.SelectionTruncatedAtStart || facts.SelectionTruncatedAtEnd)
								};
								for (int i = facts.FirstSelectedArticleIndex; i <= facts.LastSelectedArticleIndex; i++)
								{
									compilationRouteDecision.SelectedArticleIndexes.Add(i);
								}
								return compilationRouteDecision;
							}
							if (facts.UsesVisibleMarkers && facts.ArticleCount < 2)
							{
								return CompilationRouteDecision.Reject(CompilationFormatFailureReasonCode.InsufficientArticles);
							}
							return new CompilationRouteDecision
							{
								Kind = CompilationRouteKind.CompilationFull
							};
						}
						return CompilationRouteDecision.Reject(facts.UsesVisibleMarkers ? CompilationFormatFailureReasonCode.MarkerStructureInvalid : CompilationFormatFailureReasonCode.ManifestUnreadable);
					}
					return new CompilationRouteDecision
					{
						Kind = ((facts.HasMeaningfulSelection && !facts.IsWholeDocumentSelection) ? CompilationRouteKind.LegacySelection : CompilationRouteKind.LegacyFull)
					};
				}
				return new CompilationRouteDecision
				{
					Kind = CompilationRouteKind.CompilationTocUpdate
				};
			}
			return CompilationRouteDecision.Reject(CompilationFormatFailureReasonCode.SelectionSpansTocAndBody);
		}
		return CompilationRouteDecision.Reject(CompilationFormatFailureReasonCode.Unknown);
	}
}
