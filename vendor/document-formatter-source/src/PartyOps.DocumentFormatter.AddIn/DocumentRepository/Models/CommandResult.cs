using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Models;

public class CommandResult
{
	private IReadOnlyList<VerificationFinding> warnings;

	public TaskOutcomeStatus Status { get; protected set; }

	public bool Success
	{
		get
		{
			if (Status != TaskOutcomeStatus.Succeeded)
			{
				return Status == TaskOutcomeStatus.SucceededWithWarnings;
			}
			return true;
		}
	}

	public bool Cancelled => Status == TaskOutcomeStatus.Cancelled;

	public bool HasWarnings => Status == TaskOutcomeStatus.SucceededWithWarnings;

	public string Message { get; protected set; }

	public Exception Error { get; protected set; }

	public bool PresentationHandled { get; protected set; }

	public UserOutcomeKind OutcomeKind { get; set; }

	public IReadOnlyList<VerificationFinding> Warnings
	{
		get
		{
			return warnings;
		}
		set
		{
			IReadOnlyList<VerificationFinding> readOnlyList2;
			if (value != null)
			{
				IReadOnlyList<VerificationFinding> readOnlyList = new List<VerificationFinding>(value).AsReadOnly();
				readOnlyList2 = readOnlyList;
			}
			else
			{
				IReadOnlyList<VerificationFinding> readOnlyList = new VerificationFinding[0];
				readOnlyList2 = readOnlyList;
			}
			warnings = readOnlyList2;
		}
	}

	public string RecoveryId { get; set; }

	public string FailureReasonCode { get; internal set; }

	public string FailureStage { get; internal set; }

	protected CommandResult()
	{
		OutcomeKind = UserOutcomeKind.Completed;
		Warnings = new VerificationFinding[0];
	}

	public UserOutcomeKind ResolveOutcomeKind()
	{
		if (OutcomeKind != UserOutcomeKind.Completed)
		{
			return OutcomeKind;
		}
		return Status switch
		{
			TaskOutcomeStatus.Cancelled => UserOutcomeKind.Cancelled, 
			TaskOutcomeStatus.SucceededWithWarnings => UserOutcomeKind.CompletedWithWarnings, 
			TaskOutcomeStatus.Succeeded => UserOutcomeKind.Completed, 
			_ => UserOutcomeKind.Failed, 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult SuccessResult(string message = null)
	{
		return new CommandResult
		{
			Status = TaskOutcomeStatus.Succeeded,
			Message = (message ?? "操作完成")
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult WarningResult(string message)
	{
		return new CommandResult
		{
			Status = TaskOutcomeStatus.SucceededWithWarnings,
			Message = (message ?? "操作完成，但有警告。")
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult CancelledResult(string message = null)
	{
		return new CommandResult
		{
			Status = TaskOutcomeStatus.Cancelled,
			Message = (message ?? "操作已取消。")
		};
	}

	public static CommandResult FailResult(string message, Exception error = null)
	{
		return new CommandResult
		{
			Status = TaskOutcomeStatus.Failed,
			Message = message,
			Error = error
		};
	}

	public CommandResult WithHandledPresentation()
	{
		return new CommandResult
		{
			Status = Status,
			Message = Message,
			Error = Error,
			PresentationHandled = true,
			OutcomeKind = OutcomeKind,
			Warnings = Warnings,
			RecoveryId = RecoveryId,
			FailureReasonCode = FailureReasonCode,
			FailureStage = FailureStage
		};
	}

	internal CommandResult WithAdditionalWarnings(string message, IReadOnlyList<VerificationFinding> combinedWarnings)
	{
		if (!Success || combinedWarnings == null || combinedWarnings.Count == 0)
		{
			return this;
		}
		return new CommandResult
		{
			Status = TaskOutcomeStatus.SucceededWithWarnings,
			Message = (string.IsNullOrWhiteSpace(message) ? Message : message),
			Error = Error,
			PresentationHandled = PresentationHandled,
			OutcomeKind = UserOutcomeKind.CompletedWithWarnings,
			Warnings = combinedWarnings,
			RecoveryId = RecoveryId,
			FailureReasonCode = FailureReasonCode,
			FailureStage = FailureStage
		};
	}
}
