using System;
using System.IO;
using System.Runtime.InteropServices;
using DocumentRepository.Models.Rename;

namespace DocumentRepository.Services.Rename;

internal static class RenameFailureClassifier
{
	internal static RenameOperationException Classify(Exception error, RenameFailureStage stage)
	{
		RenameOperationException ex = FindControlled(error);
		if (ex != null)
		{
			return ex;
		}
		return ClassifyOutputFailure(error, stage);
	}

	internal static RenameOperationException ClassifyOutputFailure(Exception error, RenameFailureStage stage)
	{
		if (error is RenameOperationException result)
		{
			return result;
		}
		if (error is DirectoryNotFoundException)
		{
			return RenameOperationException.Create(RenameFailureReasonCode.OutputDirectoryMissing, stage, error);
		}
		if (error is UnauthorizedAccessException)
		{
			return RenameOperationException.Create(RenameFailureReasonCode.OutputDirectoryUnavailable, stage, error);
		}
		if (error is InvalidDataException)
		{
			return RenameOperationException.Create(RenameFailureReasonCode.OutputValidationFailed, stage, error);
		}
		if (!(error is PathTooLongException) && !(error is NotSupportedException) && !(error is ArgumentException))
		{
			int num = error.HResult & 0xFFFF;
			if ((error is IOException || error is COMException) && (num == 32 || num == 33))
			{
				return RenameOperationException.Create(RenameFailureReasonCode.OutputFileInUse, stage, error);
			}
			if ((error is IOException || error is COMException) && (num == 112 || num == 39))
			{
				return RenameOperationException.Create(RenameFailureReasonCode.OutputDirectoryUnavailable, stage, error);
			}
			return RenameOperationException.Create(RenameFailureReasonCode.UnexpectedFailure, stage, error);
		}
		return RenameOperationException.Create(RenameFailureReasonCode.FilenameInvalid, stage, error);
	}

	private static RenameOperationException FindControlled(Exception error)
	{
		if (error != null)
		{
			if (error is RenameOperationException result)
			{
				return result;
			}
			if (error is AggregateException ex)
			{
				foreach (Exception innerException in ex.Flatten().InnerExceptions)
				{
					RenameOperationException ex2 = FindControlled(innerException);
					if (ex2 != null)
					{
						return ex2;
					}
				}
			}
			return FindControlled(error.InnerException);
		}
		return null;
	}
}
