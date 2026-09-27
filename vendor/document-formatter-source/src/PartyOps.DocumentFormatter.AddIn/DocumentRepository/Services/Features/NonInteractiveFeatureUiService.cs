using System;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.Features;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Services.Features;

public sealed class NonInteractiveFeatureUiService : IFeatureUiService
{
	private sealed class NullTaskProgressReporter : ITaskProgressReporter, IDisposable
	{
		public bool CancellationRequested => false;

		public void Start(string taskName, int totalSteps, string message)
		{
		}

		void ITaskProgressReporter.Start(string taskName, int totalSteps, string message)
		{
			this.Start(taskName, totalSteps, message);
		}

		public void Report(TaskProgressInfo info)
		{
		}

		void ITaskProgressReporter.Report(TaskProgressInfo info)
		{
			this.Report(info);
		}

		public void Complete(string message)
		{
		}

		void ITaskProgressReporter.Complete(string message)
		{
			this.Complete(message);
		}

		public void CompleteWithWarnings(string message)
		{
		}

		void ITaskProgressReporter.CompleteWithWarnings(string message)
		{
			this.CompleteWithWarnings(message);
		}

		public void Cancel(string message)
		{
		}

		void ITaskProgressReporter.Cancel(string message)
		{
			this.Cancel(message);
		}

		public void Fail(string message)
		{
		}

		void ITaskProgressReporter.Fail(string message)
		{
			this.Fail(message);
		}

		public void Dispose()
		{
		}

		void IDisposable.Dispose()
		{
			this.Dispose();
		}
	}

	public static readonly NonInteractiveFeatureUiService Instance = new NonInteractiveFeatureUiService();

	private NonInteractiveFeatureUiService()
	{
	}

	public bool Confirm(string caption, string message)
	{
		return false;
	}

	bool IFeatureUiService.Confirm(string caption, string message)
	{
		return this.Confirm(caption, message);
	}

	public void ShowMessage(string caption, string message, FeatureMessageKind kind)
	{
	}

	void IFeatureUiService.ShowMessage(string caption, string message, FeatureMessageKind kind)
	{
		this.ShowMessage(caption, message, kind);
	}

	public void ShowTimedMessage(string caption, string message, int milliseconds)
	{
	}

	void IFeatureUiService.ShowTimedMessage(string caption, string message, int milliseconds)
	{
		this.ShowTimedMessage(caption, message, milliseconds);
	}

	public void ShowCompletionMessage(string caption, string summary, string detail, int milliseconds)
	{
	}

	void IFeatureUiService.ShowCompletionMessage(string caption, string summary, string detail, int milliseconds)
	{
		this.ShowCompletionMessage(caption, summary, detail, milliseconds);
	}

	public ITaskProgressReporter CreateProgress(string featureId, string title)
	{
		return new NullTaskProgressReporter();
	}

	ITaskProgressReporter IFeatureUiService.CreateProgress(string featureId, string title)
	{
		return this.CreateProgress(featureId, title);
	}

	public string SelectPdfForConversion(string initialDirectory)
	{
		return null;
	}

	string IFeatureUiService.SelectPdfForConversion(string initialDirectory)
	{
		return this.SelectPdfForConversion(initialDirectory);
	}

	public ConvertConflictDecision ResolveConvertConflict(string targetPath)
	{
		return ConvertConflictDecision.AutoRename;
	}

	ConvertConflictDecision IFeatureUiService.ResolveConvertConflict(string targetPath)
	{
		return this.ResolveConvertConflict(targetPath);
	}

	public void OpenFolder(string folderPath)
	{
	}

	void IFeatureUiService.OpenFolder(string folderPath)
	{
		this.OpenFolder(folderPath);
	}

	public CompilationConfirmationResult ConfirmCompilation(CompilationConfirmationRequest request)
	{
		return new CompilationConfirmationResult
		{
			Confirmed = false
		};
	}

	CompilationConfirmationResult IFeatureUiService.ConfirmCompilation(CompilationConfirmationRequest request)
	{
		return this.ConfirmCompilation(request);
	}
}
