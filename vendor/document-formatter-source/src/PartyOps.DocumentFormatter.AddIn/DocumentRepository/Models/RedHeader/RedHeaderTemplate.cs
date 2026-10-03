using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.RedHeader;

[Serializable]
public class RedHeaderTemplate
{
	public string Id { get; set; }

	public string Name { get; set; }

	public string HeaderText { get; set; }

	public string DocumentNumberText { get; set; }

	public RedHeaderTopMarkOptions TopMarks { get; set; }

	public string HeaderFont { get; set; }

	public float HeaderSize { get; set; }

	public bool HeaderBold { get; set; }

	public int HeaderColor { get; set; }

	public string HeaderAlignment { get; set; }

	public float HeaderLineSpacing { get; set; }

	public float HeaderSpaceBefore { get; set; }

	public float HeaderSpaceAfter { get; set; }

	public float HeaderIndentChars { get; set; }

	public float HeaderLayoutWidthPercent { get; set; }

	public float HeaderCharacterScalePercent { get; set; }

	public string HeaderFitMode { get; set; }

	public float HeaderMinimumScalePercent { get; set; }

	public float HeaderCharacterSpacing { get; set; }

	public string DocumentNumberFont { get; set; }

	public float DocumentNumberSize { get; set; }

	public float DocumentNumberLineSpacing { get; set; }

	public float DocumentNumberSpaceBefore { get; set; }

	public float DocumentNumberSpaceAfter { get; set; }

	public string RedLineStyle { get; set; }

	public float RedLineWidthPercent { get; set; }

	public float RedLineThickness { get; set; }

	public int RedLineColor { get; set; }

	public float RedLineSpaceBefore { get; set; }

	public float RedLineSpaceAfter { get; set; }

	public int TitleGapLines { get; set; }

	public float TitleGapLineSpacing { get; set; }

	public bool ImprintEnabled { get; set; }

	public bool ImprintOnEvenPage { get; set; }

	public string ImprintFont { get; set; }

	public string ImprintSize { get; set; }

	public float ImprintBottomOffset { get; set; }

	public float ImprintCellPaddingCm { get; set; }

	public string ImprintSend { get; set; }

	public string ImprintOffice { get; set; }

	public string ImprintDate { get; set; }

	public RedHeaderImprintDateMode ImprintDateMode { get; set; }

	public bool UseTimesNewRomanForNumbers { get; set; }

	public bool ImprintDateUseTimesNewRoman { get; set; }

	public bool ShouldSerializeImprintDateUseTimesNewRoman()
	{
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public RedHeaderTemplate()
	{
		Id = Guid.NewGuid().ToString("N");
		Name = "下行文";
		HeaderText = "某某市人民政府文件";
		DocumentNumberText = "某府发〔2026〕8号";
		TopMarks = new RedHeaderTopMarkOptions();
		HeaderFont = "方正小标宋简体";
		HeaderSize = 48f;
		HeaderBold = false;
		HeaderColor = 255;
		HeaderAlignment = "居中";
		HeaderLineSpacing = 48f;
		HeaderSpaceBefore = 70f;
		HeaderSpaceAfter = 22f;
		HeaderIndentChars = 0f;
		HeaderLayoutWidthPercent = 100f;
		HeaderCharacterScalePercent = 100f;
		HeaderFitMode = "manual";
		HeaderMinimumScalePercent = 30f;
		HeaderCharacterSpacing = 0f;
		DocumentNumberFont = "仿宋_GB2312";
		DocumentNumberSize = 16f;
		DocumentNumberLineSpacing = 28f;
		DocumentNumberSpaceBefore = 42f;
		DocumentNumberSpaceAfter = 8f;
		RedLineStyle = "normal";
		RedLineWidthPercent = 100f;
		RedLineThickness = 3f;
		RedLineColor = 255;
		RedLineSpaceBefore = 0f;
		RedLineSpaceAfter = 0f;
		TitleGapLines = 2;
		TitleGapLineSpacing = 28f;
		ImprintEnabled = true;
		ImprintOnEvenPage = true;
		ImprintFont = "仿宋_GB2312";
		ImprintSize = "四号";
		ImprintBottomOffset = 0f;
		ImprintCellPaddingCm = 0.08f;
		ImprintSend = "抄送：张三的单位，李四的单位，王二的单位。";
		ImprintOffice = "某某市人民政府办公室";
		ImprintDate = "2026年12月5日印发";
		ImprintDateMode = RedHeaderImprintDateMode.Manual;
		UseTimesNewRomanForNumbers = false;
		ImprintDateUseTimesNewRoman = false;
	}
}
