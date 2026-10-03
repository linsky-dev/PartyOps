using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Models.Features;

public interface IFeatureUiService
{
	bool Confirm(string caption, string message);

	void ShowMessage(string caption, string message, FeatureMessageKind kind);

	void ShowTimedMessage(string caption, string message, int milliseconds);

	void ShowCompletionMessage(string caption, string summary, string detail, int milliseconds);

	ITaskProgressReporter CreateProgress(string featureId, string title);

	string SelectPdfForConversion(string initialDirectory);

	ConvertConflictDecision ResolveConvertConflict(string targetPath);

	void OpenFolder(string folderPath);

	CompilationConfirmationResult ConfirmCompilation(CompilationConfirmationRequest request);
}
