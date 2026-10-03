using System;

namespace DocumentRepository.Services.Hosting;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
public sealed class ComOwnershipAttribute : Attribute
{
	public ComOwnershipKind Kind { get; }

	public ComOwnershipAttribute(ComOwnershipKind kind)
	{
		Kind = kind;
	}
}
