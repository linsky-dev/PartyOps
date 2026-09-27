using System.Collections.Generic;

namespace DocumentRepository.Models.Features;

/// <summary>
/// 描述一项可交付产品能力及其源码责任边界。
/// </summary>
public sealed class ProductCapabilityDescriptor
{
	public string FeatureId { get; set; }

	public string CapabilityId { get; set; }

	public string Description { get; set; }

	public bool RequiresOfficeHost { get; set; }

	public IList<string> OwnerTypeNames { get; private set; }

	public ProductCapabilityDescriptor()
	{
		FeatureId = string.Empty;
		CapabilityId = string.Empty;
		Description = string.Empty;
		OwnerTypeNames = new List<string>();
	}
}
