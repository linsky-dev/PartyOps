namespace DocumentRepository.Services.Hosting;

public sealed class HostCapabilities
{
	public DocumentHostKind Kind { get; internal set; }

	public string ProductName { get; internal set; }

	public string Version { get; internal set; }

	public bool SupportsScreenUpdating { get; internal set; }

	public bool SupportsDisplayAlerts { get; internal set; }

	public bool SupportsEnableEvents { get; internal set; }

	public bool SupportsUndoRecord { get; internal set; }

	public bool SupportsStableRangePageCoordinates { get; internal set; }

	public bool SupportsNativeFixedFormatExport { get; internal set; }

	public bool RequiresExplicitParagraphSpacing { get; internal set; }
}
