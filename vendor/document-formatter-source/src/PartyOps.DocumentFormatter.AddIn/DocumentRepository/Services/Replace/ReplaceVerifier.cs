using DocumentRepository.Models.Replace;
using DocumentRepository.Models.Snapshots;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Replace;

public static class ReplaceVerifier
{
	public static void Verify(Document document, Range currentScope, OutsideScopeSnapshot outsideScope, int appliedCount)
	{
		if (document == null || currentScope == null)
		{
			throw ReplaceOperationException.Create(ReplaceFailureReasonCode.VerificationFailed, ReplaceFailureStage.Verify);
		}
		if (appliedCount < 0)
		{
			throw ReplaceOperationException.Create(ReplaceFailureReasonCode.VerificationFailed, ReplaceFailureStage.Verify);
		}
		if (currentScope.Start >= document.Content.Start && currentScope.End <= document.Content.End)
		{
			DocumentSnapshotService.AssertOutsideScopeUnchanged(outsideScope, document, currentScope);
			return;
		}
		throw ReplaceOperationException.Create(ReplaceFailureReasonCode.VerificationFailed, ReplaceFailureStage.Verify);
	}
}
