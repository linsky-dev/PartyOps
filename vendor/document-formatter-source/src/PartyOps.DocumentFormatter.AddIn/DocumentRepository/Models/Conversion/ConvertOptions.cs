using System;

namespace DocumentRepository.Models.Conversion;

[Serializable]
public class ConvertOptions
{
	public ConvertFormat SelectedFormat { get; set; }

	public ConvertSaveLocation SaveLocation { get; set; }

	public string CustomOutputFolder { get; set; }

	public ConvertSameNamePolicy SameNamePolicy { get; set; }

	public bool OpenFolderAfterConvert { get; set; }

	public ImageExportMode ImageExportMode { get; set; }

	public PageSelectionMode ImagePageSelectionMode { get; set; }

	public string ImagePageRange { get; set; }

	public string ImageSelectedPages { get; set; }

	public ImageFileFormat ImageFormat { get; set; }

	public int ImageDpi { get; set; }

	public DocxConvertMode DocxMode { get; set; }

	public PdfToWordEngine PdfToWordEngine { get; set; }

	public bool PdfNormalizeChinesePunctuation { get; set; }

	public bool TxtRemoveExtraBlankLines { get; set; }

	public ConvertOptions()
	{
		SelectedFormat = ConvertFormat.Pdf;
		SaveLocation = ConvertSaveLocation.SourceFolder;
		CustomOutputFolder = "";
		SameNamePolicy = ConvertSameNamePolicy.Ask;
		OpenFolderAfterConvert = true;
		ImageExportMode = ImageExportMode.SingleImages;
		ImagePageSelectionMode = PageSelectionMode.All;
		ImagePageRange = "";
		ImageSelectedPages = "";
		ImageFormat = ImageFileFormat.Png;
		ImageDpi = 200;
		DocxMode = DocxConvertMode.SaveAsNewFile;
		PdfToWordEngine = PdfToWordEngine.Local;
		PdfNormalizeChinesePunctuation = false;
		TxtRemoveExtraBlankLines = false;
	}
}
