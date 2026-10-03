using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Protection;
using DocumentRepository.Models.Recovery;

namespace DocumentRepository.Services.Hosting;

public sealed class SessionRecoveryGuard
{
	private readonly bool required;

	private readonly string taskId;

	private readonly string featureId;

	private readonly RecoveryProtectionMode expectedEvidenceMode;

	private RecoveryCopyReceipt receipt;

	private SessionProtectionEvidence evidence;

	public bool Required => required;

	public bool HasReceipt => receipt != null;

	public RecoveryCopyReceipt Receipt => receipt;

	public bool HasEvidence => evidence != null;

	public SessionProtectionEvidence Evidence => evidence;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public SessionRecoveryGuard(bool required, string taskId, string featureId, RecoveryProtectionMode expectedEvidenceMode = RecoveryProtectionMode.NotApplicable)
	{
		this.required = required;
		if (!required || !string.IsNullOrWhiteSpace(taskId))
		{
			if (!required || !string.IsNullOrWhiteSpace(featureId))
			{
				this.taskId = taskId;
				this.featureId = featureId;
				this.expectedEvidenceMode = expectedEvidenceMode;
				return;
			}
			throw new ArgumentException("恢复凭证守卫缺少功能标识。", "featureId");
		}
		throw new ArgumentException("恢复凭证守卫缺少任务号。", "taskId");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Register(RecoveryCopyReceipt value)
	{
		if (value == null)
		{
			throw new ArgumentNullException("value");
		}
		if (!required)
		{
			throw new InvalidOperationException("本会话未要求恢复副本，拒绝登记恢复凭证。");
		}
		if (string.Equals(value.TaskId, taskId, StringComparison.Ordinal))
		{
			if (string.Equals(value.FeatureId, featureId, StringComparison.Ordinal))
			{
				if (receipt != null && receipt != value)
				{
					throw new InvalidOperationException("当前会话已登记恢复凭证，拒绝替换。");
				}
				if (evidence != null)
				{
					throw new InvalidOperationException("当前会话已持有保护证据，恢复凭证与保护证据不得并存。");
				}
				receipt = value;
				return;
			}
			throw new InvalidOperationException("恢复凭证与当前功能不匹配，拒绝登记。");
		}
		throw new InvalidOperationException("恢复凭证与当前会话不匹配，拒绝登记。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void RegisterEvidence(SessionProtectionEvidence value)
	{
		if (value == null)
		{
			throw new ArgumentNullException("value");
		}
		if (required)
		{
			if (!string.Equals(value.TaskId, taskId, StringComparison.Ordinal))
			{
				throw new InvalidOperationException("保护证据与当前会话不匹配，拒绝登记。");
			}
			if (!string.Equals(value.FeatureId, featureId, StringComparison.Ordinal))
			{
				throw new InvalidOperationException("保护证据与当前功能不匹配，拒绝登记。");
			}
			if (value.Mode != expectedEvidenceMode)
			{
				throw new InvalidOperationException("保护证据模式与当前会话不匹配，拒绝登记。");
			}
			if (evidence != null && evidence != value)
			{
				throw new InvalidOperationException("当前会话已登记保护证据，拒绝替换。");
			}
			if (receipt == null)
			{
				evidence = value;
				return;
			}
			throw new InvalidOperationException("当前会话已持有恢复凭证，保护证据与恢复凭证不得并存。");
		}
		throw new InvalidOperationException("本会话未要求保护，拒绝登记保护证据。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void AssertReadyForWrite(string documentLifecycleId)
	{
		if (required)
		{
			if (receipt == null && evidence == null)
			{
				throw new InvalidOperationException("恢复副本或保护证据未准备，已在修改文档前停止。");
			}
			if (receipt != null && !string.Equals(receipt.DocumentLifecycleId, documentLifecycleId, StringComparison.Ordinal))
			{
				throw new InvalidOperationException("恢复凭证不属于当前文档，拒绝跨文档复用。");
			}
			if (evidence != null && !string.Equals(evidence.DocumentLifecycleId, documentLifecycleId, StringComparison.Ordinal))
			{
				throw new InvalidOperationException("保护证据不属于当前文档，拒绝跨文档复用。");
			}
		}
	}
}
