namespace DocumentRepository.Models.Mutations;

public sealed class MutationPlanExecutionResult
{
	public int AppliedCount { get; internal set; }

	public bool Verified { get; internal set; }

	public VerificationReceipt Receipt { get; internal set; }
}
