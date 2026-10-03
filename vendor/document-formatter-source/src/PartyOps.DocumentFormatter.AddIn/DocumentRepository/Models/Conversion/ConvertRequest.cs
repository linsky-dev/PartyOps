using System;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Models.Conversion;

public class ConvertRequest
{
	public OperationContext Context { get; set; }

	public ConvertOptions Options { get; set; }

	public TaskRuntimeContext Task { get; set; }

	public Func<string, ConvertConflictDecision> ConflictResolver { get; set; }
}
