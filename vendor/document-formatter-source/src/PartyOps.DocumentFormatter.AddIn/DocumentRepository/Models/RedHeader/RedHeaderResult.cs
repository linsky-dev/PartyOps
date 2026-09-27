using System;
using System.Collections.Generic;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Models.RedHeader;

public class RedHeaderResult
{
	public bool Success { get; private set; }

	public bool Cancelled { get; private set; }

	public UserOutcomeKind OutcomeKind { get; set; }

	public string RecoveryId { get; set; }

	public string Message { get; private set; }

	public IReadOnlyList<VerificationFinding> Warnings { get; private set; }

	public Exception Error { get; private set; }

	public string FailureReasonCode { get; internal set; }

	public string FailureStage { get; internal set; }

	private RedHeaderResult()
	{
		Warnings = new VerificationFinding[0];
	}

	public static RedHeaderResult Ok(string message, IEnumerable<VerificationFinding> warnings = null)
	{
		RedHeaderResult obj = new RedHeaderResult
		{
			Success = true,
			Message = message
		};
		IReadOnlyList<VerificationFinding> warnings2;
		if (warnings != null)
		{
			IReadOnlyList<VerificationFinding> readOnlyList = new List<VerificationFinding>(warnings).AsReadOnly();
			warnings2 = readOnlyList;
		}
		else
		{
			IReadOnlyList<VerificationFinding> readOnlyList = new VerificationFinding[0];
			warnings2 = readOnlyList;
		}
		obj.Warnings = warnings2;
		return obj;
	}

	public static RedHeaderResult Fail(string message, Exception error = null)
	{
		return new RedHeaderResult
		{
			Success = false,
			Message = message,
			Error = error
		};
	}

	public static RedHeaderResult CancelledResult(string message)
	{
		return new RedHeaderResult
		{
			Success = false,
			Cancelled = true,
			Message = message
		};
	}
}
