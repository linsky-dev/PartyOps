using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Cleanup;

public static class FormattingCleanupService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ClearNormalStyleBorder(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		Style value = null;
		ParagraphFormat value2 = null;
		Borders value3 = null;
		Shading value4 = null;
		try
		{
			Styles styles = document.Styles;
			object Index = WdBuiltinStyle.wdStyleNormal;
			value = styles.get_Item(ref Index);
			if (value != null)
			{
				value2 = value.ParagraphFormat;
				value3 = value2.Borders;
				if (value3.Enable != 0)
				{
					value3.Enable = 0;
				}
				value4 = value2.Shading;
				if (value4.BackgroundPatternColor != WdColor.wdColorAutomatic)
				{
					value4.BackgroundPatternColor = WdColor.wdColorAutomatic;
				}
				if (Math.Abs(value2.FirstLineIndent) > 0.1f)
				{
					value2.FirstLineIndent = 0f;
				}
				if (Math.Abs(value2.CharacterUnitFirstLineIndent) > 0.1f)
				{
					value2.CharacterUnitFirstLineIndent = 0f;
				}
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("FormattingCleanupService.ClearNormalStyleBorder", ex);
		}
		finally
		{
			if (value4 != null)
			{
				ComObjectRelease.Release(ref value4, "FormattingCleanupService.shading");
			}
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "FormattingCleanupService.borders");
			}
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "FormattingCleanupService.paragraphFormat");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "FormattingCleanupService.normalStyle");
			}
		}
	}
}
