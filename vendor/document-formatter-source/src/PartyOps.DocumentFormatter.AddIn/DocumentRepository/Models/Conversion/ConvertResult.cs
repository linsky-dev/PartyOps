using System.Collections.Generic;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Models.Conversion;

public class ConvertResult
{
	public TaskOutcomeStatus Status { get; private set; }

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

	public string Message { get; set; }

	public string OutputFolder { get; set; }

	public List<string> OutputFiles { get; private set; }

	public UserOutcomeKind OutcomeKind { get; internal set; }

	public string FailureReasonCode { get; internal set; }

	public string FailureStage { get; internal set; }

	public ConvertResult()
	{
		OutputFiles = new List<string>();
		OutcomeKind = UserOutcomeKind.Completed;
	}

	public static ConvertResult Fail(string message)
	{
		return new ConvertResult
		{
			Status = TaskOutcomeStatus.Failed,
			Message = message
		};
	}

	public static ConvertResult Cancel(string message)
	{
		return new ConvertResult
		{
			Status = TaskOutcomeStatus.Cancelled,
			Message = message
		};
	}

	public static ConvertResult Ok(string message, string outputFolder, IEnumerable<string> files)
	{
		ConvertResult convertResult = new ConvertResult
		{
			Status = TaskOutcomeStatus.Succeeded,
			Message = message,
			OutputFolder = outputFolder
		};
		if (files != null)
		{
			convertResult.OutputFiles.AddRange(files);
		}
		return convertResult;
	}

	public static ConvertResult OkWithWarnings(string message, string outputFolder, IEnumerable<string> files)
	{
		ConvertResult convertResult = Ok(message, outputFolder, files);
		convertResult.Status = TaskOutcomeStatus.SucceededWithWarnings;
		return convertResult;
	}
}
