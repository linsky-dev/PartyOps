namespace PartyOps.DocumentFormatter.AddInRepair;

internal sealed class DiagnosticItem
{
	public DiagnosticSeverity Severity { get; set; }

	public string Title { get; set; }

	public string Detail { get; set; }

	public bool Repairable { get; set; }
}
