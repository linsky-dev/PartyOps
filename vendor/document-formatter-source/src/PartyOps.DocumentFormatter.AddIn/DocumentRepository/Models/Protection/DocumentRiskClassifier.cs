namespace DocumentRepository.Models.Protection;

public static class DocumentRiskClassifier
{
	public static DocumentRiskState Classify(bool? isSaved, bool? isReadOnly, bool? isProtected, bool pathProbeSucceeded, bool hasPathIdentity, bool hasStableLocalPath, bool isTrulyBlank)
	{
		if (isReadOnly != true)
		{
			if (isProtected != true)
			{
				if (isReadOnly.HasValue && isProtected.HasValue && pathProbeSucceeded)
				{
					if (hasStableLocalPath)
					{
						if (isSaved == true)
						{
							return DocumentRiskState.SavedClean;
						}
						return DocumentRiskState.SavedDirty;
					}
					if (!hasPathIdentity)
					{
						if (!isTrulyBlank || isSaved != true)
						{
							return DocumentRiskState.NewWithContent;
						}
						return DocumentRiskState.NewBlank;
					}
					return DocumentRiskState.PathUnavailable;
				}
				return DocumentRiskState.PathUnavailable;
			}
			return DocumentRiskState.Protected;
		}
		return DocumentRiskState.ReadOnly;
	}
}
