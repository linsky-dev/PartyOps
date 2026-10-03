using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Analysis;

public static class ParagraphIdentityTextNormalizer
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Normalize(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}
		return text.Replace("\r", string.Empty).Replace("\a", string.Empty).Replace("\v", string.Empty)
			.Trim();
	}
}
