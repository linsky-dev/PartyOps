using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Hosting;

public sealed class WordWpsDocumentHost : IDocumentHost
{
	public Application Application { get; }

	public Document Document { get; }

	public HostCapabilities Capabilities { get; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public WordWpsDocumentHost(Application application, Document document)
	{
		HostThreadRuntime.AssertAccess("WordWpsDocumentHost.ctor");
		Application = application ?? throw new ArgumentNullException("application");
		Document = document;
		Capabilities = ProbeCapabilities(application, document);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool TryReadScreenUpdating(out bool value)
	{
		value = true;
		try
		{
			value = Application.ScreenUpdating;
			return true;
		}
		catch (Exception ex)
		{
			LogService.Warn("Host.ScreenUpdating.Read", ex);
			return false;
		}
	}

	bool IDocumentHost.TryReadScreenUpdating(out bool value)
	{
		return this.TryReadScreenUpdating(out value);
	}

	public void SetScreenUpdating(bool value)
	{
		Application.ScreenUpdating = value;
	}

	void IDocumentHost.SetScreenUpdating(bool value)
	{
		this.SetScreenUpdating(value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool TryReadDisplayAlerts(out WdAlertLevel value)
	{
		value = WdAlertLevel.wdAlertsAll;
		try
		{
			value = Application.DisplayAlerts;
			return true;
		}
		catch (Exception ex)
		{
			LogService.Warn("Host.DisplayAlerts.Read", ex);
			return false;
		}
	}

	bool IDocumentHost.TryReadDisplayAlerts(out WdAlertLevel value)
	{
		return this.TryReadDisplayAlerts(out value);
	}

	public void SetDisplayAlerts(WdAlertLevel value)
	{
		Application.DisplayAlerts = value;
	}

	void IDocumentHost.SetDisplayAlerts(WdAlertLevel value)
	{
		this.SetDisplayAlerts(value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool TryReadEnableEvents(out bool value)
	{
		value = true;
		try
		{
			object value2 = Application.GetType().InvokeMember("EnableEvents", BindingFlags.GetProperty, null, Application, null);
			value = Convert.ToBoolean(value2);
			return true;
		}
		catch (Exception ex)
		{
			LogService.Warn("Host.EnableEvents.Read", ex);
			return false;
		}
	}

	bool IDocumentHost.TryReadEnableEvents(out bool value)
	{
		return this.TryReadEnableEvents(out value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void SetEnableEvents(bool value)
	{
		Application.GetType().InvokeMember("EnableEvents", BindingFlags.SetProperty, null, Application, new object[1] { value });
	}

	void IDocumentHost.SetEnableEvents(bool value)
	{
		this.SetEnableEvents(value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void StartUndoRecord(string name)
	{
		UndoRecord value = null;
		try
		{
			value = Application.UndoRecord;
			value.StartCustomRecord(name);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "Host.StartUndoRecord");
		}
	}

	void IDocumentHost.StartUndoRecord(string name)
	{
		this.StartUndoRecord(name);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void EndUndoRecord()
	{
		UndoRecord value = null;
		try
		{
			value = Application.UndoRecord;
			value.EndCustomRecord();
		}
		finally
		{
			ComObjectRelease.Release(ref value, "Host.EndUndoRecord");
		}
	}

	void IDocumentHost.EndUndoRecord()
	{
		this.EndUndoRecord();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void UndoLastRecord()
	{
		if (Document != null)
		{
			object Times = 1;
			if (!Document.Undo(ref Times))
			{
				throw new InvalidOperationException("Word/WPS 未能撤销本次文档变更。");
			}
			return;
		}
		throw new InvalidOperationException("宿主没有可回滚的当前文档。");
	}

	void IDocumentHost.UndoLastRecord()
	{
		this.UndoLastRecord();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public SelectionCheckpoint CaptureSelection()
	{
		if (Document == null)
		{
			return null;
		}
		Selection value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			value = Application.Selection;
			if (value != null)
			{
				value2 = value.Range;
				if (value2 == null)
				{
					return null;
				}
				int start = value2.Start;
				int end = value2.End;
				WdStoryType storyType = WdStoryType.wdMainTextStory;
				bool hasStoryType = false;
				try
				{
					storyType = value2.StoryType;
					hasStoryType = true;
				}
				catch (Exception ex)
				{
					LogService.Warn("Host.CaptureSelection.StoryType", ex);
				}
				return new SelectionCheckpoint(value2.Duplicate, start, end, storyType, hasStoryType);
			}
			return null;
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "Host.CaptureSelection.Range");
			ComObjectRelease.Release(ref value, "Host.CaptureSelection.Selection");
		}
	}

	SelectionCheckpoint IDocumentHost.CaptureSelection()
	{
		return this.CaptureSelection();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void RestoreSelection(SelectionCheckpoint checkpoint)
	{
		if (Document == null || checkpoint == null)
		{
			return;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			if (!checkpoint.HasStoryType || checkpoint.StoryType == WdStoryType.wdMainTextStory)
			{
				EnsureMainDocumentView();
			}
			if (checkpoint.LiveRange != null)
			{
				try
				{
					checkpoint.LiveRange.Select();
					return;
				}
				catch (Exception ex)
				{
					LogService.Warn("Host.RestoreSelection.LiveRange", ex);
				}
			}
			value2 = Document.Content;
			int start = value2.Start;
			int end = value2.End;
			int num = WordDocumentBoundary.ClampPosition(checkpoint.Start, start, end);
			int num2 = WordDocumentBoundary.ClampPosition(checkpoint.End, num, end);
			Document document = Document;
			object Start = num;
			object End = num2;
			value = document.Range(ref Start, ref End);
			value.Select();
		}
		finally
		{
			Microsoft.Office.Interop.Word.Range value3 = checkpoint.LiveRange;
			checkpoint.LiveRange = null;
			ComObjectRelease.Release(ref value3, "Host.RestoreSelection.LiveRange");
			ComObjectRelease.Release(ref value2, "Host.RestoreSelection.Content");
			ComObjectRelease.Release(ref value, "Host.RestoreSelection.Range");
		}
	}

	void IDocumentHost.RestoreSelection(SelectionCheckpoint checkpoint)
	{
		this.RestoreSelection(checkpoint);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void MoveSelectionToDocumentStart()
	{
		if (Document == null)
		{
			return;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			EnsureMainDocumentView();
			Document document = Document;
			object Start = 0;
			object End = 0;
			value = document.Range(ref Start, ref End);
			value.Select();
			EnsureMainDocumentView();
		}
		finally
		{
			ComObjectRelease.Release(ref value, "Host.MoveSelectionToDocumentStart");
		}
	}

	void IDocumentHost.MoveSelectionToDocumentStart()
	{
		this.MoveSelectionToDocumentStart();
	}

	public void RefreshVisibleLayout()
	{
		if (Document != null)
		{
			Document.Repaginate();
			Application.ScreenRefresh();
		}
	}

	void IDocumentHost.RefreshVisibleLayout()
	{
		this.RefreshVisibleLayout();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void EnsureMainDocumentView()
	{
		Window value = null;
		View value2 = null;
		try
		{
			value = Application.ActiveWindow;
			if (value == null)
			{
				throw new InvalidOperationException("当前没有可用于恢复正文视图的活动窗口。");
			}
			value2 = value.View;
			if (value2 != null)
			{
				if (value2.SeekView != WdSeekView.wdSeekMainDocument)
				{
					value2.SeekView = WdSeekView.wdSeekMainDocument;
				}
				return;
			}
			throw new InvalidOperationException("当前活动窗口没有可用视图。");
		}
		catch (Exception ex)
		{
			LogService.Warn("Host.EnsureMainDocumentView", ex);
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "Host.EnsureMainDocumentView.View");
			ComObjectRelease.Release(ref value, "Host.EnsureMainDocumentView.Window");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static HostCapabilities ProbeCapabilities(Application application, Document document)
	{
		string text = SafeRead(() => application.Name);
		string version = SafeRead(() => application.Version);
		string value = SafeRead(() => application.Path);
		string a = SafeRead(() => Process.GetCurrentProcess().ProcessName);
		bool flag = ContainsIgnoreCase(text, "WPS") || ContainsIgnoreCase(text, "Kingsoft") || ContainsIgnoreCase(value, "WPS Office") || ContainsIgnoreCase(value, "Kingsoft") || string.Equals(a, "wps", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "kwps", StringComparison.OrdinalIgnoreCase);
		Probe(delegate
		{
			_ = application.ScreenUpdating;
		}, out var succeeded);
		Probe(delegate
		{
			_ = application.DisplayAlerts;
		}, out var succeeded2);
		Probe([MethodImpl(MethodImplOptions.NoInlining)] () =>
		{
			application.GetType().InvokeMember("EnableEvents", BindingFlags.GetProperty, null, application, null);
		}, out var succeeded3);
		Probe([MethodImpl(MethodImplOptions.NoInlining)] () =>
		{
			ComObjectRelease.Release(application.UndoRecord, "Host.ProbeCapabilities.UndoRecord");
		}, out var succeeded4);
		return new HostCapabilities
		{
			Kind = ((!flag) ? DocumentHostKind.MicrosoftWord : DocumentHostKind.WpsWriter),
			ProductName = text,
			Version = version,
			SupportsScreenUpdating = succeeded,
			SupportsDisplayAlerts = succeeded2,
			SupportsEnableEvents = succeeded3,
			SupportsUndoRecord = succeeded4,
			SupportsStableRangePageCoordinates = !flag,
			SupportsNativeFixedFormatExport = true,
			RequiresExplicitParagraphSpacing = flag
		};
	}

	private static void Probe(Action action, out bool succeeded)
	{
		try
		{
			action();
			succeeded = true;
		}
		catch
		{
			succeeded = false;
		}
	}

	private static string SafeRead(Func<string> reader)
	{
		try
		{
			return reader() ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static bool ContainsIgnoreCase(string value, string fragment)
	{
		if (!string.IsNullOrEmpty(value))
		{
			return value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return false;
	}
}
