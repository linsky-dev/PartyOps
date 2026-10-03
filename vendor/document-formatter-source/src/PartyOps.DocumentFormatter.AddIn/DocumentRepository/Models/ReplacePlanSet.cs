using System.Collections.Generic;

namespace DocumentRepository.Models;

public class ReplacePlanSet
{
	public int SchemaVersion { get; set; }

	public string ActivePlanId { get; set; }

	public List<ReplacePlan> Plans { get; set; }

	public ReplacePlanSet()
	{
		Plans = new List<ReplacePlan>();
	}
}
