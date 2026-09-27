using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Protection;

public sealed class DocumentRiskProfile
{
	public DocumentRiskState State { get; private set; }

	public bool HasStablePath { get; private set; }

	public bool? IsSaved { get; private set; }

	internal string DocumentLifecycleId { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal DocumentRiskProfile(DocumentRiskState state, bool hasStablePath, bool? isSaved, string documentLifecycleId)
	{
		if (Enum.IsDefined(typeof(DocumentRiskState), state))
		{
			if (string.IsNullOrWhiteSpace(documentLifecycleId))
			{
				throw new ArgumentException("文档风险画像缺少文档生命周期标识。", "documentLifecycleId");
			}
			if ((state == DocumentRiskState.NewBlank || state == DocumentRiskState.NewWithContent || state == DocumentRiskState.PathUnavailable) && hasStablePath)
			{
				throw new ArgumentException("无稳定路径状态不能声明稳定路径。", "hasStablePath");
			}
			if ((state == DocumentRiskState.SavedClean || state == DocumentRiskState.SavedDirty) && !hasStablePath)
			{
				throw new ArgumentException("已保存状态必须具有稳定路径。", "hasStablePath");
			}
			if (state != DocumentRiskState.NewBlank || isSaved == true)
			{
				State = state;
				HasStablePath = hasStablePath;
				IsSaved = isSaved;
				DocumentLifecycleId = documentLifecycleId;
				return;
			}
			throw new ArgumentException("自动放行的空白新文档必须是未被用户修改的干净状态。", "isSaved");
		}
		throw new ArgumentOutOfRangeException("state");
	}
}
