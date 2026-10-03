using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace DocumentRepository;

[Serializable]
public class FormatConfig
{
	public float TopMargin { get; set; }

	public float BottomMargin { get; set; }

	public float LeftMargin { get; set; }

	public float RightMargin { get; set; }

	public float HeaderDistance { get; set; }

	public float FooterDistance { get; set; }

	public bool EnableDocumentGrid { get; set; }

	public DocumentGridOptions DocumentGridOptions { get; set; }

	public TextStyle MainTitle { get; set; }

	public TextStyle Level1 { get; set; }

	public TextStyle Level2 { get; set; }

	public TextStyle Level3 { get; set; }

	public TextStyle Body { get; set; }

	public int SpacingUnitVersion { get; set; }

	public int YiShiMode { get; set; }

	public int YiYaoMode { get; set; }

	public int DiYiMode { get; set; }

	public PageAlignType PageAlign { get; set; }

	public string PageFontSize { get; set; }

	public string PageLeftWing { get; set; }

	public string PageRightWing { get; set; }

	public string PageNumberFontName { get; set; }

	public bool PageNumberBold { get; set; }

	public string PageNumberMode { get; set; }

	public bool EnablePageNumbers { get; set; }

	public string EnglishNumberFontName { get; set; }

	public bool EnableEnglishFont { get; set; }

	public bool DeleteAiSymbols { get; set; }

	public bool DeleteSpaces { get; set; }

	public bool ClearHeadersFooters { get; set; }

	public bool RemoveHyperlinks { get; set; }

	public bool EnableFixSemicolons { get; set; }

	public bool EnableSignatureFormatting { get; set; }

	public bool EnableSignatureWithSeal { get; set; }

	public SignatureFormatOptions SignatureOptions { get; set; }

	public bool EnableOrphanCharFix { get; set; }

	public bool EnableAttachmentFormatting { get; set; }

	public bool EnableTableFormatting { get; set; }

	public bool EnableImageFormatting { get; set; }

	public AttachmentFormatOptions AttachmentOptions { get; set; }

	public TableFormatOptions TableOptions { get; set; }

	public ImageFormatOptions ImageOptions { get; set; }

	public bool EnableCompilationFormatting { get; set; }

	public CompilationFormatOptions CompilationFormatOptions { get; set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public FormatConfig()
	{
		TopMargin = 3.7f;
		BottomMargin = 3.5f;
		LeftMargin = 2.8f;
		RightMargin = 2.6f;
		HeaderDistance = 1.5f;
		FooterDistance = 2.5f;
		EnableDocumentGrid = false;
		DocumentGridOptions = new DocumentGridOptions();
		MainTitle = new TextStyle
		{
			FontName = "方正小标宋简体",
			FontSize = "二号",
			Bold = false,
			LineSpacing = "32",
			FirstLineIndent = "0",
			SpaceAfter = 28,
			RecognitionStyle = "规范标题",
			Alignment = "居中",
			OutlineLevel = "1级"
		};
		Level1 = new TextStyle
		{
			FontName = "黑体",
			FontSize = "三号",
			Bold = false,
			LineSpacing = "28",
			RecognitionStyle = "一、XX",
			Alignment = "两端对齐",
			OutlineLevel = "2级"
		};
		Level2 = new TextStyle
		{
			FontName = "楷体_GB2312",
			FontSize = "三号",
			Bold = false,
			LineSpacing = "28",
			RecognitionStyle = "（一）XX",
			Alignment = "两端对齐",
			OutlineLevel = "3级"
		};
		Level3 = new TextStyle
		{
			FontName = "仿宋_GB2312",
			FontSize = "三号",
			Bold = true,
			LineSpacing = "28",
			RecognitionStyle = "1.XX",
			Alignment = "两端对齐",
			OutlineLevel = "4级"
		};
		Body = new TextStyle
		{
			FontName = "仿宋_GB2312",
			FontSize = "三号",
			Bold = false,
			LineSpacing = "28",
			RecognitionStyle = "",
			Alignment = "两端对齐",
			OutlineLevel = "正文文本"
		};
		SpacingUnitVersion = 2;
		YiShiMode = 2;
		YiYaoMode = 2;
		DiYiMode = 0;
		PageAlign = PageAlignType.OddEvenDifferent;
		PageFontSize = "四号";
		PageLeftWing = "— ";
		PageRightWing = " —";
		PageNumberFontName = "宋体";
		PageNumberBold = false;
		PageNumberMode = "";
		EnablePageNumbers = true;
		EnglishNumberFontName = "Times New Roman";
		EnableEnglishFont = true;
		DeleteAiSymbols = true;
		DeleteSpaces = true;
		ClearHeadersFooters = false;
		RemoveHyperlinks = false;
		EnableFixSemicolons = false;
		EnableSignatureFormatting = true;
		EnableSignatureWithSeal = true;
		SignatureOptions = new SignatureFormatOptions();
		EnableOrphanCharFix = false;
		EnableAttachmentFormatting = true;
		EnableTableFormatting = false;
		EnableImageFormatting = false;
		AttachmentOptions = new AttachmentFormatOptions();
		TableOptions = new TableFormatOptions();
		ImageOptions = new ImageFormatOptions();
		EnableCompilationFormatting = false;
		CompilationFormatOptions = new CompilationFormatOptions();
	}

	public FormatConfig DeepClone()
	{
		using MemoryStream memoryStream = new MemoryStream();
		XmlSerializer val = new XmlSerializer(typeof(FormatConfig));
		val.Serialize((Stream)memoryStream, (object)this);
		memoryStream.Position = 0L;
		return (FormatConfig)val.Deserialize((Stream)memoryStream);
	}
}
