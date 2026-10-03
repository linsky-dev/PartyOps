using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Protection;

public sealed class SessionProtectionEvidence
{
	public string TaskId { get; private set; }

	public string FeatureId { get; private set; }

	public string DocumentLifecycleId { get; private set; }

	public RecoveryProtectionMode Mode { get; private set; }

	public ProtectionCoverage Coverage { get; private set; }

	public bool UserConfirmed { get; private set; }

	private SessionProtectionEvidence(string taskId, string featureId, string documentLifecycleId, RecoveryProtectionMode mode, ProtectionCoverage coverage, bool userConfirmed)
	{
		TaskId = taskId;
		FeatureId = featureId;
		DocumentLifecycleId = documentLifecycleId;
		Mode = mode;
		Coverage = coverage;
		UserConfirmed = userConfirmed;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static SessionProtectionEvidence Create(string taskId, string featureId, string documentLifecycleId, RecoveryProtectionMode mode, ProtectionCoverage coverage, bool userConfirmed)
	{
		if (!string.IsNullOrWhiteSpace(taskId))
		{
			if (string.IsNullOrWhiteSpace(featureId))
			{
				throw new ArgumentException("保护证据缺少功能标识。", "featureId");
			}
			if (string.IsNullOrWhiteSpace(documentLifecycleId))
			{
				throw new ArgumentException("保护证据缺少文档生命周期标识。", "documentLifecycleId");
			}
			switch (mode)
			{
			case RecoveryProtectionMode.UndoOnlyForUnsavedDocument:
				if (!(coverage != ProtectionCoverage.UnsavedInMemoryContent || userConfirmed))
				{
					break;
				}
				throw new ArgumentException("未保存文档仅撤销证据必须声明内存态内容，且不得伪造用户确认。");
			case RecoveryProtectionMode.PreferFileCopyAllowConfirmedUndoFallback:
				if (coverage != ProtectionCoverage.ConfirmedUndoOnly)
				{
					throw new ArgumentException("确认降级证据必须声明仅撤销（已确认）覆盖范围。");
				}
				if (!userConfirmed)
				{
					throw new ArgumentException("确认降级证据必须真实记录用户确认，不得伪造。");
				}
				break;
			case RecoveryProtectionMode.UndoOnlyForBlankDocument:
				if (coverage != ProtectionCoverage.NoPreexistingUserContent || userConfirmed)
				{
					throw new ArgumentException("空白文档仅撤销证据必须声明无既有用户内容，且不得伪造用户确认。");
				}
				break;
			default:
				throw new ArgumentOutOfRangeException("mode", mode, "未知的保护证据模式。");
			}
			return new SessionProtectionEvidence(taskId, featureId, documentLifecycleId, mode, coverage, userConfirmed);
		}
		throw new ArgumentException("保护证据缺少任务号。", "taskId");
	}
}
