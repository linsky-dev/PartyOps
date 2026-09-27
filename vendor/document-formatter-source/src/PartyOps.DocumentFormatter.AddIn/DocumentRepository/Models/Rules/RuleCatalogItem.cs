using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Rules;

public class RuleCatalogItem
{
	public string Id { get; set; }

	public string FeatureName { get; set; }

	public string DisplayName { get; set; }

	public RuleStoreKind StoreKind { get; set; }

	public RuleCatalogStatus Status { get; set; }

	public string StorePathDescription { get; set; }

	public string OwnerService { get; set; }

	public string ModelTypeName { get; set; }

	public string UiEntry { get; set; }

	public string Notes { get; set; }

	public bool IsCentralized => Status == RuleCatalogStatus.Centralized;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void EnsureValid()
	{
		if (string.IsNullOrWhiteSpace(Id))
		{
			throw new InvalidOperationException("Rule catalog item id cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(FeatureName))
		{
			throw new InvalidOperationException("Rule catalog feature name cannot be empty: " + Id);
		}
		if (string.IsNullOrWhiteSpace(DisplayName))
		{
			throw new InvalidOperationException("Rule catalog display name cannot be empty: " + Id);
		}
		if (!string.IsNullOrWhiteSpace(OwnerService))
		{
			if (string.IsNullOrWhiteSpace(StorePathDescription))
			{
				throw new InvalidOperationException("Rule catalog store path cannot be empty: " + Id);
			}
			return;
		}
		throw new InvalidOperationException("Rule catalog owner service cannot be empty: " + Id);
	}
}
