using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DocumentRepository.Services.Formatting.Images;

public static class ImageCaptionPolicy
{
	private static readonly Regex CaptionRegex = new Regex("^\\s*(?:图|图片|照片)\\s*[0-9０-９一二三四五六七八九十百]+(?:\\s*[-—－.．、]\\s*[0-9０-９一二三四五六七八九十百]+)?(?:\\s|[:：]|$)", RegexOptions.CultureInvariant);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsExistingCaption(string text)
	{
		text = (text ?? string.Empty).Replace("\r", string.Empty).Replace("\a", string.Empty).Trim();
		if (text.Length > 0)
		{
			return CaptionRegex.IsMatch(text);
		}
		return false;
	}
}
