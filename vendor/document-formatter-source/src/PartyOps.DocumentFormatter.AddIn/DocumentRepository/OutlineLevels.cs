using System.Runtime.CompilerServices;

namespace DocumentRepository;

public static class OutlineLevels
{
	public const string BodyText = "正文文本";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsValid(string value)
	{
		string text = Normalize(value);
		if (!(text == "正文文本"))
		{
			if (text == null || text.Length != 2 || text[1] != '级')
			{
				return false;
			}
			if (text[0] >= '1')
			{
				return text[0] <= '9';
			}
			return false;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Normalize(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text == "正文")
		{
			return "正文文本";
		}
		return text;
	}
}
