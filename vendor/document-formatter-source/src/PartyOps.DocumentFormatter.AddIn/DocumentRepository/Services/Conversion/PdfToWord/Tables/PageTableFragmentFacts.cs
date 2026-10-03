using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class PageTableFragmentFacts
{
	public int PageIndex { get; }

	public float Width { get; }

	public float Height { get; }

	public int Rotation { get; }

	public IReadOnlyList<TableFragmentFacts> Fragments { get; }

	public PageTableFragmentFacts(int pageIndex, float width, float height, int rotation, IEnumerable<TableFragmentFacts> fragments)
	{
		PageIndex = pageIndex;
		Width = width;
		Height = height;
		Rotation = rotation;
		Fragments = new ReadOnlyCollection<TableFragmentFacts>((fragments ?? Enumerable.Empty<TableFragmentFacts>()).ToList());
	}
}
