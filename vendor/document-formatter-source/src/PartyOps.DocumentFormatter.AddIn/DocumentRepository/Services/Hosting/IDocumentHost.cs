using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Hosting;

public interface IDocumentHost
{
	Application Application { get; }

	Document Document { get; }

	HostCapabilities Capabilities { get; }

	bool TryReadScreenUpdating(out bool value);

	void SetScreenUpdating(bool value);

	bool TryReadDisplayAlerts(out WdAlertLevel value);

	void SetDisplayAlerts(WdAlertLevel value);

	bool TryReadEnableEvents(out bool value);

	void SetEnableEvents(bool value);

	void StartUndoRecord(string name);

	void EndUndoRecord();

	void UndoLastRecord();

	SelectionCheckpoint CaptureSelection();

	void RestoreSelection(SelectionCheckpoint checkpoint);

	void MoveSelectionToDocumentStart();

	void RefreshVisibleLayout();
}
