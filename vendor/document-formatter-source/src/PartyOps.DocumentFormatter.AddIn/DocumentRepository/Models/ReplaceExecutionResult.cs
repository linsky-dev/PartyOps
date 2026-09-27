using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Models;

public class ReplaceExecutionResult
{
	public bool Success { get; private set; }

	public bool Cancelled { get; private set; }

	public UserOutcomeKind OutcomeKind { get; set; }

	public string RecoveryId { get; set; }

	public string Message { get; private set; }

	public Exception Error { get; private set; }

	public string PlanName { get; private set; }

	public int TotalCount { get; private set; }

	public string FailureReasonCode { get; internal set; }

	public string FailureStage { get; internal set; }

	private ReplaceExecutionResult()
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceExecutionResult Ok(string planName, int totalCount)
	{
		return new ReplaceExecutionResult
		{
			Success = true,
			PlanName = (planName ?? ""),
			TotalCount = totalCount,
			Message = string.Format("一键替换完成：{0}，共处理 {1} 处。", planName ?? "", totalCount)
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceExecutionResult Fail(string message, Exception error = null)
	{
		return new ReplaceExecutionResult
		{
			Success = false,
			Message = (message ?? "一键替换失败。"),
			Error = error
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceExecutionResult NoChanges(string planName)
	{
		return new ReplaceExecutionResult
		{
			Success = true,
			PlanName = (planName ?? ""),
			TotalCount = 0,
			OutcomeKind = UserOutcomeKind.NoChanges,
			Message = "没有需要处理的内容。"
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceExecutionResult CancelledResult(string message)
	{
		return new ReplaceExecutionResult
		{
			Success = false,
			Cancelled = true,
			Message = (message ?? "已取消。")
		};
	}
}
