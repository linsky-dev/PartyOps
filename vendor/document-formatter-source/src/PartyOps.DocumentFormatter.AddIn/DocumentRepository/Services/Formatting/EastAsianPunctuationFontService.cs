using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Cleanup;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class EastAsianPunctuationFontService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Normalize(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			return Normalize(value);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "EastAsianPunctuationFontService.content");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Normalize(Microsoft.Office.Interop.Word.Range container)
	{
		if (container == null)
		{
			throw new ArgumentNullException("container");
		}
		Document value = null;
		try
		{
			value = container.Document;
			bool trackRevisions;
			try
			{
				trackRevisions = value.TrackRevisions;
			}
			catch (Exception ex)
			{
				LogService.Warn("EastAsianPunctuationFontService cannot read TrackRevisions, skip: " + ex.GetType().Name);
				return 0;
			}
			if (trackRevisions)
			{
				LogService.Warn("EastAsianPunctuationFontService TrackRevisions enabled, skip punctuation slot correction.");
				return 0;
			}
			return NormalizeCore(container);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "EastAsianPunctuationFontService.ownerDocument");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int NormalizeCore(Microsoft.Office.Interop.Word.Range container)
	{
		if (container == null)
		{
			throw new ArgumentNullException("container");
		}
		string text = container.Text ?? string.Empty;
		int val = Math.Max(0, container.End - container.Start);
		int num = Math.Min(text.Length, val);
		if (num != 0)
		{
			Microsoft.Office.Interop.Word.Range value = null;
			Microsoft.Office.Interop.Word.Range value2 = null;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			int num6 = 0;
			int num7 = 0;
			try
			{
				value = container.Duplicate;
				value2 = container.Duplicate;
				for (int i = 0; i < num; i++)
				{
					if (!EastAsianPunctuationPolicy.RequiresFormatting(text, i))
					{
						continue;
					}
					int num8 = EastAsianPunctuationPolicy.FindReferenceIndex(text, i);
					if (num8 >= 0 && num8 < num)
					{
						Font value3 = null;
						Font value4 = null;
						Microsoft.Office.Interop.Word.Range value5 = null;
						string text2 = text[i].ToString();
						bool flag = false;
						try
						{
							value.SetRange(container.Start + i, container.Start + i + 1);
							value2.SetRange(container.Start + num8, container.Start + num8 + 1);
							if (!string.Equals(value.Text ?? string.Empty, text2, StringComparison.Ordinal) || !string.Equals(value2.Text ?? string.Empty, text[num8].ToString(), StringComparison.Ordinal))
							{
								num6++;
							}
							else if (!SafeTextMutationService.HasUnsafeContentForExactTextMutation(value) && !SafeTextMutationService.HasUnsafeContentForExactTextMutation(value2))
							{
								value3 = value.Font;
								value4 = value2.Font;
								string actual = NormalizeFontName(value3.Name);
								string text3 = NormalizeFontName(value4.NameFarEast);
								if (string.IsNullOrEmpty(text3))
								{
									num5++;
								}
								else if (!DocumentFontSlotService.SameFontName(actual, text3))
								{
									value5 = value2.FormattedText;
									flag = true;
									value.FormattedText = value5;
									value.SetRange(container.Start + i, container.Start + i + 1);
									value.Text = text2;
									value.SetRange(container.Start + i, container.Start + i + 1);
									if (!string.Equals(value.Text ?? string.Empty, text2, StringComparison.Ordinal))
									{
										throw new InvalidOperationException("中文标点格式继承后字符复核失败。");
									}
									ComObjectRelease.Release(ref value3, "EastAsianPunctuationFontService.targetFontRefresh");
									value3 = value.Font;
									if (!DocumentFontSlotService.SameFontName(NormalizeFontName(value3.Name), text3))
									{
										throw new InvalidOperationException("中文标点未能切换到相邻汉字字体。");
									}
									num2++;
								}
								else
								{
									num3++;
								}
							}
							else
							{
								num4++;
							}
						}
						catch (Exception ex)
						{
							if (flag)
							{
								try
								{
									value.SetRange(container.Start + i, container.Start + i + 1);
									if (!string.Equals(value.Text ?? string.Empty, text2, StringComparison.Ordinal))
									{
										value.Text = text2;
									}
									value.SetRange(container.Start + i, container.Start + i + 1);
									if (!string.Equals(value.Text ?? string.Empty, text2, StringComparison.Ordinal))
									{
										throw new InvalidOperationException("中文标点字符恢复失败。");
									}
								}
								catch (Exception innerException)
								{
									throw new InvalidOperationException("中文标点格式修正未能恢复原字符。", innerException);
								}
							}
							num7++;
							LogService.Warn("EastAsianPunctuationFontService targetIndex=" + i + ", exception=" + ex.GetType().Name);
						}
						finally
						{
							if (value5 != null)
							{
								ComObjectRelease.Release(ref value5, "EastAsianPunctuationFontService.referenceFormatted");
							}
							if (value4 != null)
							{
								ComObjectRelease.Release(ref value4, "EastAsianPunctuationFontService.referenceFont");
							}
							if (value3 != null)
							{
								ComObjectRelease.Release(ref value3, "EastAsianPunctuationFontService.targetFont");
							}
						}
					}
					else
					{
						num5++;
					}
				}
			}
			finally
			{
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "EastAsianPunctuationFontService.reference");
				}
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "EastAsianPunctuationFontService.target");
				}
			}
			LogService.Info("EastAsianPunctuationFontService normalized=" + num2 + ", alreadyCorrect=" + num3 + ", protected=" + num4 + ", noReference=" + num5 + ", coordinateMismatch=" + num6 + ", failed=" + num7);
			return num2;
		}
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string NormalizeFontName(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		string text = value.Trim();
		if (!(text == "-1") && !(text == "9999999"))
		{
			return text;
		}
		return string.Empty;
	}
}
