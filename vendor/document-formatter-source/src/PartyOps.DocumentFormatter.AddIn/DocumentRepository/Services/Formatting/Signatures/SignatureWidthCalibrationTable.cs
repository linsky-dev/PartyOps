using System;
using System.Collections.Generic;
using DocumentRepository.Services.Hosting;

namespace DocumentRepository.Services.Formatting.Signatures;

public static class SignatureWidthCalibrationTable
{
	public sealed class Entry
	{
		public float CjkPt;

		public float AsciiSpacePt;

		public float FullWidthSpacePt;

		public float DigitCjkTransitionPt;

		public float LatinToCjkTransitionPt;

		public float CjkToLatinTransitionPt;

		public float ShortLatinResidualPt;

		public float DateLatinResidualPt;

		public float MixedLatinResidualPt;

		public float[] DigitsPt;

		public float[] LettersUpperPt;
	}

	private struct Key
	{
		internal readonly DocumentHostKind Host;

		internal readonly int SizeTenths;

		internal readonly bool Grid;

		internal Key(DocumentHostKind host, int sizeTenths, bool grid)
		{
			Host = host;
			SizeTenths = sizeTenths;
			Grid = grid;
		}
	}

	public const string CalibratedAsciiFont = "Times New Roman";

	public static readonly ISet<string> CalibratedCjkFonts = new HashSet<string> { "仿宋_GB2312", "仿宋", "方正仿宋_GBK", "宋体", "方正小标宋简体" };

	private static readonly Dictionary<Key, Entry> Entries = BuildEntries();

	public static bool TryGetEntry(DocumentHostKind host, float fontSizePt, bool gridEnabled, out Entry entry)
	{
		foreach (KeyValuePair<Key, Entry> entry2 in Entries)
		{
			if (entry2.Key.Host != host || entry2.Key.Grid != gridEnabled || !(Math.Abs((float)entry2.Key.SizeTenths / 10f - fontSizePt) <= 0.05f))
			{
				continue;
			}
			entry = entry2.Value;
			return true;
		}
		entry = null;
		return false;
	}

