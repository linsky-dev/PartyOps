using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class PdfLineFrame
{
	public PdfLineFrameOrientation Orientation { get; set; }

	public float StartX { get; set; }

	public float StartY { get; set; }

	public float EndX { get; set; }

	public float EndY { get; set; }

	public float LineWidth { get; set; }

	public bool IsFilledRect { get; set; }

	public int PageIndex { get; set; }

	public float Length
	{
		get
		{
			if (Orientation != PdfLineFrameOrientation.Horizontal)
			{
				return EndY - StartY;
			}
			return EndX - StartX;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString()
	{
		if (Orientation != PdfLineFrameOrientation.Horizontal)
		{
			return $"V x={StartX:F1} y=[{StartY:F1},{EndY:F1}]";
		}
		return $"H y={StartY:F1} x=[{StartX:F1},{EndX:F1}]";
	}
}
