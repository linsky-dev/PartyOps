using System.Collections.Generic;

namespace DocumentRepository.Models;

public sealed class RenameTitleEvidence
{
	public string Title { get; set; }

	public RenameTitleRecognitionTier Tier { get; set; }

	public float Confidence { get; set; }

	public List<int> ParagraphIndexes { get; } = new List<int>();
}