	private static Dictionary<Key, Entry> BuildEntries()
	{
		Dictionary<Key, Entry> dictionary = new Dictionary<Key, Entry>();
		dictionary[new Key(DocumentHostKind.WpsWriter, 150, grid: false)] = new Entry
		{
			CjkPt = 15f,
			AsciiSpacePt = 7.5f,
			FullWidthSpacePt = 15f,
			DigitCjkTransitionPt = 3.75f,
			LatinToCjkTransitionPt = 3.709998f,
			CjkToLatinTransitionPt = 3.709998f,
			ShortLatinResidualPt = 0.029996f,
			DateLatinResidualPt = 0.050007f,
			MixedLatinResidualPt = 0.050007f,
			DigitsPt = new float[] { 7.5f, 7.5f, 7.5f, 7.5f, 7.5f, 7.5f, 7.5f, 7.5f, 7.5f, 7.5f },
			LettersUpperPt = new float[] { 10.8299999f, 10.0f, 10.0f, 10.8299999f, 9.15999889f, 8.3399992f, 10.8299999f, 10.8299999f, 4.99499989f, 5.83499908f, 10.8299999f, 9.15999889f, 13.3349991f, 10.8299999f, 10.8299999f, 8.3399992f, 10.8299999f, 10.0f, 8.3399992f, 9.15999889f, 10.8299999f, 10.8299999f, 14.1549997f, 10.8299999f, 10.8299999f, 9.15999889f }
		};
		dictionary[new Key(DocumentHostKind.WpsWriter, 160, grid: false)] = new Entry
		{
			CjkPt = 16f,
			AsciiSpacePt = 8f,
			FullWidthSpacePt = 16f,
			DigitCjkTransitionPt = 4f,
			LatinToCjkTransitionPt = 3.980003f,
			CjkToLatinTransitionPt = 3.980003f,
			ShortLatinResidualPt = 0.019988f,
			DateLatinResidualPt = 0.039996f,
			MixedLatinResidualPt = 0.019991f,
			DigitsPt = new float[] { 8.0f, 8.0f, 8.0f, 8.0f, 8.0f, 8.0f, 8.0f, 8.0f, 8.0f, 8.0f },
			LettersUpperPt = new float[] { 11.5500002f, 10.6700001f, 10.6700001f, 11.5500002f, 9.77000046f, 8.89500046f, 11.5500002f, 11.5500002f, 5.32499981f, 6.2249999f, 11.5500002f, 9.77000046f, 14.2250004f, 11.5500002f, 11.5500002f, 8.89500046f, 11.5500002f, 10.6700001f, 8.89500046f, 9.77000046f, 11.5500002f, 11.5500002f, 15.1000004f, 11.5500002f, 11.5500002f, 9.77000046f }
		};
		dictionary[new Key(DocumentHostKind.WpsWriter, 170, grid: false)] = new Entry
		{
			CjkPt = 17f,
			AsciiSpacePt = 8.5f,
			FullWidthSpacePt = 17f,
			DigitCjkTransitionPt = 4.25f,
			LatinToCjkTransitionPt = 4.244994f,
			CjkToLatinTransitionPt = 4.244994f,
			ShortLatinResidualPt = 0.005009f,
			DateLatinResidualPt = -0.014987f,
			MixedLatinResidualPt = 4E-06f,
			DigitsPt = new float[] { 8.5f, 8.5f, 8.5f, 8.5f, 8.5f, 8.5f, 8.5f, 8.5f, 8.5f, 8.5f },
			LettersUpperPt = new float[] { 12.2749996f, 11.3349991f, 11.3349991f, 12.2749996f, 10.3800001f, 9.44999981f, 12.2749996f, 12.2749996f, 5.65999889f, 6.61499882f, 12.2749996f, 10.3800001f, 15.1149988f, 12.2749996f, 12.2749996f, 9.44999981f, 12.2749996f, 11.3349991f, 9.44999981f, 10.3800001f, 12.2749996f, 12.2749996f, 16.0450001f, 12.2749996f, 12.2749996f, 10.3800001f }
		};
		dictionary[new Key(DocumentHostKind.WpsWriter, 180, grid: false)] = new Entry
		{
			CjkPt = 17.999998f,
			AsciiSpacePt = 9f,
			FullWidthSpacePt = 17.999998f,
			DigitCjkTransitionPt = 4.500001f,
			LatinToCjkTransitionPt = 4.460001f,
			CjkToLatinTransitionPt = 4.460001f,
			ShortLatinResidualPt = 0f,
			DateLatinResidualPt = 0.034998f,
			MixedLatinResidualPt = 0f,
			DigitsPt = new float[] { 9.0f, 9.0f, 9.0f, 9.0f, 9.0f, 9.0f, 9.0f, 9.0f, 9.0f, 9.0f },
			LettersUpperPt = new float[] { 12.9949999f, 12.0050001f, 12.0050001f, 12.9949999f, 10.9949999f, 10.0099993f, 12.9949999f, 12.9949999f, 5.98999882f, 7.0f, 12.9949999f, 10.9949999f, 16.0f, 12.9949999f, 12.9949999f, 10.0099993f, 12.9949999f, 12.0050001f, 10.0099993f, 10.9949999f, 12.9949999f, 12.9949999f, 16.9849987f, 12.9949999f, 12.9949999f, 10.9949999f }
		};
		dictionary[new Key(DocumentHostKind.MicrosoftWord, 150, grid: false)] = new Entry
		{
			CjkPt = 15f,
			AsciiSpacePt = 7.5f,
			FullWidthSpacePt = 15f,
			DigitCjkTransitionPt = 3.75f,
			LatinToCjkTransitionPt = 3.75f,
			CjkToLatinTransitionPt = 3.75f,
			ShortLatinResidualPt = 0f,
			DateLatinResidualPt = 0f,
			MixedLatinResidualPt = 0f,
			DigitsPt = new float[] { 7.5f, 7.5f, 7.5f, 7.5f, 7.5f, 7.5f, 7.5f, 7.5f, 7.5f, 7.5f },
			LettersUpperPt = new float[] { 10.5f, 9.75f, 9.75f, 10.5f, 9.0f, 8.25f, 10.5f, 10.5f, 5.25f, 6.0f, 10.5f, 9.0f, 13.5f, 10.5f, 10.5f, 8.25f, 10.5f, 9.75f, 8.25f, 9.0f, 10.5f, 10.5f, 14.25f, 10.5f, 10.5f, 9.0f }
		};
		dictionary[new Key(DocumentHostKind.MicrosoftWord, 160, grid: false)] = new Entry
		{
			CjkPt = 15.75f,
			AsciiSpacePt = 8.025f,
			FullWidthSpacePt = 15.975f,
			DigitCjkTransitionPt = 3.75f,
			LatinToCjkTransitionPt = 3.75f,
			CjkToLatinTransitionPt = 3.75f,
			ShortLatinResidualPt = 0f,
			DateLatinResidualPt = 0f,
			MixedLatinResidualPt = 0f,
			DigitsPt = new float[] { 8.25f, 8.25f, 8.25f, 8.25f, 8.25f, 8.25f, 8.25f, 8.25f, 8.25f, 8.25f },
			LettersUpperPt = new float[] { 11.25f, 10.5f, 10.5f, 11.25f, 9.75f, 9.0f, 11.25f, 11.25f, 5.25f, 6.0f, 11.25f, 9.75f, 14.25f, 11.25f, 11.25f, 9.0f, 11.25f, 10.5f, 9.0f, 9.75f, 11.25f, 11.25f, 15.0f, 11.25f, 11.25f, 9.75f }
		};
		dictionary[new Key(DocumentHostKind.MicrosoftWord, 170, grid: false)] = new Entry
		{
			CjkPt = 17.25f,
			AsciiSpacePt = 8.55f,
			FullWidthSpacePt = 17.025f,
			DigitCjkTransitionPt = 4.26f,
			LatinToCjkTransitionPt = 4.725f,
			CjkToLatinTransitionPt = 4.725f,
			ShortLatinResidualPt = 0.225f,
			DateLatinResidualPt = -0.705f,
			MixedLatinResidualPt = -0.975f,
			DigitsPt = new float[] { 8.69999981f, 8.69999981f, 8.69999981f, 8.69999981f, 8.69999981f, 8.69999981f, 8.69999981f, 8.69999981f, 8.69999981f, 8.69999981f },
			LettersUpperPt = new float[] { 12.5249996f, 11.25f, 11.25f, 12.5249996f, 10.5f, 9.67500019f, 12.5249996f, 12.5249996f, 5.92500019f, 6.75f, 12.5249996f, 10.5f, 15.0f, 12.5249996f, 12.5249996f, 9.67500019f, 12.5249996f, 11.25f, 9.67500019f, 10.5f, 12.5249996f, 12.5249996f, 16.2749996f, 12.5249996f, 12.5249996f, 10.5f }
		};
		dictionary[new Key(DocumentHostKind.MicrosoftWord, 180, grid: false)] = new Entry
		{
			CjkPt = 18f,
			AsciiSpacePt = 9f,
			FullWidthSpacePt = 18f,
			DigitCjkTransitionPt = 4.5f,
			LatinToCjkTransitionPt = 4.5f,
			CjkToLatinTransitionPt = 4.5f,
			ShortLatinResidualPt = 0f,
			DateLatinResidualPt = 0f,
			MixedLatinResidualPt = 0f,
			DigitsPt = new float[] { 9.0f, 9.0f, 9.0f, 9.0f, 9.0f, 9.0f, 9.0f, 9.0f, 9.0f, 9.0f },
			LettersUpperPt = new float[] { 12.75f, 12.0f, 12.0f, 12.75f, 11.25f, 9.75f, 12.75f, 12.75f, 6.0f, 6.75f, 12.75f, 11.25f, 15.75f, 12.75f, 12.75f, 9.75f, 12.75f, 12.0f, 9.75f, 11.25f, 12.75f, 12.75f, 17.25f, 12.75f, 12.75f, 11.25f }
		};
		return dictionary;
	}
}
