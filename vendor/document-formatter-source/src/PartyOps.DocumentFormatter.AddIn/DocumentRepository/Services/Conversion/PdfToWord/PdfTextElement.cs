using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class PdfTextElement
{
	public string Text { get; set; }

	public float X { get; set; }

	public float Y { get; set; }

	public float Width { get; set; }

	public float Height { get; set; }

	public float Baseline { get; set; }

	public float FontSize { get; set; }

	public string FontName { get; set; }

	public bool Bold { get; set; }

	public bool Italic { get; set; }

	public int PageIndex { get; set; }

	public float CenterX => X + Width / 2f;

	public PdfTextElement()
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public PdfTextElement(PdfTextElement other)
	{
		if (other == null)
		{
			throw new ArgumentNullException("other");
		}
		Text = other.Text;
		X = other.X;
		Y = other.Y;
		Width = other.Width;
		Height = other.Height;
		Baseline = other.Baseline;
		FontSize = other.FontSize;
		FontName = other.FontName;
		Bold = other.Bold;
		Italic = other.Italic;
		PageIndex = other.PageIndex;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString()
	{
		return $"'{Text}' x={X:F2} base={Baseline:F2} size={FontSize:F2} font={FontName ?? string.Empty}";
	}
}
