namespace DocumentRepository.Models.Mutations;

public sealed class MutationTarget
{
	public int Start { get; set; }

	public int End { get; set; }

	public string ExpectedFingerprint { get; set; }

	public bool Overlaps(MutationTarget other)
	{
		if (other == null)
		{
			return false;
		}
		if (Start < other.End)
		{
			return other.Start < End;
		}
		return false;
	}
}
