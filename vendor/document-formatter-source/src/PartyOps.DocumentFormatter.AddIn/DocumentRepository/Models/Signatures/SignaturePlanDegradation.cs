using System;

namespace DocumentRepository.Models.Signatures;

[Flags]
public enum SignaturePlanDegradation
{
	None = 0,
	NegativeIndentClamped = 2,
	ExceedsContentWidth = 4,
	UnsafeValueRejected = 8
}
