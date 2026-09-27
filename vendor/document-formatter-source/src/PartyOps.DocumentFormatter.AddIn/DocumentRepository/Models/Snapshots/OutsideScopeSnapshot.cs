namespace DocumentRepository.Models.Snapshots;

public sealed class OutsideScopeSnapshot
{
	public int PrefixStart { get; set; }

	public int PrefixEnd { get; set; }

	public int SuffixStart { get; set; }

	public int SuffixEnd { get; set; }

	public string PrefixStateFingerprint { get; set; }

	public string SuffixStateFingerprint { get; set; }
}
