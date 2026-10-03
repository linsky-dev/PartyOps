using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Recovery;

public sealed class RecoveryCopyReceipt
{
	public string RecoveryId { get; private set; }

	public string TaskId { get; private set; }

	public string DocumentLifecycleId { get; private set; }

	public string FeatureId { get; private set; }

	public RecoveryCoverage Coverage { get; private set; }

	public long Length { get; private set; }

	public DateTime CreatedAtUtc { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal RecoveryCopyReceipt(string recoveryId, string taskId, string documentLifecycleId, string featureId, RecoveryCoverage coverage, long length)
	{
		if (string.IsNullOrWhiteSpace(recoveryId))
		{
			throw new ArgumentException("恢复凭证缺少恢复标识。", "recoveryId");
		}
		if (!string.IsNullOrWhiteSpace(taskId))
		{
			if (!string.IsNullOrWhiteSpace(documentLifecycleId))
			{
				if (!string.IsNullOrWhiteSpace(featureId))
				{
					if (length < 0)
					{
						throw new ArgumentOutOfRangeException("length", "恢复凭证文件长度无效。");
					}
					RecoveryId = recoveryId;
					TaskId = taskId;
					DocumentLifecycleId = documentLifecycleId;
					FeatureId = featureId;
					Coverage = coverage;
					Length = length;
					CreatedAtUtc = DateTime.UtcNow;
					return;
				}
				throw new ArgumentException("恢复凭证缺少功能标识。", "featureId");
			}
			throw new ArgumentException("恢复凭证缺少文档生命周期标识。", "documentLifecycleId");
		}
		throw new ArgumentException("恢复凭证缺少任务号。", "taskId");
	}
}
