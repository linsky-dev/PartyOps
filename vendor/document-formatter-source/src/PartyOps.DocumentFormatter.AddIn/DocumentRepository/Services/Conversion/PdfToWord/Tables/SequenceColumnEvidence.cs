namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public struct SequenceColumnEvidence
{
	public static readonly SequenceColumnEvidence None = new SequenceColumnEvidence
	{
		ColumnIndex = -1,
		FirstDataValue = -1,
		LastDataValue = -1
	};

	public int ColumnIndex { get; set; }

	public int FirstDataValue { get; set; }

	public int LastDataValue { get; set; }

	public bool Ambiguous { get; set; }

	public bool IsReliable
	{
		get
		{
			if (ColumnIndex >= 0)
			{
				return !Ambiguous;
			}
			return false;
		}
	}
}
