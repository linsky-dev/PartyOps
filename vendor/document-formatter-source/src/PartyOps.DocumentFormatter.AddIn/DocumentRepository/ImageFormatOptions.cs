using System;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace DocumentRepository;

[Serializable]
public class ImageFormatOptions
{
	[XmlElement("FitPageWidth")]
	public bool? LegacyFitPageWidth { get; set; }

	[XmlElement("CenterAlign")]
	public bool? LegacyCenterAlign { get; set; }

	[XmlElement("ConvertFloatingToInline")]
	public bool? LegacyConvertFloatingToInline { get; set; }

	[XmlElement("UseBorder")]
	public bool? LegacyUseBorder { get; set; }

	[XmlElement("WrapType")]
	public string LegacyWrapType { get; set; }

	public int OptionsVersion { get; set; }

	public string SizeMode { get; set; }

	public string WrapMode { get; set; }

	public string AlignmentMode { get; set; }

	public string BorderMode { get; set; }

	public bool KeepAspectRatio { get; set; }

	public bool MainStoryOnly { get; set; }

	public bool IncludeTableCellImages { get; set; }

	public bool IncludeLinkedPictures { get; set; }

	public string ParagraphFilterMode { get; set; }

	public float MinimumWidthCm { get; set; }

	public float MinimumHeightCm { get; set; }

	public float WidthCm { get; set; }

	public float HeightCm { get; set; }

	public float MaxWidthCm { get; set; }

	public float MaxHeightCm { get; set; }

	public float ScalePercent { get; set; }

	public bool ApplyWrapDistances { get; set; }

	public float DistanceTopCm { get; set; }

	public float DistanceBottomCm { get; set; }

	public float DistanceLeftCm { get; set; }

	public float DistanceRightCm { get; set; }

	public bool ApplyRotation { get; set; }

	public float RotationDegrees { get; set; }

	public bool FormatExistingCaptions { get; set; }

	public string CaptionFontName { get; set; }

	public string CaptionFontSize { get; set; }

	public bool CaptionBold { get; set; }

	public int CaptionSpaceBefore { get; set; }

	public int CaptionSpaceAfter { get; set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ImageFormatOptions()
	{
		OptionsVersion = 2;
		SizeMode = "ShrinkToFit";
		WrapMode = "Preserve";
		AlignmentMode = "Preserve";
		BorderMode = "Preserve";
		KeepAspectRatio = true;
		MainStoryOnly = true;
		IncludeTableCellImages = true;
		IncludeLinkedPictures = true;
		ParagraphFilterMode = "All";
		MinimumWidthCm = 0f;
		MinimumHeightCm = 0f;
		WidthCm = 14.5f;
		HeightCm = 20f;
		MaxWidthCm = 14.5f;
		MaxHeightCm = 20f;
		ScalePercent = 100f;
		ApplyWrapDistances = false;
		DistanceTopCm = 0f;
		DistanceBottomCm = 0f;
		DistanceLeftCm = 0.32f;
		DistanceRightCm = 0.32f;
		ApplyRotation = false;
		RotationDegrees = 0f;
		FormatExistingCaptions = false;
		CaptionFontName = "宋体";
		CaptionFontSize = "小四";
		CaptionBold = false;
		CaptionSpaceBefore = 0;
		CaptionSpaceAfter = 6;
	}

	public bool ShouldSerializeLegacyFitPageWidth()
	{
		return false;
	}

	public bool ShouldSerializeLegacyCenterAlign()
	{
		return false;
	}

	public bool ShouldSerializeLegacyConvertFloatingToInline()
	{
		return false;
	}

	public bool ShouldSerializeLegacyUseBorder()
	{
		return false;
	}

	public bool ShouldSerializeLegacyWrapType()
	{
		return false;
	}
}
