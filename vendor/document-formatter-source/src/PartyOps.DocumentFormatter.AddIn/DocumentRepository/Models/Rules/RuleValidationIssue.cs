namespace DocumentRepository.Models.Rules;

public class RuleValidationIssue
{
	public RuleValidationSeverity Severity { get; set; }

	public string RuleId { get; set; }

	public string Message { get; set; }

	public static RuleValidationIssue Error(string ruleId, string message)
	{
		return new RuleValidationIssue
		{
			Severity = RuleValidationSeverity.Error,
			RuleId = ruleId,
			Message = message
		};
	}

	public static RuleValidationIssue Warning(string ruleId, string message)
	{
		return new RuleValidationIssue
		{
			Severity = RuleValidationSeverity.Warning,
			RuleId = ruleId,
			Message = message
		};
	}
}
