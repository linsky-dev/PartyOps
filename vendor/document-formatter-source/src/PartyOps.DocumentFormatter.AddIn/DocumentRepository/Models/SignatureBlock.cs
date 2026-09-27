using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DocumentRepository.Models;

public class SignatureBlock
{
	private readonly List<SignatureLine> signatureLines = new List<SignatureLine>();

	private readonly ReadOnlyCollection<SignatureLine> readOnlySignatureLines;

	public int SignatureParagraphIndex { get; set; }

	public int DateParagraphIndex { get; set; }

	public int SignatureRangeStart { get; set; }

	public int SignatureRangeEnd { get; set; }

	public int DateRangeStart { get; set; }

	public int DateRangeEnd { get; set; }

	public string SignatureText { get; set; }

	public string DateText { get; set; }

	public bool IsSelection { get; set; }

	public IList<SignatureLine> SignatureLines => readOnlySignatureLines;

	public bool IsValid
	{
		get
		{
			if (SignatureParagraphIndex > 0)
			{
				return DateParagraphIndex > 0;
			}
			return false;
		}
	}

	public SignatureBlock()
	{
		readOnlySignatureLines = signatureLines.AsReadOnly();
	}

	internal void SetSignatureLines(IEnumerable<SignatureLine> lines)
	{
		signatureLines.Clear();
		if (lines == null)
		{
			return;
		}
		foreach (SignatureLine line in lines)
		{
			if (line != null)
			{
				signatureLines.Add(line);
			}
		}
	}
}
