using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Mutations;

public sealed class MutationExecutionContext
{
	private readonly List<IDisposable> resources = new List<IDisposable>();

	private DocumentWriteLease writeLease;

	public Application Application { get; set; }

	public Document Document { get; set; }

	public Microsoft.Office.Interop.Word.Range ScopeRange { get; set; }

	public DocumentSession Session { get; set; }

	public IDictionary<string, object> Items { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

	public void RegisterResource(IDisposable resource)
	{
		if (resource != null)
		{
			resources.Add(resource);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public DocumentWriteLease RequireWriteLease()
	{
		if (writeLease == null)
		{
			throw new InvalidOperationException("变更操作缺少写入凭证：必须由统一执行器先行签发。");
		}
		return writeLease;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal void AttachWriteLease(DocumentWriteLease lease)
	{
		if (lease != null)
		{
			if (writeLease != null && writeLease != lease)
			{
				throw new InvalidOperationException("执行上下文已绑定其他写入凭证，拒绝替换。");
			}
			writeLease = lease;
			return;
		}
		throw new ArgumentNullException("lease");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal void DisposeResources()
	{
		List<Exception> list = null;
		for (int num = resources.Count - 1; num >= 0; num--)
		{
			try
			{
				resources[num].Dispose();
			}
			catch (Exception item)
			{
				if (list == null)
				{
					list = new List<Exception>();
				}
				list.Add(item);
			}
		}
		resources.Clear();
		if (list == null)
		{
			return;
		}
		throw new AggregateException("变更执行资源清理失败。", list);
	}
}
