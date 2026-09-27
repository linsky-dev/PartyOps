using System.Runtime.CompilerServices;

namespace DocumentRepository;

public static class ImageParagraphFilterModes
{
	public const string All = "All";

	public const string StandaloneOnly = "StandaloneOnly";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsValid(string value)
	{
		if (!(value == "All"))
		{
			return value == "StandaloneOnly";
		}
		return true;
	}
}
