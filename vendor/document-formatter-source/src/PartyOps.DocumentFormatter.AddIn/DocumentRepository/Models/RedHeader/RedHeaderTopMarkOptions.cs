using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.RedHeader;

[Serializable]
public class RedHeaderTopMarkOptions
{
	public bool CopyNumberEnabled { get; set; }

	public string CopyNumber { get; set; }

	public string SecurityLevel { get; set; }

	public string ConfidentialityPeriod { get; set; }

	public string UrgencyLevel { get; set; }

	public string FontName { get; set; }

	public float FontSize { get; set; }

	public float LineSpacing { get; set; }

	public float LeftIndentChars { get; set; }

	public int Color { get; set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public RedHeaderTopMarkOptions()
	{
		CopyNumberEnabled = false;
		CopyNumber = "";
		SecurityLevel = "无";
		ConfidentialityPeriod = "";
		UrgencyLevel = "无";
		FontName = "黑体";
		FontSize = 16f;
		LineSpacing = 28f;
		LeftIndentChars = 0f;
		Color = 0;
	}
}
