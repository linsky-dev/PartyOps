using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Formatting;

public static class DocumentGridMetrics
{
	private const double PointsPerCentimeter = 28.346456692913385;

	private const double WordPointStep = 0.05;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static float CalculateVerticalPitchPoints(float pageHeightPoints, float topMarginPoints, float bottomMarginPoints, int linesPerPage)
	{
		if (!(pageHeightPoints <= 0f))
		{
			if (!(topMarginPoints < 0f))
			{
				if (bottomMarginPoints < 0f)
				{
					throw new ArgumentOutOfRangeException("bottomMarginPoints");
				}
				if (linesPerPage < 1 || linesPerPage > 50)
				{
					throw new ArgumentOutOfRangeException("linesPerPage");
				}
				double num = pageHeightPoints - topMarginPoints - bottomMarginPoints;
				if (num <= 0.0)
				{
					throw new ArgumentException("页面高度必须大于上下页边距之和。");
				}
				return (float)(Math.Floor((num / (double)linesPerPage + 1E-06) / 0.05) * 0.05);
			}
			throw new ArgumentOutOfRangeException("topMarginPoints");
		}
		throw new ArgumentOutOfRangeException("pageHeightPoints");
	}

	public static float CalculateVerticalPitchFromCentimeters(float pageHeightCm, float topMarginCm, float bottomMarginCm, int linesPerPage)
	{
		return CalculateVerticalPitchPoints((float)((double)pageHeightCm * 28.346456692913385), (float)((double)topMarginCm * 28.346456692913385), (float)((double)bottomMarginCm * 28.346456692913385), linesPerPage);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static float CalculateHorizontalPitchPoints(float pageWidthPoints, float leftMarginPoints, float rightMarginPoints, int charsPerLine)
	{
		if (pageWidthPoints <= 0f)
		{
			throw new ArgumentOutOfRangeException("pageWidthPoints");
		}
		if (leftMarginPoints < 0f)
		{
			throw new ArgumentOutOfRangeException("leftMarginPoints");
		}
		if (rightMarginPoints >= 0f)
		{
			if (charsPerLine < 1 || charsPerLine > 50)
			{
				throw new ArgumentOutOfRangeException("charsPerLine");
			}
			double num = pageWidthPoints - leftMarginPoints - rightMarginPoints;
			if (num <= 0.0)
			{
				throw new ArgumentException("页面宽度必须大于左右页边距之和。");
			}
			return (float)(num / (double)charsPerLine);
		}
		throw new ArgumentOutOfRangeException("rightMarginPoints");
	}
}
