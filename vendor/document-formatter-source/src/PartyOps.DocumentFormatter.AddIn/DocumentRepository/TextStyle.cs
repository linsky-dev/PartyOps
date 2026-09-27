using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository;

[Serializable]
public class TextStyle
{
	public string FontName { get; set; }

	public string FontSize { get; set; }

	public bool Bold { get; set; }

	public string LineSpacing { get; set; }

	public string FirstLineIndent { get; set; }

	public int SpaceBefore { get; set; }

	public int SpaceAfter { get; set; }

	public string RecognitionStyle { get; set; }

	public string Alignment { get; set; }

	public string OutlineLevel { get; set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public TextStyle()
	{
		FontName = "仿宋_GB2312";
		FontSize = "三号";
		Bold = false;
		LineSpacing = "28";
		FirstLineIndent = "2";
		SpaceBefore = 0;
		SpaceAfter = 0;
		RecognitionStyle = "";
		Alignment = "两端对齐";
		OutlineLevel = "";
	}
}
