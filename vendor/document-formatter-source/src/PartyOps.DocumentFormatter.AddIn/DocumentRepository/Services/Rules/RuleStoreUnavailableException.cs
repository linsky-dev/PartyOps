using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Rules;

namespace DocumentRepository.Services.Rules;

public sealed class RuleStoreUnavailableException : Exception
{
	public string StoreId { get; private set; }

	public string ReasonCode { get; private set; }

	private RuleStoreUnavailableException(string message, string storeId, string reasonCode, Exception inner)
		: base(message, inner)
	{
		StoreId = storeId;
		ReasonCode = reasonCode;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleStoreUnavailableException From<T>(RuleLoadResult<T> result, string displayName)
	{
		string obj = (string.IsNullOrWhiteSpace(displayName) ? "规则库" : displayName);
		return new RuleStoreUnavailableException(storeId: result?.StoreId, reasonCode: result?.ReasonCode, message: obj + "已损坏或无法读取，本次未执行。请打开对应的规则/模板设置界面重置为默认配置；重置时会先自动备份原文件。", inner: result?.Error);
	}
}
