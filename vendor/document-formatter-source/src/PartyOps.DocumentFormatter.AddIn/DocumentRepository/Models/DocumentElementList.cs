using System.Collections.Generic;
using System.Linq;

namespace DocumentRepository.Models;

public class DocumentElementList
{
	public List<DocumentElement> Items { get; private set; } = new List<DocumentElement>();

	public int ParagraphCount { get; set; }

	public bool HasTables { get; set; }

	public bool HasImages { get; set; }

	public bool HasAttachments { get; set; }

	public bool HasHyperlinks { get; set; }

	public bool HasEnglishNumbers { get; set; }

	public bool HasKeywordCandidates { get; set; }

	public bool HasSemicolonCandidates { get; set; }

	public bool HasTabs { get; set; }

	public bool HasOrphanCandidates { get; set; }

	public bool HasType(ElementType type)
	{
		return Items.Any((DocumentElement item) => item != null && item.Type == type);
	}
}
