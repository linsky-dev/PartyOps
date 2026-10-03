using System;

namespace DocumentRepository.Models.Snapshots;

public sealed class DocumentSnapshotMismatchException : Exception
{
	public DocumentSnapshotMismatchKind Kind { get; private set; }

	internal DocumentSnapshotMismatchException(DocumentSnapshotMismatchKind kind, string diagnosticMessage)
		: base(diagnosticMessage)
	{
		Kind = kind;
	}
}
