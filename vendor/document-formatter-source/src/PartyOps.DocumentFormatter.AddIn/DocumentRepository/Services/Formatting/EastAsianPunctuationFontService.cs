using System;
using System.IO;
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
		bool macQuoteFont = UseMacQuoteFont();
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
					// 用户明确要求Mac引号使用Times New Roman；仅改字体，不复制FormattedText或补写正文。
					if (macQuoteFont && IsQuote(text[i]))
					{
						if (ApplyMacQuoteFont(container, value, text, i)) num2++; else num3++;
						continue;
					}
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

	private static bool UseMacQuoteFont()
	{
		// Mono在macOS可能报告Unix；系统版本文件与平台一起识别，ENV单独不能改变Win/Linux。
		PlatformID platform = Environment.OSVersion.Platform;
		bool mac = platform == PlatformID.MacOSX || (platform == PlatformID.Unix && File.Exists("/System/Library/CoreServices/SystemVersion.plist"));
		return mac && string.Equals(Environment.GetEnvironmentVariable("PARTYOPS_MAC_QUOTE_FONT"), "Times New Roman", StringComparison.Ordinal);
	}

	private static bool IsQuote(char value) => value == '‘' || value == '’' || value == '“' || value == '”';

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ApplyMacQuoteFont(Microsoft.Office.Interop.Word.Range container, Microsoft.Office.Interop.Word.Range target, string baseline, int index)
	{
		if (index < 0 || index >= baseline.Length || !IsQuote(baseline[index])) throw new InvalidOperationException("Mac引号目标无效。");
		int start = container.Start + index;
		int end = start + 1;
		target.SetRange(start, end);
		void CheckText()
		{
			if (target.Start != start || target.End != end || !string.Equals(target.Text, baseline[index].ToString(), StringComparison.Ordinal) || !string.Equals(container.Text, baseline, StringComparison.Ordinal))
				throw new InvalidOperationException("Mac引号字体设置时正文或坐标漂移；未补写文字。");
		}
		CheckText();
		Font font = null;
		try
		{
			font = target.Font;
			if (font == null || NormalizeFontName(font.Name).Length == 0 || NormalizeFontName(font.NameAscii).Length == 0 || NormalizeFontName(font.NameFarEast).Length == 0)
				throw new InvalidOperationException("Mac引号字体未知，停止字体设置。");
			float size = font.Size;
			int bold = font.Bold, italic = font.Italic;
			WdColor color = font.Color;
			if (float.IsNaN(size) || float.IsInfinity(size) || size <= 0 || size >= 9999999 || (bold != 0 && bold != -1) || (italic != 0 && italic != -1))
				throw new InvalidOperationException("Mac引号字号或字重未知，停止字体设置。");
			const string name = "Times New Roman";
			bool changed = false;
			void CheckState()
			{
				CheckText();
				if (font.Size != size || font.Bold != bold || font.Italic != italic || font.Color != color)
					throw new InvalidOperationException("Mac引号字体设置改变了字号、字重或颜色。");
			}
			CheckState();
			if (!DocumentFontSlotService.SameFontName(font.Name, name)) { font.Name = name; changed = true; }
			CheckState();
			if (!DocumentFontSlotService.SameFontName(font.NameFarEast, name)) { font.NameFarEast = name; changed = true; }
			CheckState();
			if (!DocumentFontSlotService.SameFontName(font.NameAscii, name)) { font.NameAscii = name; changed = true; }
			CheckState();
			if (!DocumentFontSlotService.SameFontName(font.Name, name) || !DocumentFontSlotService.SameFontName(font.NameAscii, name) || !DocumentFontSlotService.SameFontName(font.NameFarEast, name))
				throw new InvalidOperationException("Mac引号Times New Roman字体读回复核失败。");
			return changed;
		}
		finally
		{
			if (font != null) ComObjectRelease.Release(ref font, "EastAsianPunctuationFontService.macQuoteFont");
		}
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
