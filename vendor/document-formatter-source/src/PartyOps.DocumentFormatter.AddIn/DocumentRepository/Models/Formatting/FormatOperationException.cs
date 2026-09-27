using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Formatting;

public sealed class FormatOperationException : Exception
{
	public FormatFailureReasonCode ReasonCode { get; private set; }

	public FormatFailureStage Stage { get; private set; }

	public DocumentSafetyDisposition SafetyDisposition { get; private set; }

	public FormatParameterField ParameterField { get; private set; }

	public int AffectedCount { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	private FormatOperationException(FormatFailureReasonCode reasonCode, FormatFailureStage stage, DocumentSafetyDisposition safetyDisposition, FormatParameterField parameterField, int affectedCount, Exception innerException)
		: base("format-operation-failed:" + reasonCode, innerException)
	{
		if (affectedCount >= 0)
		{
			ValidateSafety(reasonCode, safetyDisposition);
			ReasonCode = reasonCode;
			Stage = stage;
			SafetyDisposition = safetyDisposition;
			ParameterField = parameterField;
			AffectedCount = affectedCount;
			return;
		}
		throw new ArgumentOutOfRangeException("affectedCount");
	}

	internal static FormatOperationException Create(FormatFailureReasonCode reasonCode, FormatFailureStage stage, DocumentSafetyDisposition safetyDisposition = DocumentSafetyDisposition.Unchanged, Exception innerException = null, FormatParameterField parameterField = FormatParameterField.Unknown, int affectedCount = 0)
	{
		return new FormatOperationException(reasonCode, stage, safetyDisposition, parameterField, affectedCount, innerException);
	}

	internal FormatOperationException WithSafety(DocumentSafetyDisposition safetyDisposition)
	{
		return Create(ReasonCode, Stage, safetyDisposition, base.InnerException, ParameterField, AffectedCount);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateSafety(FormatFailureReasonCode reasonCode, DocumentSafetyDisposition safetyDisposition)
	{
		if (reasonCode == FormatFailureReasonCode.RollbackVerified && safetyDisposition != DocumentSafetyDisposition.RecoveredVerified)
		{
			throw new ArgumentException("回滚已验证原因必须声明原文档已恢复。", "safetyDisposition");
		}
		if (reasonCode == FormatFailureReasonCode.RecoveryRequired && safetyDisposition != DocumentSafetyDisposition.RecoveryUnconfirmed)
		{
			throw new ArgumentException("需要恢复助手的原因必须声明恢复未确认。", "safetyDisposition");
		}
		if (IsContentIntegrityReason(reasonCode) && safetyDisposition != DocumentSafetyDisposition.RecoveredVerified && safetyDisposition != DocumentSafetyDisposition.RecoveryUnconfirmed && safetyDisposition != DocumentSafetyDisposition.PendingRecovery)
		{
			throw new ArgumentException("内容完整性失败必须声明已验证恢复或恢复未确认。", "safetyDisposition");
		}
	}

	private static bool IsContentIntegrityReason(FormatFailureReasonCode reasonCode)
	{
		if (reasonCode != FormatFailureReasonCode.OriginalTextChanged && reasonCode != FormatFailureReasonCode.TableContentChanged && reasonCode != FormatFailureReasonCode.TableStructureChanged && reasonCode != FormatFailureReasonCode.ImageObjectChanged)
		{
			return reasonCode == FormatFailureReasonCode.OutsideSelectionChanged;
		}
		return true;
	}
}
