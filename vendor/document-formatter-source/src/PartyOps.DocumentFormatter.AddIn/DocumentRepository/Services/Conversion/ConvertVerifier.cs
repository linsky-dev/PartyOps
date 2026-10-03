using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Services.FileSafety;

namespace DocumentRepository.Services.Conversion;

public static class ConvertVerifier
{
	public static void Verify(ConvertExecutionPlan plan, IList<string> files)
	{
		if (plan != null)
		{
			if (files != null && files.Count != 0)
			{
				if (files.Distinct<string>(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
				{
					throw ConvertOperationException.Create(ConvertFailureReasonCode.OutputValidationFailed, ConvertFailureStage.Verify);
				}
				foreach (string file in files)
				{
					OutputFileIntegrityValidator.Validate(file, Path.GetExtension(file));
				}
				if (plan.Options.SelectedFormat != ConvertFormat.Image && !string.IsNullOrWhiteSpace(plan.TargetPath) && !files.Any((string file) => string.Equals(Path.GetFullPath(file), Path.GetFullPath(plan.TargetPath), StringComparison.OrdinalIgnoreCase)))
				{
					throw ConvertOperationException.Create(ConvertFailureReasonCode.OutputValidationFailed, ConvertFailureStage.Verify);
				}
				return;
			}
			throw ConvertOperationException.Create(ConvertFailureReasonCode.OutputValidationFailed, ConvertFailureStage.Verify);
		}
		throw ConvertOperationException.Create(ConvertFailureReasonCode.OutputValidationFailed, ConvertFailureStage.Verify);
	}
}
