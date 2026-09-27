using System.Collections.Generic;
using System.Linq;

namespace DocumentRepository.Models.Rules;

public class RuleValidationResult
{
	public List<RuleValidationIssue> Issues { get; private set; }

	public bool Success => !Issues.Any((RuleValidationIssue i) => i.Severity == RuleValidationSeverity.Error);

	public RuleValidationResult()
	{
		Issues = new List<RuleValidationIssue>();
	}

	public void Add(RuleValidationIssue issue)
	{
		if (issue != null)
		{
			Issues.Add(issue);
		}
	}
}
