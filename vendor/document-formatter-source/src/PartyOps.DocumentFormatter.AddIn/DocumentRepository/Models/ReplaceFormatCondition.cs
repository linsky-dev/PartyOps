using System.Runtime.CompilerServices;

namespace DocumentRepository.Models;

public class ReplaceFormatCondition
{
	public string FontName { get; set; }

	public string SizeText { get; set; }

	public string Bold { get; set; }

	public string Italic { get; set; }

	public string Underline { get; set; }

	public string Alignment { get; set; }

	public string OutlineLevel { get; set; }

	public string FirstIndentChars { get; set; }

	public string SpaceBeforeLines { get; set; }

	public string SpaceAfterLines { get; set; }

	public string LineSpacing { get; set; }

	public bool IsEmpty
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			return IsAllEmpty("不限");
		}
	}

	private bool IsAllEmpty(string emptyToken)
	{
		if (IsVal(FontName, emptyToken) && IsVal(SizeText, emptyToken) && IsVal(Bold, emptyToken) && IsVal(Italic, emptyToken) && IsVal(Underline, emptyToken) && IsVal(Alignment, emptyToken) && IsVal(OutlineLevel, emptyToken) && IsVal(FirstIndentChars, emptyToken) && IsVal(SpaceBeforeLines, emptyToken) && IsVal(SpaceAfterLines, emptyToken))
		{
			return IsVal(LineSpacing, emptyToken);
		}
		return false;
	}

	private static bool IsVal(string v, string t)
	{
		if (!string.IsNullOrWhiteSpace(v))
		{
			return v.Trim() == t;
		}
		return true;
	}
}
