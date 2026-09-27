using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Rules;

public class RuleStoreDescriptor
{
	public string Id { get; set; }

	public string StorePath { get; set; }

	public string OwnerService { get; set; }

	public string ModelTypeName { get; set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void EnsureValid()
	{
		if (!string.IsNullOrWhiteSpace(Id))
		{
			if (!string.IsNullOrWhiteSpace(StorePath))
			{
				if (string.IsNullOrWhiteSpace(OwnerService))
				{
					throw new InvalidOperationException("Rule store owner service cannot be empty: " + Id);
				}
				return;
			}
			throw new InvalidOperationException("Rule store path cannot be empty: " + Id);
		}
		throw new InvalidOperationException("Rule store id cannot be empty.");
	}
}
