using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Features;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Models;

public class OperationContext
{
	[ComOwnership(ComOwnershipKind.Borrowed)]
	public Application Application { get; set; }

	[ComOwnership(ComOwnershipKind.Borrowed)]
	public Document Document { get; set; }

	[ComOwnership(ComOwnershipKind.Borrowed)]
	public Selection Selection { get; set; }

	public FormatConfig CurrentConfig { get; set; }

	public bool IsBatchMode { get; set; }

	public bool SuppressUserDialogs { get; set; }

	public string TaskId { get; set; }

	public string FeatureId { get; set; }

	public string InvocationSource { get; set; }

	public IFeatureUiService UserInterface { get; set; }

	public string Stage { get; set; }

	public string ObjectLocation { get; set; }

	public object Tag { get; set; }

	public bool HasMeaningfulSelection
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			if (Selection == null)
			{
				return false;
			}
			HostThreadRuntime.AssertAccess("OperationContext.HasMeaningfulSelection");
			Microsoft.Office.Interop.Word.Range value = null;
			try
			{
				value = Selection.Range;
				if (value == null || value.Start == value.End)
				{
					return false;
				}
				return !string.IsNullOrWhiteSpace((value.Text ?? string.Empty).Replace("\r", string.Empty).Replace("\a", string.Empty).Trim());
			}
			finally
			{
				ComObjectRelease.Release(ref value, "OperationContext.HasMeaningfulSelection.Range");
			}
		}
	}

	public T GetTag<T>() where T : class
	{
		return Tag as T;
	}

	public void SetTag<T>(T value) where T : class
	{
		Tag = value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static OperationContext FromApplication([ComOwnership(ComOwnershipKind.Borrowed)] Application app)
	{
		if (app == null)
		{
			throw new ArgumentNullException("app");
		}
		HostThreadRuntime.AssertAccess("OperationContext.FromApplication");
		OperationContext operationContext = new OperationContext
		{
			Application = app
		};
		Documents value = null;
		try
		{
			value = app.Documents;
			if (value.Count > 0)
			{
				operationContext.Document = app.ActiveDocument;
				operationContext.Selection = app.Selection;
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value, "OperationContext.FromApplication.Documents");
		}
		operationContext.CurrentConfig = ConfigManager.GetCurrentCopy();
		return operationContext;
	}
}
