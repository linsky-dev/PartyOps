using System.Runtime.CompilerServices;
using DocumentRepository.Models.Safety;

namespace DocumentRepository.Services.Recovery;

public static class RecoveryUserMessages
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Resolve(ExecutionDecisionReasonCode reasonCode)
	{
		return reasonCode switch
		{
			ExecutionDecisionReasonCode.RecoverySourceChangedDuringCopy => "文档在备份过程中被修改，已在修改文档前停止。", 
			ExecutionDecisionReasonCode.RecoveryRootUnavailable => "恢复目录不可用，已在修改文档前停止。请检查磁盘和权限。", 
			ExecutionDecisionReasonCode.RecoveryManifestWriteFailed => "恢复清单写入失败，已在修改文档前停止。", 
			ExecutionDecisionReasonCode.RecoveryDiskSpaceInsufficient => "磁盘空间不足，无法创建恢复副本，已在修改文档前停止。", 
			ExecutionDecisionReasonCode.RecoverySourceNotSaved => "文档尚未保存。请先保存文档，再执行本功能。", 
			ExecutionDecisionReasonCode.RecoveryCopyVerificationFailed => "恢复副本校验失败，已在修改文档前停止。", 
			ExecutionDecisionReasonCode.RecoverySourceMissing => "未找到文档对应的磁盘文件。请先保存文档，再执行本功能。", 
			ExecutionDecisionReasonCode.RecoveryQuotaExceeded => "恢复副本空间已达上限且无法清理，已在修改文档前停止。", 
			_ => "创建恢复副本失败，已在修改文档前停止。", 
		};
	}
}
