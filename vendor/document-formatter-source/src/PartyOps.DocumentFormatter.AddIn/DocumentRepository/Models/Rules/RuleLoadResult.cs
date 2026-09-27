using System;

namespace DocumentRepository.Models.Rules;

public sealed class RuleLoadResult<T>
{
	public RuleLoadStatus Status { get; internal set; }

	public T Value { get; internal set; }

	public string StoreId { get; internal set; }

	public string StorePath { get; internal set; }

	public string ReasonCode { get; internal set; }

	public string ArchivedPath { get; internal set; }

	public Exception Error { get; internal set; }

	public bool Usable
	{
		get
		{
			if (Status != RuleLoadStatus.Loaded && Status != RuleLoadStatus.CreatedDefault)
			{
				return Status == RuleLoadStatus.InMemoryFallback;
			}
			return true;
		}
	}

	internal static RuleLoadResult<T> Success(RuleLoadStatus status, T value, string storeId, string storePath, string reasonCode)
	{
		return new RuleLoadResult<T>
		{
			Status = status,
			Value = value,
			StoreId = storeId,
			StorePath = storePath,
			ReasonCode = reasonCode
		};
	}

	internal static RuleLoadResult<T> Failure(RuleLoadStatus status, string storeId, string storePath, string reasonCode, Exception error)
	{
		return new RuleLoadResult<T>
		{
			Status = status,
			StoreId = storeId,
			StorePath = storePath,
			ReasonCode = reasonCode,
			Error = error
		};
	}

	internal static RuleLoadResult<T> Fallback(T value, string storeId, string storePath, string reasonCode, Exception error, string archivedPath = null)
	{
		return new RuleLoadResult<T>
		{
			Status = RuleLoadStatus.InMemoryFallback,
			Value = value,
			StoreId = storeId,
			StorePath = storePath,
			ReasonCode = reasonCode,
			Error = error,
			ArchivedPath = archivedPath
		};
	}
}
