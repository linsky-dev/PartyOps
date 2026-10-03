using System;

namespace DocumentRepository.Services.Conversion.PdfToWord;

internal readonly struct ReadingOrderKey : IComparable<ReadingOrderKey>
{
	public int PageIndex { get; }

	public float AnchorY { get; }

	public int RegionTypeOrdinal { get; }

	public int ColumnOrdinal { get; }

	public int BlockOrdinalInColumn { get; }

	public int OriginalSequence { get; }

	public ReadingOrderKey(int pageIndex, float anchorY, int regionTypeOrdinal, int columnOrdinal, int blockOrdinalInColumn, int originalSequence)
	{
		PageIndex = pageIndex;
		AnchorY = anchorY;
		RegionTypeOrdinal = regionTypeOrdinal;
		ColumnOrdinal = columnOrdinal;
		BlockOrdinalInColumn = blockOrdinalInColumn;
		OriginalSequence = originalSequence;
	}

	public int CompareTo(ReadingOrderKey other)
	{
		int num = PageIndex.CompareTo(other.PageIndex);
		if (num != 0)
		{
			return num;
		}
		num = other.AnchorY.CompareTo(AnchorY);
		if (num == 0)
		{
			num = RegionTypeOrdinal.CompareTo(other.RegionTypeOrdinal);
			if (num == 0)
			{
				num = ColumnOrdinal.CompareTo(other.ColumnOrdinal);
				if (num != 0)
				{
					return num;
				}
				num = BlockOrdinalInColumn.CompareTo(other.BlockOrdinalInColumn);
				if (num != 0)
				{
					return num;
				}
				return OriginalSequence.CompareTo(other.OriginalSequence);
			}
			return num;
		}
		return num;
	}

	int IComparable<ReadingOrderKey>.CompareTo(ReadingOrderKey other)
	{
		return this.CompareTo(other);
	}
}
