using System;
using System.IO;
using DocumentRepository.Models.Conversion;

namespace DocumentRepository.Services.Conversion;

public static class ConvertFailureClassifier
{
	public static ConvertOperationException Classify(Exception error, ConvertFailureStage stage, ConvertOptions options)
	{
		if (!(error is ConvertOperationException result))
		{
			switch (stage)
			{
			case ConvertFailureStage.Execute:
				if (options != null && options.SelectedFormat == ConvertFormat.Docx && options.DocxMode == DocxConvertMode.ReplaceCurrentDocument)
				{
					return ConvertOperationException.Create(ConvertFailureReasonCode.DocxReplacementFailed, stage, error);
				}
				if (options != null && options.SelectedFormat == ConvertFormat.Image)
				{
					return ConvertOperationException.Create(ConvertFailureReasonCode.ImagePageReadFailed, stage, error);
				}
				if (error is IOException || error is UnauthorizedAccessException)
				{
					return ConvertOperationException.Create(ConvertFailureReasonCode.OutputWriteFailed, stage, error);
				}
				return ConvertOperationException.Create(ConvertFailureReasonCode.HostExportFailed, stage, error);
			case ConvertFailureStage.Plan:
				return ConvertOperationException.Create(ConvertFailureReasonCode.OutputFolderInvalid, stage, error);
			case ConvertFailureStage.Analyze:
				return ConvertOperationException.Create(ConvertFailureReasonCode.DocumentAnalysisUnavailable, stage, error);
			case ConvertFailureStage.Verify:
				return ConvertOperationException.Create(ConvertFailureReasonCode.OutputValidationFailed, stage, error);
			default:
				return ConvertOperationException.Create(ConvertFailureReasonCode.UnexpectedFailure, stage, error);
			}
		}
		return result;
	}
}
