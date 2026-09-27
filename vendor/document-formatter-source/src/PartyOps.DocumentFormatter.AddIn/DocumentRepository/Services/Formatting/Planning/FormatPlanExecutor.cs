using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.FormattingPlan;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Mutations;
using DocumentRepository.Services.Performance;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Planning;

public static class FormatPlanExecutor
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static MutationPlanExecutionResult Execute(FormatExecutionPlan plan, Application application, Document document, Microsoft.Office.Interop.Word.Range scopeRange, DocumentSession session, ITaskProgressReporter progress, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (plan != null)
		{
			MutationExecutionContext mutationExecutionContext = new MutationExecutionContext
			{
				Application = application,
				Document = document,
				ScopeRange = scopeRange,
				Session = session
			};
			mutationExecutionContext.Items["format.plan"] = plan;
			if (progress != null)
			{
				mutationExecutionContext.Items["format.progress"] = progress;
			}
			if (diagnostics != null)
			{
				mutationExecutionContext.Items["format.diagnostics"] = diagnostics;
			}
			return MutationPlanExecutor.Execute(plan, mutationExecutionContext, new FormatPlanVerifier());
		}
		throw new ArgumentNullException("plan");
	}
}
