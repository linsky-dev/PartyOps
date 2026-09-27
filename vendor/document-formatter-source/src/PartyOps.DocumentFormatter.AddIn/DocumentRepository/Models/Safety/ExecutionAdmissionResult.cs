using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Safety;

public sealed class ExecutionAdmissionResult
{
	public IReadOnlyList<ExecutionDecision> Decisions { get; private set; }

	public ExecutionDecisionOutcome FinalOutcome { get; private set; }

	public ExecutionDecision Primary { get; private set; }

	private ExecutionAdmissionResult(IReadOnlyList<ExecutionDecision> decisions, ExecutionDecisionOutcome finalOutcome, ExecutionDecision primary)
	{
		Decisions = decisions;
		FinalOutcome = finalOutcome;
		Primary = primary;
	}

	public static ExecutionAdmissionResult Aggregate(IEnumerable<ExecutionDecision> decisions)
	{
		List<ExecutionDecision> list = (decisions ?? Enumerable.Empty<ExecutionDecision>()).Where((ExecutionDecision d) => d != null).ToList();
		if (list.Count == 0)
		{
			ExecutionDecision executionDecision = ExecutionDecision.Deny(ExecutionDecisionReasonCode.NoDecisionEvidence, ExecutionDecisionSource.Host);
			return new ExecutionAdmissionResult(new ExecutionDecision[1] { executionDecision }, ExecutionDecisionOutcome.Deny, executionDecision);
		}
		ExecutionDecision executionDecision2 = list.FirstOrDefault((ExecutionDecision d) => d.Outcome == ExecutionDecisionOutcome.Deny) ?? list.FirstOrDefault((ExecutionDecision d) => d.Outcome == ExecutionDecisionOutcome.RequireConfirmation) ?? list[0];
		return new ExecutionAdmissionResult(list, executionDecision2.Outcome, executionDecision2);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ExecutionAdmissionResult FromSingle(ExecutionDecision decision)
	{
		if (decision == null)
		{
			throw new ArgumentNullException("decision");
		}
		return new ExecutionAdmissionResult(new ExecutionDecision[1] { decision }, decision.Outcome, decision);
	}
}
