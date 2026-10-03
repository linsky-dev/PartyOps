using System.Runtime.CompilerServices;

namespace DocumentRepository.Models;

public class ReplaceRule
{
	public string Name { get; set; }

	public bool Enabled { get; set; }

	public string FindText { get; set; }

	public bool FormatOnly { get; set; }

	public bool UseRegex { get; set; }

	public bool UseWildcard { get; set; }

	public string ReplaceText { get; set; }

	public ReplaceFormatCondition FindFormat { get; set; }

	public ReplaceFormatTarget ReplaceFormat { get; set; }

	public bool HasWork
	{
		get
		{
			if (!FormatOnly || FindFormat.IsEmpty || ReplaceFormat.IsEmpty)
			{
				if (FormatOnly)
				{
					return false;
				}
				return !string.IsNullOrWhiteSpace(FindText);
			}
			return true;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ReplaceRule()
	{
		Name = "新替换规则";
		FindText = "";
		ReplaceText = "";
		FindFormat = new ReplaceFormatCondition();
		ReplaceFormat = new ReplaceFormatTarget();
	}
}
