using System;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Protection;

public static class HostNativeSaveService
{
	private static Func<OperationContext, bool> saveOverrideForTesting;

	internal static void SetSaveOverrideForTesting(Func<OperationContext, bool> value)
	{
		saveOverrideForTesting = value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TrySave(OperationContext context)
	{
		if (context == null || context.Application == null || context.Document == null)
		{
			return false;
		}
		Document document = context.Document;
		try
		{
			if (saveOverrideForTesting != null)
			{
				bool num = saveOverrideForTesting(context);
				if (num)
				{
					DocumentLifecycleRegistry.Rotate(document);
				}
				return num;
			}
			string text = null;
			try
			{
				text = document.FullName;
			}
			catch
			{
			}
			if (!string.IsNullOrWhiteSpace(text) && Path.IsPathRooted(text))
			{
				document.Save();
				return true;
			}
			Dialogs value = null;
			Dialog value2 = null;
			try
			{
				value = context.Application.Dialogs;
				value2 = value[WdWordDialog.wdDialogFileSaveAs];
				Dialog dialog = value2;
				object TimeOut = Type.Missing;
				if (dialog.Show(ref TimeOut) != -1)
				{
					return false;
				}
			}
			finally
			{
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "HostNativeSave.dialog");
				}
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "HostNativeSave.dialogs");
				}
			}
			bool flag = false;
			try
			{
				flag = document.Saved;
			}
			catch
			{
			}
			if (!flag)
			{
				return false;
			}
			string text2 = null;
			try
			{
				text2 = document.FullName;
			}
			catch
			{
			}
			int num2;
			if (!string.IsNullOrWhiteSpace(text2))
			{
				num2 = (Path.IsPathRooted(text2) ? 1 : 0);
				if (num2 != 0)
				{
					DocumentLifecycleRegistry.Rotate(document);
				}
			}
			else
			{
				num2 = 0;
			}
			return (byte)num2 != 0;
		}
		catch (Exception ex)
		{
			LogService.Warn("HostNativeSaveService.TrySave", ex);
			return false;
		}
	}
}
