using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;

namespace DocumentRepository.Services.Formatting.Signatures;

public static class SignatureWidthMeasureCache
{
	public const int MaxEntries = 256;

	private static readonly object Gate = new object();

	private static readonly Dictionary<string, float> Entries = new Dictionary<string, float>(StringComparer.Ordinal);

	public static int Count
	{
		get
		{
			lock (Gate)
			{
				return Entries.Count;
			}
		}
	}

	public static bool TryGet(string documentLifecycleId, DocumentHostKind host, string hostVersion, string cjkFontName, string asciiFontName, float fontSizePt, bool gridEnabled, int charsPerLine, float contentWidthPt, string text, out float widthPt)
	{
		string key = BuildKey(documentLifecycleId, host, hostVersion, cjkFontName, asciiFontName, fontSizePt, gridEnabled, charsPerLine, contentWidthPt, text);
		lock (Gate)
		{
			return Entries.TryGetValue(key, out widthPt);
		}
	}

	public static void Store(string documentLifecycleId, DocumentHostKind host, string hostVersion, string cjkFontName, string asciiFontName, float fontSizePt, bool gridEnabled, int charsPerLine, float contentWidthPt, string text, float widthPt)
	{
		if (float.IsNaN(widthPt) || float.IsInfinity(widthPt))
		{
			return;
		}
		string key = BuildKey(documentLifecycleId, host, hostVersion, cjkFontName, asciiFontName, fontSizePt, gridEnabled, charsPerLine, contentWidthPt, text);
		lock (Gate)
		{
			if (Entries.Count >= 256 && !Entries.ContainsKey(key))
			{
				Entries.Clear();
			}
			Entries[key] = widthPt;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Release(string documentLifecycleId)
	{
		if (documentLifecycleId == null)
		{
			return;
		}
		lock (Gate)
		{
			List<string> list = new List<string>();
			foreach (KeyValuePair<string, float> entry in Entries)
			{
				if (entry.Key.StartsWith(documentLifecycleId + "\u0001", StringComparison.Ordinal))
				{
					list.Add(entry.Key);
				}
			}
			foreach (string item in list)
			{
				Entries.Remove(item);
			}
		}
	}

	public static void Clear()
	{
		lock (Gate)
		{
			Entries.Clear();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildKey(string documentLifecycleId, DocumentHostKind host, string hostVersion, string cjkFontName, string asciiFontName, float fontSizePt, bool gridEnabled, int charsPerLine, float contentWidthPt, string text)
	{
		string[] obj = new string[19]
		{
			documentLifecycleId ?? string.Empty,
			"\u0001",
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null
		};
		int num = (int)host;
		obj[2] = num.ToString(CultureInfo.InvariantCulture);
		obj[3] = "\u0001";
		obj[4] = hostVersion ?? string.Empty;
		obj[5] = "\u0001";
		obj[6] = cjkFontName ?? string.Empty;
		obj[7] = "\u0001";
		obj[8] = asciiFontName ?? string.Empty;
		obj[9] = "\u0001";
		obj[10] = Normalize(fontSizePt, 0.05f).ToString("R", CultureInfo.InvariantCulture);
		obj[11] = "\u0001";
		obj[12] = (gridEnabled ? "1" : "0");
		obj[13] = "\u0001";
		obj[14] = charsPerLine.ToString(CultureInfo.InvariantCulture);
		obj[15] = "\u0001";
		obj[16] = Normalize(contentWidthPt, 0.1f).ToString("R", CultureInfo.InvariantCulture);
		obj[17] = "\u0001";
		obj[18] = text ?? string.Empty;
		return string.Concat(obj);
	}

	private static float Normalize(float value, float stepPt)
	{
		return (float)(Math.Round(value / stepPt, MidpointRounding.AwayFromZero) * (double)stepPt);
	}
}
