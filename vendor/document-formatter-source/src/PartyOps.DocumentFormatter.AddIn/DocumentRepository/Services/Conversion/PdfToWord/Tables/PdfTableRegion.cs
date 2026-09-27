using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class PdfTableRegion
{
	public int PageIndex { get; }

	public float LeftX { get; }

	public float RightX { get; }

	public float TopY { get; }

	public float BottomY { get; }

	public float Width => RightX - LeftX;

	public float Height => TopY - BottomY;

	public PdfTableRegion(int pageIndex, float leftX, float rightX, float topY, float bottomY)
	{
		PageIndex = pageIndex;
		LeftX = leftX;
		RightX = rightX;
		TopY = topY;
		BottomY = bottomY;
	}

	public bool Contains(float x, float y, float tolerance)
	{
		if (x >= LeftX - tolerance && !(x > RightX + tolerance) && !(y < BottomY - tolerance))
		{
			return y <= TopY + tolerance;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString()
	{
		return $"page={PageIndex} x=[{LeftX:F1},{RightX:F1}] y=[{BottomY:F1},{TopY:F1}]";
	}
}
