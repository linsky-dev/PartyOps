using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;

namespace DocumentRepository.Services.Formatting.Signatures;

public static class SignatureWidthEstimator
{
	public static SignatureTextMorphology Classify(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			if (!ContainsOnlyCalibratedCharacters(text))
			{
				return SignatureTextMorphology.Unknown;
			}
			bool flag = false;
			bool flag2 = false;
			bool flag3 = false;
			bool flag4 = true;
			foreach (char c in text)
			{
				if (!IsCjkIdeograph(c))
				{
					switch (c)
					{
					case '\u3000':
					case '〇':
						continue;
					case '0':
					case '1':
					case '2':
					case '3':
					case '4':
					case '5':
					case '6':
					case '7':
					case '8':
					case '9':
						flag4 = false;
						continue;
					}
					if (c >= 'A' && c <= 'Z')
					{
						flag2 = true;
						flag4 = false;
					}
					else if (c == ' ')
					{
						flag3 = true;
						flag4 = false;
					}
				}
				else
				{
					flag = true;
				}
			}
			if (flag4)
			{
				return SignatureTextMorphology.PureCjk;
			}
			char c2 = text[0];
			if (c2 >= '0' && c2 <= '9')
			{
				if (flag3 && !flag2)
				{
					return SignatureTextMorphology.SpacedDate;
				}
				return SignatureTextMorphology.DigitCjkDate;
			}
			if (c2 >= 'A' && c2 <= 'Z' && HasLeadingUpperLatinRun(text) && flag)
			{
				return SignatureTextMorphology.LatinLeadingSignature;
			}
			if (IsCjkIdeograph(c2) && flag2)
			{
				return SignatureTextMorphology.CjkLatinMixed;
			}
			return SignatureTextMorphology.Unknown;
		}
		return SignatureTextMorphology.Unknown;
	}

	public static bool HasUpperLatin(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] >= 'A' && text[i] <= 'Z')
			{
				return true;
			}
		}
		return false;
	}

	public static bool ContainsOnlyCalibratedCharacters(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			foreach (char c in text)
			{
				if (IsCjkIdeograph(c))
				{
					continue;
				}
				switch (c)
				{
				case ' ':
				case '0':
				case '1':
				case '2':
				case '3':
				case '4':
				case '5':
				case '6':
				case '7':
				case '8':
				case '9':
				case '\u3000':
				case '〇':
					continue;
				}
				if (c >= 'A' && c <= 'Z')
				{
					continue;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static float EstimateWidth(string text, SignatureWidthCalibrationTable.Entry entry)
	{
		if (text == null)
		{
			throw new ArgumentNullException("text");
		}
		if (entry == null)
		{
			throw new ArgumentNullException("entry");
		}
		float num = 0f;
		for (int i = 0; i < text.Length; i++)
		{
			num += GetCharWidthPt(text[i], entry);
		}
		for (int j = 1; j < text.Length; j++)
		{
			char c = text[j - 1];
			char c2 = text[j];
			bool num2 = c >= '0' && c <= '9';
			bool flag = c2 >= '0' && c2 <= '9';
			bool flag2 = IsTransitionCjk(c);
			bool flag3 = IsTransitionCjk(c2);
			bool flag4 = c >= 'A' && c <= 'Z';
			bool flag5 = c2 >= 'A' && c2 <= 'Z';
			if ((num2 && flag3) || (flag2 && flag))
			{
				num += entry.DigitCjkTransitionPt;
			}
			else if (!(flag4 && flag3))
			{
				if (flag2 && flag5)
				{
					num += entry.CjkToLatinTransitionPt;
				}
			}
			else
			{
				num += entry.LatinToCjkTransitionPt;
			}
		}
		return num + GetMorphologyResidualPt(text, entry);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryResolveStatic(string text, DocumentHostKind host, string cjkFontName, string asciiFontName, float fontSizePt, bool gridEnabled, out float widthPt, out SignatureTextMorphology morphology, out SignatureStaticRejection rejection)
	{
		widthPt = 0f;
		morphology = Classify(text);
		rejection = SignatureStaticRejection.None;
		if (host != DocumentHostKind.MicrosoftWord && host != DocumentHostKind.WpsWriter)
		{
			rejection = SignatureStaticRejection.UnknownHost;
			return false;
		}
		string text2 = cjkFontName?.Trim();
		bool flag = false;
		if (text2 != null)
		{
			foreach (string calibratedCjkFont in SignatureWidthCalibrationTable.CalibratedCjkFonts)
			{
				if (string.Equals(calibratedCjkFont, text2, StringComparison.Ordinal))
				{
					flag = true;
					break;
				}
			}
		}
		if (!flag)
		{
			rejection = SignatureStaticRejection.UncalibratedCjkFont;
			return false;
		}
		if (string.Equals(asciiFontName?.Trim(), "Times New Roman", StringComparison.OrdinalIgnoreCase))
		{
			if (gridEnabled)
			{
				rejection = SignatureStaticRejection.RequiresHostMeasurement;
				return false;
			}
			if (morphology != SignatureTextMorphology.SpacedDate)
			{
				if (!ContainsOnlyCalibratedCharacters(text))
				{
					rejection = SignatureStaticRejection.UncalibratedCharacters;
					return false;
				}
				if (!SignatureWidthCalibrationTable.TryGetEntry(host, fontSizePt, gridEnabled: false, out var entry))
				{
					rejection = SignatureStaticRejection.UncalibratedFontSize;
					return false;
				}
				widthPt = EstimateWidth(text, entry);
				return true;
			}
			rejection = SignatureStaticRejection.RequiresHostMeasurement;
			return false;
		}
		rejection = SignatureStaticRejection.UncalibratedAsciiFont;
		return false;
	}

	private static float GetCharWidthPt(char ch, SignatureWidthCalibrationTable.Entry entry)
	{
		if (ch < '0' || ch > '9')
		{
			if (ch < 'A' || ch > 'Z')
			{
				if (ch == ' ')
				{
					return entry.AsciiSpacePt;
				}
				if (ch != '\u3000')
				{
					if (ch > '\u007f')
					{
						return entry.CjkPt;
					}
					return entry.CjkPt / 2f;
				}
				return entry.FullWidthSpacePt;
			}
			return entry.LettersUpperPt[ch - 65];
		}
		return entry.DigitsPt[ch - 48];
	}

	private static float GetMorphologyResidualPt(string text, SignatureWidthCalibrationTable.Entry entry)
	{
		bool flag = false;
		bool flag2 = false;
		for (int i = 0; i < text.Length; i++)
		{
			if (IsCjkIdeograph(text[i]))
			{
				flag = true;
			}
			else if (text[i] >= 'A' && text[i] <= 'Z')
			{
				flag2 = true;
			}
		}
		char c = text[0];
		if (c >= 'A' && c <= 'Z' && HasLeadingUpperLatinRun(text) && flag)
		{
			return entry.ShortLatinResidualPt;
		}
		if (c >= '0' && c <= '9' && flag2)
		{
			return entry.DateLatinResidualPt;
		}
		if (IsCjkIdeograph(c) && flag2)
		{
			return entry.MixedLatinResidualPt;
		}
		return 0f;
	}

	private static bool HasLeadingUpperLatinRun(string text)
	{
		if (text.Length > 0 && text[0] >= 'A')
		{
			return text[0] <= 'Z';
		}
		return false;
	}

	private static bool IsCjkIdeograph(char ch)
	{
		if (ch >= '一')
		{
			return ch <= '鿿';
		}
		return false;
	}

	private static bool IsTransitionCjk(char ch)
	{
		if (ch > '\u007f')
		{
			return ch != '\u3000';
		}
		return false;
	}
}
