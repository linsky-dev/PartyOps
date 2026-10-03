using System.Collections.Generic;

namespace DocumentRepository.Models.Snapshots;

public sealed class DocumentSnapshot
{
	public string SnapshotId { get; set; }

	public string DocumentIdentity { get; set; }

	public bool WasSaved { get; set; }

	public int ScopeStart { get; set; }

	public int ScopeEnd { get; set; }

	public string ContentFingerprint { get; set; }

	public string StateFingerprint { get; set; }

	public string RecoveryFingerprint { get; set; }

	public int ParagraphCount { get; set; }

	public List<ParagraphSnapshot> Paragraphs { get; } = new List<ParagraphSnapshot>();

	public ProtectedObjectSnapshot ProtectedObjects { get; set; } = new ProtectedObjectSnapshot();

	public List<CustomDocumentPropertySnapshot> CustomDocumentProperties { get; } = new List<CustomDocumentPropertySnapshot>();

	public DocumentSnapshot DeepCopy()
	{
		DocumentSnapshot documentSnapshot = new DocumentSnapshot
		{
			SnapshotId = SnapshotId,
			DocumentIdentity = DocumentIdentity,
			WasSaved = WasSaved,
			ScopeStart = ScopeStart,
			ScopeEnd = ScopeEnd,
			ContentFingerprint = ContentFingerprint,
			StateFingerprint = StateFingerprint,
			RecoveryFingerprint = RecoveryFingerprint,
			ParagraphCount = ParagraphCount,
			ProtectedObjects = ((ProtectedObjects == null) ? new ProtectedObjectSnapshot() : ProtectedObjects.DeepCopy())
		};
		foreach (ParagraphSnapshot paragraph in Paragraphs)
		{
			documentSnapshot.Paragraphs.Add(paragraph?.DeepCopy());
		}
		foreach (CustomDocumentPropertySnapshot customDocumentProperty in CustomDocumentProperties)
		{
			documentSnapshot.CustomDocumentProperties.Add(customDocumentProperty?.DeepCopy());
		}
		return documentSnapshot;
	}
}
