using System;

namespace DocumentRepository.Models;

public class StepResult
{
	public bool Success { get; protected set; }

	public string Message { get; protected set; }

	public Exception Error { get; protected set; }

	public bool CanContinue { get; protected set; }

	protected StepResult()
	{
	}

	public static StepResult Ok(string message = null)
	{
		return new StepResult
		{
			Success = true,
			Message = message,
			CanContinue = true
		};
	}

	public static StepResult Skipped(string reason)
	{
		return new StepResult
		{
			Success = true,
			Message = reason,
			CanContinue = true
		};
	}

	public static StepResult Warning(string message, Exception error = null)
	{
		return new StepResult
		{
			Success = true,
			Message = message,
			Error = error,
			CanContinue = true
		};
	}

	public static StepResult Fail(string message, Exception error = null, bool canContinue = true)
	{
		return new StepResult
		{
			Success = false,
			Message = message,
			Error = error,
			CanContinue = canContinue
		};
	}

	public static StepResult CriticalFail(string message, Exception error = null)
	{
		return new StepResult
		{
			Success = false,
			Message = message,
			Error = error,
			CanContinue = false
		};
	}
}
