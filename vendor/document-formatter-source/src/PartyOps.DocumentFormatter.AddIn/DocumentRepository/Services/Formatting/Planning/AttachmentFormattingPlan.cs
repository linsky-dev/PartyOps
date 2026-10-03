using System.Collections.Generic;
using DocumentRepository.Models;

namespace DocumentRepository.Services.Formatting.Planning;

internal sealed class AttachmentFormattingPlan
{
	public IList<DocumentElement> BodyMarkersDescending { get; } = new List<DocumentElement>();

	public IList<DocumentElement> ListParagraphsDescending { get; } = new List<DocumentElement>();
}
