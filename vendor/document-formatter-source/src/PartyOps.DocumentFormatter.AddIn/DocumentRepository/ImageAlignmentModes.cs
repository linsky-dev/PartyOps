using System.Runtime.CompilerServices;

namespace DocumentRepository;

public static class ImageAlignmentModes
{
	public const string Preserve = "Preserve";

	public const string Left = "Left";

	public const string Center = "Center";

	public const string Right = "Right";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsValid(string value)
	{
		switch (value)
		{
		default:
			return value == "Right";
		case "Preserve":
		case "Left":
		case "Center":
			return true;
		}
	}
}
