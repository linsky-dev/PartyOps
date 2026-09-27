using UglyToad.PdfPig.Content;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class PdfImage
{
	public int PageIndex { get; set; }

	public float X { get; set; }

	public float Y { get; set; }

	public float Width { get; set; }

	public float Height { get; set; }

	public byte[] Bytes { get; set; }

	public string Format { get; set; }

	public int PixelWidth { get; set; }

	public int PixelHeight { get; set; }

	public int ComponentsPerPixel { get; set; }

	public int BitsPerComponent { get; set; }

	public string ColorSpaceName { get; set; }

	public byte[] ResolvedBytes { get; set; }

	public string ResolvedExtension { get; set; }

	public PdfImageResolutionKind Resolution { get; set; }

	internal IPdfImage Source { get; set; }
}
