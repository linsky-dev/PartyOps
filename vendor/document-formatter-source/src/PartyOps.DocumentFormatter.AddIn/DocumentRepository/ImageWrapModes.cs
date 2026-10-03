using System.Runtime.CompilerServices;

namespace DocumentRepository;

public static class ImageWrapModes
{
	public const string Preserve = "Preserve";

	public const string Inline = "Inline";

	public const string Square = "Square";

	public const string Tight = "Tight";

	public const string Through = "Through";

	public const string TopBottom = "TopBottom";

	public const string Behind = "Behind";

	public const string Front = "Front";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsValid(string value)
	{
		switch (value)
		{
		default:
			return value == "Front";
		case "Preserve":
		case "Inline":
		case "Square":
		case "Tight":
		case "Through":
		case "TopBottom":
		case "Behind":
			return true;
		}
	}
}
