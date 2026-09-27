using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using DocumentRepository;
using DocumentRepository.Services.Conversion.PdfToWord;
using DocumentRepository.Services.FileSafety;
using DocumentRepository.Services.Hosting;

namespace PartyOps.DocumentFormatter.PdfToWordLauncher;

internal static class Program
{
	internal sealed class ConversionOutcome
	{
		public ConversionOutcomeKind Kind { get; set; }

		public string SafeReason { get; set; }
	}

	private const int ExitSuccess = 0;

	private const int ExitError = 1;

	private const int ExitUnauthorized = 2;

	[STAThread]
	private static int Main(string[] args)
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		if (args != null && args.Length >= 2 && string.Equals(args[0], "--format", StringComparison.OrdinalIgnoreCase))
		{
			return SubmitFormatRequest(args[1]);
		}
		string text = ResolvePdfPath(args);
		if (string.IsNullOrWhiteSpace(text))
		{
			MessageBox.Show("缺少 PDF 文件路径参数，无法转换。", "partyops公文排版助手", (MessageBoxButtons)0, (MessageBoxIcon)16);
			return 1;
		}
		if (!File.Exists(text))
		{
			MessageBox.Show("未找到该 PDF 文件。", "partyops公文排版助手", (MessageBoxButtons)0, (MessageBoxIcon)16);
			return 1;
		}
		string text2 = ResolveOutputTarget(Path.ChangeExtension(text, ".docx"));
		if (text2 == null)
		{
			return 0;
		}
		ConversionOutcome conversionOutcome = RunConversion(text, text2);
		if (conversionOutcome.Kind == ConversionOutcomeKind.Cancelled)
		{
			return 0;
		}
		if (conversionOutcome.Kind == ConversionOutcomeKind.Failed)
		{
			ShowFailure(conversionOutcome.SafeReason);
			return 1;
		}
		ShowResultForm(text2);
		return 0;
	}

	private static int SubmitFormatRequest(string sourcePath)
	{
		try
		{
			string fullPath = Path.GetFullPath(sourcePath ?? string.Empty);
			if (!File.Exists(fullPath))
			{
				throw new FileNotFoundException();
			}
			Process.Start(new ProcessStartInfo
			{
				FileName = fullPath,
				UseShellExecute = true
			});
			ExternalFormatRequestService.Enqueue(fullPath);
			return 0;
		}
		catch (NotSupportedException)
		{
			MessageBox.Show("目前仅支持对 DOCX、DOC、WPS 文档执行右键一键排版。", "partyops公文排版助手", (MessageBoxButtons)0, (MessageBoxIcon)64);
			return 1;
		}
		catch
		{
			MessageBox.Show("无法打开该文档并启动一键排版，请确认文件存在且可正常打开。", "partyops公文排版助手", (MessageBoxButtons)0, (MessageBoxIcon)48);
			return 1;
		}
	}

	private static string ResolvePdfPath(string[] args)
	{
		if (args == null || args.Length == 0)
		{
			return null;
		}
		return args[0].Trim();
	}

	private static string ResolveOutputTarget(string targetPath)
	{
		if (!File.Exists(targetPath))
		{
			return targetPath;
		}
		return PdfToWordConflictDialog.Ask(targetPath) switch
		{
			PdfToWordConflictChoice.Overwrite => targetPath, 
			PdfToWordConflictChoice.Rename => NextAvailableName(targetPath), 
			_ => null, 
		};
	}

	private static string NextAvailableName(string targetPath)
	{
		string path = Path.GetDirectoryName(targetPath) ?? ".";
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(targetPath);
		string extension = Path.GetExtension(targetPath);
		for (int i = 1; i < 1000; i++)
		{
			string text = Path.Combine(path, fileNameWithoutExtension + " (" + i + ")" + extension);
			if (!File.Exists(text))
			{
				return text;
			}
		}
		return targetPath + ".new";
	}

	private static ConversionOutcome RunConversion(string pdfPath, string targetPath)
	{
		ConversionOutcome obj = new ConversionOutcome
		{
			Kind = ConversionOutcomeKind.Succeeded
		};
		PdfToWordProgressForm pdfToWordProgressForm = new PdfToWordProgressForm(pdfPath, delegate(PdfToWordProgressForm.ConversionCallbacks callbacks)
		{
			RunConversionWorker(pdfPath, targetPath, callbacks);
		});
		Application.Run((Form)(object)pdfToWordProgressForm);
		obj.Kind = pdfToWordProgressForm.OutcomeKind;
		obj.SafeReason = pdfToWordProgressForm.SafeReason;
		return obj;
	}

	private static void RunConversionWorker(string pdfPath, string targetPath, PdfToWordProgressForm.ConversionCallbacks callbacks)
	{
		CancellationToken token = callbacks.CancellationToken;
		SafeOutputTransaction.ProduceAndCommit(targetPath, delegate(string tempPath)
		{
			using (FileStream docxStream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite))
			{
				new LocalPdfToWordEngine().Convert(pdfPath, docxStream, delegate(int page, int total)
				{
					token.ThrowIfCancellationRequested();
					callbacks.PageProgress(page, total);
				}, delegate(int page, int total)
				{
					token.ThrowIfCancellationRequested();
					callbacks.ReconstructProgress(page, total);
				});
			}
			token.ThrowIfCancellationRequested();
		}, ".docx");
	}

	private static void ShowFailure(string reason)
	{
		if (string.IsNullOrEmpty(reason))
		{
			reason = "转换过程中出现内部错误，请重试；如持续失败请重新打开文档后再转换。";
		}
		MessageBox.Show(reason, "partyops公文排版助手 - 转换失败", (MessageBoxButtons)0, (MessageBoxIcon)48);
	}

	private static void ShowResultForm(string targetPath)
	{
		PdfToWordResultForm pdfToWordResultForm = new PdfToWordResultForm(targetPath);
		try
		{
			((Form)pdfToWordResultForm).ShowDialog();
		}
		finally
		{
			((IDisposable)(object)pdfToWordResultForm)?.Dispose();
		}
	}
}
