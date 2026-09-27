using System.Collections.Generic;

namespace DocumentRepository;

public class RenameRuleSet
{
	public string ActiveRuleId { get; set; }

	public List<RenameRule> Rules { get; set; }

	public RenameRuleSet()
	{
		Rules = new List<RenameRule>();
	}
}
