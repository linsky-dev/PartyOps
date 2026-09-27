namespace DocumentRepository;

public sealed class OrphanFixResult
{
	public int Detected { get; internal set; }

	public int Fixed { get; internal set; }

	public int Unresolved { get; internal set; }
}
