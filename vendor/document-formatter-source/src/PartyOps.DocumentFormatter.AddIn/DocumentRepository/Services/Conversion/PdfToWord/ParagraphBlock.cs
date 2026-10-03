using System.Collections.Generic;
using System.Text;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class ParagraphBlock : DocumentBlock
{
	public IList<TextRun> Runs { get; private set; }

	public float SpaceBefore { get; set; }

	public ParagraphAlignment Alignment { get; set; }

	public int FirstLineIndentChars { get; set; }

	internal float LastLineStartX { get; set; }

	internal float LastLineEndX { get; set; }

	internal float LastLineFontSize { get; set; }

	internal float FirstLineFontSize { get; set; }

	internal float LeftEdgeX { get; set; }

	internal float RightEdgeX { get; set; }

	internal int LastPageIndex { get; set; }

	public string PlainText
	{
		get
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (TextRun run in Runs)
			{
				if (run.Text != null)
				{
					stringBuilder.Append(run.Text);
				}
			}
			return stringBuilder.ToString();
		}
	}

	public ParagraphBlock()
	{
		Runs = new List<TextRun>();
	}
}
