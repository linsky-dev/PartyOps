using System.Text.RegularExpressions;
using DocumentRepository.Services.Analysis;

namespace DocumentRepository.Services.Detection;

public static class AttachmentDetector
{
	private static readonly Regex MarkerAloneRegex = new Regex("^附件[：:]\\s*$", RegexOptions.Compiled);

	private static readonly Regex AttachmentMarkerRegex = new Regex("^附件\\s*[0-9０-９一二三四五六七八九十]*$", RegexOptions.Compiled);

	private static readonly Regex FirstRegex = new Regex("^附件[：:]\\s*1[\\.．、]?\\s*.+", RegexOptions.Compiled);

	private static readonly Regex SingleRegex = new Regex("^附件[：:]\\s*.+", RegexOptions.Compiled);

	private static readonly Regex ItemRegex = new Regex("^(?<num>[1-9]\\d*)[、\\.．]?\\s*(?<body>.+)$", RegexOptions.Compiled);

	public static bool IsAttachmentListMarkerAloneText(string text)
	{
		return MarkerAloneRegex.IsMatch(CleanText(text));
	}

	public static bool IsAttachmentMarkerText(string text)
	{
		return AttachmentMarkerRegex.IsMatch(CleanText(text));
	}

	public static bool IsAttachmentParagraphText(string text)
	{
		if (!IsAttachmentMarkerText(text) && !IsAttachmentListFirstText(text))
		{
			return IsAttachmentListSingleText(text);
		}
		return true;
	}

	public static bool IsAttachmentListFirstText(string text)
	{
		return FirstRegex.IsMatch(CleanText(text));
	}

	public static bool IsAttachmentListSingleText(string text)
	{
		string input = CleanText(text);
		if (SingleRegex.IsMatch(input))
		{
			return !FirstRegex.IsMatch(input);
		}
		return false;
	}

	public static bool IsAttachmentListItemText(string text)
	{
		return ItemRegex.IsMatch(CleanText(text));
	}

	private static string CleanText(string text)
	{
		return ParagraphIdentityTextNormalizer.Normalize(text);
	}
}
