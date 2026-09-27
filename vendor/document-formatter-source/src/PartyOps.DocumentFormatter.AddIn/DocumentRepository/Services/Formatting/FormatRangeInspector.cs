using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class FormatRangeInspector
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool RangeHasAttachmentText(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		Paragraphs value = null;
		try
		{
			value = range.Paragraphs;
			int count = value.Count;
			for (int i = 1; i <= count; i++)
			{
				Paragraph value2 = null;
				Microsoft.Office.Interop.Word.Range value3 = null;
				try
				{
					value2 = value[i];
					value3 = value2.Range;
					if ((value3.Text ?? string.Empty).Trim(new char[] { '\r', '\n', '\u0007', ' ', '\t', '\u00A0', '\u3000' }).StartsWith("附件", StringComparison.Ordinal))
					{
						return true;
					}
				}
				finally
				{
					if (value3 != null)
					{
						ComObjectRelease.Release(ref value3, "FormatRangeInspector.paraRange");
					}
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "FormatRangeInspector.para");
					}
				}
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "FormatRangeInspector.paragraphs");
			}
		}
		return false;
	}
}
