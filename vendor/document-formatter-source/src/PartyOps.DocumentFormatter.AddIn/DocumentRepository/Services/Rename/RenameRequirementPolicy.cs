using DocumentRepository.Models.Rename;

namespace DocumentRepository.Services.Rename;

public static class RenameRequirementPolicy
{
	public static RenameFailureReasonCode? FindMissingContent(bool needsMainTitle, string mainTitle, bool needsDocumentNumber, string documentNumber, bool needsSubtitle, string subtitle)
	{
		if (!needsMainTitle || !string.IsNullOrWhiteSpace(mainTitle))
		{
			if (!needsDocumentNumber || !string.IsNullOrWhiteSpace(documentNumber))
			{
				if (!needsSubtitle || !string.IsNullOrWhiteSpace(subtitle))
				{
					return null;
				}
				return RenameFailureReasonCode.SubtitleMissing;
			}
			return RenameFailureReasonCode.DocumentNumberMissing;
		}
		return RenameFailureReasonCode.MainTitleMissing;
	}
}
