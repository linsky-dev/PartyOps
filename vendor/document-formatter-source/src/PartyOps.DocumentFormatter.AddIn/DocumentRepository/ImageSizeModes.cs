using System.Runtime.CompilerServices;

namespace DocumentRepository;

public static class ImageSizeModes
{
	public const string Preserve = "Preserve";

	public const string ShrinkToFit = "ShrinkToFit";

	public const string FixedWidth = "FixedWidth";

	public const string FixedSize = "FixedSize";

	public const string LimitMax = "LimitMax";

	public const string OriginalScalePercent = "OriginalScalePercent";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsValid(string value)
	{
		switch (value)
		{
		default:
			return value == "OriginalScalePercent";
		case "Preserve":
		case "ShrinkToFit":
		case "FixedWidth":
		case "FixedSize":
		case "LimitMax":
			return true;
		}
	}
}
