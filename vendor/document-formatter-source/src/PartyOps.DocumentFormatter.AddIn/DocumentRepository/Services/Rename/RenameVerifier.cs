using System;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Rename;
using DocumentRepository.Services.FileSafety;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Rename;

public static class RenameVerifier
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Verify(Document document, RenameExecutionPlan plan, string validatedOutputPath, OutputIntegrityReceipt integrityReceipt)
	{
		try
		{
			if (document == null)
			{
				throw new ArgumentNullException("document");
			}
			if (plan != null)
			{
				if (plan.CopyMode || !string.Equals(plan.OriginalPath, plan.TargetPath, StringComparison.OrdinalIgnoreCase))
				{
					if (integrityReceipt == null)
					{
						throw new InvalidOperationException("rename-output-receipt-missing");
					}
					if (string.IsNullOrWhiteSpace(validatedOutputPath))
					{
						throw new InvalidOperationException("rename-output-path-missing");
					}
					integrityReceipt.AssertMatches(validatedOutputPath, Path.GetExtension(validatedOutputPath));
				}
				string path = document.FullName ?? string.Empty;
				if (!string.Equals(b: Path.GetFullPath(plan.CopyMode ? plan.OriginalPath : plan.TargetPath), a: Path.GetFullPath(path), comparisonType: StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("rename-active-document-path-mismatch");
				}
				return;
			}
			throw new ArgumentNullException("plan");
		}
		catch (RenameOperationException)
		{
			throw;
		}
		catch (Exception innerException)
		{
			throw RenameOperationException.Create(RenameFailureReasonCode.OutputValidationFailed, RenameFailureStage.Verification, innerException);
		}
	}
}
