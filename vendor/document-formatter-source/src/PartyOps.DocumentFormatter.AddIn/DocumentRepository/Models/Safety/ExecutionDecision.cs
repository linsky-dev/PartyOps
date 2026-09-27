namespace DocumentRepository.Models.Safety;

public sealed class ExecutionDecision
{
	public ExecutionDecisionOutcome Outcome { get; private set; }

	public ExecutionDecisionReasonCode ReasonCode { get; private set; }

	public ExecutionDecisionSource Source { get; private set; }

	public string PolicyId { get; private set; }

	public string MessageKey { get; private set; }

	private ExecutionDecision(ExecutionDecisionOutcome outcome, ExecutionDecisionReasonCode reasonCode, ExecutionDecisionSource source, string policyId, string messageKey)
	{
		Outcome = outcome;
		ReasonCode = reasonCode;
		Source = source;
		PolicyId = policyId;
		MessageKey = messageKey;
	}

	public static ExecutionDecision Allow(ExecutionDecisionReasonCode reasonCode, ExecutionDecisionSource source, string policyId = null)
	{
		return new ExecutionDecision(ExecutionDecisionOutcome.Allow, reasonCode, source, policyId, null);
	}

	public static ExecutionDecision Deny(ExecutionDecisionReasonCode reasonCode, ExecutionDecisionSource source, string policyId = null, string messageKey = null)
	{
		return new ExecutionDecision(ExecutionDecisionOutcome.Deny, reasonCode, source, policyId, messageKey);
	}

	public static ExecutionDecision RequireConfirmation(ExecutionDecisionReasonCode reasonCode, ExecutionDecisionSource source, string policyId = null, string messageKey = null)
	{
		return new ExecutionDecision(ExecutionDecisionOutcome.RequireConfirmation, reasonCode, source, policyId, messageKey);
	}
}
