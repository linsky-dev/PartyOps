using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Replace;

public static class ReplaceScopeService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Microsoft.Office.Interop.Word.Range GetExecutionScope(Application app)
	{
		if (app == null)
		{
			throw new ArgumentNullException("app");
		}
		return GetSelectionOrDocumentScope(app);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Microsoft.Office.Interop.Word.Range GetSelectionOrDocumentScope(Application app)
	{
		Microsoft.Office.Interop.Word.Range value = GetSelectionRange(app);
		if (!string.IsNullOrWhiteSpace(CleanRangeText(value)))
		{
			return DuplicateAndRelease(value);
		}
		if (value != null)
		{
			ComObjectRelease.Release(ref value, "ReplaceScopeService.selectionRange");
		}
		return app.ActiveDocument.Content.Duplicate;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Microsoft.Office.Interop.Word.Range GetSelectionRange(Application app)
	{
		try
		{
			return (app.Selection == null) ? null : app.Selection.Range;
		}
		catch (Exception ex)
		{
			LogService.Warn("ReplaceScopeService.GetSelectionRange", ex);
			return null;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Microsoft.Office.Interop.Word.Range DuplicateAndRelease(Microsoft.Office.Interop.Word.Range range)
	{
		Microsoft.Office.Interop.Word.Range duplicate = range.Duplicate;
		ComObjectRelease.Release(ref range, "ReplaceScopeService.range");
		return duplicate;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CleanRangeText(Microsoft.Office.Interop.Word.Range range)
	{
		string text = "";
		try
		{
			text = ((range == null) ? "" : (range.Text ?? ""));
		}
		catch (Exception ex)
		{
			LogService.Warn("ReplaceScopeService.CleanRangeText", ex);
		}
		return (text ?? "").Replace("\r", "").Replace("\a", "").Trim();
	}
}
