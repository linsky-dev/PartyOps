using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public struct CrossPageTableId : IEquatable<CrossPageTableId>
{
	public int PageIndex { get; }

	public int TableOrdinal { get; }

	public CrossPageTableId(int pageIndex, int tableOrdinal)
	{
		PageIndex = pageIndex;
		TableOrdinal = tableOrdinal;
	}

	public bool Equals(CrossPageTableId other)
	{
		if (PageIndex == other.PageIndex)
		{
			return TableOrdinal == other.TableOrdinal;
		}
		return false;
	}

	bool IEquatable<CrossPageTableId>.Equals(CrossPageTableId other)
	{
		return this.Equals(other);
	}

	public override bool Equals(object obj)
	{
		if (obj is CrossPageTableId other)
		{
			return Equals(other);
		}
		return false;
	}

	public override int GetHashCode()
	{
		return (PageIndex * 397) ^ TableOrdinal;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString()
	{
		return "p" + (PageIndex + 1) + "t" + TableOrdinal;
	}
}
