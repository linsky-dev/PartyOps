using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Rename;

public sealed class RenameOperationException : Exception
{
	public RenameFailureReasonCode ReasonCode { get; private set; }

	public RenameFailureStage Stage { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	private RenameOperationException(RenameFailureReasonCode reasonCode, RenameFailureStage stage, Exception innerException)
		: base("rename-operation-failed:" + reasonCode, innerException)
	{
		ReasonCode = reasonCode;
		Stage = stage;
	}

	internal static RenameOperationException Create(RenameFailureReasonCode reasonCode, RenameFailureStage stage, Exception innerException = null)
	{
		return new RenameOperationException(reasonCode, stage, innerException);
	}

	internal static RenameOperationException RollbackVerified(Exception operationError)
	{
		return Create(RenameFailureReasonCode.RollbackVerified, RenameFailureStage.Rollback, operationError);
	}

	internal static RenameOperationException RecoveryRequired(Exception operationError, Exception rollbackError)
	{
		return Create(RenameFailureReasonCode.RecoveryRequired, RenameFailureStage.Rollback, new AggregateException(operationError, rollbackError));
	}
}
