using System.Runtime.CompilerServices;

namespace DocumentRepository;

public static class ImageBorderModes
{
	public const string Preserve = "Preserve";

	public const string Add = "Add";

	public const string Remove = "Remove";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsValid(string value)
	{
		if (!(value == "Preserve") && !(value == "Add"))
		{
			return value == "Remove";
		}
		return true;
	}
}
